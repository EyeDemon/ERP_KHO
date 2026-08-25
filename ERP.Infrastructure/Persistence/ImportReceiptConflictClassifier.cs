using System;
using System.Linq;
using ERP.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence
{
    public interface IImportReceiptConflictClassifier
    {
        bool IsCodeUniqueConflict(DbUpdateException ex, string code);
    }

    public class ImportReceiptConflictClassifier : IImportReceiptConflictClassifier
    {
        public const string TargetIndexName = "IX_ImportReceipts_Code";

        public virtual bool IsCodeUniqueConflict(DbUpdateException ex, string code)
        {
            if (ex == null) return false;

            bool hasMatchingEntity = ex.Entries != null && ex.Entries.Any(entry =>
                entry.Entity is ImportReceipt receipt &&
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

            return ContainsExactConstraintToken(message, targetIndexName);
        }

        public static bool ContainsExactConstraintToken(string message, string targetConstraintName)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(targetConstraintName))
                return false;

            int firstQuote = message.IndexOf('\'');
            while (firstQuote >= 0 && firstQuote < message.Length - 1)
            {
                int secondQuote = message.IndexOf('\'', firstQuote + 1);
                if (secondQuote <= firstQuote) break;

                string token = message.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                if (string.Equals(token, targetConstraintName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                firstQuote = message.IndexOf('\'', secondQuote + 1);
            }

            return false;
        }
    }
}
