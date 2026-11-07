using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MvpCompleteInboundOutboundApAr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_WarehouseId_Code",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_InventorySerials_ProductId_SerialNumber",
                table: "InventorySerials");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLots_ProductId_LotNumber",
                table: "InventoryLots");

            migrationBuilder.DropIndex(
                name: "IX_Asns_WarehouseId_Code",
                table: "Asns");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "SalesOrders",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "VND");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxPercent",
                table: "SalesOrderLines",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "SalesOrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxPercent",
                table: "PurchaseOrderLines",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "PurchaseOrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "InventoryStocks",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ImportReceipts",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxPercent",
                table: "ImportReceiptDetails",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ExportReceipts",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<int>(
                name: "SalesOrderId",
                table: "ExportReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShipmentId",
                table: "ExportReceipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxPercent",
                table: "ExportReceiptDetails",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "BusinessPartners",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PayableBalance",
                table: "BusinessPartners",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PaymentTermDays",
                table: "BusinessPartners",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivableBalance",
                table: "BusinessPartners",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "PartnerDebtInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PartnerId = table.Column<int>(type: "int", nullable: false),
                    DebtType = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: "VND"),
                    SubTotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DueDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerDebtInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerDebtInvoices_BusinessPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "BusinessPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerDebtInvoices_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartnerDebtLedgers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    PartnerId = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    DebtType = table.Column<int>(type: "int", nullable: false),
                    TransactionType = table.Column<int>(type: "int", nullable: false),
                    AmountDelta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReferenceId = table.Column<int>(type: "int", nullable: true),
                    ReferenceCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartnerDebtLedgers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartnerDebtLedgers_BusinessPartners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "BusinessPartners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerDebtLedgers_PartnerDebtInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "PartnerDebtInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartnerDebtLedgers_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_TenantId_WarehouseId_Code",
                table: "PurchaseOrders",
                columns: new[] { "TenantId", "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_WarehouseId",
                table: "PurchaseOrders",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_TenantId_WarehouseId_ProductId_TransactionDate",
                table: "InventoryTransactions",
                columns: new[] { "TenantId", "WarehouseId", "ProductId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InventorySerials_ProductId",
                table: "InventorySerials",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventorySerials_TenantId_ProductId_SerialNumber",
                table: "InventorySerials",
                columns: new[] { "TenantId", "ProductId", "SerialNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_ProductId",
                table: "InventoryLots",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_TenantId_ProductId_LotNumber",
                table: "InventoryLots",
                columns: new[] { "TenantId", "ProductId", "LotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExportReceipts_SalesOrderId",
                table: "ExportReceipts",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ExportReceipts_ShipmentId",
                table: "ExportReceipts",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Asns_TenantId_WarehouseId_Code",
                table: "Asns",
                columns: new[] { "TenantId", "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Asns_WarehouseId",
                table: "Asns",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerDebtInvoices_CreatedBy",
                table: "PartnerDebtInvoices",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerDebtInvoices_PartnerId",
                table: "PartnerDebtInvoices",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerDebtInvoices_TenantId_PartnerId_DebtType_Status",
                table: "PartnerDebtInvoices",
                columns: new[] { "TenantId", "PartnerId", "DebtType", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_PartnerDebtInvoices_TenantId_DebtType_SourceType_SourceId",
                table: "PartnerDebtInvoices",
                columns: new[] { "TenantId", "DebtType", "SourceType", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PartnerDebtInvoices_TenantId_InvoiceCode",
                table: "PartnerDebtInvoices",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartnerDebtLedgers_CreatedBy",
                table: "PartnerDebtLedgers",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerDebtLedgers_InvoiceId",
                table: "PartnerDebtLedgers",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerDebtLedgers_PartnerId",
                table: "PartnerDebtLedgers",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_PartnerDebtLedgers_TenantId_InvoiceId_CreatedAtUtc",
                table: "PartnerDebtLedgers",
                columns: new[] { "TenantId", "InvoiceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PartnerDebtLedgers_TenantId_PartnerId_DebtType_CreatedAtUtc",
                table: "PartnerDebtLedgers",
                columns: new[] { "TenantId", "PartnerId", "DebtType", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartnerDebtLedgers");

            migrationBuilder.DropTable(
                name: "PartnerDebtInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_TenantId_WarehouseId_Code",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_WarehouseId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_TenantId_WarehouseId_ProductId_TransactionDate",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventorySerials_ProductId",
                table: "InventorySerials");

            migrationBuilder.DropIndex(
                name: "IX_InventorySerials_TenantId_ProductId_SerialNumber",
                table: "InventorySerials");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLots_ProductId",
                table: "InventoryLots");

            migrationBuilder.DropIndex(
                name: "IX_InventoryLots_TenantId_ProductId_LotNumber",
                table: "InventoryLots");

            migrationBuilder.DropIndex(
                name: "IX_ExportReceipts_SalesOrderId",
                table: "ExportReceipts");

            migrationBuilder.DropIndex(
                name: "IX_ExportReceipts_ShipmentId",
                table: "ExportReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Asns_TenantId_WarehouseId_Code",
                table: "Asns");

            migrationBuilder.DropIndex(
                name: "IX_Asns_WarehouseId",
                table: "Asns");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "TaxPercent",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "TaxPercent",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "InventoryStocks");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ImportReceipts");

            migrationBuilder.DropColumn(
                name: "TaxPercent",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "SalesOrderId",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "ShipmentId",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "TaxPercent",
                table: "ExportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "PayableBalance",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "PaymentTermDays",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "ReceivableBalance",
                table: "BusinessPartners");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_WarehouseId_Code",
                table: "PurchaseOrders",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventorySerials_ProductId_SerialNumber",
                table: "InventorySerials",
                columns: new[] { "ProductId", "SerialNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLots_ProductId_LotNumber",
                table: "InventoryLots",
                columns: new[] { "ProductId", "LotNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Asns_WarehouseId_Code",
                table: "Asns",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);
        }
    }
}
