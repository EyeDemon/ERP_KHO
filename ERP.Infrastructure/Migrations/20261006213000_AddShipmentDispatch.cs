using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261006213000_AddShipmentDispatch")]
public sealed class AddShipmentDispatch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DispatchedAt",
            table: "Shipments",
            type: "datetime2",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DispatchedBy",
            table: "Shipments",
            type: "int",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_DispatchedBy",
            table: "Shipments",
            column: "DispatchedBy");

        migrationBuilder.AddForeignKey(
            name: "FK_Shipments_Users_DispatchedBy",
            table: "Shipments",
            column: "DispatchedBy",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateIndex(
            name: "IX_InventoryTransactions_ShipmentReference",
            table: "InventoryTransactions",
            columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId", "LocationId" },
            unique: true,
            filter: "[ReferenceType] = 'Shipment'");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='shipment.dispatch')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('shipment.dispatch',N'Dispatch Shipment và ghi SHIP ledger',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='shipment.dispatch'
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code='shipment.dispatch';
DELETE FROM Permissions WHERE Code='shipment.dispatch';
""");

        migrationBuilder.DropIndex(
            name: "IX_InventoryTransactions_ShipmentReference",
            table: "InventoryTransactions");

        migrationBuilder.DropForeignKey(
            name: "FK_Shipments_Users_DispatchedBy",
            table: "Shipments");

        migrationBuilder.DropIndex(
            name: "IX_Shipments_DispatchedBy",
            table: "Shipments");

        migrationBuilder.DropColumn(
            name: "DispatchedAt",
            table: "Shipments");

        migrationBuilder.DropColumn(
            name: "DispatchedBy",
            table: "Shipments");
    }
}
