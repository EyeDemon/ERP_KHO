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
            return await _dbSet
                .Include(s => s.Location)
                .Include(s => s.Product)
                    .ThenInclude(p => p.Unit)
                .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId && s.Status == InventoryStatus.Available && (!s.LocationId.HasValue || (s.Location != null && s.Location.IsActive && !s.Location.IsBlocked && s.Location.IsPickable)));
        }

        public Task<InventoryStock?> GetByProductWarehouseAndStatusAsync(int productId, int warehouseId, InventoryStatus status) =>
            _dbSet.Include(s=>s.Location).FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId && s.Status == status && (!s.LocationId.HasValue || (s.Location != null && s.Location.IsActive && !s.Location.IsBlocked && (status != InventoryStatus.Available || s.Location.IsPickable))));

        public Task<InventoryStock?> GetByProductWarehouseStatusAndLocationAsync(int productId, int warehouseId, InventoryStatus status, int locationId) =>
            _dbSet.FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId && s.Status == status && s.LocationId == locationId);

        public async Task<int?> GetLegacyAdjustmentLocationIdAsync(int warehouseId, CancellationToken cancellationToken = default) =>
            await _context.WarehouseLocations.Where(x => x.WarehouseId == warehouseId && x.Code == "LEGACY" && x.IsSystemManaged)
                .Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);

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
            var today = DateTime.UtcNow.Date;
            return _dbSet.AsNoTracking()
                .UnlockedAt(_context, DateTime.UtcNow)
                .Where(x => x.ProductId == productId && x.WarehouseId == warehouseId &&
                    x.StatusDefinition.IsReservable &&
                    x.Location != null && x.Location.IsActive && !x.Location.IsBlocked && x.Location.IsPickable &&
                    (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today))
                .SumAsync(x => x.Quantity - x.ReservedQuantity, cancellationToken);
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
                var today = DateTime.UtcNow.Date;
                var rows = await _dbSet.AsNoTracking()
                    .UnlockedAt(_context, DateTime.UtcNow)
                    .Where(x => x.ProductId == productId && x.WarehouseId == warehouseId &&
                        x.StatusDefinition.IsShippable && x.LocationId.HasValue &&
                        x.Location != null && x.Location.IsActive && !x.Location.IsBlocked && x.Location.IsPickable &&
                        (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today))
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
                foreach (var row in rows)
                {
                    var unallocatedReserved = Math.Max(0m, row.ReservedQuantity - row.CommittedAllocationQuantity);
                    var take = Math.Min(remaining, unallocatedReserved);
                    if (take <= 0) continue;

                    var affected = await _dbSet
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
                    if (affected != 1)
                        throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu giữ hàng chưa Allocation đã thay đổi trong lúc tiêu thụ reservation.");

                    consumed.Add(new InventoryStockConsumption(
                        row.LocationId!.Value, take, row.Status, row.LotId, row.SerialId));
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
            var today = DateTime.UtcNow.Date;
            var candidates = await _dbSet.AsNoTracking()
                .UnlockedAt(_context, DateTime.UtcNow)
                .Where(x => x.ProductId == productId &&
                            x.WarehouseId == warehouseId &&
                            x.LocationId == locationId &&
                            x.StatusDefinition.IsShippable &&
                            x.Location != null && x.Location.IsActive && !x.Location.IsBlocked && x.Location.IsPickable &&
                            (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today) &&
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
                var today = DateTime.UtcNow.Date;
                var now = DateTime.UtcNow;
                var affected = await _dbSet
                    .UnlockedAt(_context, now)
                    .Where(x => x.ProductId == productId &&
                                x.WarehouseId == warehouseId &&
                                x.LocationId == locationId &&
                                x.Status == status &&
                                x.LotId == lotId &&
                                x.SerialId == serialId &&
                                x.StatusDefinition.IsShippable &&
                                x.Location != null &&
                                x.Location.IsActive &&
                                !x.Location.IsBlocked &&
                                x.Location.IsPickable &&
                                (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today) &&
                                x.ReservedQuantity >= quantity &&
                                x.Quantity >= quantity)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.Quantity, x => x.Quantity - quantity)
                        .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - quantity)
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
            if (_context.Database.IsSqlServer() && _context.Database.CurrentTransaction is not null)
            {
                var allocationLock = $"ERP:InventoryAllocation:{productId}:{warehouseId}";
                await _context.Database.ExecuteSqlInterpolatedAsync($@"
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock
    @Resource = {allocationLock},
    @LockMode = 'Exclusive',
    @LockOwner = 'Transaction',
    @LockTimeout = 15000;
IF @lockResult < 0
BEGIN
    DECLARE @lockMessage nvarchar(2048) =
        CONCAT(N'Không thể khóa phân bổ tồn kho. Mã khóa SQL: ', @lockResult);
    THROW 51033, @lockMessage, 1;
END;", token);
            }

            var today = DateTime.UtcNow.Date;
            var eligible = _dbSet.AsNoTracking()
                .Where(x => x.ProductId == productId &&
                            x.WarehouseId == warehouseId &&
                            x.Location != null &&
                            x.Location.IsActive &&
                            !x.Location.IsBlocked &&
                            x.Location.IsPickable);

            if (releaseOnly)
                eligible = eligible.Where(x => x.ReservedQuantity > 0);
            else
                eligible = eligible.UnlockedAt(_context, DateTime.UtcNow);

            if (releaseOnly)
            {
                // Releasing a commitment is allowed even while stock is locked.
            }
            else if (reserve)
                eligible = eligible.Where(x => x.StatusDefinition.IsReservable &&
                    (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today));
            else if (useReserved)
                eligible = eligible.Where(x => x.StatusDefinition.IsShippable &&
                    (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today));
            else
                eligible = eligible.Where(x => x.StatusDefinition.IsAvailable &&
                    (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today));

            var rows = await eligible
                .OrderBy(x => x.Lot != null && x.Lot.ExpiryDate.HasValue ? 0 : 1)
                .ThenBy(x => x.Lot == null ? null : x.Lot.ExpiryDate)
                .ThenBy(x => x.Lot == null ? DateTime.MaxValue : x.Lot.ReceivedAt)
                .ThenBy(x => x.LocationId)
                .ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Id,
                    x.Quantity,
                    x.ReservedQuantity,
                    CommittedAllocationQuantity = useReserved
                        ? (_context.StockAllocations
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
                            .Sum(a => (decimal?)a.Quantity) ?? 0m)
                        : 0m
                })
                .ToListAsync(token);

            var capacity = rows.Sum(x => useReserved
                ? Math.Max(0m, x.ReservedQuantity - x.CommittedAllocationQuantity)
                : x.Quantity - x.ReservedQuantity);
            if (capacity < quantity) return false;

            var remaining = quantity;
            foreach (var row in rows)
            {
                var available = useReserved
                    ? Math.Max(0m, row.ReservedQuantity - row.CommittedAllocationQuantity)
                    : row.Quantity - row.ReservedQuantity;
                var take = Math.Min(remaining, available);
                if (take <= 0) continue;

                var query = _dbSet.Where(x => x.Id == row.Id);
                if (!releaseOnly)
                    query = query.UnlockedAt(_context, DateTime.UtcNow);
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
                    query = query.Where(x => x.Quantity - x.ReservedQuantity >= take);
                }

                var now = DateTime.UtcNow;
                var affected = reserve
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

                if (affected != 1) return false;
                remaining -= take;
                if (remaining == 0) return true;
            }
            return false;
        }
    }
}
