using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// Background worker that captures daily 3PL storage snapshots by OwnerId and WarehouseId
/// into dbo.ThreePlStorageSnapshots.
/// </summary>
public sealed class ThreePlDailySnapshotJob(
    IServiceScopeFactory scopeFactory,
    ILogger<ThreePlDailySnapshotJob> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(12);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CaptureDailySnapshotsOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ThreePlDailySnapshotJob iteration failed; will retry on next tick.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    public async Task<int> CaptureDailySnapshotsOnceAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetService<ErpKhoDbContext>();
        if (db is null)
        {
            return 0;
        }

        db.UseTenant(1);
        var now = DateTime.UtcNow;
        var today = now.Date;
        var snapshotCode = $"3PL-SNAP-{now:yyyyMMdd}";

        var stocks = await db.InventoryStocks
            .AsNoTracking()
            .Include(x => x.Product)
            .Where(x => x.Quantity > 0m)
            .ToListAsync(cancellationToken);

        if (stocks.Count == 0)
        {
            return 0;
        }

        var existingToday = await db.ThreePlStorageSnapshots
            .Where(s => s.SnapshotDateUtc == today)
            .ToListAsync(cancellationToken);

        var grouped = stocks
            .GroupBy(x => new { x.WarehouseId, x.OwnerId })
            .ToList();

        var upserted = 0;
        foreach (var g in grouped)
        {
            var distinctSkuCount = g.Select(x => x.ProductId).Distinct().Count();
            var occupiedBinCount = g.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().Count();
            var ownerOnHandQty = g.Sum(x => x.Quantity);
            var ownerVolumeCbm = Math.Round(
                g.Sum(x => x.Quantity * (x.Product?.UnitVolumeM3 is > 0m ? x.Product.UnitVolumeM3.Value : 0.002m)),
                3);
            var palletEquivalentCount = Math.Max(1m, Math.Round(ownerVolumeCbm / 1.2m, 2));
            const decimal dailyPalletRateVnd = 18000m;
            const decimal dailyCbmRateVnd = 15000m;
            const decimal dailyBinRateVnd = 5000m;
            var estimatedDailyStorageFeeVnd = Math.Round((palletEquivalentCount * dailyPalletRateVnd) + (occupiedBinCount * dailyBinRateVnd), 0);
            var estimatedMonthlyStorageFeeVnd = estimatedDailyStorageFeeVnd * 30m;

            var existing = existingToday.FirstOrDefault(s => s.WarehouseId == g.Key.WarehouseId && s.OwnerId == g.Key.OwnerId);
            if (existing == null)
            {
                db.ThreePlStorageSnapshots.Add(new ThreePlStorageSnapshot
                {
                    TenantId = 1,
                    WarehouseId = g.Key.WarehouseId,
                    OwnerId = g.Key.OwnerId,
                    OwnerCode = g.Key.OwnerId.HasValue ? $"OWN-{g.Key.OwnerId.Value}" : "OWN-DEFAULT",
                    OwnerName = "Chủ hàng 3PL",
                    SnapshotCode = snapshotCode,
                    SnapshotDateUtc = today,
                    DistinctSkuCount = distinctSkuCount,
                    OccupiedBinCount = occupiedBinCount,
                    TotalOnHandQty = ownerOnHandQty,
                    TotalVolumeCbm = ownerVolumeCbm,
                    PalletEquivalentCount = palletEquivalentCount,
                    DailyPalletRateVnd = dailyPalletRateVnd,
                    DailyCbmRateVnd = dailyCbmRateVnd,
                    DailyBinRateVnd = dailyBinRateVnd,
                    EstimatedDailyStorageFeeVnd = estimatedDailyStorageFeeVnd,
                    EstimatedMonthlyStorageFeeVnd = estimatedMonthlyStorageFeeVnd,
                    CapturedAtUtc = now
                });
            }
            else
            {
                existing.DistinctSkuCount = distinctSkuCount;
                existing.OccupiedBinCount = occupiedBinCount;
                existing.TotalOnHandQty = ownerOnHandQty;
                existing.TotalVolumeCbm = ownerVolumeCbm;
                existing.PalletEquivalentCount = palletEquivalentCount;
                existing.EstimatedDailyStorageFeeVnd = estimatedDailyStorageFeeVnd;
                existing.EstimatedMonthlyStorageFeeVnd = estimatedMonthlyStorageFeeVnd;
                existing.CapturedAtUtc = now;
            }

            upserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return upserted;
    }
}
