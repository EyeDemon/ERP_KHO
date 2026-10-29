using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// Background worker that periodically expires soft stock reservations whose ExpiresAtUtc has passed,
/// releasing ReservedQty on the corresponding 9D InventoryStocks buckets.
/// </summary>
public sealed class SoftReservationExpiryJob(
    IServiceScopeFactory scopeFactory,
    ILogger<SoftReservationExpiryJob> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireStaleSoftReservationsOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SoftReservationExpiryJob iteration failed; will retry on next tick.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    public async Task<int> ExpireStaleSoftReservationsOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<ErpKhoDbContext>();
        if (db is null)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var expiredCandidates = await db.StockReservations
            .IgnoreQueryFilters()
            .Where(r => (r.Status == StockReservationStatus.Active ||
                         r.Status == StockReservationStatus.PartiallyConsumed ||
                         r.Status == StockReservationStatus.PartiallyAllocated) &&
                        r.ExpiresAt <= now)
            .OrderBy(r => r.ExpiresAt)
            .ThenBy(r => r.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (expiredCandidates.Count == 0)
        {
            return 0;
        }

        var expiredCount = 0;
        foreach (var reservation in expiredCandidates)
        {
            db.UseTenant(reservation.TenantId > 0 ? reservation.TenantId : 1);
            var remainingReserved = Math.Max(0m, reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity);
            reservation.ReleasedQuantity += remainingReserved;
            reservation.ReleasedAt = now;
            reservation.ReleaseReason ??= "TTL_EXPIRED_BY_WORKER";
            reservation.Status = StockReservationStatus.Expired;

            if (remainingReserved > 0m)
            {
                var stockBuckets = await db.InventoryStocks
                    .Where(s => s.TenantId == reservation.TenantId &&
                                s.WarehouseId == reservation.WarehouseId &&
                                s.ProductId == reservation.ProductId &&
                                s.Status == InventoryStatus.Available &&
                                s.ReservedQuantity > 0m)
                    .OrderByDescending(s => s.ReservedQuantity)
                    .ThenBy(s => s.Id)
                    .ToListAsync(cancellationToken);

                var toRelease = remainingReserved;
                foreach (var bucket in stockBuckets)
                {
                    if (toRelease <= 0m) break;
                    var deduct = Math.Min(bucket.ReservedQuantity, toRelease);
                    bucket.ReservedQuantity -= deduct;
                    bucket.LastUpdated = now;
                    toRelease -= deduct;
                }
            }

            expiredCount++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return expiredCount;
    }
}
