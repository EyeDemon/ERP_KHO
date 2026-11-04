using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Hosting;

/// <summary>
/// Background worker that captures daily 3PL storage snapshots by OwnerId and WarehouseId
/// into dbo.ThreePlStorageSnapshots inside isolated per-tenant scopes.
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
        List<int> activeTenantIds;
        await using (var discoveryScope = scopeFactory.CreateAsyncScope())
        {
            var discoveryDb = discoveryScope.ServiceProvider.GetService<ErpKhoDbContext>();
            if (discoveryDb is null)
            {
                return 0;
            }

            var saasTenantIds = await discoveryDb.SaasTenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.TenantId > 0 &&
                            t.LifecycleStatus != "ARCHIVED" &&
                            t.LifecycleStatus != "SUSPENDED")
                .Select(t => t.TenantId)
                .ToListAsync(cancellationToken);

            var warehouseTenantIds = await discoveryDb.Warehouses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(w => w.IsActive && w.TenantId > 0)
                .Select(w => w.TenantId)
                .ToListAsync(cancellationToken);

            activeTenantIds = saasTenantIds
                .Concat(warehouseTenantIds)
                .Distinct()
                .OrderBy(t => t)
                .ToList();
        }

        if (activeTenantIds.Count == 0)
        {
            activeTenantIds.Add(1);
        }

        var now = DateTime.UtcNow;
        var today = now.Date;
        var snapshotCode = $"3PL-SNAP-{now:yyyyMMdd}";
        var totalUpserted = 0;

        foreach (var tenantId in activeTenantIds)
        {
            try
            {
                await using var tenantScope = scopeFactory.CreateAsyncScope();
                var tenantDb = tenantScope.ServiceProvider.GetService<ErpKhoDbContext>();
                if (tenantDb is null)
                {
                    continue;
                }

                tenantDb.UseTenant(tenantId);

                var stocks = await tenantDb.InventoryStocks
                    .AsNoTracking()
                    .Include(x => x.Product)
                    .Where(x => x.TenantId == tenantId && x.Quantity > 0m)
                    .ToListAsync(cancellationToken);

                if (stocks.Count == 0)
                {
                    continue;
                }

                var ownerIds = stocks
                    .Where(x => x.OwnerId.HasValue)
                    .Select(x => x.OwnerId!.Value)
                    .Distinct()
                    .ToList();

                var tenantPartners = ownerIds.Count > 0
                    ? await tenantDb.BusinessPartners
                        .AsNoTracking()
                        .Where(p => p.TenantId == tenantId && ownerIds.Contains(p.Id))
                        .Select(p => new { p.Id, p.Code, p.Name })
                        .ToDictionaryAsync(p => p.Id, cancellationToken)
                    : [];

                var existingToday = await tenantDb.ThreePlStorageSnapshots
                    .Where(s => s.TenantId == tenantId && s.SnapshotDateUtc == today)
                    .ToListAsync(cancellationToken);

                var latestOwnerRates = await tenantDb.ThreePlStorageSnapshots
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId)
                    .OrderByDescending(s => s.SnapshotDateUtc)
                    .ThenByDescending(s => s.Id)
                    .Select(s => new
                    {
                        s.WarehouseId,
                        s.OwnerId,
                        s.DailyPalletRateVnd,
                        s.DailyCbmRateVnd,
                        s.DailyBinRateVnd
                    })
                    .ToListAsync(cancellationToken);

                var grouped = stocks
                    .GroupBy(x => new { x.WarehouseId, x.OwnerId })
                    .ToList();

                var tenantUpserted = 0;
                foreach (var g in grouped)
                {
                    var distinctSkuCount = g.Select(x => x.ProductId).Distinct().Count();
                    var occupiedBinCount = g.Where(x => x.LocationId.HasValue).Select(x => x.LocationId!.Value).Distinct().Count();
                    var ownerOnHandQty = g.Sum(x => x.Quantity);
                    var ownerVolumeCbm = Math.Round(
                        g.Sum(x => x.Quantity * (x.Product?.UnitVolumeM3 is > 0m ? x.Product.UnitVolumeM3.Value : 0.002m)),
                        3);
                    var palletEquivalentCount = Math.Max(1m, Math.Round(ownerVolumeCbm / 1.2m, 2));

                    var rateSource = latestOwnerRates.FirstOrDefault(r =>
                        r.WarehouseId == g.Key.WarehouseId && r.OwnerId == g.Key.OwnerId);
                    var dailyPalletRateVnd = rateSource?.DailyPalletRateVnd is > 0m ? rateSource.DailyPalletRateVnd : 18000m;
                    var dailyCbmRateVnd = rateSource?.DailyCbmRateVnd is > 0m ? rateSource.DailyCbmRateVnd : 15000m;
                    var dailyBinRateVnd = rateSource?.DailyBinRateVnd is > 0m ? rateSource.DailyBinRateVnd : 5000m;

                    var estimatedDailyStorageFeeVnd = Math.Round(
                        (palletEquivalentCount * dailyPalletRateVnd) + (occupiedBinCount * dailyBinRateVnd), 0);
                    var estimatedMonthlyStorageFeeVnd = estimatedDailyStorageFeeVnd * 30m;

                    var ownerCode = "OWN-DEFAULT";
                    var ownerName = "Chủ hàng 3PL";
                    if (g.Key.OwnerId.HasValue)
                    {
                        if (tenantPartners.TryGetValue(g.Key.OwnerId.Value, out var partner))
                        {
                            ownerCode = !string.IsNullOrWhiteSpace(partner.Code) ? partner.Code.Trim() : $"OWN-{g.Key.OwnerId.Value}";
                            ownerName = !string.IsNullOrWhiteSpace(partner.Name) ? partner.Name.Trim() : ownerName;
                        }
                        else
                        {
                            ownerCode = $"OWN-{g.Key.OwnerId.Value}";
                        }
                    }

                    var existing = existingToday.FirstOrDefault(s =>
                        s.TenantId == tenantId &&
                        s.WarehouseId == g.Key.WarehouseId &&
                        s.OwnerId == g.Key.OwnerId);

                    if (existing == null)
                    {
                        tenantDb.ThreePlStorageSnapshots.Add(new ThreePlStorageSnapshot
                        {
                            TenantId = tenantId,
                            WarehouseId = g.Key.WarehouseId,
                            OwnerId = g.Key.OwnerId,
                            OwnerCode = ownerCode,
                            OwnerName = ownerName,
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
                        existing.OwnerCode = ownerCode;
                        existing.OwnerName = ownerName;
                        existing.DistinctSkuCount = distinctSkuCount;
                        existing.OccupiedBinCount = occupiedBinCount;
                        existing.TotalOnHandQty = ownerOnHandQty;
                        existing.TotalVolumeCbm = ownerVolumeCbm;
                        existing.PalletEquivalentCount = palletEquivalentCount;
                        existing.EstimatedDailyStorageFeeVnd = estimatedDailyStorageFeeVnd;
                        existing.EstimatedMonthlyStorageFeeVnd = estimatedMonthlyStorageFeeVnd;
                        existing.CapturedAtUtc = now;
                    }

                    tenantUpserted++;
                }

                if (tenantUpserted > 0)
                {
                    await tenantDb.SaveChangesAsync(cancellationToken);
                    totalUpserted += tenantUpserted;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ThreePlDailySnapshotJob failed for TenantId={TenantId}.", tenantId);
            }
        }

        return totalUpserted;
    }
}
