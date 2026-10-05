using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261005214500_AllowLocationSplitExportLedger")]
public sealed class AllowLocationSplitExportLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_InventoryTransactions_ExportReceiptReference'
      AND object_id = OBJECT_ID('InventoryTransactions')
)
    DROP INDEX [IX_InventoryTransactions_ExportReceiptReference] ON [InventoryTransactions];
""");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryTransactions_ExportReceiptReference",
            table: "InventoryTransactions",
            columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId", "LocationId" },
            unique: true,
            filter: "[ReferenceType] = 'ExportReceipt'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF EXISTS (
    SELECT 1
    FROM InventoryTransactions
    WHERE ReferenceType = 'ExportReceipt' AND ReferenceId IS NOT NULL
    GROUP BY ReferenceId, TransactionType, ProductId, WarehouseId
    HAVING COUNT(*) > 1
)
    THROW 51012, 'Cannot downgrade export ledger index while location-split rows exist.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_InventoryTransactions_ExportReceiptReference'
      AND object_id = OBJECT_ID('InventoryTransactions')
)
    DROP INDEX [IX_InventoryTransactions_ExportReceiptReference] ON [InventoryTransactions];
""");

        migrationBuilder.CreateIndex(
            name: "IX_InventoryTransactions_ExportReceiptReference",
            table: "InventoryTransactions",
            columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId" },
            unique: true,
            filter: "[ReferenceType] = 'ExportReceipt'");
    }
}
