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
[Route("api/v1/stock-reservations")]
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

        if (db != null)
        {
            var query = db.StockReservations.AsNoTracking();
            if (warehouseId.HasValue)
                query = query.Where(r => r.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(r => accessibleIds.Contains(r.WarehouseId));

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

        var now = DateTime.UtcNow;
        var activeReservationsCount = 0;
        var expiredCandidateCount = 0;
        var expiringWithin24hCount = 0;
        if (db != null)
        {
            var query = db.StockReservations.AsNoTracking();
            if (warehouseId.HasValue)
                query = query.Where(r => r.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(r => accessibleIds.Contains(r.WarehouseId));

            activeReservationsCount = await query.CountAsync(
                r => r.Status == StockReservationStatus.Active,
                cancellationToken);
            expiredCandidateCount = await query.CountAsync(
                r => r.Status == StockReservationStatus.Active && r.SourceType == "Manual" && r.ExpiresAt <= now,
                cancellationToken);
            expiringWithin24hCount = await query.CountAsync(
                r => r.Status == StockReservationStatus.Active && r.ExpiresAt > now && r.ExpiresAt <= now.AddHours(24),
                cancellationToken);
        }

        var sweepId = $"RSV-SWP-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = warehouseId,
                Action = "StockReservation.SweepExpiredPreview",
                EntityName = "StockReservationSweep",
                NewValues = sweepId,
                Reason = $"Rà soát giữ hàng quá hạn ({sweepId})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            sweepId,
            warehouseId,
            activeReservationsCount,
            expiredCandidateCount,
            expiringWithin24hCount,
            status = "Đã rà soát giữ hàng quá hạn & cam kết tồn kho",
            statusLabel = "Đã rà soát giữ hàng quá hạn & cam kết tồn kho",
            ledgerMutated = false,
            checkedAtUtc = now
        });
    }
}
