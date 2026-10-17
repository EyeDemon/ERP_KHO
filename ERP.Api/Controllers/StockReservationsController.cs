using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/stock-reservations")]
[Authorize(Roles = AppRoles.AllRoles)]
public class StockReservationsController(
    IStockReservationService service,
    ErpKhoDbContext db = null!,
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
    public async Task<IActionResult> Release(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken)
    {
        await service.ReleaseAsync(id, request, cancellationToken);
        return Ok(new { message = "Đã giải phóng giữ hàng." });
    }

    [HttpPost("expire")]
    [Authorize(Roles = AppRoles.AdminManagerOrStaff)]
    public async Task<IActionResult> Expire(CancellationToken cancellationToken) => Ok(new { expired = await service.ExpireAsync(cancellationToken) });

    [HttpGet("reconciliation")]
    [Authorize(Roles = AppRoles.AdminOrManager)]
    public async Task<IActionResult> Reconciliation(CancellationToken cancellationToken) => Ok(await service.ReconcileAsync(cancellationToken));

    [HttpGet("commitment-summary")]
    public async Task<IActionResult> GetCommitmentSummary([FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        if (warehouseId.HasValue && warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        if (db != null)
        {
            var query = db.StockReservations.AsNoTracking();
            if (warehouseId.HasValue)
                query = query.Where(r => r.WarehouseId == warehouseId.Value);

            var activeThresholdUtc = DateTime.UtcNow.AddHours(24);
            var totalReservations = await query.CountAsync(cancellationToken);
            var activeCount = await query.CountAsync(
                x => x.Status == StockReservationStatus.Active
                     || x.Status == StockReservationStatus.PartiallyConsumed
                     || x.Status == StockReservationStatus.PartiallyAllocated
                     || x.Status == StockReservationStatus.Allocated,
                cancellationToken);
            var consumedCount = await query.CountAsync(x => x.Status == StockReservationStatus.Consumed, cancellationToken);
            var releasedCount = await query.CountAsync(
                x => x.Status == StockReservationStatus.Released
                     || x.Status == StockReservationStatus.Expired
                     || x.Status == StockReservationStatus.Cancelled,
                cancellationToken);
            var totalActiveRemainingQuantity = await query
                .Where(x => x.Status == StockReservationStatus.Active
                            || x.Status == StockReservationStatus.PartiallyConsumed
                            || x.Status == StockReservationStatus.PartiallyAllocated
                            || x.Status == StockReservationStatus.Allocated)
                .SumAsync(x => (decimal?)(x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity), cancellationToken) ?? 0m;
            var expiringSoonCount = await query.CountAsync(
                x => x.Status == StockReservationStatus.Active && x.ExpiresAt <= activeThresholdUtc,
                cancellationToken);

            return Ok(new
            {
                totalReservations,
                activeCount,
                consumedCount,
                releasedCount,
                releasedOrExpiredCount = releasedCount,
                totalActiveRemainingQuantity,
                expiringSoonCount
            });
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
        var warehouseId = request?.WarehouseId;
        if (warehouseId.HasValue && warehouseAccess != null)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var now = DateTime.UtcNow;
        var expiredCandidateCount = db != null
            ? await db.StockReservations.AsNoTracking()
                .Where(r => !warehouseId.HasValue || r.WarehouseId == warehouseId.Value)
                .CountAsync(r => r.Status == StockReservationStatus.Active && r.SourceType == "Manual" && r.ExpiresAt <= now, cancellationToken)
            : 0;

        return Ok(new
        {
            sweepId = $"RSV-SWP-{now:yyyyMMdd-HHmm}",
            warehouseId,
            expiredCandidateCount,
            status = "COMMITMENT_TTL_VERIFIED",
            ledgerMutated = false,
            checkedAtUtc = now
        });
    }
}
