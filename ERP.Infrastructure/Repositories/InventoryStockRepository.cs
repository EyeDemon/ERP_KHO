using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class InventoryStockRepository : Repository<InventoryStock>, IInventoryStockRepository
    {
        public InventoryStockRepository(ErpKhoDbContext context) : base(context)
        {
        }

        public async Task<InventoryStock?> GetByProductAndWarehouseAsync(int productId, int warehouseId)
        {
            await AcquireAllocationAndStockLocksAsync(productId, warehouseId);
            var tenantId = _context.CurrentTenantId;
            var source = _context.Database.IsRelational() && _context.Database.IsSqlServer() && _context.Database.CurrentTransaction != null
                ? _dbSet.FromSqlInterpolated($"SELECT * FROM dbo.InventoryStocks WITH (INDEX(IX_InventoryStocks_CanonicalBucket), UPDLOCK, ROWLOCK, REPEATABLEREAD) WHERE TenantId = {tenantId} AND ProductId = {productId} AND WarehouseId = {warehouseId} AND Status = {(int)InventoryStatus.Available}")
                : _dbSet.AsQueryable();
            return await source
                .Include(s => s.Location)
                .Include(s => s.Product)
                    .ThenInclude(p => p.Unit)
                .Where(s => s.ProductId == productId && s.WarehouseId == warehouseId && s.Status == InventoryStatus.Available && (!s.LocationId.HasValue || s.Location == null || (s.Location.IsActive && !s.Location.IsBlocked && s.Location.LocationType != WarehouseLocationType.Receiving && (s.Location.IsPickable || s.Location.LocationType == WarehouseLocationType.Storage))))
                .OrderBy(s => s.LotId.HasValue)
                .ThenBy(s => s.SerialId.HasValue)
                .ThenByDescending(s => s.Quantity - (s.ReservedQuantity > s.AllocatedQuantity ? s.ReservedQuantity : s.AllocatedQuantity) - s.LockedQuantity)
                .FirstOrDefaultAsync();
        }

        public async Task<InventoryStock?> GetByProductWarehouseAndStatusAsync(int productId, int warehouseId, InventoryStatus status)
        {
            await AcquireAllocationAndStockLocksAsync(productId, warehouseId);
            var tenantId = _context.CurrentTenantId;
            var source = _context.Database.IsRelational() && _context.Database.IsSqlServer() && _context.Database.CurrentTransaction != null
                ? _dbSet.FromSqlInterpolated($"SELECT * FROM dbo.InventoryStocks WITH (INDEX(IX_InventoryStocks_CanonicalBucket), UPDLOCK, ROWLOCK, REPEATABLEREAD) WHERE TenantId = {tenantId} AND ProductId = {productId} AND WarehouseId = {warehouseId} AND Status = {(int)status}")
                : _dbSet.AsQueryable();
            return await source
                .Include(s => s.Location)
                .Where(s => s.ProductId == productId && s.WarehouseId == warehouseId && s.Status == status && (!s.LocationId.HasValue || (s.Location != null && s.Location.IsActive && !s.Location.IsBlocked && (status != InventoryStatus.Available || s.Location.IsPickable))))
                .OrderBy(s => s.LotId.HasValue)
                .ThenBy(s => s.SerialId.HasValue)
                .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                .FirstOrDefaultAsync();
        }

        public async Task<InventoryStock?> GetByProductWarehouseStatusAndLocationAsync(int productId, int warehouseId, InventoryStatus status, int locationId)
        {
            await AcquireAllocationAndStockLocksAsync(productId, warehouseId);
            var tenantId = _context.CurrentTenantId;
            var source = _context.Database.IsRelational() && _context.Database.IsSqlServer() && _context.Database.CurrentTransaction != null
                ? _dbSet.FromSqlInterpolated($"SELECT * FROM dbo.InventoryStocks WITH (INDEX(IX_InventoryStocks_CanonicalBucket), UPDLOCK, ROWLOCK, REPEATABLEREAD) WHERE TenantId = {tenantId} AND ProductId = {productId} AND WarehouseId = {warehouseId} AND Status = {(int)status} AND LocationId = {locationId} AND LotId IS NULL AND SerialId IS NULL")
                : _dbSet.AsQueryable();
            return await source.FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId && s.Status == status && s.LocationId == locationId && s.LotId == null && s.SerialId == null);
        }

        public Task AcquireAllocationLocksAsync(int warehouseId, IEnumerable<int> productIds, CancellationToken cancellationToken = default) =>
            _context.AcquireInventoryAllocationLocksAsync(warehouseId, productIds, cancellationToken);

        private Task AcquireAllocationAndStockLocksAsync(int productId, int warehouseId, CancellationToken token = default) =>
            _context.AcquireInventoryAllocationLocksAsync([(warehouseId, productId)], token);

        public async Task<int?> GetLegacyAdjustmentLocationIdAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            await _context.WarehouseLocations.Where(x => x.WarehouseId == warehouseId && x.Code == "LEGACY" && x.IsSystemManaged)
                .Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);

        public Task<bool> IsLockedAsync(
            int warehouseId,
            int productId,
            int? locationId = null,
            int? lotId = null,
            int? serialId = null,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            return _context.InventoryLocks
                .EffectiveAt(now)
                .AnyAsync(x =>
                    x.WarehouseId == warehouseId &&
                    (!x.LocationId.HasValue || x.LocationId == locationId) &&
                    (!x.ProductId.HasValue || x.ProductId == productId) &&
                    (!x.InventoryStatus.HasValue || x.InventoryStatus == InventoryStatus.Available) &&
                    (!x.LotId.HasValue || x.LotId == lotId) &&
                    (!x.SerialId.HasValue || x.SerialId == serialId),
                    cancellationToken);
        }

        public async Task<bool> TryDecreaseStockAsync(
            int productId,
            int warehouseId,
            decimal quantity,
            CancellationToken cancellationToken = default)
        {
            if (quantity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
            }

            try
            {
                return await AllocateAsync(productId, warehouseId, quantity, false, false, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                throw new ERP.Domain.Exceptions.DeadlockException(
                    "Giao dịch cập nhật tồn kho bị deadlock.",
                    exception);
            }

        }

        public Task<decimal> GetAvailableQuantityAsync(int productId, int warehouseId, CancellationToken cancellationToken = default)
        {
            var todayUtc = DateTime.UtcNow.Date;
            return _dbSet.AsNoTracking()
                .Include(s => s.Location)
                .Include(s => s.Lot)
                .UnlockedAt(_context, DateTime.UtcNow)
                .Where(s => s.ProductId == productId && s.WarehouseId == warehouseId &&
                    (s.StatusDefinition != null ? s.StatusDefinition.IsReservable : s.Status == InventoryStatus.Available) &&
                    (s.Location == null || (s.Location.IsActive && !s.Location.IsBlocked && s.Location.LocationType != WarehouseLocationType.Receiving && (s.Location.IsPickable || s.Location.LocationType == WarehouseLocationType.Storage))) &&
                    (s.Lot == null || !s.Lot.ExpiryDate.HasValue || s.Lot.ExpiryDate.Value.Date >= todayUtc))
                .SumAsync(s => s.Quantity - (s.ReservedQuantity > s.AllocatedQuantity ? s.ReservedQuantity : s.AllocatedQuantity) - s.LockedQuantity, cancellationToken);
        }

        public async Task<bool> TryReserveAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            try
            {
                return await AllocateAsync(productId, warehouseId, quantity, true, false, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch cập nhật giữ hàng bị deadlock.", exception);
            }
        }

        public async Task<bool> TryConsumeReservationAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            try
            {
                return await AllocateAsync(productId, warehouseId, quantity, false, true, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205) { throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch tiêu thụ giữ hàng bị deadlock.", exception); }
        }

        public async Task<IReadOnlyList<InventoryStockConsumption>> ConsumeReservationWithBreakdownAsync(
            int productId,
            int warehouseId,
            decimal quantity,
            CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            try
            {
                await AcquireAllocationAndStockLocksAsync(productId, warehouseId, cancellationToken);
                var todayUtc = DateTime.UtcNow.Date;
                var rows = await _dbSet.AsNoTracking()
                    .Include(x => x.Location)
                    .Include(x => x.Lot)
                    .UnlockedAt(_context, DateTime.UtcNow)
                    .Where(x => x.ProductId == productId && x.WarehouseId == warehouseId &&
                        (x.StatusDefinition != null ? x.StatusDefinition.IsShippable : x.Status == InventoryStatus.Available) &&
                        (x.Location == null || (x.Location.IsActive && !x.Location.IsBlocked && x.Location.LocationType != WarehouseLocationType.Receiving && (x.Location.IsPickable || x.Location.LocationType == WarehouseLocationType.Storage))) &&
                        (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value.Date >= todayUtc))
                    .OrderBy(x => x.Lot != null && x.Lot.ExpiryDate.HasValue ? 0 : 1)
                    .ThenBy(x => x.Lot == null ? null : x.Lot.ExpiryDate)
                    .ThenBy(x => x.Lot == null ? DateTime.MaxValue : x.Lot.ReceivedAt)
                    .ThenBy(x => x.LocationId)
                    .ThenBy(x => x.Id)
                    .Select(x => new
                    {
                        x.Id,
                        x.LocationId,
                        x.Status,
                        x.LotId,
                        x.SerialId,
                        x.Quantity,
                        x.ReservedQuantity,
                        CommittedAllocationQuantity = _context.StockAllocations
                            .Where(a => a.WarehouseId == x.WarehouseId &&
                                        a.ProductId == x.ProductId &&
                                        a.LocationId == x.LocationId &&
                                        a.InventoryStatus == x.Status &&
                                        a.LotId == x.LotId &&
                                        a.SerialId == x.SerialId &&
                                        (a.Status == StockAllocationStatus.Active ||
                                         a.Status == StockAllocationStatus.Picking ||
                                         a.Status == StockAllocationStatus.Picked))
                            .Sum(a => (decimal?)a.Quantity) ?? 0m
                    })
                    .ToListAsync(cancellationToken);

                if (rows.Sum(x => Math.Max(0m, x.ReservedQuantity - x.CommittedAllocationQuantity)) < quantity)
                    return Array.Empty<InventoryStockConsumption>();

                var remaining = quantity;
                var consumed = new List<InventoryStockConsumption>();
                var now = DateTime.UtcNow;
                var isRelational = _context.Database.IsRelational();
                foreach (var row in rows)
                {
                    var unallocatedReserved = Math.Max(0m, row.ReservedQuantity - row.CommittedAllocationQuantity);
                    var take = Math.Min(remaining, unallocatedReserved);
                    if (take <= 0) continue;

                    int affected;
                    if (isRelational)
                    {
                        affected = await _dbSet
                            .UnlockedAt(_context, now)
                            .Where(x => x.Id == row.Id &&
                                        x.Quantity >= take &&
                                        x.ReservedQuantity -
                                        (_context.StockAllocations
                                            .Where(a => a.WarehouseId == x.WarehouseId &&
                                                        a.ProductId == x.ProductId &&
                                                        a.LocationId == x.LocationId &&
                                                        a.InventoryStatus == x.Status &&
                                                        a.LotId == x.LotId &&
                                                        a.SerialId == x.SerialId &&
                                                        (a.Status == StockAllocationStatus.Active ||
                                                         a.Status == StockAllocationStatus.Picking ||
                                                         a.Status == StockAllocationStatus.Picked))
                                            .Sum(a => (decimal?)a.Quantity) ?? 0m) >= take)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(x => x.Quantity, x => x.Quantity - take)
                                .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - take)
                                .SetProperty(x => x.LastUpdated, now), cancellationToken);
                        if (affected == 1)
                        {
                            var trackedEntry = _context.ChangeTracker.Entries<InventoryStock>()
                                .FirstOrDefault(e => e.Entity.Id == row.Id);
                            if (trackedEntry is not null)
                            {
                                var wasUnchanged = trackedEntry.State == EntityState.Unchanged;
                                var tracked = trackedEntry.Entity;
                                tracked.Quantity -= take;
                                tracked.ReservedQuantity -= take;
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
                        var tracked = await _dbSet.FirstOrDefaultAsync(x => x.Id == row.Id, cancellationToken);
                        if (tracked is null || tracked.Quantity < take || tracked.ReservedQuantity < take)
                            affected = 0;
                        else
                        {
                            tracked.Quantity -= take;
                            tracked.ReservedQuantity -= take;
                            tracked.LastUpdated = now;
                            await _context.SaveChangesAsync(cancellationToken);
                            affected = 1;
                        }
                    }
                    if (affected != 1)
                        throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu giữ hàng chưa Allocation đã thay đổi trong lúc tiêu thụ reservation.");

                    consumed.Add(new InventoryStockConsumption(
                        row.LocationId ?? 1, take, row.Status, row.LotId, row.SerialId));
                    remaining -= take;
                    if (remaining == 0) return consumed;
                }

                throw new ERP.Domain.Exceptions.ConcurrencyException("Không thể xác định đầy đủ bucket tồn kho chưa Allocation để tiêu thụ.");
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch tiêu thụ giữ hàng bị deadlock.", exception);
            }
        }

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
