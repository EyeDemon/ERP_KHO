using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerExportDispatchReadinessTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task ProductionPreflight_IsReadOnlySafeAndRequiredIndexExists()
    {
        await using var context = new ErpKhoDbContext(
            new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);
        await context.Database.OpenConnectionAsync();

        (await ScalarAsync(context, "SELECT COUNT(*) FROM InventoryStocks WHERE Quantity < 0 OR ReservedQuantity < 0 OR ReservedQuantity > Quantity"))
            .Should().Be(0, "negative or over-reserved stock makes migration unsafe");
        (await ScalarAsync(context,
            """
            SELECT COUNT(*) FROM
            (
                SELECT ReferenceId, ProductId, WarehouseId
                FROM InventoryTransactions
                WHERE ReferenceType = 'ExportReceipt' AND TransactionType = 1 AND ReferenceId IS NOT NULL
                GROUP BY ReferenceId, ProductId, WarehouseId
                HAVING COUNT(*) > 1
            ) duplicate_exports
            """))
            .Should().Be(0, "duplicate physical export transactions break idempotency");
        (await ScalarAsync(context,
            """
            SELECT COUNT(*) FROM ExportReceipts e WHERE e.Status = 1 AND
            (
                NOT EXISTS (SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id)
                OR EXISTS (SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id AND d.Quantity <= 0)
                OR EXISTS
                (
                    SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id AND 1 <>
                    (SELECT COUNT(*) FROM InventoryTransactions t WHERE t.ReferenceType = 'ExportReceipt' AND t.ReferenceId = e.Id AND t.TransactionType = 1 AND t.ProductId = d.ProductId AND t.WarehouseId = e.WarehouseId AND t.Quantity = d.Quantity)
                )
                OR (SELECT COUNT(*) FROM InventoryTransactions t WHERE t.ReferenceType = 'ExportReceipt' AND t.ReferenceId = e.Id AND t.TransactionType = 1)
                   <> (SELECT COUNT(*) FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id)
                OR EXISTS (SELECT 1 FROM StockReservations r WHERE r.SourceType = 'ExportReceipt' AND r.SourceId = e.Id AND r.Status IN (0, 1))
            )
            """))
            .Should().Be(0, "legacy Approved receipts must have an exact safe backfill ledger");
        (await ScalarAsync(context,
            "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_InventoryTransactions_ExportReceiptReference' AND object_id = OBJECT_ID('InventoryTransactions')"))
            .Should().Be(1);
    }

    private static async Task<int> ScalarAsync(ErpKhoDbContext context, string sql)
    {
        await using var command = new SqlCommand(sql, (SqlConnection)context.Database.GetDbConnection());
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}
