using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExportReceiptDispatchWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Read-only preflight. This statement must remain before every DDL or data mutation.
                IF EXISTS (SELECT 1 FROM InventoryStocks WHERE Quantity < 0 OR ReservedQuantity < 0 OR ReservedQuantity > Quantity)
                    THROW 51010, 'Unsafe export workflow migration: inventory contains negative or over-reserved stock.', 1;

                IF EXISTS
                (
                    SELECT 1
                    FROM InventoryTransactions
                    WHERE ReferenceType = 'ExportReceipt' AND TransactionType = 1 AND ReferenceId IS NOT NULL
                    GROUP BY ReferenceId, ProductId, WarehouseId
                    HAVING COUNT(*) > 1
                )
                    THROW 51010, 'Unsafe export workflow migration: duplicate Export transactions exist.', 1;

                IF EXISTS
                (
                    SELECT 1 FROM ExportReceipts e WHERE e.Status = 1 AND
                    (
                        NOT EXISTS (SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id)
                        OR EXISTS (SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id AND d.Quantity <= 0)
                        OR EXISTS
                        (
                            SELECT 1 FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id AND 1 <>
                            (SELECT COUNT(*) FROM InventoryTransactions t WHERE t.ReferenceType = 'ExportReceipt' AND t.ReferenceId = e.Id AND t.TransactionType = 1 AND t.ProductId = d.ProductId AND t.WarehouseId = e.WarehouseId AND t.Quantity = d.Quantity)
                        )
                        OR
                        (
                            (SELECT COUNT(*) FROM InventoryTransactions t WHERE t.ReferenceType = 'ExportReceipt' AND t.ReferenceId = e.Id AND t.TransactionType = 1)
                            <> (SELECT COUNT(*) FROM ExportReceiptDetails d WHERE d.ExportReceiptId = e.Id)
                        )
                        OR EXISTS (SELECT 1 FROM StockReservations r WHERE r.SourceType = 'ExportReceipt' AND r.SourceId = e.Id AND r.Status IN (0, 1))
                    )
                )
                    THROW 51010, 'Unsafe legacy export receipt backfill: Approved receipt does not have an exact one-to-one Export transaction ledger or has an active reservation.', 1;
                """);

            migrationBuilder.AddColumn<int>(
                name: "DispatchMode",
                table: "ExportReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchedAt",
                table: "ExportReceipts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DispatchedBy",
                table: "ExportReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("UPDATE ExportReceipts SET Status = 3, DispatchMode = 1, DispatchedAt = ApprovedAt, DispatchedBy = ApprovedBy WHERE Status = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ExportReceiptReference",
                table: "InventoryTransactions",
                columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId" },
                unique: true,
                filter: "[ReferenceType] = 'ExportReceipt'");

            migrationBuilder.CreateIndex(
                name: "IX_ExportReceipts_DispatchedBy",
                table: "ExportReceipts",
                column: "DispatchedBy");

            migrationBuilder.AddForeignKey(
                name: "FK_ExportReceipts_Users_DispatchedBy",
                table: "ExportReceipts",
                column: "DispatchedBy",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- Downgrade cannot distinguish newly dispatched receipts from legacy backfilled receipts.
                -- A production rollback needs an approved data plan; do not silently reinterpret physical exports.
                IF EXISTS (SELECT 1 FROM ExportReceipts WHERE Status = 3)
                    THROW 51011, 'Cannot downgrade while Dispatched export receipts exist. Use an approved data rollback plan.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_ExportReceipts_Users_DispatchedBy",
                table: "ExportReceipts");

            migrationBuilder.Sql(
                "IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_InventoryTransactions_ExportReceiptReference' AND object_id = OBJECT_ID('InventoryTransactions')) DROP INDEX [IX_InventoryTransactions_ExportReceiptReference] ON [InventoryTransactions];");

            migrationBuilder.DropIndex(
                name: "IX_ExportReceipts_DispatchedBy",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "DispatchMode",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "DispatchedBy",
                table: "ExportReceipts");
        }
    }
}
