using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ERP.Application.Interfaces;

namespace ERP.Infrastructure.Repositories
{
    public class ImportReceiptRepository : Repository<ImportReceipt>, IImportReceiptRepository
    {
        private readonly IImportReceiptConflictClassifier _classifier;
        private readonly IWarehouseAuthorizationService? _warehouseAuthorization;

        internal ImportReceiptRepository(ErpKhoDbContext context)
            : this(context, new ImportReceiptConflictClassifier(), null)
        {
        }

        internal ImportReceiptRepository(ErpKhoDbContext context, IImportReceiptConflictClassifier classifier)
            : this(context, classifier, null)
        {
        }

        public ImportReceiptRepository(ErpKhoDbContext context, IWarehouseAuthorizationService warehouseAuthorization)
            : this(context, new ImportReceiptConflictClassifier(), warehouseAuthorization)
        {
        }

        public ImportReceiptRepository(ErpKhoDbContext context, IImportReceiptConflictClassifier classifier, IWarehouseAuthorizationService? warehouseAuthorization)
            : base(context)
        {
            _classifier = classifier ?? new ImportReceiptConflictClassifier();
            _warehouseAuthorization = warehouseAuthorization;
        }

        public Task<ImportReceipt?> GetByIdWithDetailsAsync(int id) => LoadWithDetailsAsync(id, false);

        public Task<ImportReceipt?> GetByIdWithDetailsForUpdateAsync(int id) => LoadWithDetailsAsync(id, true);

        private async Task<ImportReceipt?> LoadWithDetailsAsync(int id, bool forUpdate)
        {
            // Status alone does not protect two partial QC updates that remain QcPending.
            var source = forUpdate && _context.Database.IsSqlServer()
                ? _dbSet.FromSqlInterpolated($"SELECT * FROM dbo.ImportReceipts WITH (UPDLOCK,HOLDLOCK) WHERE Id = {id}")
                : _dbSet.AsQueryable();
            var query = source
                .Include(i => i.Warehouse)
                .Include(i => i.Supplier)
                .Include(i => i.PurchaseOrder)
                .Include(i => i.Asn)
                .Include(i => i.Details)
                    .ThenInclude(d => d.Product)
                        .ThenInclude(p => p.Unit)
                .Include(i => i.CreatedByUser)
                .Include(i => i.ApprovedByUser)
                .AsQueryable();
            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(i => allowedWarehouseIds.Contains(i.WarehouseId));
            }
            return await query.FirstOrDefaultAsync(i => i.Id == id);
        }

        public async Task<IEnumerable<ImportReceipt>> GetAllWithDetailsAsync(ERP.Domain.Enums.ReceiptStatus? status)
        {
            var query = _dbSet
                .Include(i => i.Warehouse)
                .Include(i => i.Supplier)
                .Include(i => i.PurchaseOrder)
                .Include(i => i.Asn)
                .AsQueryable();

            if (_warehouseAuthorization is not null)
            {
                var allowedWarehouseIds = await _warehouseAuthorization.GetAccessibleWarehouseIdsAsync();
                query = query.Where(i => allowedWarehouseIds.Contains(i.WarehouseId));
            }

            if (status.HasValue)
            {
                query = query.Where(i => i.Status == status.Value);
            }

            return await query.OrderByDescending(i => i.CreatedAt).ToListAsync();
        }

        public async Task<bool> ExistsByCodeAsync(string code, int? excludeId = null)
        {
            var query = _dbSet.Where(i => i.Code == code);
            if (excludeId.HasValue)
            {
                query = query.Where(i => i.Id != excludeId.Value);
            }
            return await query.AnyAsync();
        }

        public override async Task<ImportReceipt> AddAsync(ImportReceipt entity, CancellationToken cancellationToken = default)
        {
            try
            {
                if (entity.SupplierId.HasValue && !await _context.BusinessPartners.AnyAsync(x => x.Id == entity.SupplierId && x.IsActive && x.IsSupplier, cancellationToken))
                    throw new BusinessRuleException("Nhà cung cấp không tồn tại, không hoạt động hoặc sai vai trò.");
                return await base.AddAsync(entity, cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                if (_classifier.IsCodeUniqueConflict(ex, entity.Code))
                {
                    throw new BusinessRuleException($"Mã phiếu nhập '{entity.Code}' đã tồn tại", ex);
                }
                throw;
            }
        }
    }
}

