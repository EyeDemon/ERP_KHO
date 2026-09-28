using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundPutawayLocationMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StocktakeDetails_StocktakeId",
                table: "StocktakeDetails");

            migrationBuilder.DropIndex(
                name: "IX_InventoryStocks_ProductId_WarehouseId_Status",
                table: "InventoryStocks");

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "StocktakeDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "InventoryTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "InventoryStocks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PutawayTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AssignedUserId = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExceptionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PutawayTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PutawayTasks_ImportReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "ImportReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PutawayTasks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WarehouseLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LocationType = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    IsPickable = table.Column<bool>(type: "bit", nullable: false),
                    IsReceivable = table.Column<bool>(type: "bit", nullable: false),
                    IsSystemManaged = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehouseLocations", x => x.Id);
                    table.CheckConstraint("CK_WarehouseLocations_Code", "[Code] = UPPER(LTRIM(RTRIM([Code]))) AND LEN([Code]) > 0");
                    table.ForeignKey(
                        name: "FK_WarehouseLocations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PutawayTaskItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PutawayTaskId = table.Column<int>(type: "int", nullable: false),
                    ReceiptLineId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    InventoryStatus = table.Column<int>(type: "int", nullable: false),
                    SourceLocationId = table.Column<int>(type: "int", nullable: false),
                    OperationUnitId = table.Column<int>(type: "int", nullable: false),
                    OperationUnitCodeSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaseUnitId = table.Column<int>(type: "int", nullable: false),
                    BaseUnitCodeSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConversionFactorSnapshot = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConversionVersionSnapshot = table.Column<int>(type: "int", nullable: false),
                    BaseUnitDecimalPlaces = table.Column<int>(type: "int", nullable: false),
                    RequiredOperationQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RequiredBaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MovedBaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PutawayTaskItems", x => x.Id);
                    table.CheckConstraint("CK_PutawayTaskItems_Moved", "[MovedBaseQuantity] >= 0 AND [MovedBaseQuantity] <= [RequiredBaseQuantity]");
                    table.CheckConstraint("CK_PutawayTaskItems_Required", "[RequiredBaseQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_PutawayTaskItems_ImportReceiptDetails_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ImportReceiptDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PutawayTaskItems_PutawayTasks_PutawayTaskId",
                        column: x => x.PutawayTaskId,
                        principalTable: "PutawayTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PutawayTaskItems_WarehouseLocations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "WarehouseLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryLocationMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    InventoryStatus = table.Column<int>(type: "int", nullable: false),
                    FromLocationId = table.Column<int>(type: "int", nullable: false),
                    ToLocationId = table.Column<int>(type: "int", nullable: false),
                    BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EnteredQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    EnteredUnitCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PutawayTaskId = table.Column<int>(type: "int", nullable: false),
                    PutawayTaskItemId = table.Column<int>(type: "int", nullable: false),
                    ReceiptId = table.Column<int>(type: "int", nullable: false),
                    ReceiptLineId = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryLocationMovements", x => x.Id);
                    table.CheckConstraint("CK_InventoryLocationMovements_Locations", "[FromLocationId] <> [ToLocationId]");
                    table.CheckConstraint("CK_InventoryLocationMovements_Quantity", "[BaseQuantity] > 0");
                    table.ForeignKey(
                        name: "FK_InventoryLocationMovements_ImportReceiptDetails_ReceiptLineId",
                        column: x => x.ReceiptLineId,
                        principalTable: "ImportReceiptDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationMovements_ImportReceipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "ImportReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationMovements_PutawayTaskItems_PutawayTaskItemId",
                        column: x => x.PutawayTaskItemId,
                        principalTable: "PutawayTaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationMovements_PutawayTasks_PutawayTaskId",
                        column: x => x.PutawayTaskId,
                        principalTable: "PutawayTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationMovements_WarehouseLocations_FromLocationId",
                        column: x => x.FromLocationId,
                        principalTable: "WarehouseLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationMovements_WarehouseLocations_ToLocationId",
                        column: x => x.ToLocationId,
                        principalTable: "WarehouseLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLocationMovements_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Existing warehouse stock has no trustworthy receipt/location history. Place it in
            // deterministic system LEGACY buckets without inventing movements or putaway tasks.
            migrationBuilder.Sql("""
                INSERT INTO WarehouseLocations
                    (WarehouseId, Code, Name, LocationType, IsActive, IsBlocked, IsPickable, IsReceivable, IsSystemManaged, CreatedAt, CreatedBy)
                SELECT w.Id, v.Code, v.Name, v.LocationType, 1, 0, v.IsPickable, v.IsReceivable, 1, SYSUTCDATETIME(), 0
                FROM Warehouses w
                CROSS APPLY (VALUES
                    ('RECEIVING', N'Vị trí nhận hàng', 0, CAST(0 AS bit), CAST(1 AS bit)),
                    ('LEGACY', N'Tồn kho kế thừa', 4, CAST(1 AS bit), CAST(0 AS bit)),
                    ('LEGACY-DAMAGED', N'Hàng hư hỏng kế thừa', 2, CAST(0 AS bit), CAST(0 AS bit)),
                    ('LEGACY-REJECTED', N'Hàng từ chối kế thừa', 3, CAST(0 AS bit), CAST(0 AS bit))
                ) v(Code, Name, LocationType, IsPickable, IsReceivable)
                WHERE NOT EXISTS (
                    SELECT 1 FROM WarehouseLocations l WHERE l.WarehouseId = w.Id AND l.Code = v.Code
                );

                UPDATE s
                SET LocationId = l.Id
                FROM InventoryStocks s
                JOIN WarehouseLocations l ON l.WarehouseId = s.WarehouseId
                  AND l.Code = CASE s.Status WHEN 1 THEN 'LEGACY-DAMAGED' WHEN 2 THEN 'LEGACY-REJECTED' ELSE 'LEGACY' END
                WHERE s.LocationId IS NULL;

                IF EXISTS (SELECT 1 FROM InventoryStocks WHERE LocationId IS NULL)
                    THROW 51031, 'InventoryStock location backfill incomplete.', 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "LocationId",
                table: "InventoryStocks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeDetails_LocationId",
                table: "StocktakeDetails",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeDetails_StocktakeId_ProductId_LocationId",
                table: "StocktakeDetails",
                columns: new[] { "StocktakeId", "ProductId", "LocationId" },
                unique: true,
                filter: "[LocationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_LocationId",
                table: "InventoryTransactions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_LocationId",
                table: "InventoryStocks",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_ProductId_WarehouseId_Status_LocationId",
                table: "InventoryStocks",
                columns: new[] { "ProductId", "WarehouseId", "Status", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationMovements_FromLocationId",
                table: "InventoryLocationMovements",
                column: "FromLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationMovements_ProductId",
                table: "InventoryLocationMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationMovements_PutawayTaskId",
                table: "InventoryLocationMovements",
                column: "PutawayTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationMovements_PutawayTaskItemId_CreatedAt",
                table: "InventoryLocationMovements",
                columns: new[] { "PutawayTaskItemId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationMovements_ReceiptId",
                table: "InventoryLocationMovements",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationMovements_ReceiptLineId",
                table: "InventoryLocationMovements",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationMovements_ToLocationId",
                table: "InventoryLocationMovements",
                column: "ToLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLocationMovements_WarehouseId",
                table: "InventoryLocationMovements",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PutawayTaskItems_PutawayTaskId_ReceiptLineId_InventoryStatus",
                table: "PutawayTaskItems",
                columns: new[] { "PutawayTaskId", "ReceiptLineId", "InventoryStatus" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PutawayTaskItems_ReceiptLineId",
                table: "PutawayTaskItems",
                column: "ReceiptLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PutawayTaskItems_SourceLocationId",
                table: "PutawayTaskItems",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PutawayTasks_ReceiptId",
                table: "PutawayTasks",
                column: "ReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PutawayTasks_WarehouseId",
                table: "PutawayTasks",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseLocations_WarehouseId_Code",
                table: "WarehouseLocations",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryStocks_WarehouseLocations_LocationId",
                table: "InventoryStocks",
                column: "LocationId",
                principalTable: "WarehouseLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryTransactions_WarehouseLocations_LocationId",
                table: "InventoryTransactions",
                column: "LocationId",
                principalTable: "WarehouseLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StocktakeDetails_WarehouseLocations_LocationId",
                table: "StocktakeDetails",
                column: "LocationId",
                principalTable: "WarehouseLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryStocks_WarehouseLocations_LocationId",
                table: "InventoryStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryTransactions_WarehouseLocations_LocationId",
                table: "InventoryTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_StocktakeDetails_WarehouseLocations_LocationId",
                table: "StocktakeDetails");

            migrationBuilder.DropTable(
                name: "InventoryLocationMovements");

            migrationBuilder.DropTable(
                name: "PutawayTaskItems");

            migrationBuilder.DropTable(
                name: "PutawayTasks");

            migrationBuilder.DropTable(
                name: "WarehouseLocations");

            migrationBuilder.DropIndex(
                name: "IX_StocktakeDetails_LocationId",
                table: "StocktakeDetails");

            migrationBuilder.DropIndex(
                name: "IX_StocktakeDetails_StocktakeId_ProductId_LocationId",
                table: "StocktakeDetails");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_LocationId",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InventoryStocks_LocationId",
                table: "InventoryStocks");

            migrationBuilder.DropIndex(
                name: "IX_InventoryStocks_ProductId_WarehouseId_Status_LocationId",
                table: "InventoryStocks");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "StocktakeDetails");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "InventoryStocks");

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeDetails_StocktakeId",
                table: "StocktakeDetails",
                column: "StocktakeId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryStocks_ProductId_WarehouseId_Status",
                table: "InventoryStocks",
                columns: new[] { "ProductId", "WarehouseId", "Status" },
                unique: true);
        }
    }
}
