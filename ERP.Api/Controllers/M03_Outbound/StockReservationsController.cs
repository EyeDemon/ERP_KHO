using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/stock-reservations")]
[Route("api/v1/stock-reservations")]
[Authorize]
public class StockReservationsController(
    IStockReservationService service,
    IWmsGovernanceOperationsService govOps,
    IWarehouseAuthorizationService warehouseAccess,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.AllocationRead, AppPermissions.InventoryRead)]
    public async Task<ActionResult<StockReservationPageDto>> GetPage([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? warehouseId = null, [FromQuery] int? productId = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        Ok(await service.GetPageAsync(page, pageSize, warehouseId, productId, status, cancellationToken));

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.AllocationRead, AppPermissions.InventoryRead)]
    public async Task<ActionResult<StockReservationDto>> Get(int id, CancellationToken cancellationToken) => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [PermissionAuthorize(AppPermissions.AllocationCreate, AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("InventoryReservation.Create")]
    public async Task<ActionResult<StockReservationDto>> Create(CreateStockReservationDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/release")]
    [PermissionAuthorize(AppPermissions.AllocationRelease, AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("InventoryReservation.Release")]
    public async Task<IActionResult> Release(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken)
    {
        await service.ReleaseAsync(id, request, cancellationToken);
        return Ok(new { message = "Đã giải phóng giữ hàng." });
    }

    [HttpPost("expire")]
    [PermissionAuthorize(AppPermissions.AllocationRelease, AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("InventoryReservation.Expire")]
    public async Task<IActionResult> Expire(CancellationToken cancellationToken) => Ok(new { expired = await service.ExpireAsync(cancellationToken) });

    [HttpGet("reconciliation")]
    [PermissionAuthorize(AppPermissions.AllocationRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> Reconciliation(CancellationToken cancellationToken) => Ok(await service.ReconcileAsync(cancellationToken));

    [HttpGet("commitment-summary")]
    [PermissionAuthorize(AppPermissions.AllocationRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetCommitmentSummary([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        return this.ToActionResult(await govOps.GetStockReservationCommitmentSummaryAsync(
            warehouseId,
            accessibleIds,
            cancellationToken));
    }

    public sealed record SweepExpiredReservationsRequest(int? WarehouseId = null);

    [HttpPost("sweep-expired-preview")]
    [PermissionAuthorize(AppPermissions.AllocationRelease, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> SweepExpiredPreview(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SweepExpiredReservationsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(currentUser, AppPermissions.AllocationRelease, AppPermissions.InventoryMovementCreate))
            return Forbid();

        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var actorUserId = int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : (int?)null;

        return this.ToActionResult(await govOps.SweepExpiredReservationsPreviewAsync(
            warehouseId,
            accessibleIds,
            actorUserId,
            cancellationToken));
    }
}
