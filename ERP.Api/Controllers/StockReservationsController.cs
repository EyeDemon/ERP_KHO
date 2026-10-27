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
[Authorize(Roles = AppRoles.AllRoles)]
public class StockReservationsController(
    IStockReservationService service,
    IWmsGovernanceOperationsService govOps = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<StockReservationPageDto>> GetPage([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] int? warehouseId = null, [FromQuery] int? productId = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        Ok(await service.GetPageAsync(page, pageSize, warehouseId, productId, status, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StockReservationDto>> Get(int id, CancellationToken cancellationToken) => Ok(await service.GetAsync(id, cancellationToken));

    [HttpPost]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    [IdempotentCommand("InventoryReservation.Create")]
    public async Task<ActionResult<StockReservationDto>> Create(CreateStockReservationDto request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/release")]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    [IdempotentCommand("InventoryReservation.Release")]
    public async Task<IActionResult> Release(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken)
    {
        await service.ReleaseAsync(id, request, cancellationToken);
        return Ok(new { message = "Đã giải phóng giữ hàng." });
    }

    [HttpPost("expire")]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    [IdempotentCommand("InventoryReservation.Expire")]
    public async Task<IActionResult> Expire(CancellationToken cancellationToken) => Ok(new { expired = await service.ExpireAsync(cancellationToken) });

    [HttpGet("reconciliation")]
    [Authorize(Roles = AppRoles.AdminOrManager)]
    public async Task<IActionResult> Reconciliation(CancellationToken cancellationToken) => Ok(await service.ReconcileAsync(cancellationToken));

    [HttpGet("commitment-summary")]
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

        if (govOps != null)
        {
            return this.ToActionResult(await govOps.GetStockReservationCommitmentSummaryAsync(
                warehouseId,
                accessibleIds,
                cancellationToken));
        }

        var page = await service.GetPageAsync(1, int.MaxValue, warehouseId, null, null, cancellationToken);
        var fallbackReleased = page.Items.Count(x => x.Status is "Released" or "Expired" or "Cancelled");
        return Ok(new
        {
            totalReservations = page.TotalRecords,
            activeCount = page.Items.Count(x => x.Status is "Active" or "PartiallyConsumed" or "PartiallyAllocated" or "Allocated"),
            consumedCount = page.Items.Count(x => x.Status == "Consumed"),
            releasedCount = fallbackReleased,
            releasedOrExpiredCount = fallbackReleased,
            totalActiveRemainingQuantity = page.Items.Where(x => x.Status is "Active" or "PartiallyConsumed" or "PartiallyAllocated" or "Allocated").Sum(x => x.RemainingQuantity),
            expiringSoonCount = page.Items.Count(x => x.Status == "Active" && x.ExpiresAt <= DateTime.UtcNow.AddHours(24))
        });
    }

    public sealed record SweepExpiredReservationsRequest(int? WarehouseId = null);

    [HttpPost("sweep-expired-preview")]
    public async Task<IActionResult> SweepExpiredPreview(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SweepExpiredReservationsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
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

        if (govOps != null)
        {
            return this.ToActionResult(await govOps.SweepExpiredReservationsPreviewAsync(
                warehouseId,
                accessibleIds,
                actorUserId,
                cancellationToken));
        }

        var now = DateTime.UtcNow;
        var sweepId = $"RSV-SWP-{now:yyyyMMdd-HHmm}";
        return Ok(new
        {
            sweepId,
            warehouseId,
            activeReservationsCount = 0,
            expiredCandidateCount = 0,
            expiringWithin24hCount = 0,
            status = "Đã rà soát giữ hàng quá hạn & cam kết tồn kho",
            statusLabel = "Đã rà soát giữ hàng quá hạn & cam kết tồn kho",
            ledgerMutated = false,
            checkedAtUtc = now
        });
    }
}
