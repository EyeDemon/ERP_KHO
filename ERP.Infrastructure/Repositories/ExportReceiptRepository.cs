using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ERP.Application.Interfaces;
using ERP.Application.Services;

namespace ERP.Infrastructure.Repositories
{
    public class ExportReceiptRepository : Repository<ExportReceipt>, IExportReceiptRepository, IExportReceiptBundleResolver
    {
        private readonly IExportReceiptConflictClassifier _classifier;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;

        internal ExportReceiptRepository(ErpKhoDbContext context)
            : this(context, new ExportReceiptConflictClassifier(), null)
        {
        }

        internal ExportReceiptRepository(ErpKhoDbContext context, IExportReceiptConflictClassifier classifier)
            : this(context, classifier, null)
        {
        }

        public ExportReceiptRepository(ErpKhoDbContext context, IWarehouseAuthorizationService warehouseAuthorization)
            : this(context, new ExportReceiptConflictClassifier(), warehouseAuthorization)
        {
        }

        public ExportReceiptRepository(ErpKhoDbContext context, IExportReceiptConflictClassifier classifier, IWarehouseAuthorizationService? warehouseAuthorization)
            : base(context)
        {
            _classifier = classifier ?? new ExportReceiptConflictClassifier();
            _warehouseAuthorization = warehouseAuthorization;
        }

        public async Task<bool> ExistsByCodeAsync(string code, int? excludeId = null)
        {
            var query = _dbSet.Where(e => e.Code == code);
            if (excludeId.HasValue)
            {
                query = query.Where(e => e.Id != excludeId.Value);
            }
            return await query.AnyAsync();
        }

        public async Task<ExportReceipt?> GetByIdWithDetailsAsync(int id)
        {
            var query = _dbSet
                .Include(e => e.Warehouse)
                .Include(e => e.Customer)
                .Include(e => e.SalesOrder)
                .Include(e => e.CreatedByUser)
                .Include(e => e.ApprovedByUser)
                .Include(e => e.DispatchedByUser)
                .Include(e => e.Details)
                    .ThenInclude(d => d.Product)
                        .ThenInclude(p => p.Unit)
                .AsQueryable();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(e => allowedWarehouseIds.Contains(e.WarehouseId));
            }
            var receipt = await query.FirstOrDefaultAsync(e => e.Id == id);
            if (receipt is null)
            {
                var fallbackQuery = _dbSet
                    .Include(e => e.Customer)
                    .Include(e => e.SalesOrder)
                    .Include(e => e.Details)
                        .ThenInclude(d => d.Product)
                    .AsQueryable();
                if (_warehouseAuthorization is not null)
                {
                    var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                    fallbackQuery = fallbackQuery.Where(e => allowedWarehouseIds.Contains(e.WarehouseId));
                }
                receipt = await fallbackQuery.FirstOrDefaultAsync(e => e.Id == id);
            }
            if (receipt is not null && receipt.SalesOrder is null)
            {
                if (receipt.SalesOrderId.HasValue)
                {
                    receipt.SalesOrder = await _context.SalesOrders.AsNoTracking()
                        .FirstOrDefaultAsync(o => o.Id == receipt.SalesOrderId.Value);
                }
                else if (!string.IsNullOrWhiteSpace(receipt.MarketplaceOrderId))
                {
                    var mpOrder = receipt.MarketplaceOrderId.Trim();
                    receipt.SalesOrder = await _context.SalesOrders.AsNoTracking()
                        .FirstOrDefaultAsync(o => o.ExternalOrderId == mpOrder || o.OrderCode == mpOrder);
                }
            }
            return receipt;
        }

        public async Task<IEnumerable<ExportReceipt>> GetListAsync()
        {
            var query = _dbSet.AsQueryable();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(e => allowedWarehouseIds.Contains(e.WarehouseId));
            }
            return await query
                .Include(e => e.Warehouse)
                .Include(e => e.Customer)
                .Include(e => e.SalesOrder)
                .Include(e => e.CreatedByUser)
                .Include(e => e.ApprovedByUser)
                .Include(e => e.DispatchedByUser)
                .Include(e => e.Details)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<ProductBundleComponent>> GetActiveBundleComponentsAsync(
            IEnumerable<int> parentProductIds,
            CancellationToken cancellationToken = default)
        {
            var ids = parentProductIds.Distinct().ToArray();
            if (ids.Length == 0) return [];
            var tenantId = _context.CurrentTenantId;
            return await _context.ProductBundleComponents.AsNoTracking()
                .Where(b => b.TenantId == tenantId && b.IsActive && ids.Contains(b.ParentProductId))
                .OrderBy(b => b.ParentProductId)
                .ThenBy(b => b.ComponentProductId)
                .ThenBy(b => b.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<SalesOrderLine>> GetSalesOrderLinesAsync(
            int? salesOrderId,
            string? marketplaceOrderId,
            CancellationToken cancellationToken = default)
        {
            if (salesOrderId is > 0)
            {
                return await _context.SalesOrderLines.AsNoTracking()
                    .Where(l => l.SalesOrderId == salesOrderId.Value)
                    .ToListAsync(cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(marketplaceOrderId))
            {
                var code = marketplaceOrderId.Trim();
                var soId = await _context.SalesOrders.AsNoTracking()
                    .Where(o => o.ExternalOrderId == code || o.OrderCode == code)
                    .Select(o => (int?)o.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (soId.HasValue)
                {
                    return await _context.SalesOrderLines.AsNoTracking()
                        .Where(l => l.SalesOrderId == soId.Value)
                        .ToListAsync(cancellationToken);
                }
            }
            return [];
        }

        public async Task<IReadOnlySet<int>> GetActiveGiftProductIdsAsync(
            int warehouseId,
            CancellationToken cancellationToken = default)
        {
            var giftIds = await _context.PromotionGiftRules.AsNoTracking()
                .Where(r => r.IsActive && (!r.WarehouseId.HasValue || r.WarehouseId.Value == warehouseId))
                .Select(r => r.GiftProductId)
                .Distinct()
                .ToListAsync(cancellationToken);
            return giftIds.ToHashSet();
        }

        public override async Task<ExportReceipt> AddAsync(ExportReceipt entity, CancellationToken cancellationToken = default)
        {
            try
            {
                if (entity.CustomerId.HasValue && !await _context.BusinessPartners.AnyAsync(x => x.Id == entity.CustomerId && x.IsActive && x.IsCustomer, cancellationToken))
                    throw new BusinessRuleException("Khách hàng không tồn tại, không hoạt động hoặc sai vai trò.");
                return await base.AddAsync(entity, cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                if (_classifier.IsCodeUniqueConflict(ex, entity.Code))
                {
                    throw new BusinessRuleException($"Mã phiếu xuất '{entity.Code}' đã tồn tại", ex);
                }
                throw;
            }
        }
    }
}
