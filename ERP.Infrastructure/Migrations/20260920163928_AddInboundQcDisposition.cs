using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundQcDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryStocks_ProductId_WarehouseId",
                table: "InventoryStocks");

            migrationBuilder.AddColumn<int>(
                name: "InventoryStatus",
                table: "InventoryTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "InventoryStocks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "QcCompletedAt",
                table: "ImportReceiptDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QcCompletedBy",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QcDispositionNote",
                table: "ImportReceiptDetails",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QcDispositionReasonCode",
                table: "ImportReceiptDetails",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QcPolicyEffectiveAtUtc",
                table: "ImportReceiptDetails",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QcPolicyId",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QcPolicySourceSnapshot",
                table: "ImportReceiptDetails",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QcPolicyVersion",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QcRuleSnapshot",
                table: "ImportReceiptDetails",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QcState",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresQc",
                table: "ImportReceiptDetails",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "QcPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RequiresQc = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Rule = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcPolicies_BusinessPartners_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "BusinessPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcPolicies_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_ProductId_WarehouseId_Status",
                table: "InventoryStocks",
                columns: new[] { "ProductId", "WarehouseId", "Status" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcPolicies_ProductId_SupplierId_Version",
                table: "QcPolicies",
                columns: new[] { "ProductId", "SupplierId", "Version" },
                unique: true,
                filter: "[SupplierId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QcPolicies_ProductId_Version",
                table: "QcPolicies",
                columns: new[] { "ProductId", "Version" },
                unique: true,
                filter: "[SupplierId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QcPolicies_SupplierId",
                table: "QcPolicies",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QcPolicies");

            migrationBuilder.DropIndex(
                name: "IX_InventoryStocks_ProductId_WarehouseId_Status",
                table: "InventoryStocks");

            migrationBuilder.DropColumn(
                name: "InventoryStatus",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "InventoryStocks");

            migrationBuilder.DropColumn(
                name: "QcCompletedAt",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcCompletedBy",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcDispositionNote",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcDispositionReasonCode",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcPolicyEffectiveAtUtc",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcPolicyId",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcPolicySourceSnapshot",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcPolicyVersion",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcRuleSnapshot",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "QcState",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "RequiresQc",
                table: "ImportReceiptDetails");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_ProductId_WarehouseId",
                table: "InventoryStocks",
                columns: new[] { "ProductId", "WarehouseId" },
                unique: true);
        }
    }
}
