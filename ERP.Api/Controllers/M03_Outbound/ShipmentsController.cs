using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/shipments")]
[Route("api/v1/shipments")]
[Authorize]
public sealed partial class ShipmentsController(
    IShipmentService service,
    IShipmentOperationsService shipmentOps,
    IWarehouseAuthorizationService warehouseAccess) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<IReadOnlyList<ShipmentListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("delivery-sla-summary"), PermissionAuthorize(AppPermissions.ShipmentRead)]
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

    [HttpGet("load-planning-and-tms"), PermissionAuthorize(AppPermissions.ShipmentRead)]
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

    [HttpPost("sync-tms-manifest"), IdempotentCommand("Shipment.SyncTmsManifest"), PermissionAuthorize(AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)]
    public async Task<IActionResult> SyncTmsManifest(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SyncTmsManifestRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentUpdate, AppPermissions.ShipmentDispatch)) return Forbid();
        return this.ToActionResult(await shipmentOps.SyncTmsManifestAsync(request, GetActorUserId(), warehouseAccess, cancellationToken));
    }

    [HttpGet("fleet-assignments"), PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<IActionResult> GetFleetAssignments(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        return this.ToActionResult(await shipmentOps.GetFleetAssignmentsAsync(warehouseId, warehouseAccess, cancellationToken));
    }

    [HttpPost("fleet-assignments"), IdempotentCommand("Shipment.AssignFleetVehicle")]
    [PermissionAuthorize(AppPermissions.ShipmentLoad, AppPermissions.ShipmentDispatch)]
    public async Task<IActionResult> AssignFleetVehicle(
        [FromBody] AssignFleetVehicleRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ShipmentLoad, AppPermissions.ShipmentDispatch)) return Forbid();

        if (request == null || string.IsNullOrWhiteSpace(request.VehiclePlate) || string.IsNullOrWhiteSpace(request.DriverName))
        {
            return BadRequest(new
            {
                code = "FLEET_VEHICLE_DRIVER_REQUIRED",
                errorCode = "FLEET_VEHICLE_DRIVER_REQUIRED",
                message = "Bắt buộc nhập biển số xe (VehiclePlate) và họ tên tài xế/bưu tá (DriverName) khi phân công vận chuyển."
            });
        }

        return this.ToActionResult(await shipmentOps.AssignFleetVehicleAsync(request, GetActorUserId(), warehouseAccess, cancellationToken));
    }

    [HttpGet("{id:int}"), PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<ShipmentDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpGet("{id:int}/tracking"), PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<ShipmentTrackingDto>> Tracking(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetTrackingAsync(id, cancellationToken));

    [HttpPost("{id:int}/stage"), PermissionAuthorize(AppPermissions.ShipmentStage), IdempotentCommand("Shipment.Stage")]
    public async Task<ActionResult<ShipmentDto>> Stage(int id, StageShipmentDto request, CancellationToken cancellationToken) =>
        Ok(await service.StageAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/start-loading"), PermissionAuthorize(AppPermissions.ShipmentLoad), PermissionAuthorize(AppPermissions.LoadingExecute), IdempotentCommand("Shipment.StartLoading")]
    public async Task<ActionResult<ShipmentDto>> StartLoading(int id, StartShipmentLoadingDto request, CancellationToken cancellationToken) =>
        Ok(await service.StartLoadingAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/load-hu"), PermissionAuthorize(AppPermissions.ShipmentLoad), PermissionAuthorize(AppPermissions.LoadingExecute), IdempotentCommand("Shipment.LoadHu")]
    public async Task<ActionResult<ShipmentDto>> LoadHandlingUnit(int id, LoadShipmentHandlingUnitDto request, CancellationToken cancellationToken) =>
        Ok(await service.LoadHandlingUnitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/complete-loading"), PermissionAuthorize(AppPermissions.ShipmentLoad), PermissionAuthorize(AppPermissions.LoadingExecute), IdempotentCommand("Shipment.CompleteLoading")]
    public async Task<ActionResult<ShipmentDto>> CompleteLoading(int id, CompleteShipmentLoadingDto request, CancellationToken cancellationToken) =>
        Ok(await service.CompleteLoadingAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/dispatch"), PermissionAuthorize(AppPermissions.ShipmentDispatch), IdempotentCommand("Shipment.Dispatch")]
    public async Task<ActionResult<ShipmentDto>> Dispatch(int id, ShipmentStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.DispatchAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/mark-in-transit"), PermissionAuthorize(AppPermissions.ShipmentUpdate), IdempotentCommand("Shipment.MarkInTransit")]
    public async Task<ActionResult<ShipmentDto>> MarkInTransit(int id, MarkShipmentInTransitDto request, CancellationToken cancellationToken) =>
        Ok(await service.MarkInTransitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/delivery-confirm"), PermissionAuthorize(AppPermissions.ShipmentConfirmDelivery), IdempotentCommand("Shipment.ConfirmDelivery")]
    public async Task<ActionResult<ShipmentDto>> ConfirmDelivery(int id, ConfirmShipmentDeliveryDto request, CancellationToken cancellationToken) =>
        Ok(await service.ConfirmDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/delivery-failed"), PermissionAuthorize(AppPermissions.ShipmentUpdate), IdempotentCommand("Shipment.DeliveryFailed")]
    public async Task<ActionResult<ShipmentDto>> DeliveryFailed(int id, FailShipmentDeliveryDto request, CancellationToken cancellationToken) =>
        Ok(await service.FailDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/retry-delivery"), PermissionAuthorize(AppPermissions.ShipmentUpdate), IdempotentCommand("Shipment.RetryDelivery")]
    public async Task<ActionResult<ShipmentDto>> RetryDelivery(int id, RetryShipmentDeliveryDto request, CancellationToken cancellationToken) =>
        Ok(await service.RetryDeliveryAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/return-initiate")]
    [HttpPost("{id:int}/return-to-warehouse")]
    [PermissionAuthorize(AppPermissions.ShipmentUpdate)]
    [IdempotentCommand("Shipment.ReturnInitiate")]
    public async Task<ActionResult<ShipmentDto>> ReturnInitiate(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] InitiateShipmentReturnDto? request = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.InitiateReturnAsync(
            id,
            request ?? new InitiateShipmentReturnDto { ReasonCode = "DELIVERY_FAILED_RTS", RowVersion = string.Empty },
            cancellationToken));

    [HttpPost("{id:int}/complete"), PermissionAuthorize(AppPermissions.ShipmentUpdate), IdempotentCommand("Shipment.Complete")]
    public async Task<ActionResult<ShipmentDto>> Complete(int id, ShipmentStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/complete-rts-return"), PermissionAuthorize(AppPermissions.ShipmentUpdate), IdempotentCommand("Shipment.CompleteRtsReturn")]
    public async Task<IActionResult> CompleteRtsReturn(
        int id,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CompleteShipmentRtsReturnRequest? request = null,
        CancellationToken cancellationToken = default) =>
        this.ToActionResult(await shipmentOps.CompleteRtsReturnAsync(id, request, GetActorUserId(), warehouseAccess, cancellationToken));

    private int? GetActorUserId() =>
        int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;
}

public sealed record SyncTmsManifestRequest(int? WarehouseId, string? CarrierCode)
    : SyncTmsManifestInput(WarehouseId, CarrierCode);

public sealed record AssignFleetVehicleRequest(
    int? ShipmentId = null, int? ManifestId = null, int? WarehouseId = null, string? CarrierCode = null,
    string? VehiclePlate = null, string? TrailerPlate = null, string? DriverName = null, string? DriverPhone = null, string? SealNumber = null)
    : AssignFleetVehicleInput(ShipmentId, ManifestId, WarehouseId, CarrierCode, VehiclePlate, TrailerPlate, DriverName, DriverPhone, SealNumber);

public sealed record CompleteShipmentRtsReturnRequest(
    int? QuarantineLocationId = null, int? ProductId = null, decimal? Quantity = null, string? Note = null,
    int? TargetLocationId = null, bool? IsSealedIntact = null)
    : CompleteShipmentRtsReturnInput(QuarantineLocationId, ProductId, Quantity, Note, TargetLocationId, IsSealedIntact);
