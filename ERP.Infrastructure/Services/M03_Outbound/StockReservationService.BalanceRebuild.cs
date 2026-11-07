using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public partial class StockReservationService
{
    public async Task<int> RebuildBalancesAsync(int? warehouseId = null, int? productId = null, CancellationToken cancellationToken = default)
    {
        var allowed = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            allowed = [warehouseId.Value];
        }

        var stocksQuery = context.InventoryStocks
            .Include(x => x.Location)
            .Where(x => allowed.Contains(x.WarehouseId));
        if (productId.HasValue)
            stocksQuery = stocksQuery.Where(x => x.ProductId == productId.Value);

        var stocks = await stocksQuery.OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductId).ThenBy(x => x.Id).ToListAsync(cancellationToken);

        var activeAllocations = await context.StockAllocations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        (!productId.HasValue || x.ProductId == productId.Value) &&
                        (x.Status == StockAllocationStatus.Active ||
                         x.Status == StockAllocationStatus.Picking ||
                         x.Status == StockAllocationStatus.Picked))
            .ToListAsync(cancellationToken);

        var activeReservations = await context.StockReservations.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        (!productId.HasValue || x.ProductId == productId.Value) &&
                        (x.Status == StockReservationStatus.Active ||
                         x.Status == StockReservationStatus.PartiallyConsumed ||
                         x.Status == StockReservationStatus.PartiallyAllocated ||
                         x.Status == StockReservationStatus.Allocated))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var activeLocks = await context.InventoryLocks.AsNoTracking()
            .EffectiveAt(now)
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        (!productId.HasValue || !x.ProductId.HasValue || x.ProductId == productId.Value))
            .ToListAsync(cancellationToken);

        var ledgerTransactions = await context.InventoryTransactions.AsNoTracking()
            .Where(x => allowed.Contains(x.WarehouseId) &&
                        (!productId.HasValue || x.ProductId == productId.Value))
            .ToListAsync(cancellationToken);

        EnsureMissingLedgerBucketsExist(stocks, ledgerTransactions, now);
        if (stocks.Count == 0) return 0;

        RecomputeStockBuckets(stocks, ledgerTransactions, activeReservations, activeAllocations, activeLocks);
        await context.SaveChangesAsync(cancellationToken);
        return stocks.Count;
    }

    private void EnsureMissingLedgerBucketsExist(
        List<InventoryStock> stocks,
        List<InventoryTransaction> ledgerTransactions,
        DateTime now)
    {
        foreach (var txGroup in ledgerTransactions.GroupBy(t => (t.WarehouseId, t.ProductId)))
        {
            var existingGroupStocks = stocks
                .Where(s => s.WarehouseId == txGroup.Key.WarehouseId && s.ProductId == txGroup.Key.ProductId)
                .ToList();
            var enforceHu = txGroup.Any(t => t.HandlingUnitId.HasValue) &&
                            existingGroupStocks.Select(b => b.HandlingUnitId).Distinct().Count() > 1;
            var enforceOwner = txGroup.Any(t => t.OwnerId.HasValue) &&
                               existingGroupStocks.Select(b => b.OwnerId).Distinct().Count() > 1;

            var candidateGrains = new List<(int TenantId, int WarehouseId, int? LocationId, int ProductId, int? LotId, int? SerialId, int? HandlingUnitId, InventoryStatus Status, int? OwnerId)>();
            foreach (var tx in txGroup)
            {
                if (tx.TransactionType == TransactionType.TransferAdjustment || tx.TransactionType == TransactionType.Reversal)
                    continue;

                var huId = enforceHu ? tx.HandlingUnitId : null;
                var ownId = enforceOwner ? tx.OwnerId : null;

                if (tx.TransactionType == TransactionType.Move)
                {
                    if (tx.LocationId.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.LocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.InventoryStatus, ownId));
                    if (tx.FromLocationId.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.FromLocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.FromInventoryStatus ?? tx.InventoryStatus, ownId));
                    if (tx.ToLocationId.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.ToLocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.ToInventoryStatus ?? tx.InventoryStatus, ownId));
                }
                else if (tx.TransactionType == TransactionType.StatusChange)
                {
                    if (tx.LocationId.HasValue && tx.FromInventoryStatus.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.LocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.FromInventoryStatus.Value, ownId));
                    if (tx.LocationId.HasValue && tx.ToInventoryStatus.HasValue)
                        candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.LocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.ToInventoryStatus.Value, ownId));
                }
                else if (tx.LocationId.HasValue)
                {
                    candidateGrains.Add((tx.TenantId, tx.WarehouseId, tx.LocationId, tx.ProductId, tx.LotId, tx.SerialId, huId, tx.InventoryStatus, ownId));
                }
            }

            foreach (var candidate in candidateGrains.Distinct())
            {
                var exists = stocks.Any(s =>
                    s.TenantId == candidate.TenantId &&
                    s.WarehouseId == candidate.WarehouseId &&
                    s.LocationId == candidate.LocationId &&
                    s.ProductId == candidate.ProductId &&
                    s.LotId == candidate.LotId &&
                    s.SerialId == candidate.SerialId &&
                    (!enforceHu || s.HandlingUnitId == candidate.HandlingUnitId) &&
                    s.Status == candidate.Status &&
                    (!enforceOwner || s.OwnerId == candidate.OwnerId));

                if (!exists)
                {
                    var newStock = new InventoryStock
                    {
                        TenantId = candidate.TenantId,
                        WarehouseId = candidate.WarehouseId,
                        LocationId = candidate.LocationId,
                        ProductId = candidate.ProductId,
                        LotId = candidate.LotId,
                        SerialId = candidate.SerialId,
                        HandlingUnitId = candidate.HandlingUnitId,
                        Status = candidate.Status,
                        OwnerId = candidate.OwnerId,
                        Quantity = 0m,
                        ReservedQuantity = 0m,
                        AllocatedQuantity = 0m,
                        LockedQuantity = 0m,
                        LastUpdated = now
                    };
                    newStock.RefreshGrainHash();
                    context.InventoryStocks.Add(newStock);
                    stocks.Add(newStock);
                }
            }
        }
    }

    private static void RecomputeStockBuckets(
        List<InventoryStock> stocks,
        List<InventoryTransaction> ledgerTransactions,
        List<StockReservation> activeReservations,
        List<StockAllocation> activeAllocations,
        List<InventoryLock> activeLocks)
    {
        foreach (var group in stocks.GroupBy(x => (x.WarehouseId, x.ProductId)))
        {
            var groupTxs = ledgerTransactions
                .Where(t => t.WarehouseId == group.Key.WarehouseId && t.ProductId == group.Key.ProductId)
                .ToList();

            var totalReservedTarget = activeReservations
                .Where(r => r.WarehouseId == group.Key.WarehouseId && r.ProductId == group.Key.ProductId)
                .Sum(r => Math.Max(0m, r.Quantity - r.ConsumedQuantity - r.ReleasedQuantity));

            var enforceHuDimension = groupTxs.Any(t => t.HandlingUnitId.HasValue) &&
                                     group.Select(b => b.HandlingUnitId).Distinct().Count() > 1;
            var enforceOwnerDimension = groupTxs.Any(t => t.OwnerId.HasValue) &&
                                        group.Select(b => b.OwnerId).Distinct().Count() > 1;

            decimal allocatedSum = 0m;
            foreach (var bucket in group)
            {
                var bucketTxs = groupTxs.Where(t =>
                    t.TenantId == bucket.TenantId &&
                    t.WarehouseId == bucket.WarehouseId &&
                    t.ProductId == bucket.ProductId &&
                    t.LotId == bucket.LotId &&
                    t.SerialId == bucket.SerialId &&
                    (!enforceHuDimension || t.HandlingUnitId == bucket.HandlingUnitId) &&
                    (!enforceOwnerDimension || t.OwnerId == bucket.OwnerId) &&
                    (t.LocationId == bucket.LocationId ||
                     (t.TransactionType == TransactionType.Move &&
                      (t.FromLocationId == bucket.LocationId || t.ToLocationId == bucket.LocationId))) &&
                    (t.InventoryStatus == bucket.Status ||
                     t.FromInventoryStatus == bucket.Status ||
                     t.ToInventoryStatus == bucket.Status)).ToList();

                var hasExplicitMoveLegsForBucket = bucketTxs.Any(t =>
                    t.TransactionType == TransactionType.Move &&
                    t.LocationId == bucket.LocationId);

                if (bucketTxs.Count > 0)
                {
                    var ledgerQty = ComputeBucketLedgerQuantity(bucket, bucketTxs, groupTxs, hasExplicitMoveLegsForBucket);
                    if (ledgerQty >= 0m)
                    {
                        bucket.Quantity = ledgerQty;
                    }
                    else
                    {
                        throw new UnprocessableDomainException(
                            $"Phát hiện số dư sổ cái 9 chiều bị âm ({ledgerQty:0.0000}) tại Kho #{bucket.WarehouseId}, Sản phẩm #{bucket.ProductId}, Vị trí #{bucket.LocationId}, Lô #{bucket.LotId?.ToString() ?? "NULL"}.",
                            "INV_NEGATIVE_LEDGER_BUCKET_REBUILD");
                    }
                }
                else if (groupTxs.Count > 0 || bucket.Quantity < 0m)
                {
                    bucket.Quantity = 0m;
                }

                var bucketAllocated = activeAllocations
                    .Where(a => a.WarehouseId == bucket.WarehouseId &&
                                a.ProductId == bucket.ProductId &&
                                a.LocationId == bucket.LocationId &&
                                a.InventoryStatus == bucket.Status &&
                                a.LotId == bucket.LotId &&
                                a.SerialId == bucket.SerialId &&
                                (!a.HandlingUnitId.HasValue || a.HandlingUnitId == bucket.HandlingUnitId) &&
                                (!a.OwnerId.HasValue || a.OwnerId == bucket.OwnerId))
                    .Sum(a => a.Quantity);

                var isLocked = activeLocks.Any(l =>
                    l.WarehouseId == bucket.WarehouseId &&
                    (!l.LocationId.HasValue || l.LocationId == bucket.LocationId) &&
                    (!l.ProductId.HasValue || l.ProductId == bucket.ProductId) &&
                    (!l.InventoryStatus.HasValue || l.InventoryStatus == bucket.Status) &&
                    (!l.LotId.HasValue || l.LotId == bucket.LotId) &&
                    (!l.SerialId.HasValue || l.SerialId == bucket.SerialId));

                bucket.AllocatedQuantity = Math.Min(bucket.Quantity, bucketAllocated);
                bucket.LockedQuantity = isLocked ? bucket.Quantity : 0m;
                bucket.ReservedQuantity = bucket.AllocatedQuantity;
                allocatedSum += bucket.AllocatedQuantity;
            }

            var remainingUnallocatedReserve = Math.Max(0m, totalReservedTarget - allocatedSum);
            foreach (var bucket in group
                .Where(b => b.Status == InventoryStatus.Available && b.LockedQuantity == 0)
                .OrderBy(b => b.Location?.LocationType == WarehouseLocationType.Receiving ? 1 : 0)
                .ThenBy(b => b.LocationId)
                .ThenBy(b => b.Id))
            {
                if (remainingUnallocatedReserve <= 0) break;
                var freeCapacity = Math.Max(0m, bucket.Quantity - bucket.ReservedQuantity);
                var add = Math.Min(remainingUnallocatedReserve, freeCapacity);
                if (add > 0)
                {
                    bucket.ReservedQuantity += add;
                    remainingUnallocatedReserve -= add;
                }
            }
        }
    }

    private static decimal ComputeBucketLedgerQuantity(
        InventoryStock bucket,
        List<InventoryTransaction> bucketTxs,
        List<InventoryTransaction> groupTxs,
        bool hasExplicitMoveLegsForBucket)
    {
        decimal ledgerQty = 0m;
        foreach (var tx in bucketTxs)
        {
            if (tx.TransactionType == TransactionType.TransferAdjustment || tx.TransactionType == TransactionType.Reversal)
                continue;
            if (tx.TransactionType == TransactionType.StatusChange)
            {
                if (tx.LocationId != bucket.LocationId) continue;
                if (tx.ToInventoryStatus == bucket.Status && tx.Quantity > 0)
                    ledgerQty += tx.Quantity;
                else if (tx.FromInventoryStatus == bucket.Status)
                    ledgerQty -= Math.Abs(tx.Quantity);
            }
            else if (tx.TransactionType == TransactionType.Move)
            {
                if (tx.InventoryStatus != bucket.Status &&
                    tx.FromInventoryStatus != bucket.Status &&
                    tx.ToInventoryStatus != bucket.Status)
                    continue;

                if (tx.LocationId == bucket.LocationId)
                {
                    if (tx.Quantity < 0m)
                    {
                        ledgerQty += tx.Quantity;
                    }
                    else if (tx.FromLocationId == bucket.LocationId &&
                             tx.ToLocationId.HasValue &&
                             tx.ToLocationId != bucket.LocationId &&
                             !groupTxs.Any(other =>
                                 other.Id != tx.Id &&
                                 other.TransactionType == TransactionType.Move &&
                                 other.LocationId == tx.ToLocationId &&
                                 other.FromLocationId == tx.FromLocationId &&
                                 other.ToLocationId == tx.ToLocationId &&
                                 Math.Abs(other.Quantity) == Math.Abs(tx.Quantity)))
                    {
                        ledgerQty -= tx.Quantity;
                    }
                    else
                    {
                        ledgerQty += tx.Quantity;
                    }
                }
                else if (!hasExplicitMoveLegsForBucket)
                {
                    if (tx.FromLocationId == bucket.LocationId)
                        ledgerQty -= Math.Abs(tx.Quantity);
                    else if (tx.ToLocationId == bucket.LocationId)
                        ledgerQty += Math.Abs(tx.Quantity);
                }
            }
            else if (tx.LocationId == bucket.LocationId && tx.InventoryStatus == bucket.Status)
            {
                ledgerQty += tx.Quantity < 0m ? tx.Quantity : tx.TransactionType.ApplySign(tx.Quantity);
            }
        }

        return ledgerQty;
    }
}
