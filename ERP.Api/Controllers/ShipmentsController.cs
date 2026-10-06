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

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.ShipmentRead)]
    public async Task<ActionResult<ShipmentDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

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
}
