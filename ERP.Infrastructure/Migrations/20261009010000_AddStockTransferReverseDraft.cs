using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261009010000_AddStockTransferReverseDraft")]
public sealed class AddStockTransferReverseDraft : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "ReverseOfTransferId", table: "StockTransfers", type: "int", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReverseReasonCode", table: "StockTransfers", type: "nvarchar(40)", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>(name: "ReverseReason", table: "StockTransfers", type: "nvarchar(400)", maxLength: 400, nullable: true);
        // Historic documents have no verified reverse link; do not backfill from notes.
        migrationBuilder.CreateIndex(name: "UX_StockTransfers_ReverseOfTransferId", table: "StockTransfers",
            column: "ReverseOfTransferId", unique: true, filter: "[ReverseOfTransferId] IS NOT NULL");
        migrationBuilder.AddForeignKey(name: "FK_StockTransfers_StockTransfers_ReverseOfTransferId",
            table: "StockTransfers", column: "ReverseOfTransferId", principalTable: "StockTransfers",
            principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "FK_StockTransfers_StockTransfers_ReverseOfTransferId", table: "StockTransfers");
        migrationBuilder.DropIndex(name: "UX_StockTransfers_ReverseOfTransferId", table: "StockTransfers");
        migrationBuilder.DropColumn(name: "ReverseOfTransferId", table: "StockTransfers");
        migrationBuilder.DropColumn(name: "ReverseReasonCode", table: "StockTransfers");
        migrationBuilder.DropColumn(name: "ReverseReason", table: "StockTransfers");
    }
}
