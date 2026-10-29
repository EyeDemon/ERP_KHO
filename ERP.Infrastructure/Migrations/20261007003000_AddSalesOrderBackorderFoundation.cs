using System;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261007003000_AddSalesOrderBackorderFoundation")]
public sealed class AddSalesOrderBackorderFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SalesOrders",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                OrderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                ExternalOrderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                CustomerId = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                RequestedShipDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                Priority = table.Column<int>(type: "int", nullable: false),
                ShippingMethod = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesOrders", x => x.Id);
                table.CheckConstraint("CK_SalesOrders_Priority", "[Priority] >= 0");
                table.ForeignKey(
                    name: "FK_SalesOrders_BusinessPartners_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "BusinessPartners",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SalesOrders_Users_CreatedBy",
                    column: x => x.CreatedBy,
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SalesOrders_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "SalesOrderLines",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                SalesOrderId = table.Column<int>(type: "int", nullable: false),
                ExternalLineId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                OrderedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                CancelledQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                UomCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SalesOrderLines", x => x.Id);
                table.CheckConstraint("CK_SalesOrderLines_OrderedQuantity", "[OrderedQuantity] > 0");
                table.CheckConstraint("CK_SalesOrderLines_CancelledQuantity", "[CancelledQuantity] >= 0 AND [CancelledQuantity] <= [OrderedQuantity]");
                table.ForeignKey(
                    name: "FK_SalesOrderLines_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_SalesOrderLines_SalesOrders_SalesOrderId",
                    column: x => x.SalesOrderId,
                    principalTable: "SalesOrders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Backorders",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                BackorderCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                SalesOrderLineId = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                RecoveredQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                CancelledQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                Status = table.Column<int>(type: "int", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Backorders", x => x.Id);
                table.CheckConstraint("CK_Backorders_Quantity", "[Quantity] > 0");
                table.CheckConstraint("CK_Backorders_RecoveredQuantity", "[RecoveredQuantity] >= 0");
                table.CheckConstraint("CK_Backorders_CancelledQuantity", "[CancelledQuantity] >= 0 AND [RecoveredQuantity] + [CancelledQuantity] <= [Quantity]");
                table.ForeignKey(
                    name: "FK_Backorders_SalesOrderLines_SalesOrderLineId",
                    column: x => x.SalesOrderLineId,
                    principalTable: "SalesOrderLines",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Backorders_Warehouses_WarehouseId",
                    column: x => x.WarehouseId,
                    principalTable: "Warehouses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_SalesOrders_CreatedBy", table: "SalesOrders", column: "CreatedBy");
        migrationBuilder.CreateIndex(name: "IX_SalesOrders_CustomerId", table: "SalesOrders", column: "CustomerId");
        migrationBuilder.CreateIndex(name: "IX_SalesOrders_ExternalOrderId", table: "SalesOrders", column: "ExternalOrderId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_SalesOrders_OrderCode", table: "SalesOrders", column: "OrderCode", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_SalesOrders_WarehouseId_Status_RequestedShipDate",
            table: "SalesOrders",
            columns: new[] { "WarehouseId", "Status", "RequestedShipDate" });

        migrationBuilder.CreateIndex(name: "IX_SalesOrderLines_ProductId", table: "SalesOrderLines", column: "ProductId");
        migrationBuilder.CreateIndex(
            name: "IX_SalesOrderLines_SalesOrderId_ExternalLineId",
            table: "SalesOrderLines",
            columns: new[] { "SalesOrderId", "ExternalLineId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_SalesOrderLines_SalesOrderId_ProductId",
            table: "SalesOrderLines",
            columns: new[] { "SalesOrderId", "ProductId" },
            unique: true);

        migrationBuilder.CreateIndex(name: "IX_Backorders_BackorderCode", table: "Backorders", column: "BackorderCode", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Backorders_SalesOrderLineId", table: "Backorders", column: "SalesOrderLineId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Backorders_WarehouseId_Status_CreatedAt", table: "Backorders", columns: new[] { "WarehouseId", "Status", "CreatedAt" });

        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='sales_order.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('sales_order.read',N'Xem Sales Order',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='sales_order.create')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('sales_order.create',N'Tạo Sales Order',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='sales_order.update')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('sales_order.update',N'Cập nhật Sales Order',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='sales_order.hold')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('sales_order.hold',N'Hold Sales Order',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='sales_order.release')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('sales_order.release',N'Release Sales Order',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='sales_order.cancel')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('sales_order.cancel',N'Hủy Sales Order',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='sales_order.close')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('sales_order.close',N'Đóng Sales Order',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='backorder.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('backorder.read',N'Xem Backorder',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='backorder.manage')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('backorder.manage',N'Quản lý Backorder',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN (
    'sales_order.read','sales_order.create','sales_order.update','sales_order.hold',
    'sales_order.release','sales_order.cancel','sales_order.close',
    'backorder.read','backorder.manage')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN ('sales_order.read','backorder.read')
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
WHERE p.Code IN (
    'sales_order.read','sales_order.create','sales_order.update','sales_order.hold',
    'sales_order.release','sales_order.cancel','sales_order.close',
    'backorder.read','backorder.manage');
DELETE FROM Permissions
WHERE Code IN (
    'sales_order.read','sales_order.create','sales_order.update','sales_order.hold',
    'sales_order.release','sales_order.cancel','sales_order.close',
    'backorder.read','backorder.manage');
""");

        migrationBuilder.DropTable(name: "Backorders");
        migrationBuilder.DropTable(name: "SalesOrderLines");
        migrationBuilder.DropTable(name: "SalesOrders");
    }
}
