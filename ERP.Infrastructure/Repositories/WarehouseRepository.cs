using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class WarehouseRepository : Repository<Warehouse>, IWarehouseRepository
    {
        public WarehouseRepository(ErpKhoDbContext context) : base(context)
        {
        }

        public async Task<bool> ExistsByCodeAsync(string code, int? excludeId = null)
        {
            var query = _dbSet.Where(w => w.Code == code);
            if (excludeId.HasValue)
            {
                query = query.Where(w => w.Id != excludeId.Value);
            }
            return await query.AnyAsync();
        }

        public async Task<bool> HasTransactionsAsync(int warehouseId)
        {
            var hasTransactions = await _context.InventoryTransactions.AnyAsync(t => t.WarehouseId == warehouseId);
            if (hasTransactions) return true;

            var hasStocks = await _context.InventoryStocks.AnyAsync(s => s.WarehouseId == warehouseId && s.Quantity > 0);
            if (hasStocks) return true;

            return false;
        }
    }
}
