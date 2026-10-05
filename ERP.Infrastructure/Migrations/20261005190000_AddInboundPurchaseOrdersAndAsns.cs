using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

public partial class AddInboundPurchaseOrdersAndAsns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PurchaseOrders",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ExternalPoId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SourceSystem = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                SupplierId = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                ExpectedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                Currency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                ExternalVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                table.ForeignKey("FK_PurchaseOrders_BusinessPartners_SupplierId", x => x.SupplierId, "BusinessPartners", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PurchaseOrders_Users_CreatedBy", x => x.CreatedBy, "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PurchaseOrders_Warehouses_WarehouseId", x => x.WarehouseId, "Warehouses", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PurchaseOrderLines",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                ExternalLineId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                LineNo = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                OrderedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                OperationUnitId = table.Column<int>(type: "int", nullable: false),
                OperationUnitCodeSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                OperationUnitDecimalPlaces = table.Column<int>(type: "int", nullable: false),
                BaseUnitId = table.Column<int>(type: "int", nullable: false),
                BaseUnitCodeSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                BaseUnitDecimalPlaces = table.Column<int>(type: "int", nullable: false),
                ConversionFactorSnapshot = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                ConversionVersionSnapshot = table.Column<int>(type: "int", nullable: false),
                BaseOrderedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                AllowedOverReceiptPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                AllowedUnderReceiptPct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                table.CheckConstraint("CK_PurchaseOrderLines_Quantity", "[OrderedQuantity] > 0 AND [BaseOrderedQuantity] > 0 AND [ConversionFactorSnapshot] > 0");
                table.CheckConstraint("CK_PurchaseOrderLines_Tolerance", "[AllowedOverReceiptPct] >= 0 AND [AllowedOverReceiptPct] <= 100 AND [AllowedUnderReceiptPct] >= 0 AND [AllowedUnderReceiptPct] <= 100");
                table.ForeignKey("FK_PurchaseOrderLines_Products_ProductId", x => x.ProductId, "Products", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId", x => x.PurchaseOrderId, "PurchaseOrders", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_PurchaseOrderLines_Units_BaseUnitId", x => x.BaseUnitId, "Units", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PurchaseOrderLines_Units_OperationUnitId", x => x.OperationUnitId, "Units", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Asns",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                SupplierId = table.Column<int>(type: "int", nullable: false),
                WarehouseId = table.Column<int>(type: "int", nullable: false),
                ExpectedArrivalAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                CarrierName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                VehiclePlate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<int>(type: "int", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                UpdatedBy = table.Column<int>(type: "int", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Asns", x => x.Id);
                table.ForeignKey("FK_Asns_BusinessPartners_SupplierId", x => x.SupplierId, "BusinessPartners", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Asns_PurchaseOrders_PurchaseOrderId", x => x.PurchaseOrderId, "PurchaseOrders", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Asns_Users_CreatedBy", x => x.CreatedBy, "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_Asns_Warehouses_WarehouseId", x => x.WarehouseId, "Warehouses", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AsnLines",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                AsnId = table.Column<int>(type: "int", nullable: false),
                PurchaseOrderLineId = table.Column<int>(type: "int", nullable: true),
                LineNo = table.Column<int>(type: "int", nullable: false),
                ProductId = table.Column<int>(type: "int", nullable: false),
                ExpectedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                OperationUnitId = table.Column<int>(type: "int", nullable: false),
                OperationUnitCodeSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                OperationUnitDecimalPlaces = table.Column<int>(type: "int", nullable: false),
                BaseUnitId = table.Column<int>(type: "int", nullable: false),
                BaseUnitCodeSnapshot = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                BaseUnitDecimalPlaces = table.Column<int>(type: "int", nullable: false),
                ConversionFactorSnapshot = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                ConversionVersionSnapshot = table.Column<int>(type: "int", nullable: false),
                BaseExpectedQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AsnLines", x => x.Id);
                table.CheckConstraint("CK_AsnLines_Quantity", "[ExpectedQuantity] > 0 AND [BaseExpectedQuantity] > 0 AND [ConversionFactorSnapshot] > 0");
                table.ForeignKey("FK_AsnLines_Asns_AsnId", x => x.AsnId, "Asns", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_AsnLines_Products_ProductId", x => x.ProductId, "Products", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AsnLines_PurchaseOrderLines_PurchaseOrderLineId", x => x.PurchaseOrderLineId, "PurchaseOrderLines", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AsnLines_Units_BaseUnitId", x => x.BaseUnitId, "Units", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_AsnLines_Units_OperationUnitId", x => x.OperationUnitId, "Units", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_PurchaseOrders_CreatedBy", "PurchaseOrders", "CreatedBy");
        migrationBuilder.CreateIndex("IX_PurchaseOrders_SupplierId", "PurchaseOrders", "SupplierId");
        migrationBuilder.CreateIndex("IX_PurchaseOrders_WarehouseId_Code", "PurchaseOrders", new[] { "WarehouseId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_PurchaseOrders_SourceSystem_ExternalPoId", "PurchaseOrders", new[] { "SourceSystem", "ExternalPoId" }, unique: true);
        migrationBuilder.CreateIndex("IX_PurchaseOrderLines_ProductId", "PurchaseOrderLines", "ProductId");
        migrationBuilder.CreateIndex("IX_PurchaseOrderLines_BaseUnitId", "PurchaseOrderLines", "BaseUnitId");
        migrationBuilder.CreateIndex("IX_PurchaseOrderLines_OperationUnitId", "PurchaseOrderLines", "OperationUnitId");
        migrationBuilder.CreateIndex("IX_PurchaseOrderLines_PurchaseOrderId_ExternalLineId", "PurchaseOrderLines", new[] { "PurchaseOrderId", "ExternalLineId" }, unique: true);
        migrationBuilder.CreateIndex("IX_PurchaseOrderLines_PurchaseOrderId_LineNo", "PurchaseOrderLines", new[] { "PurchaseOrderId", "LineNo" }, unique: true);
        migrationBuilder.CreateIndex("IX_Asns_CreatedBy", "Asns", "CreatedBy");
        migrationBuilder.CreateIndex("IX_Asns_PurchaseOrderId", "Asns", "PurchaseOrderId");
        migrationBuilder.CreateIndex("IX_Asns_SupplierId", "Asns", "SupplierId");
        migrationBuilder.CreateIndex("IX_Asns_WarehouseId_Code", "Asns", new[] { "WarehouseId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_AsnLines_ProductId", "AsnLines", "ProductId");
        migrationBuilder.CreateIndex("IX_AsnLines_PurchaseOrderLineId", "AsnLines", "PurchaseOrderLineId");
        migrationBuilder.CreateIndex("IX_AsnLines_BaseUnitId", "AsnLines", "BaseUnitId");
        migrationBuilder.CreateIndex("IX_AsnLines_OperationUnitId", "AsnLines", "OperationUnitId");
        migrationBuilder.CreateIndex("IX_AsnLines_AsnId_LineNo", "AsnLines", new[] { "AsnId", "LineNo" }, unique: true);

        migrationBuilder.Sql(@"
DECLARE @codes TABLE(Code nvarchar(100), Description nvarchar(250));
INSERT INTO @codes VALUES
('purchase_order.read','Đọc đơn mua'),
('purchase_order.create','Tạo đơn mua'),
('purchase_order.update','Cập nhật đơn mua'),
('purchase_order.release','Mở đơn mua cho nhận hàng'),
('purchase_order.close','Đóng đơn mua'),
('purchase_order.cancel','Hủy phần còn lại của đơn mua'),
('asn.read','Đọc ASN'),
('asn.create','Tạo ASN'),
('asn.update','Cập nhật ASN'),
('asn.confirm','Xác nhận ASN'),
('asn.receive','Thực hiện arrival/receiving ASN'),
('asn.cancel','Hủy ASN');

INSERT INTO Permissions(Code,Description,CreatedAt)
SELECT c.Code,c.Description,SYSUTCDATETIME()
FROM @codes c
WHERE NOT EXISTS (SELECT 1 FROM Permissions p WHERE p.Code=c.Code);

INSERT INTO RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN (SELECT Code FROM @codes)
WHERE (
    LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
    OR (LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff' AND p.Code IN ('purchase_order.read','asn.read','asn.receive'))
    OR (LOWER(LTRIM(RTRIM(r.RoleName)))='viewer' AND p.Code IN ('purchase_order.read','asn.read'))
)
AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code IN ('purchase_order.read','purchase_order.create','purchase_order.update','purchase_order.release','purchase_order.close','purchase_order.cancel','asn.read','asn.create','asn.update','asn.confirm','asn.receive','asn.cancel');
DELETE FROM Permissions
WHERE Code IN ('purchase_order.read','purchase_order.create','purchase_order.update','purchase_order.release','purchase_order.close','purchase_order.cancel','asn.read','asn.create','asn.update','asn.confirm','asn.receive','asn.cancel');
");
        migrationBuilder.DropTable("AsnLines");
        migrationBuilder.DropTable("Asns");
        migrationBuilder.DropTable("PurchaseOrderLines");
        migrationBuilder.DropTable("PurchaseOrders");
    }
}
