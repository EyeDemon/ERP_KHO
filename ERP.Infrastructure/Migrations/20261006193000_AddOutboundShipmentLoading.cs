using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261006193000_AddOutboundShipmentLoading")]
public sealed class AddOutboundShipmentLoading : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Shipments",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ShipmentCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                PackingSessionId = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                SourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                SourceId = table.Column<int>(type: "int", nullable: true),
                SourceCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                DockAppointmentId = table.Column<int>(type: "int", nullable: true),
                DockId = table.Column<int>(type: "int", nullable: true),
                VehiclePlate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                TrailerPlate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                SealNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                StagedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LoadingStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LoadedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shipments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Shipments_DockAppointments_DockAppointmentId",
                    column: x => x.DockAppointmentId,
                    principalTable: "DockAppointments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Shipments_Docks_DockId",
                    column: x => x.DockId,
                    principalTable: "Docks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Shipments_PackingSessions_PackingSessionId",
                    column: x => x.PackingSessionId,
                    principalTable: "PackingSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Shipments_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Shipments_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ShipmentHandlingUnits",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ShipmentId = table.Column<int>(type: "int", nullable: false),
                HandlingUnitId = table.Column<int>(type: "int", nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                StagedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LoadedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LoadedBy = table.Column<int>(type: "int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShipmentHandlingUnits", x => x.Id);
                table.ForeignKey(
                    name: "FK_ShipmentHandlingUnits_HandlingUnits_HandlingUnitId",
                    column: x => x.HandlingUnitId,
                    principalTable: "HandlingUnits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShipmentHandlingUnits_Shipments_ShipmentId",
                    column: x => x.ShipmentId,
                    principalTable: "Shipments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ShipmentHandlingUnits_Users_LoadedBy",
                    column: x => x.LoadedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_CreatedBy",
            table: "Shipments",
            column: "CreatedBy");
        migrationBuilder.CreateIndex(
            name: "IX_Shipments_DockAppointmentId",
            table: "Shipments",
            column: "DockAppointmentId");
        migrationBuilder.CreateIndex(
            name: "IX_Shipments_DockId",
            table: "Shipments",
            column: "DockId");
        migrationBuilder.CreateIndex(
            name: "IX_Shipments_PackingSessionId",
            table: "Shipments",
            column: "PackingSessionId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_Shipments_SourceType_SourceId",
            table: "Shipments",
            columns: new[] { "SourceType", "SourceId" });
        migrationBuilder.CreateIndex(
            name: "IX_Shipments_WarehouseId_ShipmentCode",
            table: "Shipments",
            columns: new[] { "WarehouseId", "ShipmentCode" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_Shipments_WarehouseId_Status_CreatedAt",
            table: "Shipments",
            columns: new[] { "WarehouseId", "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_ShipmentHandlingUnits_HandlingUnitId",
            table: "ShipmentHandlingUnits",
            column: "HandlingUnitId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_ShipmentHandlingUnits_LoadedBy",
            table: "ShipmentHandlingUnits",
            column: "LoadedBy");
        migrationBuilder.CreateIndex(
            name: "IX_ShipmentHandlingUnits_ShipmentId_Sequence",
            table: "ShipmentHandlingUnits",
            columns: new[] { "ShipmentId", "Sequence" },
            unique: true);

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='shipment.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('shipment.read',N'Xem Shipment',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='shipment.update')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('shipment.update',N'Cập nhật Shipment',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='shipment.stage')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('shipment.stage',N'Đưa Shipment vào staging',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='shipment.load')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('shipment.load',N'Thực hiện Shipment loading',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='loading.execute')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('loading.execute',N'Thực hiện loading tại dock',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('shipment.read','shipment.update','shipment.stage','shipment.load','loading.execute')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('shipment.read','shipment.stage','shipment.load','loading.execute')
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='shipment.read'
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
WHERE p.Code IN ('shipment.read','shipment.update','shipment.stage','shipment.load','loading.execute');
DELETE FROM Permissions
WHERE Code IN ('shipment.read','shipment.update','shipment.stage','shipment.load','loading.execute');
""");
        migrationBuilder.DropTable(name: "ShipmentHandlingUnits");
        migrationBuilder.DropTable(name: "Shipments");
    }
}
