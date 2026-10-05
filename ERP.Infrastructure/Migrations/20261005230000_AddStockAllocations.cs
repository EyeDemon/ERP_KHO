using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261005230000_AddStockAllocations")]
public sealed class AddStockAllocations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "AllocatedQuantity",
            table: "StockReservations",
            type: "decimal(18,4)",
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<int>(
            name: "AllocationVersion",
            table: "StockReservations",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_StockReservations_Quantity')
    ALTER TABLE [StockReservations] DROP CONSTRAINT [CK_StockReservations_Quantity];
ALTER TABLE [StockReservations] ADD CONSTRAINT [CK_StockReservations_Quantity]
CHECK ([Quantity] > 0
   AND [ConsumedQuantity] >= 0
   AND [ReleasedQuantity] >= 0
   AND [AllocatedQuantity] >= 0
   AND [ConsumedQuantity] + [ReleasedQuantity] <= [Quantity]
   AND [AllocatedQuantity] <= [Quantity] - [ConsumedQuantity] - [ReleasedQuantity]);
""");

        migrationBuilder.CreateTable(
            name: "StockAllocations",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AllocationCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ReservationId = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                LocationId = table.Column<int>(type: "int", nullable: false),
                InventoryStatus = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                Strategy = table.Column<int>(type: "int", nullable: false),
                SelectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                AllocatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                AllocatedBy = table.Column<int>(type: "int", nullable: false),
                ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ReleasedBy = table.Column<int>(type: "int", nullable: true),
                ReleaseReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Version = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockAllocations", x => x.Id);
                table.CheckConstraint("CK_StockAllocations_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_StockAllocations_Version", "[Version] >= 0");
                table.ForeignKey("FK_StockAllocations_StockReservations_ReservationId", x => x.ReservationId, "StockReservations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StockAllocations_Warehouses_WarehouseId", x => x.WarehouseId, "Warehouses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StockAllocations_Products_ProductId", x => x.ProductId, "Products", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StockAllocations_WarehouseLocations_LocationId", x => x.LocationId, "WarehouseLocations", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StockAllocations_Users_AllocatedBy", x => x.AllocatedBy, "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_StockAllocations_Users_ReleasedBy", x => x.ReleasedBy, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_StockAllocations_AllocationCode", "StockAllocations", "AllocationCode", unique: true);
        migrationBuilder.CreateIndex("IX_StockAllocations_ReservationId", "StockAllocations", "ReservationId");
        migrationBuilder.CreateIndex("IX_StockAllocations_AllocatedBy", "StockAllocations", "AllocatedBy");
        migrationBuilder.CreateIndex("IX_StockAllocations_ReleasedBy", "StockAllocations", "ReleasedBy");
        migrationBuilder.CreateIndex("IX_StockAllocations_WarehouseId", "StockAllocations", "WarehouseId");
        migrationBuilder.CreateIndex("IX_StockAllocations_ProductId", "StockAllocations", "ProductId");
        migrationBuilder.CreateIndex("IX_StockAllocations_LocationId", "StockAllocations", "LocationId");
        migrationBuilder.CreateIndex(
            name: "IX_StockAllocations_WarehouseId_LocationId_ProductId_Status",
            table: "StockAllocations",
            columns: new[] { "WarehouseId", "LocationId", "ProductId", "Status" });
        migrationBuilder.CreateIndex(
            name: "IX_StockAllocations_ReservationId_LocationId_InventoryStatus_Status",
            table: "StockAllocations",
            columns: new[] { "ReservationId", "LocationId", "InventoryStatus", "Status" });

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='allocation.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('allocation.read',N'Xem Allocation',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='allocation.create')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('allocation.create',N'Tạo Allocation',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='allocation.release')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('allocation.release',N'Giải phóng Allocation',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='allocation.reallocate')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('allocation.reallocate',N'Phân bổ lại Allocation',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('allocation.read','allocation.create','allocation.release','allocation.reallocate')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('allocation.read','allocation.create')
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='allocation.read'
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='viewer'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM StockAllocations WHERE Status IN (0,1,2))
    THROW 51020, 'Cannot remove StockAllocations while active/picking/picked allocations exist.', 1;

DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code IN ('allocation.read','allocation.create','allocation.release','allocation.reallocate');
DELETE FROM Permissions WHERE Code IN ('allocation.read','allocation.create','allocation.release','allocation.reallocate');

UPDATE StockReservations SET Status = 0 WHERE Status IN (6,7);
""");

        migrationBuilder.DropTable(name: "StockAllocations");

        migrationBuilder.Sql("""
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_StockReservations_Quantity')
    ALTER TABLE [StockReservations] DROP CONSTRAINT [CK_StockReservations_Quantity];
ALTER TABLE [StockReservations] ADD CONSTRAINT [CK_StockReservations_Quantity]
CHECK ([Quantity] > 0
   AND [ConsumedQuantity] >= 0
   AND [ReleasedQuantity] >= 0
   AND [ConsumedQuantity] + [ReleasedQuantity] <= [Quantity]);
""");

        migrationBuilder.DropColumn(name: "AllocatedQuantity", table: "StockReservations");
        migrationBuilder.DropColumn(name: "AllocationVersion", table: "StockReservations");
    }
}
