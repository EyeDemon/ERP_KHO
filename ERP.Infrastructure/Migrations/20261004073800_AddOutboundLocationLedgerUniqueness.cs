using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundLocationLedgerUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The legacy index allowed one warehouse-level row, before location-traceable dispatch.
            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_ExportReceiptReference",
                table: "InventoryTransactions");
            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ExportReceiptLocationReference",
                table: "InventoryTransactions",
                columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId", "LocationId" },
                unique: true,
                filter: "[ReferenceType] = 'ExportReceipt'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM InventoryTransactions WHERE ReferenceType='ExportReceipt'
                    GROUP BY ReferenceType,ReferenceId,TransactionType,ProductId,WarehouseId HAVING COUNT(*)>1)
                    THROW 51012, 'Cannot downgrade location export ledger without an approved backup and data plan.', 1;
                """);
            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_ExportReceiptLocationReference",
                table: "InventoryTransactions");
            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ExportReceiptReference",
                table: "InventoryTransactions",
                columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId" },
                unique: true,
                filter: "[ReferenceType] = 'ExportReceipt'");
        }
    }
}
