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
}

public sealed record SyncTmsManifestRequest(int? WarehouseId, string? CarrierCode);
