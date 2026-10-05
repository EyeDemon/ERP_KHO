using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/packing-sessions")]
[Authorize]
public sealed class PackingSessionsController(IPackingService service) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<ActionResult<IReadOnlyList<PackingSessionListDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.PackingRead)]
    public async Task<ActionResult<PackingSessionDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Create")]
    public async Task<ActionResult<PackingSessionDto>> Create(
        CreatePackingSessionDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateAsync(request, cancellationToken));

    [HttpPost("{id:int}/create-hu")]
    [PermissionAuthorize(AppPermissions.HandlingUnitCreate)]
    [IdempotentCommand("Packing.CreateHu")]
    public async Task<ActionResult<PackingSessionDto>> CreateHandlingUnit(
        int id,
        CreateHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CreateHandlingUnitAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/pack")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Pack")]
    public async Task<ActionResult<PackingSessionDto>> Pack(
        int id,
        PackIntoHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.PackAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/close")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.CloseHu")]
    public async Task<ActionResult<PackingSessionDto>> CloseHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CloseHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/cancel")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Cancel")]
    public async Task<ActionResult<PackingSessionDto>> CancelHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/nest")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Nest")]
    public async Task<ActionResult<PackingSessionDto>> NestHandlingUnit(
        int id,
        int handlingUnitId,
        NestHandlingUnitDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.NestHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/handling-units/{handlingUnitId:int}/unnest")]
    [PermissionAuthorize(AppPermissions.HandlingUnitModify)]
    [IdempotentCommand("HandlingUnit.Unnest")]
    public async Task<ActionResult<PackingSessionDto>> UnnestHandlingUnit(
        int id,
        int handlingUnitId,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.UnnestHandlingUnitAsync(id, handlingUnitId, request, cancellationToken));

    [HttpPost("{id:int}/complete")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Complete")]
    public async Task<ActionResult<PackingSessionDto>> Complete(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CompleteAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/close")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Close")]
    public async Task<ActionResult<PackingSessionDto>> Close(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CloseAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [PermissionAuthorize(AppPermissions.PackingExecute)]
    [IdempotentCommand("Packing.Cancel")]
    public async Task<ActionResult<PackingSessionDto>> Cancel(
        int id,
        PackingStateCommandDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelAsync(id, request, cancellationToken));
}
