using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public sealed class InventoryStatusController(IInventoryStatusService service) : ControllerBase
{
    [HttpGet("statuses")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryStatusDefinitionDto>>> GetStatuses(
        CancellationToken cancellationToken) =>
        Ok(await service.GetStatusesAsync(cancellationToken));

    [HttpGet("status-buckets")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryStatusBucketDto>>> GetBuckets(
        [FromQuery] int? warehouseId = null,
        [FromQuery] int? productId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetBucketsAsync(warehouseId, productId, status, cancellationToken));

    [HttpGet("quarantine")]
    [PermissionAuthorize(AppPermissions.QuarantineRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryStatusBucketDto>>> GetQuarantine(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.GetQuarantineAsync(warehouseId, cancellationToken));

    [HttpPost("status-changes")]
    [PermissionAuthorize(AppPermissions.InventoryStatusChangeCreate)]
    [IdempotentCommand("Inventory.StatusChange")]
    public async Task<ActionResult<InventoryStatusChangeResultDto>> ChangeStatus(
        InventoryStatusChangeRequestDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ChangeStatusAsync(request, cancellationToken));

    [HttpPost("quarantine/{stockId:int}/release")]
    [PermissionAuthorize(AppPermissions.InventoryStatusChangeCreate)]
    [PermissionAuthorize(AppPermissions.QuarantineManage)]
    [IdempotentCommand("Inventory.QuarantineRelease")]
    public async Task<ActionResult<InventoryStatusChangeResultDto>> ReleaseQuarantine(
        int stockId,
        QuarantineReleaseRequestDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ReleaseQuarantineAsync(stockId, request, cancellationToken));
}
