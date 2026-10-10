using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/backorders")]
[Authorize]
public sealed class BackordersController(ISalesOrderBackorderService service) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.BackorderRead)]
    public async Task<ActionResult<IReadOnlyList<BackorderDto>>> List(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListBackordersAsync(warehouseId, status, cancellationToken));

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.BackorderRead)]
    public async Task<ActionResult<BackorderDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetBackorderAsync(id, cancellationToken));

    [HttpPost("{id:int}/reallocate")]
    [PermissionAuthorize(AppPermissions.BackorderManage)]
    [IdempotentCommand("Backorder.Reallocate")]
    public async Task<ActionResult<BackorderDto>> Reallocate(
        int id,
        BackorderReallocateDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ReallocateBackorderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [PermissionAuthorize(AppPermissions.BackorderManage)]
    [IdempotentCommand("Backorder.Cancel")]
    public async Task<ActionResult<BackorderDto>> Cancel(
        int id,
        BackorderCancelDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.CancelBackorderAsync(id, request, cancellationToken));
}
