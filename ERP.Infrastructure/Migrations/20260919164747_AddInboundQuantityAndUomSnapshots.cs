using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundQuantityAndUomSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DecimalPlaces",
                table: "Units",
                type: "int",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "AcceptedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseAcceptedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseDamagedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseExpectedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BasePostedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseReceivedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BaseRejectedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BaseUnitCodeSnapshot",
                table: "ImportReceiptDetails",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "BaseUnitDecimalPlaces",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BaseUnitId",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                table: "ImportReceiptDetails",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ConversionVersion",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DamagedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "OperationUnitCodeSnapshot",
                table: "ImportReceiptDetails",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "OperationUnitDecimalPlaces",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OperationUnitId",
                table: "ImportReceiptDetails",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PostedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ReceivedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RejectedQuantity",
                table: "ImportReceiptDetails",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ProductUoms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    UnitId = table.Column<int>(type: "int", nullable: false),
                    ConversionFactor = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductUoms", x => x.Id);
                    table.CheckConstraint("CK_ProductUoms_ConversionFactor", "[ConversionFactor] > 0");
                    table.ForeignKey(
                        name: "FK_ProductUoms_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductUoms_Units_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Units",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                UPDATE d SET
                    ExpectedQuantity = d.Quantity,
                    ReceivedQuantity = CASE WHEN r.Status IN (1,4,5,6) THEN d.Quantity ELSE 0 END,
                    AcceptedQuantity = CASE WHEN r.Status IN (1,4,5,6) THEN d.Quantity ELSE 0 END,
                    PostedQuantity = CASE WHEN r.Status IN (1,6) THEN d.Quantity ELSE 0 END,
                    OperationUnitId = p.UnitId,
                    OperationUnitCodeSnapshot = u.Code,
                    OperationUnitDecimalPlaces = u.DecimalPlaces,
                    BaseUnitId = p.UnitId,
                    BaseUnitCodeSnapshot = u.Code,
                    BaseUnitDecimalPlaces = u.DecimalPlaces,
                    ConversionFactor = 1,
                    ConversionVersion = 1,
                    BaseExpectedQuantity = d.Quantity,
                    BaseReceivedQuantity = CASE WHEN r.Status IN (1,4,5,6) THEN d.Quantity ELSE 0 END,
                    BaseAcceptedQuantity = CASE WHEN r.Status IN (1,4,5,6) THEN d.Quantity ELSE 0 END,
                    BasePostedQuantity = CASE WHEN r.Status IN (1,6) THEN d.Quantity ELSE 0 END
                FROM ImportReceiptDetails d
                JOIN ImportReceipts r ON r.Id = d.ImportReceiptId
                JOIN Products p ON p.Id = d.ProductId
                JOIN Units u ON u.Id = p.UnitId;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Units_DecimalPlaces",
                table: "Units",
                sql: "[DecimalPlaces] BETWEEN 0 AND 4");

            migrationBuilder.CreateIndex(
                name: "IX_ProductUoms_ProductId_UnitId_Version",
                table: "ProductUoms",
                columns: new[] { "ProductId", "UnitId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductUoms_UnitId",
                table: "ProductUoms",
                column: "UnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductUoms");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Units_DecimalPlaces",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "DecimalPlaces",
                table: "Units");

            migrationBuilder.DropColumn(
                name: "AcceptedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseAcceptedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseDamagedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseExpectedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BasePostedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseReceivedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseRejectedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseUnitCodeSnapshot",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseUnitDecimalPlaces",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseUnitId",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "ConversionVersion",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "DamagedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "ExpectedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "OperationUnitCodeSnapshot",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "OperationUnitDecimalPlaces",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "OperationUnitId",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "PostedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "ReceivedQuantity",
                table: "ImportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "RejectedQuantity",
                table: "ImportReceiptDetails");
        }
    }
}
