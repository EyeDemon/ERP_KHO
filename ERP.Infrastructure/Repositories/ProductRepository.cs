using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class ProductRepository : Repository<Product>, IProductRepository
    {
        public ProductRepository(ErpKhoDbContext context) : base(context)
        {
        }

        public override Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return _dbSet.Include(p => p.Unit).Include(p => p.Uoms).ThenInclude(x => x.Unit).Include(p => p.Category).Include(p => p.Barcodes)
                .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        }

        public async Task<bool> ExistsByCodeAsync(string code, int? excludeId = null, CancellationToken cancellationToken = default)
        {
            var query = _dbSet.Where(p => p.Code == code);
            if (excludeId.HasValue)
            {
                query = query.Where(p => p.Id != excludeId.Value);
            }
            return await query.AnyAsync(cancellationToken);
        }

        public async Task<IEnumerable<Product>> GetProductsWithDetailsAsync(CancellationToken cancellationToken = default)
        {
            return await _dbSet
                .Include(p => p.Unit)
                .Include(p => p.Uoms).ThenInclude(x => x.Unit)
                .Include(p => p.Category)
                .Include(p => p.Barcodes)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }

        public async Task<(IReadOnlyList<Product> Items, int TotalRecords)> GetPagedAsync(int pageIndex, int pageSize, string? keyword, CancellationToken cancellationToken = default)
        {
            var query = _dbSet.Include(p => p.Unit).Include(p => p.Uoms).ThenInclude(x => x.Unit).Include(p => p.Category).Include(p => p.Barcodes).AsNoTracking();

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var lowerKeyword = keyword.Trim().ToLower();
                query = query.Where(p => p.Code.ToLower().Contains(lowerKeyword) ||
                                         p.Name.ToLower().Contains(lowerKeyword));
            }

            var totalRecords = await query.CountAsync(cancellationToken);

            var items = await query
                .OrderByDescending(p => p.CreatedAt)
                .ThenBy(p => p.Id)
                .Skip((pageIndex - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return (items, totalRecords);
        }

        public async Task<bool> HasTransactionsAsync(int productId, CancellationToken cancellationToken = default)
        {
            // Kiểm tra xem sản phẩm đã có trong InventoryTransactions hoặc InventoryStocks chưa
            var hasTransactions = await _context.InventoryTransactions.AnyAsync(t => t.ProductId == productId, cancellationToken);
            if (hasTransactions) return true;

            var hasStocks = await _context.InventoryStocks.AnyAsync(s => s.ProductId == productId && s.Quantity > 0, cancellationToken);
            if (hasStocks) return true;

            return false;
        }
    }
}
