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
            return await _dbSet
                .Include(s => s.Details)
                .FirstOrDefaultAsync(s => s.Id == id);
        }
    }
}
