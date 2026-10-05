using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
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

        public Task<decimal> GetAvailableQuantityAsync(int productId, int warehouseId, CancellationToken cancellationToken = default) =>
            _dbSet.AsNoTracking()
                .Where(x => x.ProductId == productId && x.WarehouseId == warehouseId &&
                    x.Status == InventoryStatus.Available && x.Location != null &&
                    x.Location.IsActive && !x.Location.IsBlocked && x.Location.IsPickable)
                .SumAsync(x => x.Quantity - x.ReservedQuantity, cancellationToken);

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
                var rows = await _dbSet.AsNoTracking()
                    .Where(x => x.ProductId == productId && x.WarehouseId == warehouseId &&
                        x.Status == InventoryStatus.Available && x.LocationId.HasValue &&
                        x.Location != null && x.Location.IsActive && !x.Location.IsBlocked && x.Location.IsPickable)
                    .OrderBy(x => x.LocationId)
                    .Select(x => new { x.Id, x.LocationId, x.Quantity, x.ReservedQuantity })
                    .ToListAsync(cancellationToken);

                if (rows.Sum(x => x.ReservedQuantity) < quantity)
                    return Array.Empty<InventoryStockConsumption>();

                var remaining = quantity;
                var consumed = new List<InventoryStockConsumption>();
                var now = DateTime.UtcNow;
                foreach (var row in rows)
                {
                    var take = Math.Min(remaining, row.ReservedQuantity);
                    if (take <= 0) continue;
                    var affected = await _dbSet
                        .Where(x => x.Id == row.Id && x.ReservedQuantity >= take && x.Quantity >= take)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(x => x.Quantity, x => x.Quantity - take)
                            .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - take)
                            .SetProperty(x => x.LastUpdated, now), cancellationToken);
                    if (affected != 1)
                        throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu tồn kho tại vị trí đã thay đổi trong lúc tiêu thụ reservation.");

                    consumed.Add(new InventoryStockConsumption(row.LocationId!.Value, take));
                    remaining -= take;
                    if (remaining == 0) return consumed;
                }

                throw new ERP.Domain.Exceptions.ConcurrencyException("Không thể xác định đầy đủ vị trí tồn kho đã tiêu thụ.");
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
            try
            {
                var now = DateTime.UtcNow;
                var affected = await _dbSet
                    .Where(x => x.ProductId == productId &&
                                x.WarehouseId == warehouseId &&
                                x.LocationId == locationId &&
                                x.Status == InventoryStatus.Available &&
                                x.Location != null &&
                                x.Location.IsActive &&
                                !x.Location.IsBlocked &&
                                x.Location.IsPickable &&
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
                    "Giao dịch tiêu thụ giữ hàng tại vị trí bị deadlock.",
                    exception);
            }
        }

        public async Task<bool> TryReleaseReservationAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            try
            {
                return await AllocateAsync(productId, warehouseId, quantity, false, true, cancellationToken, releaseOnly: true);
            }
            catch (SqlException exception) when (exception.Number == 1205) { throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch giải phóng giữ hàng bị deadlock.", exception); }
        }

        private async Task<bool> AllocateAsync(int productId, int warehouseId, decimal quantity, bool reserve, bool useReserved, CancellationToken token, bool releaseOnly = false)
        {
            var rows = await _dbSet.AsNoTracking()
                .Where(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.Status == InventoryStatus.Available && x.Location != null && x.Location.IsActive && !x.Location.IsBlocked && x.Location.IsPickable)
                .OrderBy(x => x.LocationId).Select(x => new { x.Id, x.Quantity, x.ReservedQuantity }).ToListAsync(token);
            var capacity = rows.Sum(x => useReserved ? x.ReservedQuantity : x.Quantity - x.ReservedQuantity);
            if (capacity < quantity) return false;
            var remaining = quantity;
            foreach (var row in rows)
            {
                var available = useReserved ? row.ReservedQuantity : row.Quantity - row.ReservedQuantity;
                var take = Math.Min(remaining, available);
                if (take <= 0) continue;
                var query = _dbSet.Where(x => x.Id == row.Id);
                if (useReserved) query = query.Where(x => x.ReservedQuantity >= take && (releaseOnly || x.Quantity >= take));
                else query = query.Where(x => x.Quantity - x.ReservedQuantity >= take);
                var now = DateTime.UtcNow;
                var affected = reserve
                    ? await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity + take).SetProperty(x => x.LastUpdated, now), token)
                    : useReserved
                        ? releaseOnly
                            ? await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - take).SetProperty(x => x.LastUpdated, now), token)
                            : await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - take).SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - take).SetProperty(x => x.LastUpdated, now), token)
                        : await query.ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - take).SetProperty(x => x.LastUpdated, now), token);
                if (affected != 1) return false;
                remaining -= take;
                if (remaining == 0) return true;
            }
            return false;
        }
    }
}
