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
            return _dbSet.Include(p => p.Unit).Include(p => p.Uoms).ThenInclude(x => x.Unit).Include(p => p.Category).Include(p => p.Barcodes).Include(p => p.QcPolicies)
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
            if (await _context.InventoryTransactions.AnyAsync(t => t.ProductId == productId, cancellationToken)) return true;
            if (await _context.InventoryStocks.AnyAsync(s => s.ProductId == productId && (s.Quantity != 0 || s.ReservedQuantity != 0), cancellationToken)) return true;
            if (await _context.ImportReceiptDetails.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.ExportReceiptDetails.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.PurchaseOrderLines.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.AsnLines.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.SalesOrderLines.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.StocktakeDetails.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.StockTransferDetails.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.PutawayTaskItems.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.PickingTaskLines.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.HandlingUnitContents.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.StockReservations.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.StockAllocations.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.InventoryLots.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.InventorySerials.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.InventoryLocationMovements.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;
            if (await _context.InventoryLocks.AnyAsync(x => x.ProductId == productId, cancellationToken)) return true;

            return false;
        }

        public override async Task DeleteAsync(Product entity, CancellationToken cancellationToken = default)
        {
            var productId = entity.Id;

            var emptyStocks = await _context.InventoryStocks
                .Where(x => x.ProductId == productId && x.Quantity == 0 && x.ReservedQuantity == 0)
                .ToListAsync(cancellationToken);
            if (emptyStocks.Count > 0) _context.InventoryStocks.RemoveRange(emptyStocks);

            var qcPolicies = await _context.QcPolicies
                .Where(x => x.ProductId == productId)
                .ToListAsync(cancellationToken);
            if (qcPolicies.Count > 0) _context.QcPolicies.RemoveRange(qcPolicies);

            var tolerancePolicies = await _context.ReceivingTolerancePolicies
                .Where(x => x.ProductId == productId)
                .ToListAsync(cancellationToken);
            if (tolerancePolicies.Count > 0) _context.ReceivingTolerancePolicies.RemoveRange(tolerancePolicies);

            entity.QcPolicies.Clear();
            await base.DeleteAsync(entity, cancellationToken);
        }
    }
}
