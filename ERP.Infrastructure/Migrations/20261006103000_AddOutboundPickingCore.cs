using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261006103000_AddOutboundPickingCore")]
public sealed class AddOutboundPickingCore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PickingTasks",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                TaskCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                SourceId = table.Column<int>(type: "int", nullable: true),
                SourceCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                PickingType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Priority = table.Column<int>(type: "int", nullable: false),
                AssignedUserId = table.Column<int>(type: "int", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PickingTasks", x => x.Id);
                table.CheckConstraint("CK_PickingTasks_Priority", "[Priority] >= 0");
                table.ForeignKey(
                    name: "FK_PickingTasks_Users_AssignedUserId",
                    column: x => x.AssignedUserId,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PickingTasks_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PickingTasks_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PickingTaskLines",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PickingTaskId = table.Column<int>(type: "int", nullable: false),
                AllocationId = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                SourceLocationId = table.Column<int>(type: "int", nullable: false),
                RequestedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                PickedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                Sequence = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PickingTaskLines", x => x.Id);
                table.CheckConstraint("CK_PickingTaskLines_Picked", "[PickedQuantity] >= 0 AND [PickedQuantity] <= [RequestedQuantity]");
                table.CheckConstraint("CK_PickingTaskLines_Requested", "[RequestedQuantity] > 0");
                table.CheckConstraint("CK_PickingTaskLines_Sequence", "[Sequence] > 0");
                table.ForeignKey(
                    name: "FK_PickingTaskLines_PickingTasks_PickingTaskId",
                    column: x => x.PickingTaskId,
                    principalTable: "PickingTasks",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PickingTaskLines_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PickingTaskLines_StockAllocations_AllocationId",
                    column: x => x.AllocationId,
                    principalTable: "StockAllocations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PickingTaskLines_WarehouseLocations_SourceLocationId",
                    column: x => x.SourceLocationId,
                    principalTable: "WarehouseLocations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ShortPickExceptions",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PickingTaskLineId = table.Column<int>(type: "int", nullable: false),
                ExpectedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                PickedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                ShortageQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ResolutionType = table.Column<int>(type: "int", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ResolvedBy = table.Column<int>(type: "int", nullable: true),
                ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShortPickExceptions", x => x.Id);
                table.CheckConstraint(
                    "CK_ShortPickExceptions_Quantities",
                    "[ExpectedQuantity] > 0 AND [PickedQuantity] >= 0 AND [ShortageQuantity] > 0 AND [PickedQuantity] + [ShortageQuantity] = [ExpectedQuantity]");
                table.ForeignKey(
                    name: "FK_ShortPickExceptions_PickingTaskLines_PickingTaskLineId",
                    column: x => x.PickingTaskLineId,
                    principalTable: "PickingTaskLines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShortPickExceptions_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ShortPickExceptions_Users_ResolvedBy",
                    column: x => x.ResolvedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PickingTasks_AssignedUserId",
            table: "PickingTasks",
            column: "AssignedUserId");
        migrationBuilder.CreateIndex(
            name: "IX_PickingTasks_CreatedBy",
            table: "PickingTasks",
            column: "CreatedBy");
        migrationBuilder.CreateIndex(
            name: "IX_PickingTasks_SourceType_SourceId",
            table: "PickingTasks",
            columns: new[] { "SourceType", "SourceId" },
            unique: true,
            filter: "[SourceId] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_PickingTasks_WarehouseId_Status_Priority",
            table: "PickingTasks",
            columns: new[] { "WarehouseId", "Status", "Priority" });
        migrationBuilder.CreateIndex(
            name: "IX_PickingTasks_WarehouseId_TaskCode",
            table: "PickingTasks",
            columns: new[] { "WarehouseId", "TaskCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PickingTaskLines_AllocationId",
            table: "PickingTaskLines",
            column: "AllocationId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_PickingTaskLines_ProductId",
            table: "PickingTaskLines",
            column: "ProductId");
        migrationBuilder.CreateIndex(
            name: "IX_PickingTaskLines_SourceLocationId",
            table: "PickingTaskLines",
            column: "SourceLocationId");
        migrationBuilder.CreateIndex(
            name: "IX_PickingTaskLines_PickingTaskId_Sequence",
            table: "PickingTaskLines",
            columns: new[] { "PickingTaskId", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShortPickExceptions_CreatedBy",
            table: "ShortPickExceptions",
            column: "CreatedBy");
        migrationBuilder.CreateIndex(
            name: "IX_ShortPickExceptions_ResolvedBy",
            table: "ShortPickExceptions",
            column: "ResolvedBy");
        migrationBuilder.CreateIndex(
            name: "IX_ShortPickExceptions_PickingTaskLineId_Status",
            table: "ShortPickExceptions",
            columns: new[] { "PickingTaskLineId", "Status" });

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='picking.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('picking.read',N'Xem nhiệm vụ Picking',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='picking.assign')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('picking.assign',N'Phân công nhiệm vụ Picking',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='picking.execute')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('picking.execute',N'Thực hiện Picking bằng scan',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='picking.short_pick')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('picking.short_pick',N'Báo và xử lý Short Pick',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='picking.override')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('picking.override',N'Supervisor override Short Pick',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('picking.read','picking.assign','picking.execute','picking.short_pick','picking.override')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('picking.read','picking.execute','picking.short_pick')
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='picking.read'
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
WHERE p.Code IN ('picking.read','picking.assign','picking.execute','picking.short_pick','picking.override');
DELETE FROM Permissions
WHERE Code IN ('picking.read','picking.assign','picking.execute','picking.short_pick','picking.override');
""");

        migrationBuilder.DropTable(name: "ShortPickExceptions");
        migrationBuilder.DropTable(name: "PickingTaskLines");
        migrationBuilder.DropTable(name: "PickingTasks");
    }
}
