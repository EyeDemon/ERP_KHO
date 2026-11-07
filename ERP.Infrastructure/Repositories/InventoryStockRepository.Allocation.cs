using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Queries;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public partial class InventoryStockRepository
    {
        public async Task<bool> TryConsumeReservationAtLocationAsync(
            int productId,
            int warehouseId,
            int locationId,
            decimal quantity,
            CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            await AcquireAllocationAndStockLocksAsync(productId, warehouseId, cancellationToken);
            var todayUtc = DateTime.UtcNow.Date;
            var candidates = await _dbSet.AsNoTracking()
                .Include(x => x.Location)
                .Include(x => x.Lot)
                .UnlockedAt(_context, DateTime.UtcNow)
                .Where(x => x.ProductId == productId &&
                            x.WarehouseId == warehouseId &&
                            x.LocationId == locationId &&
                            (x.StatusDefinition != null ? x.StatusDefinition.IsShippable : x.Status == InventoryStatus.Available) &&
                            (x.Location == null || (x.Location.IsActive && !x.Location.IsBlocked && x.Location.LocationType != WarehouseLocationType.Receiving && (x.Location.IsPickable || x.Location.LocationType == WarehouseLocationType.Storage))) &&
                            (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value.Date >= todayUtc) &&
                            x.ReservedQuantity >= quantity &&
                            x.Quantity >= quantity)
                .Select(x => new { x.Status, x.LotId, x.SerialId })
                .Take(2)
                .ToListAsync(cancellationToken);
            if (candidates.Count != 1) return false;
            var bucket = candidates[0];
            return await TryConsumeReservationAtBucketAsync(
                productId, warehouseId, locationId, bucket.Status, bucket.LotId, bucket.SerialId, quantity, cancellationToken);
        }

        public async Task<bool> TryConsumeReservationAtBucketAsync(
            int productId,
            int warehouseId,
            int locationId,
            InventoryStatus status,
            int? lotId,
            int? serialId,
            decimal quantity,
            CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            try
            {
                await AcquireAllocationAndStockLocksAsync(productId, warehouseId, cancellationToken);
                var todayUtc = DateTime.UtcNow.Date;
                var now = DateTime.UtcNow;
                if (!_context.Database.IsRelational())
                {
                    var tracked = await _dbSet
                        .Include(x => x.Location)
                        .Include(x => x.Lot)
                        .FirstOrDefaultAsync(x =>
                            x.ProductId == productId &&
                            x.WarehouseId == warehouseId &&
                            x.LocationId == locationId &&
                            x.Status == status &&
                            x.LotId == lotId &&
                            x.SerialId == serialId &&
                            (x.Location == null || (x.Location.IsActive && !x.Location.IsBlocked && x.Location.LocationType != WarehouseLocationType.Receiving)) &&
                            (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value.Date >= todayUtc) &&
                            x.ReservedQuantity >= quantity &&
                            x.Quantity >= quantity,
                            cancellationToken);
                    if (tracked is null) return false;
                    tracked.Quantity -= quantity;
                    tracked.ReservedQuantity -= quantity;
                    tracked.AllocatedQuantity = tracked.AllocatedQuantity >= quantity ? tracked.AllocatedQuantity - quantity : 0m;
                    tracked.LastUpdated = now;
                    await _context.SaveChangesAsync(cancellationToken);
                    return true;
                }

                var affected = await _dbSet
                    .UnlockedAt(_context, now)
                    .Where(x => x.ProductId == productId &&
                                x.WarehouseId == warehouseId &&
                                x.LocationId == locationId &&
                                x.Status == status &&
                                x.LotId == lotId &&
                                x.SerialId == serialId &&
                                (x.StatusDefinition != null ? x.StatusDefinition.IsShippable : x.Status == InventoryStatus.Available) &&
                                x.Location != null &&
                                x.Location.IsActive &&
                                !x.Location.IsBlocked &&
                                x.Location.LocationType != WarehouseLocationType.Receiving &&
                                (x.Location.IsPickable || x.Location.LocationType == WarehouseLocationType.Storage) &&
                                (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value.Date >= todayUtc) &&
                                x.ReservedQuantity >= quantity &&
                                x.Quantity >= quantity)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.Quantity, x => x.Quantity - quantity)
                        .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - quantity)
                        .SetProperty(x => x.AllocatedQuantity, x => x.AllocatedQuantity >= quantity ? x.AllocatedQuantity - quantity : 0m)
                        .SetProperty(x => x.LastUpdated, now), cancellationToken);
                return affected == 1;
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                throw new ERP.Domain.Exceptions.DeadlockException(
                    "Giao dịch tiêu thụ giữ hàng tại bucket bị deadlock.",
                    exception);
            }
        }

        public async Task<bool> TryReleaseReservationAsync(
            int productId,
            int warehouseId,
            decimal quantity,
            int? excludedReservationId,
            CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            try
            {
                return await AllocateAsync(
                    productId,
                    warehouseId,
                    quantity,
                    false,
                    true,
                    cancellationToken,
                    releaseOnly: true,
                    excludedReservationId: excludedReservationId);
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch giải phóng giữ hàng bị deadlock.", exception);
            }
        }

        private async Task<bool> AllocateAsync(
            int productId,
            int warehouseId,
            decimal quantity,
            bool reserve,
            bool useReserved,
            CancellationToken token,
            bool releaseOnly = false,
            int? excludedReservationId = null)
        {
            // Serialize allocations of the same product/warehouse before
            // reading stock candidates. Document locks alone cannot protect
            // two independent transfers spending one source stock bucket.
            await AcquireAllocationAndStockLocksAsync(productId, warehouseId, token);

            var todayUtc = DateTime.UtcNow.Date;
            var eligible = _dbSet.AsNoTracking()
                .Include(s => s.Location)
                .Include(s => s.Lot)
                .Where(s => s.ProductId == productId &&
                            s.WarehouseId == warehouseId);

            if (releaseOnly)
            {
                eligible = eligible.Where(s => s.ReservedQuantity > 0);
                // Releasing a commitment is allowed even while stock or location is locked/blocked.
            }
            else
            {
                eligible = eligible
                    .UnlockedAt(_context, DateTime.UtcNow)
                    .Where(s => s.Location == null ||
                                (s.Location.IsActive &&
                                 !s.Location.IsBlocked &&
                                 s.Location.LocationType != WarehouseLocationType.Receiving &&
                                 (s.Location.IsPickable ||
                                  s.Location.LocationType == WarehouseLocationType.Storage)))
                    .Where(s => s.Lot == null || !s.Lot.ExpiryDate.HasValue || s.Lot.ExpiryDate.Value.Date >= todayUtc);
            }

            if (releaseOnly)
            {
                // Already filtered by ReservedQuantity > 0 above.
            }
            else if (reserve)
                eligible = eligible.Where(s => s.StatusDefinition != null ? s.StatusDefinition.IsReservable : s.Status == InventoryStatus.Available);
            else if (useReserved)
                eligible = eligible.Where(s => s.StatusDefinition != null ? s.StatusDefinition.IsShippable : s.Status == InventoryStatus.Available);
            else
                eligible = eligible.Where(s => s.StatusDefinition != null ? s.StatusDefinition.IsAvailable : s.Status == InventoryStatus.Available);

            var rows = await eligible
                .OrderBy(s => s.Lot != null && s.Lot.ExpiryDate.HasValue ? 0 : 1)
                .ThenBy(s => s.Lot == null ? null : s.Lot.ExpiryDate)
                .ThenBy(s => s.Lot == null ? DateTime.MaxValue : s.Lot.ReceivedAt)
                .ThenBy(s => s.LocationId)
                .ThenBy(s => s.Id)
                .Select(s => new
                {
                    s.Id,
                    s.Quantity,
                    s.ReservedQuantity,
                    s.AllocatedQuantity,
                    s.LockedQuantity,
                    CommittedAllocationQuantity = useReserved
                        ? (_context.StockAllocations
                            .Where(a => a.WarehouseId == s.WarehouseId &&
                                        a.ProductId == s.ProductId &&
                                        a.LocationId == s.LocationId &&
                                        a.InventoryStatus == s.Status &&
                                        a.LotId == s.LotId &&
                                        a.SerialId == s.SerialId &&
                                        (!excludedReservationId.HasValue || a.ReservationId != excludedReservationId.Value) &&
                                        (a.Status == StockAllocationStatus.Active ||
                                         a.Status == StockAllocationStatus.Picking ||
                                         a.Status == StockAllocationStatus.Picked))
                            .Sum(a => (decimal?)a.Quantity) ?? 0m)
                        : 0m
                })
                .ToListAsync(token);

            var capacity = rows.Sum(stock => useReserved
                ? Math.Max(0m, stock.ReservedQuantity - stock.CommittedAllocationQuantity)
                : Math.Max(0m, stock.Quantity - Math.Max(stock.ReservedQuantity, stock.AllocatedQuantity) - stock.LockedQuantity));
            if (capacity < quantity) return false;

            var remaining = quantity;
            var isRelational = _context.Database.IsRelational();
            foreach (var stock in rows)
            {
                var availableInRow = useReserved
                    ? Math.Max(0m, stock.ReservedQuantity - stock.CommittedAllocationQuantity)
                    : stock.Quantity - Math.Max(stock.ReservedQuantity, stock.AllocatedQuantity) - stock.LockedQuantity;
                var take = Math.Min(remaining, availableInRow);
                if (take <= 0) continue;

                var now = DateTime.UtcNow;
                int affected;
                if (isRelational)
                {
                    var query = _dbSet.Where(x => x.Id == stock.Id);
                    if (!releaseOnly)
                        query = query.UnlockedAt(_context, now);
                    if (useReserved)
                    {
                        query = query.Where(x =>
                            x.ReservedQuantity -
                            (_context.StockAllocations
                                .Where(a => a.WarehouseId == x.WarehouseId &&
                                            a.ProductId == x.ProductId &&
                                            a.LocationId == x.LocationId &&
                                            a.InventoryStatus == x.Status &&
                                            a.LotId == x.LotId &&
                                            a.SerialId == x.SerialId &&
                                            (!excludedReservationId.HasValue || a.ReservationId != excludedReservationId.Value) &&
                                            (a.Status == StockAllocationStatus.Active ||
                                             a.Status == StockAllocationStatus.Picking ||
                                             a.Status == StockAllocationStatus.Picked))
                                .Sum(a => (decimal?)a.Quantity) ?? 0m) >= take &&
                            (releaseOnly || x.Quantity >= take));
                    }
                    else
                    {
                        query = query.Where(x => x.Quantity - (x.ReservedQuantity > x.AllocatedQuantity ? x.ReservedQuantity : x.AllocatedQuantity) - x.LockedQuantity >= take);
                    }

                    affected = reserve
                        ? await query.ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity + take)
                            .SetProperty(x => x.LastUpdated, now), token)
                        : useReserved
                            ? releaseOnly
                                ? await query.ExecuteUpdateAsync(s => s
                                    .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - take)
                                    .SetProperty(x => x.LastUpdated, now), token)
                                : await query.ExecuteUpdateAsync(s => s
                                    .SetProperty(x => x.Quantity, x => x.Quantity - take)
                                    .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - take)
                                    .SetProperty(x => x.LastUpdated, now), token)
                            : await query.ExecuteUpdateAsync(s => s
                                .SetProperty(x => x.Quantity, x => x.Quantity - take)
                                .SetProperty(x => x.LastUpdated, now), token);

                    if (affected == 1)
                    {
                        var trackedEntry = _context.ChangeTracker.Entries<InventoryStock>()
                            .FirstOrDefault(e => e.Entity.Id == stock.Id);
                        if (trackedEntry is not null)
                        {
                            var wasUnchanged = trackedEntry.State == EntityState.Unchanged;
                            var tracked = trackedEntry.Entity;
                            if (reserve) tracked.ReservedQuantity += take;
                            else if (useReserved)
                            {
                                tracked.ReservedQuantity -= take;
                                if (!releaseOnly) tracked.Quantity -= take;
                            }
                            else tracked.Quantity -= take;
                            tracked.LastUpdated = now;
                            if (wasUnchanged)
                            {
                                trackedEntry.OriginalValues.SetValues(tracked);
                                trackedEntry.State = EntityState.Unchanged;
                            }
                        }
                    }
                }
                else
                {
                    var tracked = await _dbSet.FirstOrDefaultAsync(x => x.Id == stock.Id, token);
                    if (tracked is null) return false;
                    if (!useReserved && tracked.Quantity - Math.Max(tracked.ReservedQuantity, tracked.AllocatedQuantity) - tracked.LockedQuantity < take)
                        return false;
                    if (reserve) tracked.ReservedQuantity += take;
                    else if (useReserved)
                    {
                        if (tracked.ReservedQuantity < take || (!releaseOnly && tracked.Quantity < take)) return false;
                        tracked.ReservedQuantity -= take;
                        if (!releaseOnly) tracked.Quantity -= take;
                    }
                    else tracked.Quantity -= take;
                    tracked.LastUpdated = now;
                    await _context.SaveChangesAsync(token);
                    affected = 1;
                }

                if (affected != 1) return false;
                remaining -= take;
                if (remaining == 0) return true;
            }
            return false;
        }
    }
}
