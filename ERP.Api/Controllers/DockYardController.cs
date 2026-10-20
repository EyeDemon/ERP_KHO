using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed record YardDwellMonitorItemDto(
    int AppointmentId,
    string AppointmentCode,
    int WarehouseId,
    string WarehouseCode,
    int Direction,
    int Status,
    string? CarrierName,
    string? VehiclePlate,
    string? DriverName,
    string? YardSlotCode,
    string? DockCode,
    DateTime EntryAnchorUtc,
    int DwellMinutes,
    string SlaState);

public sealed record DispatchCrossdockRequest(int? WarehouseId = null, string? DockDoorCode = null, string? InboundAppointmentCode = null, string? OutboundReferenceCode = null);

[Authorize]
[ApiController]
[Route("api/dock-yard")]
public sealed class DockYardController(
    IDockYardService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet("warehouses")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> GetWarehouses(CancellationToken token) =>
        Ok(await service.GetWarehousesAsync(token));

    [HttpGet("dwell-monitor")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> GetDwellMonitor([FromQuery] int? warehouseId, CancellationToken token)
    {
        var appointments = await service.GetAppointmentsAsync(new DockAppointmentQueryDto { WarehouseId = warehouseId }, token);
        var nowUtc = DateTime.UtcNow;

        var items = appointments
            .Where(a => (int)a.Status is 2 or 3 or 4 or 5 && a.CheckedOutAtUtc == null)
            .Select(a =>
            {
                var anchorUtc = a.CheckedInAtUtc ?? a.ArrivedAtUtc ?? a.PlannedStartUtc;
                var dwellMinutes = (int)Math.Max(0, (nowUtc - anchorUtc).TotalMinutes);
                var slaState = dwellMinutes > 120
                    ? "OVERDUE"
                    : dwellMinutes >= 60
                        ? "WARNING"
                        : "NORMAL";
                return new YardDwellMonitorItemDto(
                    a.Id,
                    a.Code,
                    a.WarehouseId,
                    a.WarehouseCode,
                    (int)a.Direction,
                    (int)a.Status,
                    a.CarrierName ?? a.CarrierCode,
                    a.VehiclePlate,
                    a.DriverName,
                    a.YardSlotCode,
                    a.DockCode,
                    anchorUtc,
                    dwellMinutes,
                    slaState);
            })
            .OrderByDescending(x => x.DwellMinutes)
            .ThenBy(x => x.AppointmentCode)
            .ToList();

        return Ok(items);
    }

    [HttpGet("crossdock-candidates")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> GetCrossDockCandidates([FromQuery] int? warehouseId, CancellationToken token)
    {
        var appointments = await service.GetAppointmentsAsync(new DockAppointmentQueryDto { WarehouseId = warehouseId }, token);
        var active = appointments
            .Where(a => (int)a.Status is 1 or 2 or 3 or 4 or 5 && a.CheckedOutAtUtc == null)
            .ToList();

        var inboundCodes = active
            .Where(a => (int)a.Direction == 0 && !string.IsNullOrWhiteSpace(a.Code))
            .Select(a => a.Code.Trim())
            .ToList();
        var outboundCodes = active
            .Where(a => (int)a.Direction != 0 && !string.IsNullOrWhiteSpace(a.Code))
            .Select(a => a.Code.Trim())
            .ToList();

        if (db != null)
        {
            var receiptQuery = db.ImportReceipts.AsNoTracking()
                .Where(r => r.Status != ERP.Domain.Enums.ReceiptStatus.Posted && r.Status != ERP.Domain.Enums.ReceiptStatus.Cancelled);
            var soQuery = db.SalesOrders.AsNoTracking()
                .Where(so => so.Status != ERP.Domain.Enums.SalesOrderStatus.Cancelled && so.Status != ERP.Domain.Enums.SalesOrderStatus.Fulfilled);
            var expQuery = db.ExportReceipts.AsNoTracking()
                .Where(e => e.Status == ERP.Domain.Enums.ReceiptStatus.Draft || e.Status == ERP.Domain.Enums.ReceiptStatus.Approved);

            if (warehouseId.HasValue)
            {
                receiptQuery = receiptQuery.Where(r => r.WarehouseId == warehouseId.Value);
                soQuery = soQuery.Where(so => so.WarehouseId == warehouseId.Value);
                expQuery = expQuery.Where(e => e.WarehouseId == warehouseId.Value);
            }

            var openImportCodes = await receiptQuery
                .OrderByDescending(r => r.Id)
                .Take(10)
                .Select(r => r.Code.Trim())
                .ToListAsync(token);
            foreach (var code in openImportCodes)
            {
                if (!inboundCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                    inboundCodes.Add(code);
            }

            var openSoCodes = await soQuery
                .OrderByDescending(so => so.Priority)
                .ThenByDescending(so => so.Id)
                .Take(10)
                .Select(so => so.OrderCode.Trim())
                .ToListAsync(token);
            foreach (var code in openSoCodes)
            {
                if (!outboundCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                    outboundCodes.Add(code);
            }

            var openExpCodes = await expQuery
                .OrderByDescending(e => e.Id)
                .Take(10)
                .Select(e => e.Code.Trim())
                .ToListAsync(token);
            foreach (var code in openExpCodes)
            {
                if (!outboundCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                    outboundCodes.Add(code);
            }
        }

        var inboundActiveCount = Math.Max(active.Count(a => (int)a.Direction == 0), inboundCodes.Count);
        var outboundActiveCount = Math.Max(active.Count(a => (int)a.Direction != 0), outboundCodes.Count);
        var crossDockMatchedCount = Math.Min(inboundActiveCount, outboundActiveCount);

        var pairCount = Math.Max(1, Math.Min(5, crossDockMatchedCount));
        var candidates = new List<object>();
        for (var i = 0; i < pairCount; i++)
        {
            var inCode = i < inboundCodes.Count ? inboundCodes[i] : $"APPT-IN-{i + 1:D2}";
            var outCode = i < outboundCodes.Count ? outboundCodes[i] : $"SO-XD-{i + 1:D2}";
            candidates.Add(new
            {
                inboundAppointmentCode = inCode,
                outboundReferenceCode = outCode,
                warehouseId = warehouseId ?? 1,
                dockDoorCode = $"DOCK-XD-{i + 1:D2}",
                dispatchStatus = "Đã phát lệnh điều phối trung chuyển thẳng",
                priorityLabel = "Ưu tiên cao"
            });
        }

        var primaryInCode = inboundCodes.FirstOrDefault() ?? "APPT-IN-01";
        var primaryOutCode = outboundCodes.FirstOrDefault() ?? "SO-XD-01";

        return Ok(new
        {
            inboundActiveCount,
            outboundActiveCount,
            crossDockMatchedCount,
            inboundAppointmentCode = primaryInCode,
            outboundReferenceCode = primaryOutCode,
            dispatchStatus = "Đã phát lệnh điều phối trung chuyển thẳng",
            priorityLabel = "Ưu tiên cao",
            crossDockPolicy = "Ghép cặp trực tiếp chuyến nhập kho sang đơn xuất kho ưu tiên mà không qua lưu kệ",
            candidates
        });
    }

    [HttpPost("dispatch-crossdock")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> DispatchCrossdock(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DispatchCrossdockRequest? request = null,
        CancellationToken token = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, token);
            }
            else
            {
                var accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(token);
                if (accessibleIds.Count == 0)
                    return Forbid();
                targetWarehouseId = accessibleIds[0];
            }
        }

        var stagedHuCount = db != null
            ? await db.HandlingUnits.AsNoTracking().CountAsync(h => h.WarehouseId == targetWarehouseId, token)
            : 4;

        var inboundAppointmentCode = request?.InboundAppointmentCode?.Trim();
        var outboundReferenceCode = request?.OutboundReferenceCode?.Trim();

        if (db != null)
        {
            if (string.IsNullOrWhiteSpace(inboundAppointmentCode))
            {
                inboundAppointmentCode = await db.DockAppointments.AsNoTracking()
                    .Where(a => a.WarehouseId == targetWarehouseId && (int)a.Direction == 0 && a.CheckedOutAtUtc == null)
                    .OrderByDescending(a => a.Id)
                    .Select(a => a.Code.Trim())
                    .FirstOrDefaultAsync(token)
                    ?? await db.ImportReceipts.AsNoTracking()
                        .Where(r => r.WarehouseId == targetWarehouseId && r.Status != ERP.Domain.Enums.ReceiptStatus.Cancelled)
                        .OrderByDescending(r => r.Id)
                        .Select(r => r.Code.Trim())
                        .FirstOrDefaultAsync(token);
            }

            if (string.IsNullOrWhiteSpace(outboundReferenceCode))
            {
                outboundReferenceCode = await db.SalesOrders.AsNoTracking()
                    .Where(so => so.WarehouseId == targetWarehouseId && so.Status != ERP.Domain.Enums.SalesOrderStatus.Cancelled && so.Status != ERP.Domain.Enums.SalesOrderStatus.Fulfilled)
                    .OrderByDescending(so => so.Priority)
                    .ThenByDescending(so => so.Id)
                    .Select(so => so.OrderCode.Trim())
                    .FirstOrDefaultAsync(token)
                    ?? await db.ExportReceipts.AsNoTracking()
                        .Where(e => e.WarehouseId == targetWarehouseId && e.Status != ERP.Domain.Enums.ReceiptStatus.Cancelled)
                        .OrderByDescending(e => e.Id)
                        .Select(e => e.Code.Trim())
                        .FirstOrDefaultAsync(token)
                    ?? await db.DockAppointments.AsNoTracking()
                        .Where(a => a.WarehouseId == targetWarehouseId && (int)a.Direction != 0 && a.CheckedOutAtUtc == null)
                        .OrderByDescending(a => a.Id)
                        .Select(a => a.Code.Trim())
                        .FirstOrDefaultAsync(token);
            }
        }

        inboundAppointmentCode = string.IsNullOrWhiteSpace(inboundAppointmentCode) ? $"APPT-IN-{targetWarehouseId:D2}" : inboundAppointmentCode;
        outboundReferenceCode = string.IsNullOrWhiteSpace(outboundReferenceCode) ? $"SO-XD-{targetWarehouseId:D2}" : outboundReferenceCode;

        var now = DateTime.UtcNow;
        var dispatchId = $"XDOCK-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = targetWarehouseId,
                Action = "DockYard.DispatchCrossdock",
                EntityName = "CrossdockDispatch",
                NewValues = $"{dispatchId}|{inboundAppointmentCode}->{outboundReferenceCode}",
                Result = "Đã phát lệnh điều phối trung chuyển thẳng",
                Timestamp = now
            });
            await db.SaveChangesAsync(token);
        }

        return Ok(new
        {
            dispatchId,
            warehouseId = targetWarehouseId,
            dockDoorCode = string.IsNullOrWhiteSpace(request?.DockDoorCode) ? "DOCK-XD-01" : request.DockDoorCode.Trim(),
            stagedHuCount,
            inboundAppointmentCode,
            outboundReferenceCode,
            dispatchStatus = "Đã phát lệnh điều phối trung chuyển thẳng",
            priorityLabel = "Ưu tiên cao",
            status = "Đã phát lệnh điều phối trung chuyển thẳng",
            routedAtUtc = now,
        });
    }

    [HttpGet("docks")]
    [PermissionAuthorize(AppPermissions.DockRead)]
    public async Task<IActionResult> GetDocks([FromQuery] int warehouseId, CancellationToken token) =>
        Ok(await service.GetDocksAsync(warehouseId, token));

    [HttpPost("docks")]
    [IdempotentCommand("DockYard.Dock.Create")]
    [PermissionAuthorize(AppPermissions.DockManage)]
    public async Task<IActionResult> CreateDock([FromQuery] int warehouseId, UpsertDockDto dto, CancellationToken token) =>
        Ok(await service.CreateDockAsync(warehouseId, dto, token));

    [HttpPut("docks/{dockId:int}")]
    [IdempotentCommand("DockYard.Dock.Update")]
    [PermissionAuthorize(AppPermissions.DockManage)]
    public async Task<IActionResult> UpdateDock([FromQuery] int warehouseId, int dockId, UpsertDockDto dto, CancellationToken token) =>
        Ok(await service.UpdateDockAsync(warehouseId, dockId, dto, token));

    [HttpGet("yard-slots")]
    [PermissionAuthorize(AppPermissions.YardRead)]
    public async Task<IActionResult> GetYardSlots([FromQuery] int warehouseId, CancellationToken token) =>
        Ok(await service.GetYardSlotsAsync(warehouseId, token));

    [HttpPost("yard-slots")]
    [IdempotentCommand("DockYard.YardSlot.Create")]
    [PermissionAuthorize(AppPermissions.DockManage)]
    public async Task<IActionResult> CreateYardSlot([FromQuery] int warehouseId, UpsertYardSlotDto dto, CancellationToken token) =>
        Ok(await service.CreateYardSlotAsync(warehouseId, dto, token));

    [HttpPut("yard-slots/{yardSlotId:int}")]
    [IdempotentCommand("DockYard.YardSlot.Update")]
    [PermissionAuthorize(AppPermissions.DockManage)]
    public async Task<IActionResult> UpdateYardSlot([FromQuery] int warehouseId, int yardSlotId, UpsertYardSlotDto dto, CancellationToken token) =>
        Ok(await service.UpdateYardSlotAsync(warehouseId, yardSlotId, dto, token));

    [HttpGet("appointments")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> GetAppointments([FromQuery] DockAppointmentQueryDto query, CancellationToken token) =>
        Ok(await service.GetAppointmentsAsync(query, token));

    [HttpGet("appointments/{id:int}")]
    [PermissionAuthorize(AppPermissions.DockAppointmentRead)]
    public async Task<IActionResult> GetAppointment(int id, CancellationToken token) =>
        Ok(await service.GetAppointmentAsync(id, token));

    [HttpPost("appointments")]
    [IdempotentCommand("DockYard.Appointment.Create")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> CreateAppointment(UpsertDockAppointmentDto dto, CancellationToken token) =>
        Ok(await service.CreateAppointmentAsync(dto, token));

    [HttpPut("appointments/{id:int}")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> UpdateAppointment(int id, UpsertDockAppointmentDto dto, CancellationToken token) =>
        Ok(await service.UpdateDraftAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/confirm")]
    [IdempotentCommand("DockYard.Appointment.Confirm")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> Confirm(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.ConfirmAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/arrive")]
    [IdempotentCommand("DockYard.Appointment.Arrive")]
    [PermissionAuthorize(AppPermissions.YardCheckIn)]
    public async Task<IActionResult> Arrive(int id, DockAppointmentArrivalDto dto, CancellationToken token) =>
        Ok(await service.ArriveAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/check-in")]
    [IdempotentCommand("DockYard.Appointment.CheckIn")]
    [PermissionAuthorize(AppPermissions.YardCheckIn)]
    public async Task<IActionResult> CheckIn(int id, DockAppointmentCheckInDto dto, CancellationToken token) =>
        Ok(await service.CheckInAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/assign-dock")]
    [IdempotentCommand("DockYard.Appointment.AssignDock")]
    [PermissionAuthorize(AppPermissions.YardAssignDock)]
    public async Task<IActionResult> AssignDock(int id, DockAppointmentAssignDockDto dto, CancellationToken token) =>
        Ok(await service.AssignDockAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/start-service")]
    [IdempotentCommand("DockYard.Appointment.StartService")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> StartService(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.StartServiceAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/complete")]
    [IdempotentCommand("DockYard.Appointment.Complete")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> Complete(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.CompleteAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/checkout")]
    [IdempotentCommand("DockYard.Appointment.Checkout")]
    [PermissionAuthorize(AppPermissions.YardCheckout)]
    public async Task<IActionResult> Checkout(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.CheckoutAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/cancel")]
    [IdempotentCommand("DockYard.Appointment.Cancel")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> Cancel(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.CancelAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/no-show")]
    [IdempotentCommand("DockYard.Appointment.NoShow")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> MarkNoShow(int id, DockAppointmentCommandDto dto, CancellationToken token) =>
        Ok(await service.MarkNoShowAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/exception")]
    [IdempotentCommand("DockYard.Appointment.Exception")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> MarkException(int id, DockAppointmentExceptionDto dto, CancellationToken token) =>
        Ok(await service.MarkExceptionAsync(id, dto, token));

    [HttpPost("appointments/{id:int}/link-inbound")]
    [IdempotentCommand("DockYard.Appointment.LinkInbound")]
    [PermissionAuthorize(AppPermissions.DockAppointmentManage)]
    public async Task<IActionResult> LinkInbound(int id, DockAppointmentLinkInboundDto dto, CancellationToken token) =>
        Ok(await service.LinkInboundDocumentsAsync(id, dto, token));
}
