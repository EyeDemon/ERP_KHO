using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentService(
    ErpKhoDbContext context,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IStockReservationService stockReservationService,
    IPartnerDebtService? partnerDebt = null) : IShipmentService, IShipmentDispatchReadiness
{
    public async Task<IReadOnlyList<ShipmentListDto>> ListAsync(
        int? warehouseId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var query = Query().AsNoTracking().Where(x => accessible.Contains(x.WarehouseId));
        if (Enum.TryParse<ShipmentStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(250)
            .ToListAsync(cancellationToken);
        var shipmentIds = rows.Select(x => x.Id).ToArray();
        Dictionary<int, (int Id, string Code)> linkedExportReceipts;
        if (shipmentIds.Length == 0)
        {
            linkedExportReceipts = new Dictionary<int, (int Id, string Code)>();
        }
        else
        {
            var exportReceiptRows = await context.ExportReceipts.AsNoTracking()
                .Where(x => x.ShipmentId.HasValue && shipmentIds.Contains(x.ShipmentId.Value))
                .Select(x => new { ShipmentId = x.ShipmentId!.Value, x.Id, x.Code })
                .ToListAsync(cancellationToken);
            linkedExportReceipts = exportReceiptRows
                .GroupBy(x => x.ShipmentId)
                .ToDictionary(
                    g => g.Key,
                    g =>
                    {
                        var latest = g.OrderByDescending(x => x.Id).First();
                        return (latest.Id, latest.Code);
                    });
        }
        return rows.Select(r =>
        {
            var dto = MapList(r);
            if (linkedExportReceipts.TryGetValue(r.Id, out var er))
            {
                dto.ExportReceiptId = er.Id;
                dto.ExportReceiptCode = er.Code.Trim();
            }
            else if (r.SourceType == "ExportReceipt" && r.SourceId.HasValue)
            {
                dto.ExportReceiptId = r.SourceId;
                dto.ExportReceiptCode = r.SourceCode?.Trim();
            }
            return dto;
        }).ToList();
    }

    public async Task<ShipmentDto> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var shipment = await LoadAsync(id, false, cancellationToken);
        var dto = Map(shipment);
        var linkedEr = await context.ExportReceipts.AsNoTracking()
            .Where(x => x.ShipmentId == shipment.Id)
            .Select(x => new { x.Id, x.Code })
            .FirstOrDefaultAsync(cancellationToken);
        if (linkedEr is not null)
        {
            dto.ExportReceiptId = linkedEr.Id;
            dto.ExportReceiptCode = linkedEr.Code.Trim();
        }
        else if (shipment.SourceType == "ExportReceipt" && shipment.SourceId.HasValue)
        {
            dto.ExportReceiptId = shipment.SourceId;
            dto.ExportReceiptCode = shipment.SourceCode?.Trim();
        }
        return dto;
    }

    public Task<ShipmentDto> StageAsync(
        int id,
        StageShipmentDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Ready, "Chỉ Shipment READY mới có thể đưa vào staging.");
            if (shipment.PackingSession.Status is not PackingSessionStatus.Packed and not PackingSessionStatus.Closed)
                throw Conflict("SHIPMENT_PACKING_NOT_READY", "Packing session chưa hoàn tất.");

            var locationCode = CleanScan(request.StagingLocationCode)?.ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(locationCode))
                throw new BusinessRuleException("Staging location là bắt buộc.");
            var stagingLocation = await context.WarehouseLocations.SingleOrDefaultAsync(
                x => x.WarehouseId == shipment.WarehouseId &&
                     x.Code == locationCode &&
                     x.LocationType == WarehouseLocationType.Staging &&
                     x.IsActive &&
                     !x.IsBlocked,
                token);
            if (stagingLocation is null)
                throw Conflict("SHIPMENT_STAGING_LOCATION_INVALID", "Không tìm thấy staging location hợp lệ trong kho.");

            var active = ActivePackingHus(shipment);
            ValidateAssignedRoots(shipment, active);
            if (active.Any(x => x.Status != HandlingUnitStatus.Closed))
                throw Conflict("SHIPMENT_HU_NOT_CLOSED", "Tất cả Handling Unit phải CLOSED trước khi staging.");

            var now = DateTime.UtcNow;
            foreach (var hu in active) hu.Status = HandlingUnitStatus.Staged;
            foreach (var link in shipment.HandlingUnits) link.StagedAt = now;
            shipment.StagingLocationId = stagingLocation.Id;
            shipment.StagingLocation = stagingLocation;
            shipment.Status = ShipmentStatus.Staging;
            shipment.StagedAt = now;
            AddAudit("Shipment.Staged", shipment, $"HandlingUnits: {shipment.HandlingUnits.Count}; StagingLocationId: {stagingLocation.Id}; Code: {stagingLocation.Code}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> StartLoadingAsync(
        int id,
        StartShipmentLoadingDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Staging, "Shipment phải ở STAGING trước khi bắt đầu loading.");
            var appointment = await context.DockAppointments
                .Include(x => x.Dock)
                .SingleOrDefaultAsync(x => x.Id == request.DockAppointmentId, token)
                ?? throw Conflict("SHIPMENT_DOCK_APPOINTMENT_INVALID", "Không tìm thấy outbound dock appointment.");

            if (appointment.WarehouseId != shipment.WarehouseId ||
                appointment.Direction != DockAppointmentDirection.Outbound ||
                appointment.Status != DockAppointmentStatus.InService ||
                appointment.DockId is null ||
                appointment.Dock is null ||
                !appointment.Dock.IsActive ||
                !appointment.Dock.SupportsOutbound)
                throw Conflict("SHIPMENT_DOCK_APPOINTMENT_INVALID", "Appointment phải là outbound, cùng kho, IN_SERVICE và có dock outbound đang hoạt động.");

            if (string.IsNullOrWhiteSpace(appointment.VehiclePlate))
                throw Conflict("SHIPMENT_VEHICLE_REQUIRED", "Appointment chưa có biển số xe thực tế.");

            var active = ActivePackingHus(shipment);
            ValidateAssignedRoots(shipment, active);
            if (active.Any(x => x.Status != HandlingUnitStatus.Staged))
                throw Conflict("SHIPMENT_HU_NOT_STAGED", "Tất cả Handling Unit phải ở STAGED trước khi loading.");

            shipment.DockAppointmentId = appointment.Id;
            shipment.DockAppointment = appointment;
            shipment.DockId = appointment.DockId;
            shipment.Dock = appointment.Dock;
            shipment.VehiclePlate = appointment.VehiclePlate;
            shipment.TrailerPlate = appointment.TrailerPlate;
            shipment.SealNumber = NormalizeSeal(appointment.SealNumber);
            shipment.Status = ShipmentStatus.Loading;
            shipment.LoadingStartedAt = DateTime.UtcNow;
            AddAudit(
                "Shipment.LoadingStarted",
                shipment,
                $"DockAppointmentId: {appointment.Id}; DockId: {appointment.DockId}; Vehicle: {appointment.VehiclePlate}");
        }, cancellationToken);

    public Task<ShipmentDto> LoadHandlingUnitAsync(
        int id,
        LoadShipmentHandlingUnitDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Loading, "Shipment phải ở LOADING trước khi quét HU.");
            var scan = CleanScan(request.HandlingUnitBarcode);
            if (string.IsNullOrWhiteSpace(scan))
                throw new BusinessRuleException("Barcode/HU code là bắt buộc.");

            var all = ActivePackingHus(shipment);
            var scanned = all.SingleOrDefault(x =>
                string.Equals(x.Barcode, scan, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.HuCode, scan, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(x.Sscc) && string.Equals(x.Sscc, scan, StringComparison.OrdinalIgnoreCase)));

            if (scanned is null)
            {
                var existsElsewhere = await context.HandlingUnits.AsNoTracking().AnyAsync(x =>
                    x.WarehouseId == shipment.WarehouseId &&
                    (x.Barcode == scan || x.HuCode == scan || x.Sscc == scan), token);
                throw existsElsewhere
                    ? Conflict("SHIPMENT_HU_MISMATCH", "Handling Unit không thuộc Shipment này.")
                    : Conflict("HU_NOT_FOUND", "Không tìm thấy Handling Unit theo mã đã quét.");
            }

            var link = shipment.HandlingUnits.SingleOrDefault(x => x.HandlingUnitId == scanned.Id);
            if (link is null)
                throw Conflict("SHIPMENT_HU_MISMATCH", "Hãy quét Handling Unit gốc đã được gắn vào Shipment, không quét HU con.");

            if (link.LoadedAt.HasValue || scanned.Status == HandlingUnitStatus.Loaded)
                throw Conflict("HU_ALREADY_LOADED", "Handling Unit đã được load lên xe.");

            var tree = Hierarchy(all, scanned.Id);
            if (tree.Any(x => x.Status != HandlingUnitStatus.Staged))
                throw Conflict("SHIPMENT_HU_NOT_STAGED", "Handling Unit hoặc HU con chưa ở trạng thái STAGED.");

            var now = DateTime.UtcNow;
            foreach (var hu in tree) hu.Status = HandlingUnitStatus.Loaded;
            link.LoadedAt = now;
            link.LoadedBy = currentUser.UserId;
            AddAudit("Shipment.HandlingUnitLoaded", shipment, $"HandlingUnitId: {scanned.Id}; HuCode: {scanned.HuCode}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<ShipmentDto> CompleteLoadingAsync(
        int id,
        CompleteShipmentLoadingDto request,
        CancellationToken cancellationToken = default) =>
        MutateAsync(id, request.RowVersion, async (shipment, token) =>
        {
            RequireStatus(shipment, ShipmentStatus.Loading, "Shipment phải ở LOADING trước khi xác nhận LOADED.");
            if (shipment.DockAppointmentId is null || shipment.DockId is null || string.IsNullOrWhiteSpace(shipment.VehiclePlate))
                throw Conflict("SHIPMENT_LOADING_CONTEXT_REQUIRED", "Shipment thiếu dock/vehicle context.");

            var appointment = await context.DockAppointments.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == shipment.DockAppointmentId.Value, token);
            if (appointment is null ||
                appointment.WarehouseId != shipment.WarehouseId ||
                appointment.Direction != DockAppointmentDirection.Outbound ||
                appointment.Status != DockAppointmentStatus.InService)
                throw Conflict("SHIPMENT_DOCK_APPOINTMENT_INVALID", "Outbound appointment không còn IN_SERVICE.");

            if (shipment.HandlingUnits.Count == 0 || shipment.HandlingUnits.Any(x => !x.LoadedAt.HasValue))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Chưa load đủ Handling Unit của Shipment.");

            var active = ActivePackingHus(shipment);
            if (active.Any(x => x.Status != HandlingUnitStatus.Loaded))
                throw Conflict("SHIPMENT_HU_NOT_LOADED", "Handling Unit hierarchy chưa được load đầy đủ.");

            var seal = NormalizeSeal(request.SealNumber);
            shipment.SealNumber = seal ?? shipment.SealNumber;
            shipment.Status = ShipmentStatus.Loaded;
            shipment.LoadedAt = DateTime.UtcNow;
            AddAudit(
                "Shipment.Loaded",
                shipment,
                $"HandlingUnits: {shipment.HandlingUnits.Count}; DockId: {shipment.DockId}; Vehicle: {shipment.VehiclePlate}; Seal: {shipment.SealNumber}");
            await Task.CompletedTask;
        }, cancellationToken);
}
