using System;
using System.Linq;
using ERP.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence
{
    public interface IExportReceiptConflictClassifier
    {
        bool IsCodeUniqueConflict(DbUpdateException ex, string code);
    }

    public class ExportReceiptConflictClassifier : IExportReceiptConflictClassifier
    {
        public const string TargetIndexName = "IX_ExportReceipts_Code";

        public virtual bool IsCodeUniqueConflict(DbUpdateException ex, string code)
        {
            if (ex == null) return false;

            bool hasMatchingEntity = ex.Entries != null && ex.Entries.Any(entry =>
                entry.Entity is ExportReceipt receipt &&
                string.Equals(receipt.Code, code, StringComparison.OrdinalIgnoreCase));

            if (!hasMatchingEntity) return false;

            if (ex.InnerException is SqlException sqlEx)
            {
                return IsDuplicateKeyWithExactIndex(sqlEx.Number, sqlEx.Message, TargetIndexName);
            }

            return false;
        }

        public static bool IsDuplicateKeyWithExactIndex(int sqlErrorNumber, string message, string targetIndexName)
        {
            if (sqlErrorNumber != 2601 && sqlErrorNumber != 2627)
                return false;

            return ImportReceiptConflictClassifier.ContainsExactConstraintToken(message, targetIndexName);
        }
    }
}
