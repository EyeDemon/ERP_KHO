using ERP.Application.Common;
using ERP.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ShipmentOperationsService
{
    public async Task<WmsEndpointOutcome> GetFleetAssignmentsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else if (currentUser == null || (currentUser.IsAuthenticated && !currentUser.IsGlobalAdmin))
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var shipmentQuery = db.Shipments.AsNoTracking().Include(s => s.DockAppointment).AsQueryable();
        var manifestQuery = db.HandoverManifests.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
        {
            shipmentQuery = shipmentQuery.Where(s => s.WarehouseId == warehouseId.Value);
            manifestQuery = manifestQuery.Where(m => m.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            shipmentQuery = shipmentQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
            manifestQuery = manifestQuery.Where(m => accessibleIds.Contains(m.WarehouseId));
        }

        var shipmentList = await shipmentQuery
            .OrderByDescending(s => s.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var manifestList = await manifestQuery
            .OrderByDescending(m => m.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = shipmentList.Count,
            manifestCount = manifestList.Count,
            items = shipmentList.Select(s =>
            {
                var matchedManifest = manifestList.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(s.VehiclePlate) &&
                    string.Equals(m.VehiclePlate, s.VehiclePlate, StringComparison.OrdinalIgnoreCase));
                return new
                {
                    shipmentId = s.Id,
                    shipmentCode = s.ShipmentCode,
                    warehouseId = s.WarehouseId,
                    status = s.Status.ToString(),
                    vehiclePlate = s.VehiclePlate ?? matchedManifest?.VehiclePlate,
                    trailerPlate = s.TrailerPlate,
                    sealNumber = s.SealNumber,
                    driverName = matchedManifest?.DriverName ?? s.DockAppointment?.DriverName,
                    driverPhone = matchedManifest?.DriverPhone ?? s.DockAppointment?.DriverPhone,
                    carrierCode = matchedManifest?.CarrierCode ?? s.DockAppointment?.CarrierName,
                    dockAppointmentId = s.DockAppointmentId
                };
            }),
            manifests = manifestList.Select(m => new
            {
                manifestId = m.Id,
                manifestCode = m.ManifestCode,
                warehouseId = m.WarehouseId,
                carrierCode = m.CarrierCode,
                vehiclePlate = m.VehiclePlate,
                driverName = m.DriverName,
                driverPhone = m.DriverPhone,
                status = m.Status,
                totalPackages = m.TotalPackages,
                createdAtUtc = m.CreatedAtUtc,
                sealedAtUtc = m.SealedAtUtc
            }),
            statusLabel = "Đã tải danh sách phân công phương tiện & tài xế vận chuyển"
        });
    }

    public async Task<WmsEndpointOutcome> AssignFleetVehicleAsync(
        AssignFleetVehicleInput? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null ||
            string.IsNullOrWhiteSpace(request.VehiclePlate) ||
            string.IsNullOrWhiteSpace(request.DriverName))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "FLEET_VEHICLE_DRIVER_REQUIRED",
                errorCode = "FLEET_VEHICLE_DRIVER_REQUIRED",
                message = "Bắt buộc nhập biển số xe (VehiclePlate) và họ tên tài xế/bưu tá (DriverName) khi phân công vận chuyển."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var vehiclePlate = request.VehiclePlate.Trim().ToUpperInvariant();
        var driverName = request.DriverName.Trim();
        var driverPhone = string.IsNullOrWhiteSpace(request.DriverPhone) ? null : request.DriverPhone.Trim();
        var carrierCode = string.IsNullOrWhiteSpace(request.CarrierCode) ? "GHN" : request.CarrierCode.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;

        ERP.Domain.Entities.Shipment? shipment = null;
        if (request.ShipmentId is > 0)
        {
            shipment = await db.Shipments
                .Include(s => s.DockAppointment)
                .FirstOrDefaultAsync(s => s.Id == request.ShipmentId.Value, cancellationToken);
            if (shipment == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "SHIPMENT_NOT_FOUND",
                    errorCode = "SHIPMENT_NOT_FOUND",
                    message = $"Không tìm thấy chuyến giao hàng #{request.ShipmentId.Value}."
                });
            }
        }

        ERP.Domain.Entities.HandoverManifest? manifest = null;
        if (request.ManifestId is > 0)
        {
            manifest = await db.HandoverManifests.FirstOrDefaultAsync(m => m.Id == request.ManifestId.Value, cancellationToken);
            if (manifest == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "MANIFEST_NOT_FOUND",
                    errorCode = "MANIFEST_NOT_FOUND",
                    message = $"Không tìm thấy biên bản bàn giao #{request.ManifestId.Value}."
                });
            }
        }

        var targetWarehouseId = request.WarehouseId
            ?? shipment?.WarehouseId
            ?? manifest?.WarehouseId
            ?? await db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (targetWarehouseId <= 0) targetWarehouseId = 1;

        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        if (shipment != null)
        {
            shipment.VehiclePlate = vehiclePlate;
            if (!string.IsNullOrWhiteSpace(request.TrailerPlate))
                shipment.TrailerPlate = request.TrailerPlate.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(request.SealNumber))
                shipment.SealNumber = request.SealNumber.Trim();
            if (shipment.DockAppointment != null)
            {
                shipment.DockAppointment.VehiclePlate = vehiclePlate;
                shipment.DockAppointment.DriverName = driverName;
                if (driverPhone != null)
                    shipment.DockAppointment.DriverPhone = driverPhone;
            }
        }

        var candidateActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
        var resolvedActorId = candidateActorId is > 0 && await db.Users.AsNoTracking().AnyAsync(u => u.Id == candidateActorId.Value, cancellationToken)
            ? candidateActorId
            : null;

        if (manifest != null)
        {
            manifest.VehiclePlate = vehiclePlate;
            manifest.DriverName = driverName;
            manifest.DriverPhone = driverPhone;
            if (!string.IsNullOrWhiteSpace(request.CarrierCode))
                manifest.CarrierCode = carrierCode;
        }
        else if (shipment == null)
        {
            manifest = await db.HandoverManifests
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(m => m.WarehouseId == targetWarehouseId && m.CarrierCode == carrierCode && m.Status == "OPEN", cancellationToken);
            if (manifest == null)
            {
                manifest = new ERP.Domain.Entities.HandoverManifest
                {
                    ManifestCode = $"HND-{carrierCode}-{now:yyyyMMdd-HHmmss}",
                    WarehouseId = targetWarehouseId,
                    CarrierCode = carrierCode,
                    VehiclePlate = vehiclePlate,
                    DriverName = driverName,
                    DriverPhone = driverPhone,
                    Status = "OPEN",
                    TotalPackages = 0,
                    CreatedBy = resolvedActorId ?? 1,
                    CreatedAtUtc = now
                };
                db.HandoverManifests.Add(manifest);
            }
            else
            {
                manifest.VehiclePlate = vehiclePlate;
                manifest.DriverName = driverName;
                manifest.DriverPhone = driverPhone;
            }
        }

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = resolvedActorId,
            WarehouseId = targetWarehouseId,
            EntityName = shipment != null ? "Shipment" : "HandoverManifest",
            EntityId = shipment?.Id ?? manifest?.Id,
            Action = "Shipment.AssignFleetVehicle",
            NewValues = $"VehiclePlate={vehiclePlate}; DriverName={driverName}; DriverPhone={driverPhone}; SealNumber={request.SealNumber}",
            Reason = $"Phân công phương tiện {vehiclePlate} & tài xế {driverName}",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            shipmentId = shipment?.Id,
            shipmentCode = shipment?.ShipmentCode,
            manifestId = manifest?.Id,
            manifestCode = manifest?.ManifestCode,
            warehouseId = targetWarehouseId,
            carrierCode,
            vehiclePlate,
            trailerPlate = shipment?.TrailerPlate ?? request.TrailerPlate,
            sealNumber = shipment?.SealNumber ?? request.SealNumber,
            driverName,
            driverPhone,
            assignedAtUtc = now,
            statusLabel = $"Đã cập nhật phân công xe {vehiclePlate} & tài xế {driverName}"
        });
    }
}
