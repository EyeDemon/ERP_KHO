using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261007150000_AddInventoryStatusLotSerialFoundation")]
public sealed class AddInventoryStatusLotSerialFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InventoryStatusDefinitions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false),
                Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                IsReservable = table.Column<bool>(type: "bit", nullable: false),
                IsAllocatable = table.Column<bool>(type: "bit", nullable: false),
                IsPickable = table.Column<bool>(type: "bit", nullable: false),
                IsShippable = table.Column<bool>(type: "bit", nullable: false),
                IsSystem = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_InventoryStatusDefinitions", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_InventoryStatusDefinitions_Code",
            table: "InventoryStatusDefinitions",
            column: "Code",
            unique: true);

        migrationBuilder.Sql("""
INSERT INTO InventoryStatusDefinitions
    (Id, Code, Name, IsAvailable, IsReservable, IsAllocatable, IsPickable, IsShippable, IsSystem)
VALUES
    (0, 'AVAILABLE', N'Available', 1, 1, 1, 1, 1, 1),
    (1, 'QC_HOLD', N'QC Hold', 0, 0, 0, 0, 0, 1),
    (2, 'QUARANTINE', N'Quarantine', 0, 0, 0, 0, 0, 1),
    (3, 'DAMAGED', N'Damaged', 0, 0, 0, 0, 0, 1),
    (4, 'REJECTED', N'Rejected', 0, 0, 0, 0, 0, 1),
    (5, 'BLOCKED', N'Blocked', 0, 0, 0, 0, 0, 1),
    (6, 'EXPIRED', N'Expired', 0, 0, 0, 0, 0, 1),
    (7, 'RECALL_BLOCKED', N'Recall Blocked', 0, 0, 0, 0, 0, 1);
""");

        migrationBuilder.AddColumn<int>(
            name: "TrackingType",
            table: "Products",
            type: "int",
            nullable: false,
            defaultValue: 0);
        migrationBuilder.AddColumn<bool>(
            name: "ExpiryControl",
            table: "Products",
            type: "bit",
            nullable: false,
            defaultValue: false);
        migrationBuilder.AddColumn<int>(
            name: "ShelfLifeDays",
            table: "Products",
            type: "int",
            nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_Products_TrackingPolicy",
            table: "Products",
            sql: "([ExpiryControl] = 0 OR [TrackingType] <> 0) AND ([ShelfLifeDays] IS NULL OR [ShelfLifeDays] > 0) AND ([ExpiryControl] = 1 OR [ShelfLifeDays] IS NULL)");

        migrationBuilder.CreateTable(
            name: "InventoryLots",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProductId = table.Column<int>(type: "int", nullable: false),
                LotNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ManufactureDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryLots", x => x.Id);
                table.CheckConstraint("CK_InventoryLots_Dates", "[ExpiryDate] IS NULL OR [ManufactureDate] IS NULL OR [ExpiryDate] >= [ManufactureDate]");
                table.ForeignKey(
                    name: "FK_InventoryLots_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryLots_ExpiryDate",
            table: "InventoryLots",
            column: "ExpiryDate");
        migrationBuilder.CreateIndex(
            name: "IX_InventoryLots_ProductId_LotNumber",
            table: "InventoryLots",
            columns: new[] { "ProductId", "LotNumber" },
            unique: true);

        migrationBuilder.CreateTable(
            name: "InventorySerials",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProductId = table.Column<int>(type: "int", nullable: false),
                LotId = table.Column<int>(type: "int", nullable: true),
                SerialNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventorySerials", x => x.Id);
                table.ForeignKey(
                    name: "FK_InventorySerials_InventoryLots_LotId",
                    column: x => x.LotId,
                    principalTable: "InventoryLots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InventorySerials_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_InventorySerials_LotId",
            table: "InventorySerials",
            column: "LotId");
        migrationBuilder.CreateIndex(
            name: "IX_InventorySerials_ProductId_SerialNumber",
            table: "InventorySerials",
            columns: new[] { "ProductId", "SerialNumber" },
            unique: true);

        migrationBuilder.CreateTable(
            name: "ImportReceiptInventoryIdentities",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ImportReceiptDetailId = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                TargetStatus = table.Column<int>(type: "int", nullable: false),
                BaseQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                LotNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                ManufactureDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                SerialNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ImportReceiptInventoryIdentities", x => x.Id);
                table.CheckConstraint("CK_ImportReceiptInventoryIdentities_Quantity", "[BaseQuantity] > 0");
                table.CheckConstraint("CK_ImportReceiptInventoryIdentities_SerialQuantity", "[SerialNumber] IS NULL OR [BaseQuantity] = 1");
                table.CheckConstraint("CK_ImportReceiptInventoryIdentities_Dates", "[ExpiryDate] IS NULL OR [ManufactureDate] IS NULL OR [ExpiryDate] >= [ManufactureDate]");
                table.ForeignKey(
                    name: "FK_ImportReceiptInventoryIdentities_ImportReceiptDetails_ImportReceiptDetailId",
                    column: x => x.ImportReceiptDetailId,
                    principalTable: "ImportReceiptDetails",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ImportReceiptInventoryIdentities_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ImportReceiptInventoryIdentities_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_ImportReceiptInventoryIdentities_CreatedBy",
            table: "ImportReceiptInventoryIdentities",
            column: "CreatedBy");
        migrationBuilder.CreateIndex(
            name: "IX_ImportReceiptInventoryIdentities_ImportReceiptDetailId_TargetStatus",
            table: "ImportReceiptInventoryIdentities",
            columns: new[] { "ImportReceiptDetailId", "TargetStatus" });
        migrationBuilder.CreateIndex(
            name: "IX_ImportReceiptInventoryIdentities_ProductId_SerialNumber",
            table: "ImportReceiptInventoryIdentities",
            columns: new[] { "ProductId", "SerialNumber" },
            unique: true,
            filter: "[SerialNumber] IS NOT NULL");

        migrationBuilder.DropIndex(
            name: "IX_InventoryStocks_ProductId_WarehouseId_Status_LocationId",
            table: "InventoryStocks");
        migrationBuilder.AddColumn<int>(name: "LotId", table: "InventoryStocks", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "SerialId", table: "InventoryStocks", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "CanonicalLocationId",
            table: "InventoryStocks",
            type: "int",
            nullable: false,
            computedColumnSql: "ISNULL([LocationId], 0)",
            stored: true);
        migrationBuilder.AddColumn<int>(
            name: "CanonicalLotId",
            table: "InventoryStocks",
            type: "int",
            nullable: false,
            computedColumnSql: "ISNULL([LotId], 0)",
            stored: true);
        migrationBuilder.AddColumn<int>(
            name: "CanonicalSerialId",
            table: "InventoryStocks",
            type: "int",
            nullable: false,
            computedColumnSql: "ISNULL([SerialId], 0)",
            stored: true);
        migrationBuilder.CreateIndex(name: "IX_InventoryStocks_LotId", table: "InventoryStocks", column: "LotId");
        migrationBuilder.CreateIndex(name: "IX_InventoryStocks_SerialId", table: "InventoryStocks", column: "SerialId");
        migrationBuilder.CreateIndex(name: "IX_InventoryStocks_Status", table: "InventoryStocks", column: "Status");
        migrationBuilder.Sql("""
CREATE UNIQUE INDEX IX_InventoryStocks_CanonicalBucket
ON InventoryStocks(ProductId, WarehouseId, Status, CanonicalLocationId, CanonicalLotId, CanonicalSerialId);
""");
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryStocks_InventoryLots_LotId",
            table: "InventoryStocks",
            column: "LotId",
            principalTable: "InventoryLots",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryStocks_InventorySerials_SerialId",
            table: "InventoryStocks",
            column: "SerialId",
            principalTable: "InventorySerials",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryStocks_InventoryStatusDefinitions_Status",
            table: "InventoryStocks",
            column: "Status",
            principalTable: "InventoryStatusDefinitions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropIndex(
            name: "IX_InventoryTransactions_ShipmentReference",
            table: "InventoryTransactions");
        migrationBuilder.AddColumn<int>(name: "LotId", table: "InventoryTransactions", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "SerialId", table: "InventoryTransactions", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "FromInventoryStatus", table: "InventoryTransactions", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "ToInventoryStatus", table: "InventoryTransactions", type: "int", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_InventoryTransactions_LotId", table: "InventoryTransactions", column: "LotId");
        migrationBuilder.CreateIndex(name: "IX_InventoryTransactions_SerialId", table: "InventoryTransactions", column: "SerialId");
        migrationBuilder.CreateIndex(name: "IX_InventoryTransactions_InventoryStatus", table: "InventoryTransactions", column: "InventoryStatus");
        migrationBuilder.CreateIndex(
            name: "IX_InventoryTransactions_ShipmentReference",
            table: "InventoryTransactions",
            columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId", "LocationId", "InventoryStatus", "LotId", "SerialId" },
            unique: true,
            filter: "[ReferenceType] = 'Shipment'");
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryTransactions_InventoryLots_LotId",
            table: "InventoryTransactions",
            column: "LotId",
            principalTable: "InventoryLots",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryTransactions_InventorySerials_SerialId",
            table: "InventoryTransactions",
            column: "SerialId",
            principalTable: "InventorySerials",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryTransactions_InventoryStatusDefinitions_InventoryStatus",
            table: "InventoryTransactions",
            column: "InventoryStatus",
            principalTable: "InventoryStatusDefinitions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropIndex(
            name: "IX_StockAllocations_WarehouseId_LocationId_ProductId_Status",
            table: "StockAllocations");
        migrationBuilder.DropIndex(
            name: "IX_StockAllocations_ReservationId_LocationId_InventoryStatus_Status",
            table: "StockAllocations");
        migrationBuilder.AddColumn<int>(name: "LotId", table: "StockAllocations", type: "int", nullable: true);
        migrationBuilder.AddColumn<int>(name: "SerialId", table: "StockAllocations", type: "int", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_StockAllocations_LotId", table: "StockAllocations", column: "LotId");
        migrationBuilder.CreateIndex(name: "IX_StockAllocations_SerialId", table: "StockAllocations", column: "SerialId");
        migrationBuilder.CreateIndex(
            name: "IX_StockAllocations_WarehouseId_LocationId_ProductId_LotId_SerialId_Status",
            table: "StockAllocations",
            columns: new[] { "WarehouseId", "LocationId", "ProductId", "LotId", "SerialId", "Status" });
        migrationBuilder.CreateIndex(
            name: "IX_StockAllocations_ReservationId_LocationId_InventoryStatus_LotId_SerialId_Status",
            table: "StockAllocations",
            columns: new[] { "ReservationId", "LocationId", "InventoryStatus", "LotId", "SerialId", "Status" });
        migrationBuilder.AddForeignKey(
            name: "FK_StockAllocations_InventoryLots_LotId",
            table: "StockAllocations",
            column: "LotId",
            principalTable: "InventoryLots",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_StockAllocations_InventorySerials_SerialId",
            table: "StockAllocations",
            column: "SerialId",
            principalTable: "InventorySerials",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('inventory.read',N'Xem inventory canonical',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory_availability.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('inventory_availability.read',N'Xem inventory availability',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory_ledger.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('inventory_ledger.read',N'Xem inventory ledger',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory_traceability.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('inventory_traceability.read',N'Xem Lot Serial traceability',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory_status_change.create')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('inventory_status_change.create',N'Tạo inventory status change',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('inventory.read','inventory_availability.read','inventory_ledger.read','inventory_traceability.read','inventory_status_change.create')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('inventory.read','inventory_availability.read','inventory_ledger.read','inventory_traceability.read')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('warehousestaff','viewer')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code IN ('inventory.read','inventory_availability.read','inventory_ledger.read','inventory_traceability.read','inventory_status_change.create');
DELETE FROM Permissions
WHERE Code IN ('inventory.read','inventory_availability.read','inventory_ledger.read','inventory_traceability.read','inventory_status_change.create');
""");

        migrationBuilder.DropForeignKey(name: "FK_StockAllocations_InventoryLots_LotId", table: "StockAllocations");
        migrationBuilder.DropForeignKey(name: "FK_StockAllocations_InventorySerials_SerialId", table: "StockAllocations");
        migrationBuilder.DropIndex(name: "IX_StockAllocations_WarehouseId_LocationId_ProductId_LotId_SerialId_Status", table: "StockAllocations");
        migrationBuilder.DropIndex(name: "IX_StockAllocations_ReservationId_LocationId_InventoryStatus_LotId_SerialId_Status", table: "StockAllocations");
        migrationBuilder.DropIndex(name: "IX_StockAllocations_LotId", table: "StockAllocations");
        migrationBuilder.DropIndex(name: "IX_StockAllocations_SerialId", table: "StockAllocations");
        migrationBuilder.DropColumn(name: "LotId", table: "StockAllocations");
        migrationBuilder.DropColumn(name: "SerialId", table: "StockAllocations");
        migrationBuilder.CreateIndex(
            name: "IX_StockAllocations_WarehouseId_LocationId_ProductId_Status",
            table: "StockAllocations",
            columns: new[] { "WarehouseId", "LocationId", "ProductId", "Status" });
        migrationBuilder.CreateIndex(
            name: "IX_StockAllocations_ReservationId_LocationId_InventoryStatus_Status",
            table: "StockAllocations",
            columns: new[] { "ReservationId", "LocationId", "InventoryStatus", "Status" });

        migrationBuilder.DropForeignKey(name: "FK_InventoryTransactions_InventoryLots_LotId", table: "InventoryTransactions");
        migrationBuilder.DropForeignKey(name: "FK_InventoryTransactions_InventorySerials_SerialId", table: "InventoryTransactions");
        migrationBuilder.DropForeignKey(name: "FK_InventoryTransactions_InventoryStatusDefinitions_InventoryStatus", table: "InventoryTransactions");
        migrationBuilder.DropIndex(name: "IX_InventoryTransactions_LotId", table: "InventoryTransactions");
        migrationBuilder.DropIndex(name: "IX_InventoryTransactions_SerialId", table: "InventoryTransactions");
        migrationBuilder.DropIndex(name: "IX_InventoryTransactions_InventoryStatus", table: "InventoryTransactions");
        migrationBuilder.DropIndex(name: "IX_InventoryTransactions_ShipmentReference", table: "InventoryTransactions");
        migrationBuilder.DropColumn(name: "LotId", table: "InventoryTransactions");
        migrationBuilder.DropColumn(name: "SerialId", table: "InventoryTransactions");
        migrationBuilder.DropColumn(name: "FromInventoryStatus", table: "InventoryTransactions");
        migrationBuilder.DropColumn(name: "ToInventoryStatus", table: "InventoryTransactions");
        migrationBuilder.CreateIndex(
            name: "IX_InventoryTransactions_ShipmentReference",
            table: "InventoryTransactions",
            columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId", "LocationId", "InventoryStatus" },
            unique: true,
            filter: "[ReferenceType] = 'Shipment'");

        migrationBuilder.DropForeignKey(name: "FK_InventoryStocks_InventoryLots_LotId", table: "InventoryStocks");
        migrationBuilder.DropForeignKey(name: "FK_InventoryStocks_InventorySerials_SerialId", table: "InventoryStocks");
        migrationBuilder.DropForeignKey(name: "FK_InventoryStocks_InventoryStatusDefinitions_Status", table: "InventoryStocks");
        migrationBuilder.DropIndex(name: "IX_InventoryStocks_LotId", table: "InventoryStocks");
        migrationBuilder.DropIndex(name: "IX_InventoryStocks_SerialId", table: "InventoryStocks");
        migrationBuilder.DropIndex(name: "IX_InventoryStocks_Status", table: "InventoryStocks");
        migrationBuilder.DropIndex(name: "IX_InventoryStocks_CanonicalBucket", table: "InventoryStocks");
        migrationBuilder.DropColumn(name: "CanonicalLocationId", table: "InventoryStocks");
        migrationBuilder.DropColumn(name: "CanonicalLotId", table: "InventoryStocks");
        migrationBuilder.DropColumn(name: "CanonicalSerialId", table: "InventoryStocks");
        migrationBuilder.DropColumn(name: "LotId", table: "InventoryStocks");
        migrationBuilder.DropColumn(name: "SerialId", table: "InventoryStocks");
        migrationBuilder.CreateIndex(
            name: "IX_InventoryStocks_ProductId_WarehouseId_Status_LocationId",
            table: "InventoryStocks",
            columns: new[] { "ProductId", "WarehouseId", "Status", "LocationId" },
            unique: true);

        migrationBuilder.DropTable(name: "ImportReceiptInventoryIdentities");
        migrationBuilder.DropTable(name: "InventorySerials");
        migrationBuilder.DropTable(name: "InventoryLots");

        migrationBuilder.DropCheckConstraint(name: "CK_Products_TrackingPolicy", table: "Products");
        migrationBuilder.DropColumn(name: "TrackingType", table: "Products");
        migrationBuilder.DropColumn(name: "ExpiryControl", table: "Products");
        migrationBuilder.DropColumn(name: "ShelfLifeDays", table: "Products");

        migrationBuilder.DropTable(name: "InventoryStatusDefinitions");
    }
}
