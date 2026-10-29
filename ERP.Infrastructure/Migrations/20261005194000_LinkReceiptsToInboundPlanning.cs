using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261005194000_LinkReceiptsToInboundPlanning")]
public partial class LinkReceiptsToInboundPlanning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AsnId",
            table: "ImportReceipts",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PurchaseOrderId",
            table: "ImportReceipts",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "AsnLineId",
            table: "ImportReceiptDetails",
            type: "int",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PurchaseOrderLineId",
            table: "ImportReceiptDetails",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ImportReceipts_AsnId",
            table: "ImportReceipts",
            column: "AsnId",
            unique: true,
            filter: "[AsnId] IS NOT NULL AND [Status] <> 2");

        migrationBuilder.CreateIndex(
            name: "IX_ImportReceipts_PurchaseOrderId",
            table: "ImportReceipts",
            column: "PurchaseOrderId");

        migrationBuilder.CreateIndex(
            name: "IX_ImportReceiptDetails_AsnLineId",
            table: "ImportReceiptDetails",
            column: "AsnLineId");

        migrationBuilder.CreateIndex(
            name: "IX_ImportReceiptDetails_PurchaseOrderLineId",
            table: "ImportReceiptDetails",
            column: "PurchaseOrderLineId");

        migrationBuilder.AddForeignKey(
            name: "FK_ImportReceipts_Asns_AsnId",
            table: "ImportReceipts",
            column: "AsnId",
            principalTable: "Asns",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ImportReceipts_PurchaseOrders_PurchaseOrderId",
            table: "ImportReceipts",
            column: "PurchaseOrderId",
            principalTable: "PurchaseOrders",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ImportReceiptDetails_AsnLines_AsnLineId",
            table: "ImportReceiptDetails",
            column: "AsnLineId",
            principalTable: "AsnLines",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_ImportReceiptDetails_PurchaseOrderLines_PurchaseOrderLineId",
            table: "ImportReceiptDetails",
            column: "PurchaseOrderLineId",
            principalTable: "PurchaseOrderLines",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_ImportReceipts_Asns_AsnId", "ImportReceipts");
        migrationBuilder.DropForeignKey("FK_ImportReceipts_PurchaseOrders_PurchaseOrderId", "ImportReceipts");
        migrationBuilder.DropForeignKey("FK_ImportReceiptDetails_AsnLines_AsnLineId", "ImportReceiptDetails");
        migrationBuilder.DropForeignKey("FK_ImportReceiptDetails_PurchaseOrderLines_PurchaseOrderLineId", "ImportReceiptDetails");

        migrationBuilder.DropIndex("IX_ImportReceipts_AsnId", "ImportReceipts");
        migrationBuilder.DropIndex("IX_ImportReceipts_PurchaseOrderId", "ImportReceipts");
        migrationBuilder.DropIndex("IX_ImportReceiptDetails_AsnLineId", "ImportReceiptDetails");
        migrationBuilder.DropIndex("IX_ImportReceiptDetails_PurchaseOrderLineId", "ImportReceiptDetails");

        migrationBuilder.DropColumn("AsnId", "ImportReceipts");
        migrationBuilder.DropColumn("PurchaseOrderId", "ImportReceipts");
        migrationBuilder.DropColumn("AsnLineId", "ImportReceiptDetails");
        migrationBuilder.DropColumn("PurchaseOrderLineId", "ImportReceiptDetails");
    }
}
