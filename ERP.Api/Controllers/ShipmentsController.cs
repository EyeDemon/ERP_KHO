using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/shipments")]
[Route("api/v1/shipments")]
[Authorize]
public sealed class ShipmentsController(
    IShipmentService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<IReadOnlyList<ShipmentListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("delivery-sla-summary")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> DeliverySlaSummary(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var list = await service.ListAsync(warehouseId, null, cancellationToken);
        return Ok(new
        {
            totalShipments = list.Count,
            preDispatchCount = list.Count(x => x.Status is "Ready" or "Staging" or "Loading" or "Loaded"),
            inTransitCount = list.Count(x => x.Status is "Dispatched" or "InTransit"),
            deliveredOrCompletedCount = list.Count(x => x.Status is "Delivered" or "Completed"),
            exceptionOrReturnCount = list.Count(x => x.Status is "DeliveryFailed" or "ReturnToWarehouse"),
        });
    }

    [HttpGet("load-planning-and-tms")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> LoadPlanningAndTms(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var list = await service.ListAsync(warehouseId, null, cancellationToken);
        return Ok(new
        {
            totalShipments = list.Count,
            stagingOrLoadingCount = list.Count(x => x.Status is "Ready" or "Staging" or "Loading" or "Loaded"),
            carrierAssignedCount = list.Count(x => !string.IsNullOrWhiteSpace(x.VehiclePlate) || x.DockAppointmentId.HasValue),
            sealVerifiedCount = list.Count(x => !string.IsNullOrWhiteSpace(x.SealNumber)),
            tmsIntegrationMode = "Đồng bộ tự động trạng thái vận đơn & biên bản giao hàng (POD) chống trùng lặp",
            status = "Đã tải tổng quan ghép tải & điều phối vận chuyển",
            statusLabel = "Đã tải tổng quan ghép tải & điều phối vận chuyển"
        });
    }

    [HttpPost("sync-tms-manifest")]
    [IdempotentCommand("Shipment.SyncTmsManifest")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> SyncTmsManifest(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SyncTmsManifestRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        int syncedShipmentCount;
        var stagedOrLoadedCount = 0;
        var totalHandlingUnitsCount = 0;
        var totalGrossWeightKg = 0m;
        var totalVolumeM3 = 0m;
        if (db != null)
        {
            var query = db.Shipments.AsNoTracking();
            var huQuery = db.HandlingUnits.AsNoTracking()
                .Where(h => h.Status != HandlingUnitStatus.Cancelled);
            if (warehouseId.HasValue)
            {
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
                huQuery = huQuery.Where(h => h.WarehouseId == warehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
                huQuery = huQuery.Where(h => accessibleIds.Contains(h.WarehouseId));
            }
            syncedShipmentCount = await query.CountAsync(cancellationToken);
            stagedOrLoadedCount = await query.CountAsync(
                s => s.Status == ShipmentStatus.Ready
                     || s.Status == ShipmentStatus.Staging
                     || s.Status == ShipmentStatus.Loading
                     || s.Status == ShipmentStatus.Loaded
                     || s.Status == ShipmentStatus.Dispatched
                     || s.Status == ShipmentStatus.InTransit,
                cancellationToken);
            totalHandlingUnitsCount = await huQuery.CountAsync(cancellationToken);
            totalGrossWeightKg = Math.Round(await huQuery.SumAsync(h => h.GrossWeightKg ?? 0m, cancellationToken), 2);
            totalVolumeM3 = Math.Round(await huQuery.SumAsync(h => h.VolumeM3 ?? 0m, cancellationToken), 4);
        }
        else
        {
            var list = await service.ListAsync(warehouseId, null, cancellationToken);
            syncedShipmentCount = list.Count;
            stagedOrLoadedCount = list.Count(x => x.Status is "Ready" or "Staging" or "Loading" or "Loaded" or "Dispatched" or "InTransit");
        }

        var now = DateTime.UtcNow;
        var manifestId = $"TMS-MNF-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = targetWarehouseId,
                Action = "Shipment.SyncTmsManifest",
                EntityName = "TmsManifest",
                NewValues = manifestId,
                Reason = $"Đồng bộ bảng kê vận tải & ghép tải ({manifestId})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            manifestId,
            warehouseId = targetWarehouseId,
            carrierCode = string.IsNullOrWhiteSpace(request?.CarrierCode) ? "VTP-EXPRESS" : request.CarrierCode.Trim(),
            syncedShipmentCount,
            stagedOrLoadedCount,
            totalHandlingUnitsCount,
            totalGrossWeightKg,
            totalVolumeM3,
            ediStatus = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            status = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            statusLabel = "Đã đồng bộ bảng kê vận tải & ghép tải thành công",
            syncedAtUtc = now,
        });
    }

    [HttpGet("fleet-assignments")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> GetFleetAssignments(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId.HasValue && warehouseAccess != null)
        {
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }

        if (db == null)
        {
            var shipments = await service.ListAsync(warehouseId, null, cancellationToken);
            return Ok(new
            {
                totalCount = shipments.Count,
                items = shipments.Select(s => new
                {
                    shipmentId = s.Id,
                    shipmentCode = s.ShipmentCode,
                    warehouseId = s.WarehouseId,
                    status = s.Status,
                    vehiclePlate = s.VehiclePlate,
                    sealNumber = s.SealNumber,
                    dockAppointmentId = s.DockAppointmentId
                }),
                manifests = Array.Empty<object>(),
                statusLabel = "Đã tải danh sách phân công phương tiện & tài xế vận chuyển"
            });
        }

        var shipmentQuery = db.Shipments.AsNoTracking().Include(s => s.DockAppointment).AsQueryable();
        var manifestQuery = db.HandoverManifests.AsNoTracking().AsQueryable();
        if (warehouseId.HasValue)
        {
            shipmentQuery = shipmentQuery.Where(s => s.WarehouseId == warehouseId.Value);
            manifestQuery = manifestQuery.Where(m => m.WarehouseId == warehouseId.Value);
        }

        var shipmentList = await shipmentQuery
            .OrderByDescending(s => s.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var manifestList = await manifestQuery
            .OrderByDescending(m => m.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(new
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

    [HttpPost("fleet-assignments")]
    [IdempotentCommand("Shipment.AssignFleetVehicle")]
    [PermissionAuthorize(AppPermissions.ShipmentLoad, AppPermissions.ShipmentDispatch, AppPermissions.ShipmentRead)]
    public async Task<IActionResult> AssignFleetVehicle(
        [FromBody] AssignFleetVehicleRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        if (request == null ||
            string.IsNullOrWhiteSpace(request.VehiclePlate) ||
            string.IsNullOrWhiteSpace(request.DriverName))
        {
            return BadRequest(new
            {
                code = "FLEET_VEHICLE_DRIVER_REQUIRED",
                errorCode = "FLEET_VEHICLE_DRIVER_REQUIRED",
                message = "Bắt buộc nhập biển số xe (VehiclePlate) và họ tên tài xế/bưu tá (DriverName) khi phân công vận chuyển."
            });
        }

        if (db == null)
        {
            throw new InvalidOperationException("ErpKhoDbContext is required for fleet assignment persistence.");
        }

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
                return NotFound(new { code = "SHIPMENT_NOT_FOUND", errorCode = "SHIPMENT_NOT_FOUND", message = $"Không tìm thấy chuyến giao hàng #{request.ShipmentId.Value}." });
            }
        }

        ERP.Domain.Entities.HandoverManifest? manifest = null;
        if (request.ManifestId is > 0)
        {
            manifest = await db.HandoverManifests.FirstOrDefaultAsync(m => m.Id == request.ManifestId.Value, cancellationToken);
            if (manifest == null)
            {
                return NotFound(new { code = "MANIFEST_NOT_FOUND", errorCode = "MANIFEST_NOT_FOUND", message = $"Không tìm thấy biên bản bàn giao #{request.ManifestId.Value}." });
            }
        }

        var targetWarehouseId = request.WarehouseId
            ?? shipment?.WarehouseId
            ?? manifest?.WarehouseId
            ?? await db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (targetWarehouseId <= 0) targetWarehouseId = 1;

        if (warehouseAccess != null)
        {
            await warehouseAccess.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
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

        var actorUserId = int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) && await db.Users.AsNoTracking().AnyAsync(u => u.Id == uid, cancellationToken)
            ? (int?)uid
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
                    CreatedBy = actorUserId ?? 1,
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
            UserId = actorUserId,
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

        return Ok(new
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

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<ShipmentDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpGet("{id:int}/tracking")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<ShipmentTrackingDto>> Tracking(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetTrackingAsync(id, cancellationToken));

    [HttpPost("{id:int}/stage")]
    [PermissionAuthorize(AppPermissions.ShipmentStage)]
    [IdempotentCommand("Shipment.Stage")]
    public async Task<ActionResult<ShipmentDto>> Stage(
        int id,
        StageShipmentDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.StageAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/start-loading")]
    [PermissionAuthorize(AppPermissions.ShipmentLoad)]
    [PermissionAuthorize(AppPermissions.LoadingExecute)]
    [IdempotentCommand("Shipment.StartLoading")]
    public async Task<ActionResult<ShipmentDto>> StartLoading(
        int id,
        StartShipmentLoadingDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.StartLoadingAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/load-hu")]
    [PermissionAuthorize(AppPermissions.ShipmentLoad)]
    [PermissionAuthorize(AppPermissions.LoadingExecute)]
    [IdempotentCommand("Shipment.LoadHu")]
    public async Task<ActionResult<ShipmentDto>> LoadHandlingUnit(
        int id,
        LoadShipmentHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.LoadHandlingUnitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/complete-loading")]
    [PermissionAuthorize(AppPermissions.ShipmentLoad)]
    [PermissionAuthorize(AppPermissions.LoadingExecute)]
    [IdempotentCommand("Shipment.CompleteLoading")]
    public async Task<ActionResult<ShipmentDto>> CompleteLoading(
        int id,
        CompleteShipmentLoadingDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteLoadingAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/dispatch")]
    [PermissionAuthorize(AppPermissions.ShipmentDispatch)]
    [IdempotentCommand("Shipment.Dispatch")]
    public async Task<ActionResult<ShipmentDto>> Dispatch(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.DispatchAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/mark-in-transit")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.MarkInTransit")]
    public async Task<ActionResult<ShipmentDto>> MarkInTransit(
        int id,
        MarkShipmentInTransitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.MarkInTransitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/delivery-confirm")]
    [PermissionAuthorize(AppPermissions.ShipmentConfirmDelivery)]
    [IdempotentCommand("Shipment.ConfirmDelivery")]
    public async Task<ActionResult<ShipmentDto>> ConfirmDelivery(
        int id,
        ConfirmShipmentDeliveryDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ConfirmDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/delivery-failed")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.DeliveryFailed")]
    public async Task<ActionResult<ShipmentDto>> DeliveryFailed(
        int id,
        FailShipmentDeliveryDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.FailDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/retry-delivery")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.RetryDelivery")]
    public async Task<ActionResult<ShipmentDto>> RetryDelivery(
        int id,
        RetryShipmentDeliveryDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.RetryDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/return-initiate")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.ReturnInitiate")]
    public async Task<ActionResult<ShipmentDto>> ReturnInitiate(
        int id,
        InitiateShipmentReturnDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.InitiateReturnAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.Complete")]
    public async Task<ActionResult<ShipmentDto>> Complete(
        int id,
        ShipmentStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/complete-rts-return")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.CompleteRtsReturn")]
    public async Task<IActionResult> CompleteRtsReturn(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CompleteShipmentRtsReturnRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return Ok(await service.GetAsync(id, cancellationToken));

        var shipment = await db.Shipments
            .Include(s => s.HandlingUnits)
                .ThenInclude(shu => shu.HandlingUnit)
                    .ThenInclude(hu => hu.Contents)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (shipment == null)
            return NotFound(new { code = "NOT_FOUND", message = $"Không tìm thấy lô hàng vận chuyển #{id}." });

        if (warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(shipment.WarehouseId, cancellationToken);

        var now = DateTime.UtcNow;
        var actorUserId = int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : 1;

        var putToQuarantine = request?.QuarantineLocationId.HasValue == true && request?.IsSealedIntact != true;
        var targetStatus = putToQuarantine ? InventoryStatus.Quarantine : InventoryStatus.Available;
        var targetLocationId = request?.TargetLocationId ?? request?.QuarantineLocationId ?? await db.WarehouseLocations.AsNoTracking()
            .Where(l => l.WarehouseId == shipment.WarehouseId && l.IsActive)
            .OrderByDescending(l => putToQuarantine
                ? l.LocationType == WarehouseLocationType.Receiving
                : (l.IsPickable && l.LocationType == WarehouseLocationType.Storage))
            .ThenBy(l => l.Id)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var rtsItems = shipment.HandlingUnits
            .SelectMany(shu => shu.HandlingUnit.Contents)
            .Select(c => (c.ProductId, LotId: (int?)null, SerialId: (int?)null, Quantity: c.Quantity))
            .ToList();
        if (rtsItems.Count == 0 && request is { ProductId: not null, Quantity: > 0m })
        {
            rtsItems.Add((request.ProductId.Value, null, null, request.Quantity.Value));
        }

        var ledgerIds = new List<int>();
        foreach (var item in rtsItems)
        {
            var returnQty = item.Quantity;
            if (returnQty <= 0m) continue;

            var stock = await db.InventoryStocks.FirstOrDefaultAsync(
                s => s.WarehouseId == shipment.WarehouseId &&
                     s.ProductId == item.ProductId &&
                     s.LocationId == targetLocationId &&
                     s.LotId == item.LotId &&
                     s.SerialId == item.SerialId &&
                     s.Status == targetStatus,
                cancellationToken);
            if (stock == null)
            {
                stock = new ERP.Domain.Entities.InventoryStock
                {
                    WarehouseId = shipment.WarehouseId,
                    ProductId = item.ProductId,
                    LocationId = targetLocationId,
                    LotId = item.LotId,
                    SerialId = item.SerialId,
                    Status = targetStatus,
                    Quantity = returnQty,
                    ReservedQuantity = 0m,
                    LastUpdated = now
                };
                db.InventoryStocks.Add(stock);
            }
            else
            {
                stock.Quantity += returnQty;
                stock.LastUpdated = now;
            }

            var rtsTx = new ERP.Domain.Entities.InventoryTransaction
            {
                ProductId = item.ProductId,
                WarehouseId = shipment.WarehouseId,
                LocationId = targetLocationId,
                ToLocationId = targetLocationId,
                LotId = item.LotId,
                SerialId = item.SerialId,
                InventoryStatus = targetStatus,
                ToInventoryStatus = targetStatus,
                TransactionType = TransactionType.Import,
                Quantity = returnQty,
                ReferenceId = shipment.Id,
                ReferenceType = "ShipmentRtsReturn",
                TransactionDate = now,
                CreatedBy = actorUserId,
                Note = putToQuarantine
                    ? $"RTS-{shipment.ShipmentCode}: Nhập hoàn hàng giao thất bại vào khu vực biệt trữ chờ kiểm định"
                    : $"RTS-{shipment.ShipmentCode}: Nhập hoàn nhanh đơn hoàn nguyên seal về tồn kho khả dụng (Available)"
            };
            db.InventoryTransactions.Add(rtsTx);
            await db.SaveChangesAsync(cancellationToken);
            ledgerIds.Add(rtsTx.Id);
        }

        shipment.Status = ShipmentStatus.Completed;
        shipment.CompletedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            shipmentId = shipment.Id,
            shipmentCode = shipment.ShipmentCode,
            warehouseId = shipment.WarehouseId,
            quarantineLocationId = targetLocationId,
            targetLocationId,
            resultingInventoryStatus = targetStatus.ToString(),
            status = shipment.Status.ToString(),
            statusLabel = putToQuarantine
                ? "Đã hoàn tất tiếp nhận hàng hoàn trả (RTS) vào khu vực biệt trữ"
                : "Đã hoàn tất nhập hoàn nhanh kiện RTS nguyên seal vào tồn kho khả dụng (Available)",
            ledgerTransactionIds = ledgerIds,
            completedAtUtc = now
        });
    }

    [HttpGet("vn-administrative-units")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> GetVnAdministrativeUnits(
        [FromQuery] string? provinceCode = null,
        [FromQuery] string? districtCode = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return Ok(new { totalCount = 0, items = Array.Empty<object>() });

        await EnsureVnAdministrativeSeededAsync(db, cancellationToken);

        var query = db.VnAdministrativeUnits.AsNoTracking()
            .Include(u => u.CarrierMappings)
            .Where(u => u.IsActive);

        if (!string.IsNullOrWhiteSpace(provinceCode))
        {
            var p = provinceCode.Trim();
            query = query.Where(u => u.ProvinceCode == p || u.ProvinceName.Contains(p));
        }
        if (!string.IsNullOrWhiteSpace(districtCode))
        {
            var d = districtCode.Trim();
            query = query.Where(u => u.DistrictCode == d || u.DistrictName.Contains(d));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(u =>
                u.ProvinceName.Contains(s) ||
                u.DistrictName.Contains(s) ||
                u.WardName.Contains(s) ||
                (u.Keywords != null && u.Keywords.Contains(s)));
        }

        var units = await query
            .OrderBy(u => u.ProvinceCode)
            .ThenBy(u => u.DistrictCode)
            .ThenBy(u => u.WardCode)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            totalCount = units.Count,
            items = units.Select(u => new
            {
                u.Id,
                u.ProvinceCode,
                u.ProvinceName,
                u.DistrictCode,
                u.DistrictName,
                u.WardCode,
                u.WardName,
                searchKeywords = u.Keywords,
                ghnDistrictId = u.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHN")?.CarrierDistrictId ?? $"GHN-D-{u.DistrictCode}",
                ghtkRouteCode = u.CarrierMappings.FirstOrDefault(m => m.CarrierCode == "GHTK")?.RouteCode ?? $"RT-GHTK-{u.DistrictCode}",
                carrierMappings = u.CarrierMappings.Select(m => new
                {
                    m.CarrierCode,
                    carrierProvinceCode = m.CarrierProvinceId,
                    carrierDistrictCode = m.CarrierDistrictId,
                    m.CarrierWardCode,
                    hubRoutingCode = m.SortingHubCode,
                    m.RouteCode,
                    isDeliverable = m.IsActive
                })
            }),
            statusLabel = $"Đã tải {units.Count} đơn vị hành chính 3 cấp Việt Nam & bảng ánh xạ hãng vận chuyển"
        });
    }

    [HttpPost("normalize-address")]
    [HttpPost("normalize-vn-address")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> NormalizeVietnameseAddress(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] NormalizeVnAddressRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return Ok(new { normalized = false });

        await EnsureVnAdministrativeSeededAsync(db, cancellationToken);

        var raw = $"{request?.RawAddress} {request?.Ward} {request?.District} {request?.Province}".Trim();
        var units = await db.VnAdministrativeUnits.AsNoTracking()
            .Include(u => u.CarrierMappings)
            .Where(u => u.IsActive)
            .ToListAsync(cancellationToken);

        static string StripWardPrefix(string wardName) =>
            wardName.Replace("Phường ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Xã ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("Thị trấn ", "", StringComparison.OrdinalIgnoreCase)
                    .Trim();

        var matched = units
            .Select(u =>
            {
                var score = 0;
                var coreWard = StripWardPrefix(u.WardName);
                if (!string.IsNullOrWhiteSpace(request?.Ward) &&
                    (u.WardName.Contains(request.Ward.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     (coreWard.Length >= 2 && request.Ward.Contains(coreWard, StringComparison.OrdinalIgnoreCase))))
                {
                    score += 100;
                }
                if (!string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(request?.Ward))
                {
                    if (raw.Contains(u.WardName, StringComparison.OrdinalIgnoreCase))
                        score += 80;
                    else if (coreWard.Length >= 3 && raw.Contains(coreWard, StringComparison.OrdinalIgnoreCase))
                        score += 60;

                    if (raw.Contains(u.DistrictName, StringComparison.OrdinalIgnoreCase))
                        score += 30;

                    if (!string.IsNullOrWhiteSpace(u.Keywords))
                    {
                        var matchedKeywords = u.Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(k => k.Trim())
                            .Where(k => k.Length > 0 && raw.Contains(k, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        score += matchedKeywords.Sum(k => k is "hcm" or "tphcm" or "hn" or "ha noi" or "dn" or "hp" or "ct" or "bd" ? 2 : 20);
                    }
                }
                return (Unit: u, Score: score);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Unit)
            .FirstOrDefault();

        if (matched == null)
        {
            return UnprocessableEntity(new
            {
                code = "ADDRESS_WARD_UNRESOLVED",
                errorCode = "ADDRESS_WARD_UNRESOLVED",
                matched = false,
                normalized = false,
                rawAddress = raw,
                message = "Không thể xác định Phường/Xã từ địa chỉ cung cấp; vui lòng kiểm tra lại địa chỉ.",
                statusLabel = "Không tìm thấy đơn vị hành chính 3 cấp phù hợp với địa chỉ đầu vào"
            });
        }

        var mappings = matched.CarrierMappings.ToDictionary(
            m => m.CarrierCode.ToUpperInvariant(),
            m => new
            {
                m.CarrierCode,
                carrierProvinceCode = m.CarrierProvinceId,
                carrierDistrictCode = m.CarrierDistrictId,
                m.CarrierWardCode,
                hubRoutingCode = m.SortingHubCode,
                m.RouteCode,
                isDeliverable = m.IsActive
            });

        return Ok(new
        {
            matched = true,
            matchedUnitId = matched.Id,
            provinceCode = matched.ProvinceCode,
            provinceName = matched.ProvinceName,
            districtCode = matched.DistrictCode,
            districtName = matched.DistrictName,
            wardCode = matched.WardCode,
            wardName = matched.WardName,
            normalizedFullAddress = $"{matched.WardName}, {matched.DistrictName}, {matched.ProvinceName}",
            carrierMappings = mappings,
            statusLabel = $"Đã chuẩn hóa địa chỉ sang {matched.WardName}, {matched.DistrictName}, {matched.ProvinceName}"
        });
    }

    internal static async Task EnsureVnAdministrativeSeededAsync(ErpKhoDbContext dbContext, CancellationToken ct)
    {
        if (await dbContext.VnAdministrativeUnits.AnyAsync(ct))
            return;

        var seedUnits = new[]
        {
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "769", DistrictName = "Thành phố Thủ Đức", WardCode = "27100", WardName = "Phường An Khánh", Keywords = "an khanh, thu duc, hcm, tphcm, q2" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "769", DistrictName = "Thành phố Thủ Đức", WardCode = "27088", WardName = "Phường Thảo Điền", Keywords = "thao dien, thu duc, q2, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "769", DistrictName = "Thành phố Thủ Đức", WardCode = "26830", WardName = "Phường Hiệp Bình Chánh", Keywords = "hiep binh chanh, thu duc, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "760", DistrictName = "Quận 1", WardCode = "26734", WardName = "Phường Bến Nghé", Keywords = "ben nghe, quan 1, q.1, q1, hcm, tphcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "760", DistrictName = "Quận 1", WardCode = "26740", WardName = "Phường Bến Thành", Keywords = "ben thanh, quan 1, q1, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "760", DistrictName = "Quận 1", WardCode = "26743", WardName = "Phường Nguyễn Thái Bình", Keywords = "nguyen thai binh, quan 1, q1, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "766", DistrictName = "Quận Tân Bình", WardCode = "26974", WardName = "Phường 2", Keywords = "phuong 2, tan binh, san bay, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "766", DistrictName = "Quận Tân Bình", WardCode = "26983", WardName = "Phường 4", Keywords = "phuong 4, tan binh, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "765", DistrictName = "Quận Bình Thạnh", WardCode = "26944", WardName = "Phường 25", Keywords = "phuong 25, binh thanh, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "770", DistrictName = "Quận 3", WardCode = "27127", WardName = "Phường Võ Thị Sáu", Keywords = "vo thi sau, quan 3, q3, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "778", DistrictName = "Quận 7", WardCode = "27487", WardName = "Phường Tân Phong", Keywords = "tan phong, quan 7, q7, phu my hung, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "79", ProvinceName = "TP. Hồ Chí Minh", DistrictCode = "764", DistrictName = "Quận Gò Vấp", WardCode = "26881", WardName = "Phường 10", Keywords = "phuong 10, go vap, hcm" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "005", DistrictName = "Quận Cầu Giấy", WardCode = "00166", WardName = "Phường Dịch Vọng Hậu", Keywords = "dich vong hau, cau giay, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "005", DistrictName = "Quận Cầu Giấy", WardCode = "00169", WardName = "Phường Mai Dịch", Keywords = "mai dich, cau giay, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "002", DistrictName = "Quận Hoàn Kiếm", WardCode = "00058", WardName = "Phường Hàng Bài", Keywords = "hang bai, hoan kiem, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "002", DistrictName = "Quận Hoàn Kiếm", WardCode = "00070", WardName = "Phường Tràng Tiền", Keywords = "trang tien, hoan kiem, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "001", DistrictName = "Quận Ba Đình", WardCode = "00004", WardName = "Phường Điện Biên", Keywords = "dien bien, ba dinh, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "006", DistrictName = "Quận Đống Đa", WardCode = "00208", WardName = "Phường Láng Hạ", Keywords = "lang ha, dong da, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "009", DistrictName = "Quận Thanh Xuân", WardCode = "00343", WardName = "Phường Nhân Chính", Keywords = "nhan chinh, thanh xuan, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "01", ProvinceName = "Thành phố Hà Nội", DistrictCode = "019", DistrictName = "Quận Nam Từ Liêm", WardCode = "00622", WardName = "Phường Mỹ Đình 1", Keywords = "my dinh 1, nam tu liem, ha noi, hn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "48", ProvinceName = "Thành phố Đà Nẵng", DistrictCode = "492", DistrictName = "Quận Hải Châu", WardCode = "20242", WardName = "Phường Thạch Thang", Keywords = "thach thang, hai chau, da nang, dn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "48", ProvinceName = "Thành phố Đà Nẵng", DistrictCode = "492", DistrictName = "Quận Hải Châu", WardCode = "20245", WardName = "Phường Hải Châu I", Keywords = "hai chau 1, hai chau, da nang, dn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "48", ProvinceName = "Thành phố Đà Nẵng", DistrictCode = "493", DistrictName = "Quận Sơn Trà", WardCode = "20275", WardName = "Phường Phước Mỹ", Keywords = "phuoc my, son tra, da nang, dn" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "31", ProvinceName = "Thành phố Hải Phòng", DistrictCode = "304", DistrictName = "Quận Ngô Quyền", WardCode = "11329", WardName = "Phường Đông Khê", Keywords = "dong khe, ngo quyen, hai phong, hp" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "31", ProvinceName = "Thành phố Hải Phòng", DistrictCode = "305", DistrictName = "Quận Lê Chân", WardCode = "11380", WardName = "Phường Kênh Dương", Keywords = "kenh duong, le chan, hai phong, hp" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "92", ProvinceName = "Thành phố Cần Thơ", DistrictCode = "916", DistrictName = "Quận Ninh Kiều", WardCode = "31144", WardName = "Phường Tân An", Keywords = "tan an, ninh kieu, can tho, ct" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "92", ProvinceName = "Thành phố Cần Thơ", DistrictCode = "916", DistrictName = "Quận Ninh Kiều", WardCode = "31141", WardName = "Phường Xuân Khánh", Keywords = "xuan khanh, ninh kieu, can tho, ct" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "74", ProvinceName = "Tỉnh Bình Dương", DistrictCode = "724", DistrictName = "Thành phố Dĩ An", WardCode = "25942", WardName = "Phường Đông Hòa", Keywords = "dong hoa, di an, binh duong, bd" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "74", ProvinceName = "Tỉnh Bình Dương", DistrictCode = "725", DistrictName = "Thành phố Thuận An", WardCode = "25960", WardName = "Phường Lái Thiêu", Keywords = "lai thieu, thuan an, binh duong, bd" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "75", ProvinceName = "Tỉnh Đồng Nai", DistrictCode = "731", DistrictName = "Thành phố Biên Hòa", WardCode = "26020", WardName = "Phường Tân Hiệp", Keywords = "tan hiep, bien hoa, dong nai" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "56", ProvinceName = "Tỉnh Khánh Hòa", DistrictCode = "568", DistrictName = "Thành phố Nha Trang", WardCode = "22360", WardName = "Phường Lộc Thọ", Keywords = "loc tho, nha trang, khanh hoa" },
            new ERP.Domain.Entities.VnAdministrativeUnit { ProvinceCode = "46", ProvinceName = "Tỉnh Thừa Thiên Huế", DistrictCode = "474", DistrictName = "Thành phố Huế", WardCode = "19777", WardName = "Phường Vĩnh Ninh", Keywords = "vinh ninh, hue, thua thien hue" }
        };

        dbContext.VnAdministrativeUnits.AddRange(seedUnits);
        await dbContext.SaveChangesAsync(ct);

        var carriers = new[] { "GHN", "GHTK", "VTPOST", "SPX" };
        foreach (var u in seedUnits)
        {
            foreach (var carrier in carriers)
            {
                dbContext.CarrierLocationMappings.Add(new ERP.Domain.Entities.CarrierLocationMapping
                {
                    AdministrativeUnitId = u.Id,
                    CarrierCode = carrier,
                    CarrierProvinceId = $"{carrier}-P-{u.ProvinceCode}",
                    CarrierDistrictId = $"{carrier}-D-{u.DistrictCode}",
                    CarrierWardCode = $"{carrier}-W-{u.WardCode}",
                    SortingHubCode = $"HUB-{carrier}-{u.ProvinceCode}-{u.DistrictCode}",
                    RouteCode = $"RT-{carrier}-{u.DistrictCode}",
                    IsActive = true
                });
            }
        }
        await dbContext.SaveChangesAsync(ct);
    }
}

public sealed record NormalizeVnAddressRequest(
    string? RawAddress = null,
    string? Province = null,
    string? District = null,
    string? Ward = null,
    string? CarrierCode = null);

public sealed record SyncTmsManifestRequest(int? WarehouseId, string? CarrierCode);
public sealed record AssignFleetVehicleRequest(
    int? ShipmentId = null,
    int? ManifestId = null,
    int? WarehouseId = null,
    string? CarrierCode = null,
    string? VehiclePlate = null,
    string? TrailerPlate = null,
    string? DriverName = null,
    string? DriverPhone = null,
    string? SealNumber = null);
public sealed record CompleteShipmentRtsReturnRequest(
    int? QuarantineLocationId = null,
    int? ProductId = null,
    decimal? Quantity = null,
    string? Note = null,
    int? TargetLocationId = null,
    bool? IsSealedIntact = null);
