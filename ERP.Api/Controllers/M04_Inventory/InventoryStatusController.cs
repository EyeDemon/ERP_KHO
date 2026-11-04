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
[Route("api/v1/inventory")]
public sealed class InventoryStatusController(
    IInventoryStatusService service,
    IWmsGovernanceOperationsService govOps,
    IWarehouseAuthorizationService warehouseAccess,
    ICurrentUser currentUser) : ControllerBase
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

    public sealed record VerifyBucketInvariantsRequest(int? WarehouseId = null);

    [HttpPost("verify-bucket-invariants")]
    [HttpPost("/api/v1/inventory-status/verify-bucket-invariants")]
    [HttpPost("/api/inventory-status/verify-bucket-invariants")]
    [PermissionAuthorize(AppPermissions.InventoryStatusChangeCreate, AppPermissions.InventoryLockManage)]
    public async Task<IActionResult> VerifyBucketInvariants(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyBucketInvariantsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(currentUser, AppPermissions.InventoryStatusChangeCreate, AppPermissions.InventoryLockManage))
            return Forbid();

        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseId.HasValue)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        else
            accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);

        var actorUserId = int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : (int?)null;

        var outcome = await govOps.VerifyBucketInvariantsAsync(warehouseId, accessibleIds, actorUserId, cancellationToken);
        return outcome.ToActionResult(this);
    }

    public sealed record VerifyLockPrecedenceRequest(int? WarehouseId = null);

    [HttpPost("verify-lock-precedence")]
    [HttpPost("/api/v1/inventory-locks/verify-lock-precedence")]
    [HttpPost("/api/inventory-locks/verify-lock-precedence")]
    [PermissionAuthorize(AppPermissions.InventoryStatusChangeCreate, AppPermissions.InventoryLockManage)]
    public async Task<IActionResult> VerifyLockPrecedence(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyLockPrecedenceRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(currentUser, AppPermissions.InventoryStatusChangeCreate, AppPermissions.InventoryLockManage))
            return Forbid();

        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseId.HasValue)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        else
            accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);

        var actorUserId = int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : (int?)null;

        var outcome = await govOps.VerifyLockPrecedenceAsync(warehouseId, accessibleIds, actorUserId, cancellationToken);
        return outcome.ToActionResult(this);
    }
}
