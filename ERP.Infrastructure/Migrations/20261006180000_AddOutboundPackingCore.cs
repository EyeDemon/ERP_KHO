using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261006180000_AddOutboundPackingCore")]
public sealed class AddOutboundPackingCore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PackingSessions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SessionCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                PickingTaskId = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                PackedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PackingSessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_PackingSessions_PickingTasks_PickingTaskId",
                    column: x => x.PickingTaskId,
                    principalTable: "PickingTasks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PackingSessions_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PackingSessions_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "HandlingUnits",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                HuCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Barcode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Sscc = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                PackingSessionId = table.Column<int>(type: "int", nullable: false),
                ParentHandlingUnitId = table.Column<int>(type: "int", nullable: true),
                Type = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                GrossWeightKg = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                NetWeightKg = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                VolumeM3 = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                SealedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HandlingUnits", x => x.Id);
                table.CheckConstraint(
                    "CK_HandlingUnits_Metrics",
                    "([GrossWeightKg] IS NULL OR [GrossWeightKg] > 0) AND ([NetWeightKg] IS NULL OR [NetWeightKg] > 0) AND ([VolumeM3] IS NULL OR [VolumeM3] > 0) AND ([GrossWeightKg] IS NULL OR [NetWeightKg] IS NULL OR [GrossWeightKg] >= [NetWeightKg])");
                table.ForeignKey(
                    name: "FK_HandlingUnits_HandlingUnits_ParentHandlingUnitId",
                    column: x => x.ParentHandlingUnitId,
                    principalTable: "HandlingUnits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_HandlingUnits_PackingSessions_PackingSessionId",
                    column: x => x.PackingSessionId,
                    principalTable: "PackingSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_HandlingUnits_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_HandlingUnits_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "HandlingUnitContents",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                HandlingUnitId = table.Column<int>(type: "int", nullable: false),
                PickingTaskLineId = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                PackedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                PackedBy = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HandlingUnitContents", x => x.Id);
                table.CheckConstraint("CK_HandlingUnitContents_Quantity", "[Quantity] > 0");
                table.ForeignKey(
                    name: "FK_HandlingUnitContents_HandlingUnits_HandlingUnitId",
                    column: x => x.HandlingUnitId,
                    principalTable: "HandlingUnits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_HandlingUnitContents_PickingTaskLines_PickingTaskLineId",
                    column: x => x.PickingTaskLineId,
                    principalTable: "PickingTaskLines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_HandlingUnitContents_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_HandlingUnitContents_Users_PackedBy",
                    column: x => x.PackedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PackingSessions_CreatedBy",
            table: "PackingSessions",
            column: "CreatedBy");
        migrationBuilder.CreateIndex(
            name: "IX_PackingSessions_PickingTaskId",
            table: "PackingSessions",
            column: "PickingTaskId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_PackingSessions_WarehouseId_SessionCode",
            table: "PackingSessions",
            columns: new[] { "WarehouseId", "SessionCode" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_PackingSessions_WarehouseId_Status_CreatedAt",
            table: "PackingSessions",
            columns: new[] { "WarehouseId", "Status", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnits_CreatedBy",
            table: "HandlingUnits",
            column: "CreatedBy");
        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnits_ParentHandlingUnitId",
            table: "HandlingUnits",
            column: "ParentHandlingUnitId");
        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnits_PackingSessionId_Status",
            table: "HandlingUnits",
            columns: new[] { "PackingSessionId", "Status" });
        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnits_Sscc",
            table: "HandlingUnits",
            column: "Sscc",
            unique: true,
            filter: "[Sscc] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnits_WarehouseId_Barcode",
            table: "HandlingUnits",
            columns: new[] { "WarehouseId", "Barcode" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnits_WarehouseId_HuCode",
            table: "HandlingUnits",
            columns: new[] { "WarehouseId", "HuCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnitContents_HandlingUnitId_PickingTaskLineId",
            table: "HandlingUnitContents",
            columns: new[] { "HandlingUnitId", "PickingTaskLineId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnitContents_PackedBy",
            table: "HandlingUnitContents",
            column: "PackedBy");
        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnitContents_PickingTaskLineId",
            table: "HandlingUnitContents",
            column: "PickingTaskLineId");
        migrationBuilder.CreateIndex(
            name: "IX_HandlingUnitContents_ProductId",
            table: "HandlingUnitContents",
            column: "ProductId");

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='packing.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('packing.read',N'Xem Packing session',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='packing.execute')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('packing.execute',N'Thực hiện Packing',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='packing.reopen')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('packing.reopen',N'Mở lại Packing theo kiểm soát',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='handling_unit.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('handling_unit.read',N'Xem Handling Unit',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='handling_unit.create')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('handling_unit.create',N'Tạo Handling Unit',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='handling_unit.modify')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('handling_unit.modify',N'Sửa cấu trúc Handling Unit',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='handling_unit.split')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('handling_unit.split',N'Tách Handling Unit',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='handling_unit.merge')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('handling_unit.merge',N'Gộp Handling Unit',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='handling_unit.repack')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('handling_unit.repack',N'Đóng gói lại Handling Unit',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN (
    'packing.read','packing.execute','packing.reopen',
    'handling_unit.read','handling_unit.create','handling_unit.modify',
    'handling_unit.split','handling_unit.merge','handling_unit.repack')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN (
    'packing.read','packing.execute',
    'handling_unit.read','handling_unit.create','handling_unit.modify',
    'handling_unit.split','handling_unit.merge','handling_unit.repack')
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('packing.read','handling_unit.read')
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
WHERE p.Code IN (
    'packing.read','packing.execute','packing.reopen',
    'handling_unit.read','handling_unit.create','handling_unit.modify',
    'handling_unit.split','handling_unit.merge','handling_unit.repack');
DELETE FROM Permissions
WHERE Code IN (
    'packing.read','packing.execute','packing.reopen',
    'handling_unit.read','handling_unit.create','handling_unit.modify',
    'handling_unit.split','handling_unit.merge','handling_unit.repack');
""");

        migrationBuilder.DropTable(name: "HandlingUnitContents");
        migrationBuilder.DropTable(name: "HandlingUnits");
        migrationBuilder.DropTable(name: "PackingSessions");
    }
}
