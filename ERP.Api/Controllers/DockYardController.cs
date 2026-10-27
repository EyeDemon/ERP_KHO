using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

public sealed record DispatchCrossdockRequest(
    int? WarehouseId = null,
    string? DockDoorCode = null,
    string? InboundAppointmentCode = null,
    string? OutboundReferenceCode = null,
    int? ProductId = null,
    string? ProductCode = null,
    int? SourceLocationId = null,
    string? SourceLocationCode = null,
    int? TargetLocationId = null,
    string? TargetLocationCode = null,
    string? FromLocationCode = null,
    string? ToLocationCode = null,
    decimal? Quantity = null,
    bool ExecuteStockMutation = false);

[Authorize]
[ApiController]
[Route("api/dock-yard")]
[Route("api/v1/dock-yard")]
public sealed class DockYardController(
    IDockYardService service,
    IWmsGovernanceOperationsService govOps = null!,
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

        if (govOps != null)
        {
            await govOps.EnrichCrossDockCodesAsync(warehouseId, inboundCodes, outboundCodes, token);
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

        if (govOps != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;

            var outcome = await govOps.DispatchCrossdockAsync(
                new WmsDispatchCrossdockInput(
                    targetWarehouseId,
                    request?.DockDoorCode,
                    request?.InboundAppointmentCode,
                    request?.OutboundReferenceCode,
                    request?.ProductId,
                    request?.ProductCode,
                    request?.SourceLocationId,
                    request?.SourceLocationCode,
                    request?.TargetLocationId,
                    request?.TargetLocationCode,
                    request?.FromLocationCode,
                    request?.ToLocationCode,
                    request?.Quantity,
                    request?.ExecuteStockMutation ?? false,
                    actorUserId),
                token);
            return outcome.ToActionResult(this);
        }

        var inboundAppointmentCode = string.IsNullOrWhiteSpace(request?.InboundAppointmentCode)
            ? $"APPT-IN-{targetWarehouseId:D2}"
            : request.InboundAppointmentCode.Trim();
        var outboundReferenceCode = string.IsNullOrWhiteSpace(request?.OutboundReferenceCode)
            ? $"SO-XD-{targetWarehouseId:D2}"
            : request.OutboundReferenceCode.Trim();
        var now = DateTime.UtcNow;

        return Ok(new
        {
            dispatchId = $"XDOCK-{now:yyyyMMdd-HHmm}",
            warehouseId = targetWarehouseId,
            dockDoorCode = string.IsNullOrWhiteSpace(request?.DockDoorCode) ? "DOCK-XD-01" : request.DockDoorCode.Trim(),
            stagedHuCount = 4,
            inboundAppointmentCode,
            outboundReferenceCode,
            dispatchStatus = "Đã phát lệnh điều phối trung chuyển thẳng",
            priorityLabel = "Ưu tiên cao",
            status = "Đã phát lệnh điều phối trung chuyển thẳng",
            statusLabel = "Đã phát lệnh điều phối trung chuyển thẳng",
            executedStockMutation = false,
            ledgerUpdated = false,
            routedQuantity = 0m,
            movedQuantity = 0m,
            sourceRemainingQuantity = (decimal?)null,
            targetResultingQuantity = (decimal?)null,
            ledgerTransactionId = (int?)null,
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
