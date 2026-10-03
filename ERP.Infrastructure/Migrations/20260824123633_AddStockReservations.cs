using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReservedQuantity",
                table: "InventoryStocks",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "StockReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConsumedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReleasedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: true),
                    SourceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedBy = table.Column<int>(type: "int", nullable: true),
                    ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                    table.CheckConstraint("CK_StockReservations_Expiry", "[ExpiresAt] > [CreatedAt]");
                    table.CheckConstraint("CK_StockReservations_Quantity", "[Quantity] > 0 AND [ConsumedQuantity] >= 0 AND [ReleasedQuantity] >= 0 AND [ConsumedQuantity] + [ReleasedQuantity] <= [Quantity]");
                    table.ForeignKey(
                        name: "FK_StockReservations_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_Users_ReleasedBy",
                        column: x => x.ReleasedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockReservations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryStocks_Reservation",
                table: "InventoryStocks",
                sql: "[ReservedQuantity] >= 0 AND [Quantity] >= [ReservedQuantity]");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_CreatedBy",
                table: "StockReservations",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ProductId_WarehouseId_Status_ExpiresAt",
                table: "StockReservations",
                columns: new[] { "ProductId", "WarehouseId", "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ReleasedBy",
                table: "StockReservations",
                column: "ReleasedBy");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_ReservationCode",
                table: "StockReservations",
                column: "ReservationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_SourceType_SourceId_ProductId",
                table: "StockReservations",
                columns: new[] { "SourceType", "SourceId", "ProductId" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_WarehouseId",
                table: "StockReservations",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockReservations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryStocks_Reservation",
                table: "InventoryStocks");

            migrationBuilder.DropColumn(
                name: "ReservedQuantity",
                table: "InventoryStocks");
        }
    }
}
