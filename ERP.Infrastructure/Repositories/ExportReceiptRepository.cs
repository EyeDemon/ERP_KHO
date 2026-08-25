using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ERP.Application.Interfaces;

namespace ERP.Infrastructure.Repositories
{
    public class ExportReceiptRepository : Repository<ExportReceipt>, IExportReceiptRepository
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
                .Include(e => e.CreatedByUser)
                .Include(e => e.ApprovedByUser)
                .Include(e => e.DispatchedByUser)
                .Include(e => e.Details)
                    .ThenInclude(d => d.Product).AsQueryable();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(e => allowedWarehouseIds.Contains(e.WarehouseId));
            }
            return await query.FirstOrDefaultAsync(e => e.Id == id);
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
                .Include(e => e.CreatedByUser)
                .Include(e => e.ApprovedByUser)
                .Include(e => e.DispatchedByUser)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();
        }

        public override async Task<ExportReceipt> AddAsync(ExportReceipt entity, CancellationToken cancellationToken = default)
        {
            try
            {
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
