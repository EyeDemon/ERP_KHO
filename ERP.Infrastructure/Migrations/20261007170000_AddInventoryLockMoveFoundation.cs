using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261007170000_AddInventoryLockMoveFoundation")]
public sealed class AddInventoryLockMoveFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "FromLocationId",
            table: "InventoryTransactions",
            type: "int",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ToLocationId",
            table: "InventoryTransactions",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_InventoryTransactions_FromLocationId",
            table: "InventoryTransactions",
            column: "FromLocationId");
        migrationBuilder.CreateIndex(
            name: "IX_InventoryTransactions_ToLocationId",
            table: "InventoryTransactions",
            column: "ToLocationId");
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryTransactions_WarehouseLocations_FromLocationId",
            table: "InventoryTransactions",
            column: "FromLocationId",
            principalTable: "WarehouseLocations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryTransactions_WarehouseLocations_ToLocationId",
            table: "InventoryTransactions",
            column: "ToLocationId",
            principalTable: "WarehouseLocations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropForeignKey(
            name: "FK_InventoryLocationMovements_ImportReceiptDetails_ReceiptLineId",
            table: "InventoryLocationMovements");
        migrationBuilder.DropForeignKey(
            name: "FK_InventoryLocationMovements_ImportReceipts_ReceiptId",
            table: "InventoryLocationMovements");
        migrationBuilder.DropForeignKey(
            name: "FK_InventoryLocationMovements_PutawayTaskItems_PutawayTaskItemId",
            table: "InventoryLocationMovements");
        migrationBuilder.DropForeignKey(
            name: "FK_InventoryLocationMovements_PutawayTasks_PutawayTaskId",
            table: "InventoryLocationMovements");

        migrationBuilder.AlterColumn<int>(
            name: "ReceiptLineId",
            table: "InventoryLocationMovements",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");
        migrationBuilder.AlterColumn<int>(
            name: "ReceiptId",
            table: "InventoryLocationMovements",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");
        migrationBuilder.AlterColumn<int>(
            name: "PutawayTaskItemId",
            table: "InventoryLocationMovements",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");
        migrationBuilder.AlterColumn<int>(
            name: "PutawayTaskId",
            table: "InventoryLocationMovements",
            type: "int",
            nullable: true,
            oldClrType: typeof(int),
            oldType: "int");

        migrationBuilder.AddColumn<int>(
            name: "LotId",
            table: "InventoryLocationMovements",
            type: "int",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "SerialId",
            table: "InventoryLocationMovements",
            type: "int",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "ReferenceType",
            table: "InventoryLocationMovements",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "ReferenceId",
            table: "InventoryLocationMovements",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_InventoryLocationMovements_LotId",
            table: "InventoryLocationMovements",
            column: "LotId");
        migrationBuilder.CreateIndex(
            name: "IX_InventoryLocationMovements_SerialId",
            table: "InventoryLocationMovements",
            column: "SerialId");
        migrationBuilder.CreateIndex(
            name: "IX_InventoryLocationMovements_ReferenceType_ReferenceId",
            table: "InventoryLocationMovements",
            columns: new[] { "ReferenceType", "ReferenceId" });

        migrationBuilder.AddForeignKey(
            name: "FK_InventoryLocationMovements_InventoryLots_LotId",
            table: "InventoryLocationMovements",
            column: "LotId",
            principalTable: "InventoryLots",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryLocationMovements_InventorySerials_SerialId",
            table: "InventoryLocationMovements",
            column: "SerialId",
            principalTable: "InventorySerials",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryLocationMovements_ImportReceiptDetails_ReceiptLineId",
            table: "InventoryLocationMovements",
            column: "ReceiptLineId",
            principalTable: "ImportReceiptDetails",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryLocationMovements_ImportReceipts_ReceiptId",
            table: "InventoryLocationMovements",
            column: "ReceiptId",
            principalTable: "ImportReceipts",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryLocationMovements_PutawayTaskItems_PutawayTaskItemId",
            table: "InventoryLocationMovements",
            column: "PutawayTaskItemId",
            principalTable: "PutawayTaskItems",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_InventoryLocationMovements_PutawayTasks_PutawayTaskId",
            table: "InventoryLocationMovements",
            column: "PutawayTaskId",
            principalTable: "PutawayTasks",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable(
            name: "InventoryLocks",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                LockType = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                LocationId = table.Column<int>(type: "int", nullable: true),
                ProductId = table.Column<int>(type: "int", nullable: true),
                InventoryStatus = table.Column<int>(type: "int", nullable: true),
                LotId = table.Column<int>(type: "int", nullable: true),
                SerialId = table.Column<int>(type: "int", nullable: true),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReleasedBy = table.Column<int>(type: "int", nullable: true),
                ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InventoryLocks", x => x.Id);
                table.CheckConstraint("CK_InventoryLocks_Expiry", "[ExpiresAt] IS NULL OR [ExpiresAt] > [CreatedAt]");
                table.ForeignKey(
                    name: "FK_InventoryLocks_InventoryLots_LotId",
                    column: x => x.LotId,
                    principalTable: "InventoryLots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InventoryLocks_InventorySerials_SerialId",
                    column: x => x.SerialId,
                    principalTable: "InventorySerials",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InventoryLocks_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InventoryLocks_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InventoryLocks_Users_ReleasedBy",
                    column: x => x.ReleasedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InventoryLocks_WarehouseLocations_LocationId",
                    column: x => x.LocationId,
                    principalTable: "WarehouseLocations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InventoryLocks_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_InventoryLocks_CreatedBy", table: "InventoryLocks", column: "CreatedBy");
        migrationBuilder.CreateIndex(name: "IX_InventoryLocks_ReleasedBy", table: "InventoryLocks", column: "ReleasedBy");
        migrationBuilder.CreateIndex(name: "IX_InventoryLocks_LocationId_Status", table: "InventoryLocks", columns: new[] { "LocationId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_InventoryLocks_ProductId_Status", table: "InventoryLocks", columns: new[] { "ProductId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_InventoryLocks_InventoryStatus_Status", table: "InventoryLocks", columns: new[] { "InventoryStatus", "Status" });
        migrationBuilder.CreateIndex(name: "IX_InventoryLocks_LotId_Status", table: "InventoryLocks", columns: new[] { "LotId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_InventoryLocks_SerialId_Status", table: "InventoryLocks", columns: new[] { "SerialId", "Status" });
        migrationBuilder.CreateIndex(name: "IX_InventoryLocks_WarehouseId_Status", table: "InventoryLocks", columns: new[] { "WarehouseId", "Status" });

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory_lock.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('inventory_lock.read',N'Xem Inventory Lock / Freeze',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory_lock.manage')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('inventory_lock.manage',N'Tạo và release Inventory Lock / Freeze',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory_movement.create')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('inventory_movement.create',N'Tạo internal inventory location move',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('inventory_lock.read','inventory_lock.manage','inventory_movement.create')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('inventory_lock.read','inventory_movement.create')
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='inventory_lock.read'
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='viewer'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code IN ('inventory_lock.read','inventory_lock.manage','inventory_movement.create');
DELETE FROM Permissions
WHERE Code IN ('inventory_lock.read','inventory_lock.manage','inventory_movement.create');
""");

        migrationBuilder.DropTable(name: "InventoryLocks");

        migrationBuilder.DropForeignKey(name: "FK_InventoryTransactions_WarehouseLocations_FromLocationId", table: "InventoryTransactions");
        migrationBuilder.DropForeignKey(name: "FK_InventoryTransactions_WarehouseLocations_ToLocationId", table: "InventoryTransactions");
        migrationBuilder.DropIndex(name: "IX_InventoryTransactions_FromLocationId", table: "InventoryTransactions");
        migrationBuilder.DropIndex(name: "IX_InventoryTransactions_ToLocationId", table: "InventoryTransactions");
        migrationBuilder.DropColumn(name: "FromLocationId", table: "InventoryTransactions");
        migrationBuilder.DropColumn(name: "ToLocationId", table: "InventoryTransactions");

        migrationBuilder.DropForeignKey(name: "FK_InventoryLocationMovements_InventoryLots_LotId", table: "InventoryLocationMovements");
        migrationBuilder.DropForeignKey(name: "FK_InventoryLocationMovements_InventorySerials_SerialId", table: "InventoryLocationMovements");
        migrationBuilder.DropIndex(name: "IX_InventoryLocationMovements_LotId", table: "InventoryLocationMovements");
        migrationBuilder.DropIndex(name: "IX_InventoryLocationMovements_SerialId", table: "InventoryLocationMovements");
        migrationBuilder.DropIndex(name: "IX_InventoryLocationMovements_ReferenceType_ReferenceId", table: "InventoryLocationMovements");
        migrationBuilder.DropColumn(name: "LotId", table: "InventoryLocationMovements");
        migrationBuilder.DropColumn(name: "SerialId", table: "InventoryLocationMovements");
        migrationBuilder.DropColumn(name: "ReferenceType", table: "InventoryLocationMovements");
        migrationBuilder.DropColumn(name: "ReferenceId", table: "InventoryLocationMovements");

        migrationBuilder.DropForeignKey(name: "FK_InventoryLocationMovements_ImportReceiptDetails_ReceiptLineId", table: "InventoryLocationMovements");
        migrationBuilder.DropForeignKey(name: "FK_InventoryLocationMovements_ImportReceipts_ReceiptId", table: "InventoryLocationMovements");
        migrationBuilder.DropForeignKey(name: "FK_InventoryLocationMovements_PutawayTaskItems_PutawayTaskItemId", table: "InventoryLocationMovements");
        migrationBuilder.DropForeignKey(name: "FK_InventoryLocationMovements_PutawayTasks_PutawayTaskId", table: "InventoryLocationMovements");

        migrationBuilder.AlterColumn<int>(name: "ReceiptLineId", table: "InventoryLocationMovements", type: "int", nullable: false, defaultValue: 0, oldClrType: typeof(int), oldType: "int", oldNullable: true);
        migrationBuilder.AlterColumn<int>(name: "ReceiptId", table: "InventoryLocationMovements", type: "int", nullable: false, defaultValue: 0, oldClrType: typeof(int), oldType: "int", oldNullable: true);
        migrationBuilder.AlterColumn<int>(name: "PutawayTaskItemId", table: "InventoryLocationMovements", type: "int", nullable: false, defaultValue: 0, oldClrType: typeof(int), oldType: "int", oldNullable: true);
        migrationBuilder.AlterColumn<int>(name: "PutawayTaskId", table: "InventoryLocationMovements", type: "int", nullable: false, defaultValue: 0, oldClrType: typeof(int), oldType: "int", oldNullable: true);

        migrationBuilder.AddForeignKey(name: "FK_InventoryLocationMovements_ImportReceiptDetails_ReceiptLineId", table: "InventoryLocationMovements", column: "ReceiptLineId", principalTable: "ImportReceiptDetails", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_InventoryLocationMovements_ImportReceipts_ReceiptId", table: "InventoryLocationMovements", column: "ReceiptId", principalTable: "ImportReceipts", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_InventoryLocationMovements_PutawayTaskItems_PutawayTaskItemId", table: "InventoryLocationMovements", column: "PutawayTaskItemId", principalTable: "PutawayTaskItems", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "FK_InventoryLocationMovements_PutawayTasks_PutawayTaskId", table: "InventoryLocationMovements", column: "PutawayTaskId", principalTable: "PutawayTasks", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
    }
}
