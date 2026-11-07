using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Wave02_CustomerReturnRmaEnhancementsAndPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAtUtc",
                table: "CustomerReturns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedBy",
                table: "CustomerReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAtUtc",
                table: "CustomerReturns",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QuarantineLocationId",
                table: "CustomerReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CustomerReturns",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "ShipmentId",
                table: "CustomerReturns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUomCode",
                table: "CustomerReturnLines",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PCS");

            migrationBuilder.AddColumn<int>(
                name: "BaseUomId",
                table: "CustomerReturnLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionRateSnapshot",
                table: "CustomerReturnLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "CustomerReturnLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LotId",
                table: "CustomerReturnLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingConsumedQty",
                table: "CustomerReturnLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PostedLedgerTransactionId",
                table: "CustomerReturnLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RestockLocationId",
                table: "CustomerReturnLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SerialId",
                table: "CustomerReturnLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionUomCode",
                table: "CustomerReturnLines",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PCS");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.RolePermissions', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'return.read')
        INSERT INTO dbo.Permissions (Code, Description, CreatedAt) VALUES (N'return.read', N'Xem phiếu hàng hoàn trả (RMA)', SYSUTCDATETIME());

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'return.create')
        INSERT INTO dbo.Permissions (Code, Description, CreatedAt) VALUES (N'return.create', N'Tạo phiếu tiếp nhận hàng hoàn trả (RMA)', SYSUTCDATETIME());

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'return.receive')
        INSERT INTO dbo.Permissions (Code, Description, CreatedAt) VALUES (N'return.receive', N'Tiếp nhận vật lý hàng hoàn trả vào khu biệt trữ', SYSUTCDATETIME());

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'return.inspect')
        INSERT INTO dbo.Permissions (Code, Description, CreatedAt) VALUES (N'return.inspect', N'Kiểm định chất lượng hàng hoàn trả (QC/RMA)', SYSUTCDATETIME());

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'return.disposition')
        INSERT INTO dbo.Permissions (Code, Description, CreatedAt) VALUES (N'return.disposition', N'Định đoạt phân luồng hàng hoàn trả (Nhập lại kệ / Tân trang / Trả NCC / Hủy)', SYSUTCDATETIME());

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'return.post')
        INSERT INTO dbo.Permissions (Code, Description, CreatedAt) VALUES (N'return.post', N'Ghi sổ bút toán định đoạt hàng hoàn trả (RMA)', SYSUTCDATETIME());

    INSERT INTO dbo.RolePermissions (RoleId, PermissionId, GrantedAt)
    SELECT r.Id, p.Id, SYSUTCDATETIME()
    FROM dbo.Roles r
    CROSS JOIN dbo.Permissions p
    WHERE p.Code IN (N'return.read', N'return.create', N'return.receive', N'return.inspect', N'return.disposition', N'return.post')
      AND r.RoleName IN (N'Admin', N'Manager', N'WarehouseManager')
      AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);

    INSERT INTO dbo.RolePermissions (RoleId, PermissionId, GrantedAt)
    SELECT r.Id, p.Id, SYSUTCDATETIME()
    FROM dbo.Roles r
    CROSS JOIN dbo.Permissions p
    WHERE p.Code IN (N'return.read', N'return.create', N'return.receive', N'return.inspect', N'return.disposition')
      AND r.RoleName IN (N'WarehouseStaff')
      AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);

    INSERT INTO dbo.RolePermissions (RoleId, PermissionId, GrantedAt)
    SELECT r.Id, p.Id, SYSUTCDATETIME()
    FROM dbo.Roles r
    CROSS JOIN dbo.Permissions p
    WHERE p.Code IN (N'return.read')
      AND r.RoleName IN (N'Accountant', N'Viewer', N'SalesStaff', N'ProcurementStaff')
      AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE rp
FROM [RolePermissions] rp
INNER JOIN [Permissions] p ON p.[Id] = rp.[PermissionId]
WHERE p.[Code] IN (N'return.read', N'return.create', N'return.receive', N'return.inspect', N'return.disposition', N'return.post');

DELETE FROM [Permissions]
WHERE [Code] IN (N'return.read', N'return.create', N'return.receive', N'return.inspect', N'return.disposition', N'return.post');
");

            migrationBuilder.DropColumn(
                name: "TransactionUomCode",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "SerialId",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "RestockLocationId",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "PostedLedgerTransactionId",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "PackagingConsumedQty",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "LotId",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "ConversionRateSnapshot",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "BaseUomId",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "BaseUomCode",
                table: "CustomerReturnLines");

            migrationBuilder.DropColumn(
                name: "ShipmentId",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "QuarantineLocationId",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "PostedAtUtc",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "CustomerReturns");

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "CustomerReturns");
        }
    }
}
