using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/inventory")]
public sealed class InventoryStatusController(IInventoryStatusService service) : ControllerBase
{
    [HttpGet("statuses")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryStatusDefinitionDto>>> GetStatuses(
        CancellationToken cancellationToken) =>
        Ok(await service.GetStatusesAsync(cancellationToken));

    [HttpGet("buckets")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryBucketDto>>> GetBuckets(
        [FromQuery] int? warehouseId,
        [FromQuery] int? productId,
        [FromQuery] string? status,
        [FromQuery] string? lotNumber,
        [FromQuery] string? serialNumber,
        CancellationToken cancellationToken) =>
        Ok(await service.GetBucketsAsync(warehouseId, productId, status, lotNumber, serialNumber, cancellationToken));

    [HttpPost("status-changes")]
    [PermissionAuthorize(AppPermissions.InventoryStatusChangeCreate)]
    [IdempotentCommand("InventoryStatus.Change")]
    public async Task<ActionResult<InventoryStatusChangeResultDto>> Change(
        CreateInventoryStatusChangeDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ChangeAsync(request, cancellationToken));

    [HttpPost("disposition")]
    [PermissionAuthorize(AppPermissions.InventoryStatusChangeCreate)]
    [IdempotentCommand("InventoryStatus.Disposition")]
    public async Task<ActionResult<InventoryDispositionResultDto>> Disposition(
        CreateInventoryDispositionDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ExecuteDispositionAsync(request, cancellationToken));

    [HttpPost("recall-block")]
    [PermissionAuthorize(AppPermissions.InventoryStatusChangeCreate)]
    [IdempotentCommand("InventoryStatus.RecallBlock")]
    public async Task<ActionResult<InventoryRecallBlockResultDto>> RecallBlock(
        CreateInventoryRecallBlockDto request,
        CancellationToken cancellationToken) =>
        Ok(await service.ExecuteRecallBlockAsync(request, cancellationToken));
}


