using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Wave60RoadmapEpics601To603 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CrossDockAllocatedQty",
                table: "PutawayTasks",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CrossDockTargetBackorderId",
                table: "PutawayTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CrossDockTargetExportReceiptId",
                table: "PutawayTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CrossDockTargetSalesOrderId",
                table: "PutawayTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCrossDock",
                table: "PutawayTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "KitWorkOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    KitProductId = table.Column<int>(type: "int", nullable: false),
                    WorkOrderType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PlannedKitQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    CompletedKitQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    TargetLocationId = table.Column<int>(type: "int", nullable: true),
                    TargetLotId = table.Column<int>(type: "int", nullable: true),
                    TargetHandlingUnitId = table.Column<int>(type: "int", nullable: true),
                    RolledUpUnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    EarliestComponentExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitWorkOrders", x => x.Id);
                    table.CheckConstraint("CK_KitWorkOrders_CompletedKitQty", "[CompletedKitQty] >= 0");
                    table.CheckConstraint("CK_KitWorkOrders_PlannedKitQty", "[PlannedKitQty] > 0");
                    table.CheckConstraint("CK_KitWorkOrders_RolledUpUnitCost", "[RolledUpUnitCost] >= 0");
                    table.ForeignKey(
                        name: "FK_KitWorkOrders_HandlingUnits_TargetHandlingUnitId",
                        column: x => x.TargetHandlingUnitId,
                        principalTable: "HandlingUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitWorkOrders_InventoryLots_TargetLotId",
                        column: x => x.TargetLotId,
                        principalTable: "InventoryLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitWorkOrders_Products_KitProductId",
                        column: x => x.KitProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitWorkOrders_WarehouseLocations_TargetLocationId",
                        column: x => x.TargetLocationId,
                        principalTable: "WarehouseLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitWorkOrders_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KitWorkOrderComponentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    KitWorkOrderId = table.Column<int>(type: "int", nullable: false),
                    ComponentProductId = table.Column<int>(type: "int", nullable: false),
                    ComponentRatioQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    RequiredBaseQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    ConsumedBaseQty = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    SourceLocationId = table.Column<int>(type: "int", nullable: true),
                    SourceLotId = table.Column<int>(type: "int", nullable: true),
                    ComponentUnitCost = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitWorkOrderComponentLines", x => x.Id);
                    table.CheckConstraint("CK_KitWorkOrderComponentLines_ComponentRatioQty", "[ComponentRatioQty] > 0");
                    table.CheckConstraint("CK_KitWorkOrderComponentLines_ComponentUnitCost", "[ComponentUnitCost] >= 0");
                    table.CheckConstraint("CK_KitWorkOrderComponentLines_ConsumedBaseQty", "[ConsumedBaseQty] >= 0");
                    table.CheckConstraint("CK_KitWorkOrderComponentLines_RequiredBaseQty", "[RequiredBaseQty] > 0");
                    table.ForeignKey(
                        name: "FK_KitWorkOrderComponentLines_InventoryLots_SourceLotId",
                        column: x => x.SourceLotId,
                        principalTable: "InventoryLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitWorkOrderComponentLines_KitWorkOrders_KitWorkOrderId",
                        column: x => x.KitWorkOrderId,
                        principalTable: "KitWorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KitWorkOrderComponentLines_Products_ComponentProductId",
                        column: x => x.ComponentProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KitWorkOrderComponentLines_WarehouseLocations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "WarehouseLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrderComponentLines_ComponentProductId",
                table: "KitWorkOrderComponentLines",
                column: "ComponentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrderComponentLines_KitWorkOrderId",
                table: "KitWorkOrderComponentLines",
                column: "KitWorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrderComponentLines_SourceLocationId",
                table: "KitWorkOrderComponentLines",
                column: "SourceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrderComponentLines_SourceLotId",
                table: "KitWorkOrderComponentLines",
                column: "SourceLotId");

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrderComponentLines_TenantId_KitWorkOrderId_ComponentProductId",
                table: "KitWorkOrderComponentLines",
                columns: new[] { "TenantId", "KitWorkOrderId", "ComponentProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrders_KitProductId",
                table: "KitWorkOrders",
                column: "KitProductId");

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrders_TargetHandlingUnitId",
                table: "KitWorkOrders",
                column: "TargetHandlingUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrders_TargetLocationId",
                table: "KitWorkOrders",
                column: "TargetLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrders_TargetLotId",
                table: "KitWorkOrders",
                column: "TargetLotId");

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrders_TenantId_WarehouseId_WorkOrderCode",
                table: "KitWorkOrders",
                columns: new[] { "TenantId", "WarehouseId", "WorkOrderCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KitWorkOrders_WarehouseId",
                table: "KitWorkOrders",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KitWorkOrderComponentLines");

            migrationBuilder.DropTable(
                name: "KitWorkOrders");

            migrationBuilder.DropColumn(
                name: "CrossDockAllocatedQty",
                table: "PutawayTasks");

            migrationBuilder.DropColumn(
                name: "CrossDockTargetBackorderId",
                table: "PutawayTasks");

            migrationBuilder.DropColumn(
                name: "CrossDockTargetExportReceiptId",
                table: "PutawayTasks");

            migrationBuilder.DropColumn(
                name: "CrossDockTargetSalesOrderId",
                table: "PutawayTasks");

            migrationBuilder.DropColumn(
                name: "IsCrossDock",
                table: "PutawayTasks");
        }
    }
}
