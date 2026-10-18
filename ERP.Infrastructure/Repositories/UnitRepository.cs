using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class UnitRepository : Repository<Unit>, IUnitRepository
    {
        public UnitRepository(ErpKhoDbContext context) : base(context)
        {
        }

        public async Task<bool> ExistsByCodeAsync(string code, int? excludeId = null)
        {
            var query = _dbSet.Where(u => u.Code == code);
            if (excludeId.HasValue)
            {
                query = query.Where(u => u.Id != excludeId.Value);
            }
            return await query.AnyAsync();
        }

        public async Task<bool> HasProductsAsync(int unitId)
        {
            return await _context.Products.AnyAsync(p => p.UnitId == unitId)
                || await _context.ProductUoms.AnyAsync(u => u.UnitId == unitId)
                || await _context.PurchaseOrderLines.AnyAsync(l => l.OperationUnitId == unitId || l.BaseUnitId == unitId)
                || await _context.AsnLines.AnyAsync(l => l.OperationUnitId == unitId || l.BaseUnitId == unitId)
                || await _context.ImportReceiptDetails.AnyAsync(d => d.OperationUnitId == unitId || d.BaseUnitId == unitId)
                || await _context.PutawayTaskItems.AnyAsync(t => t.OperationUnitId == unitId || t.BaseUnitId == unitId)
                || await _context.ReceivingObservationVersions.AnyAsync(v => v.ObservedUnitId == unitId);
        }

        public override async Task DeleteAsync(Unit entity, CancellationToken cancellationToken = default)
        {
            try
            {
                await base.DeleteAsync(entity, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
            {
                var conflict = new BusinessRuleException("Không thể xóa đơn vị tính đang được sử dụng cho sản phẩm", ex);
                conflict.Data["HttpStatusCode"] = 409;
                throw conflict;
            }
        }
    }
}
