using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/shipments")]
[Authorize]
public sealed class ShipmentsController(IShipmentService service) : ControllerBase
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
            tmsIntegrationMode = "IDEMPOTENT_CARRIER_WEBHOOK_AND_POD_SYNC",
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
