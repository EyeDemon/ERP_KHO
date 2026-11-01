using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Wave01_TotpPersistenceAndPermissionSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TotpActivatedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpEnrollmentStatus",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "IDLE");

            migrationBuilder.AddColumn<string>(
                name: "TotpRecoveryCodeHashesJson",
                table: "Users",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TotpSecretProtected",
                table: "Users",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.Permissions', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.Roles', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.RolePermissions', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'inventory_opening_balance.import')
    BEGIN
        INSERT INTO dbo.Permissions (Code, Description, CreatedAt)
        VALUES (N'inventory_opening_balance.import', N'Nhập số dư tồn kho đầu kỳ và khóa kỳ chốt sổ', SYSUTCDATETIME());
    END;

    IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE Code = N'export_receipt.reprint_awb')
    BEGIN
        INSERT INTO dbo.Permissions (Code, Description, CreatedAt)
        VALUES (N'export_receipt.reprint_awb', N'In lại vận đơn AWB từ lần thứ 2 trở đi', SYSUTCDATETIME());
    END;

    INSERT INTO dbo.RolePermissions (RoleId, PermissionId, GrantedAt)
    SELECT r.Id, p.Id, SYSUTCDATETIME()
    FROM dbo.Roles r
    CROSS JOIN dbo.Permissions p
    WHERE p.Code IN (N'inventory_opening_balance.import', N'export_receipt.reprint_awb')
      AND r.RoleName IN (N'Admin', N'Manager')
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.RolePermissions rp
          WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
      );
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
WHERE p.[Code] IN (N'inventory_opening_balance.import', N'export_receipt.reprint_awb');

DELETE FROM [Permissions]
WHERE [Code] IN (N'inventory_opening_balance.import', N'export_receipt.reprint_awb');
");

            migrationBuilder.DropColumn(
                name: "TotpActivatedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpEnrollmentStatus",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpRecoveryCodeHashesJson",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TotpSecretProtected",
                table: "Users");
        }
    }
}
