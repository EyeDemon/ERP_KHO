using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class StocktakeRepository : Repository<Stocktake>, IStocktakeRepository
    {
        public StocktakeRepository(ErpKhoDbContext context) : base(context)
        {
        }

        public async Task<Stocktake?> GetByIdWithDetailsAsync(int id)
        {
            if (_context.Database.IsSqlServer() && _context.Database.CurrentTransaction is not null)
            {
                var tenantId = _context.CurrentTenantId;
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM dbo.Stocktakes WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {id}");
            }

            return await _dbSet
                .Include(s => s.Details)
                .FirstOrDefaultAsync(s => s.Id == id);
        }
    }
}
