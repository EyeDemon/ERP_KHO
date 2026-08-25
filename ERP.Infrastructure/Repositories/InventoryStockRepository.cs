using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
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
                .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
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

            var updatedAt = DateTime.UtcNow;
            int affectedRows;
            try
            {
                affectedRows = await _dbSet
                    .Where(stock =>
                        stock.ProductId == productId &&
                        stock.WarehouseId == warehouseId &&
                        stock.Quantity - stock.ReservedQuantity >= quantity)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(stock => stock.Quantity, stock => stock.Quantity - quantity)
                            .SetProperty(stock => stock.LastUpdated, updatedAt),
                        cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205)
            {
                throw new ERP.Domain.Exceptions.DeadlockException(
                    "Giao dịch cập nhật tồn kho bị deadlock.",
                    exception);
            }

            return affectedRows == 1;
        }

        public async Task<bool> TryReserveAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            try
            {
                return await _dbSet.Where(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.Quantity - x.ReservedQuantity >= quantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity + quantity).SetProperty(x => x.LastUpdated, DateTime.UtcNow), cancellationToken) == 1;
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
                return await _dbSet.Where(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.ReservedQuantity >= quantity && x.Quantity >= quantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - quantity).SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - quantity).SetProperty(x => x.LastUpdated, DateTime.UtcNow), cancellationToken) == 1;
            }
            catch (SqlException exception) when (exception.Number == 1205) { throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch tiêu thụ giữ hàng bị deadlock.", exception); }
        }

        public async Task<bool> TryReleaseReservationAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default)
        {
            if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
            try
            {
                return await _dbSet.Where(x => x.ProductId == productId && x.WarehouseId == warehouseId && x.ReservedQuantity >= quantity)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - quantity).SetProperty(x => x.LastUpdated, DateTime.UtcNow), cancellationToken) == 1;
            }
            catch (SqlException exception) when (exception.Number == 1205) { throw new ERP.Domain.Exceptions.DeadlockException("Giao dịch giải phóng giữ hàng bị deadlock.", exception); }
        }
    }
}
