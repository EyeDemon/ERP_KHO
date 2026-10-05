using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class InventoryTransactionRepository : Repository<InventoryTransaction>, IInventoryTransactionRepository
    {
        public InventoryTransactionRepository(ErpKhoDbContext context) : base(context)
        {
        }

        public override async Task<InventoryTransaction> AddAsync(InventoryTransaction entity, CancellationToken cancellationToken = default)
        {
            try
            {
                return await base.AddAsync(entity, cancellationToken);
            }
            catch (DbUpdateException exception) when (IsExportReceiptLedgerConflict(entity, exception))
            {
                throw new ERP.Domain.Exceptions.ConcurrencyException(
                    "Giao dịch xuất kho của phiếu này đã được ghi bởi yêu cầu đồng thời khác.",
                    exception);
            }
        }

        private static bool IsExportReceiptLedgerConflict(InventoryTransaction entity, DbUpdateException exception) =>
            string.Equals(entity.ReferenceType, "ExportReceipt", StringComparison.Ordinal) &&
            exception.InnerException is SqlException { Number: 2601 or 2627 } sql &&
            sql.Message.Contains("IX_InventoryTransactions_ExportReceiptReference", StringComparison.OrdinalIgnoreCase);
    }
}
