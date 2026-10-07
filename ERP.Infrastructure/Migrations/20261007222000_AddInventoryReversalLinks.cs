using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261007222000_AddInventoryReversalLinks")]
public sealed class AddInventoryReversalLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CorrectiveTransactionId",
            table: "InventoryTransactions",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ReversalOfTransactionId",
            table: "InventoryTransactions",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "UX_InventoryTransactions_CorrectiveTransaction",
            table: "InventoryTransactions",
            column: "CorrectiveTransactionId",
            unique: true,
            filter: "[CorrectiveTransactionId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UX_InventoryTransactions_ReversalOfTransaction",
            table: "InventoryTransactions",
            column: "ReversalOfTransactionId",
            unique: true,
            filter: "[ReversalOfTransactionId] IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "FK_InventoryTransactions_InventoryTransactions_CorrectiveTransactionId",
            table: "InventoryTransactions",
            column: "CorrectiveTransactionId",
            principalTable: "InventoryTransactions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_InventoryTransactions_InventoryTransactions_ReversalOfTransactionId",
            table: "InventoryTransactions",
            column: "ReversalOfTransactionId",
            principalTable: "InventoryTransactions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_InventoryTransactions_InventoryTransactions_CorrectiveTransactionId",
            table: "InventoryTransactions");

        migrationBuilder.DropForeignKey(
            name: "FK_InventoryTransactions_InventoryTransactions_ReversalOfTransactionId",
            table: "InventoryTransactions");

        migrationBuilder.DropIndex(
            name: "UX_InventoryTransactions_CorrectiveTransaction",
            table: "InventoryTransactions");

        migrationBuilder.DropIndex(
            name: "UX_InventoryTransactions_ReversalOfTransaction",
            table: "InventoryTransactions");

        migrationBuilder.DropColumn(
            name: "CorrectiveTransactionId",
            table: "InventoryTransactions");

        migrationBuilder.DropColumn(
            name: "ReversalOfTransactionId",
            table: "InventoryTransactions");
    }
}
