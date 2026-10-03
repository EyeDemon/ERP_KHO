using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionCodeAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Permissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                    table.CheckConstraint("CK_Permissions_Code", "[Code] = LOWER(LTRIM(RTRIM([Code]))) AND [Code] LIKE '%.%'");
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionId = table.Column<int>(type: "int", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GrantedByUserId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_RolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Users_GrantedByUserId",
                        column: x => x.GrantedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM Roles WHERE LOWER(LTRIM(RTRIM(RoleName)))='admin' GROUP BY LOWER(LTRIM(RTRIM(RoleName))) HAVING COUNT(*)>1) THROW 51000, 'Permission bootstrap requires one canonical Admin role.', 1;
IF NOT EXISTS (SELECT 1 FROM Users u JOIN Roles r ON r.Id=u.RoleId WHERE u.IsActive=1 AND (u.LockoutEnd IS NULL OR u.LockoutEnd<=SYSUTCDATETIME()) AND LOWER(LTRIM(RTRIM(r.RoleName)))='admin') THROW 51001, 'Permission bootstrap requires an active Admin user.', 1;
INSERT INTO Permissions (Code,Description,CreatedAt) VALUES
('receipt.read','',SYSUTCDATETIME()),
('receipt.create','',SYSUTCDATETIME()),
('receipt.update','',SYSUTCDATETIME()),
('receipt.cancel','',SYSUTCDATETIME()),
('receipt.receive','',SYSUTCDATETIME()),
('receipt.complete','',SYSUTCDATETIME()),
('receipt.post','',SYSUTCDATETIME()),
('quality_inspection.execute','',SYSUTCDATETIME()),
('quality_inspection.complete','',SYSUTCDATETIME()),
('quality_disposition.approve','',SYSUTCDATETIME()),
('receiving_discrepancy.read','',SYSUTCDATETIME()),
('receiving_discrepancy.create','',SYSUTCDATETIME()),
('receiving_discrepancy.submit','',SYSUTCDATETIME()),
('receiving_discrepancy.approve','',SYSUTCDATETIME()),
('receiving_discrepancy.reject','',SYSUTCDATETIME()),
('receiving_discrepancy.resolve','',SYSUTCDATETIME()),
('putaway.read','',SYSUTCDATETIME()),
('putaway.assign','',SYSUTCDATETIME()),
('putaway.execute','',SYSUTCDATETIME()),
('putaway.cancel','',SYSUTCDATETIME()),
('location.read','',SYSUTCDATETIME()),
('location.manage','',SYSUTCDATETIME()),
('product.read','',SYSUTCDATETIME()),
('product.create','',SYSUTCDATETIME()),
('product.update','',SYSUTCDATETIME()),
('product.deactivate','',SYSUTCDATETIME()),
('product_uom.manage','',SYSUTCDATETIME()),
('uom.read','',SYSUTCDATETIME()),
('uom.manage','',SYSUTCDATETIME()),
('partner.read','',SYSUTCDATETIME()),
('partner.create','',SYSUTCDATETIME()),
('partner.update','',SYSUTCDATETIME()),
('partner.deactivate','',SYSUTCDATETIME()),
('reason_code.read','',SYSUTCDATETIME()),
('reason_code.manage','',SYSUTCDATETIME()),
('quality_policy.read','',SYSUTCDATETIME()),
('quality_policy.manage','',SYSUTCDATETIME()),
('receiving_tolerance_policy.read','',SYSUTCDATETIME()),
('receiving_tolerance_policy.manage','',SYSUTCDATETIME()),
('permission.read','',SYSUTCDATETIME()),
('permission.assign','',SYSUTCDATETIME()),
('role.read','',SYSUTCDATETIME()),
('role.manage','',SYSUTCDATETIME()),
('user.read','',SYSUTCDATETIME()),
('user.manage','',SYSUTCDATETIME()),
('user_warehouse.read','',SYSUTCDATETIME()),
('user_warehouse.manage','',SYSUTCDATETIME());
INSERT INTO RolePermissions (RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL FROM Roles r CROSS JOIN Permissions p WHERE
(LOWER(LTRIM(RTRIM(r.RoleName)))='admin' AND p.Code IN ('receipt.read','receipt.create','receipt.update','receipt.cancel','receipt.receive','receipt.complete','receipt.post','quality_inspection.execute','quality_inspection.complete','quality_disposition.approve','receiving_discrepancy.read','receiving_discrepancy.create','receiving_discrepancy.submit','receiving_discrepancy.approve','receiving_discrepancy.reject','receiving_discrepancy.resolve','putaway.read','putaway.assign','putaway.execute','putaway.cancel','location.read','location.manage','product.read','product.create','product.update','product.deactivate','product_uom.manage','uom.read','uom.manage','partner.read','partner.create','partner.update','partner.deactivate','reason_code.read','reason_code.manage','quality_policy.read','quality_policy.manage','receiving_tolerance_policy.read','receiving_tolerance_policy.manage','permission.read','permission.assign','role.read','role.manage','user.read','user.manage','user_warehouse.read','user_warehouse.manage')) OR
(LOWER(LTRIM(RTRIM(r.RoleName)))='manager' AND p.Code IN ('receipt.read','receipt.create','receipt.update','receipt.cancel','receipt.receive','receipt.complete','receipt.post','quality_inspection.execute','quality_inspection.complete','quality_disposition.approve','receiving_discrepancy.read','receiving_discrepancy.create','receiving_discrepancy.submit','receiving_discrepancy.approve','receiving_discrepancy.reject','receiving_discrepancy.resolve','putaway.read','putaway.assign','putaway.execute','putaway.cancel','location.read','location.manage','product.read','product.create','product.update','product.deactivate','product_uom.manage','uom.read','uom.manage','partner.read','partner.create','partner.update','partner.deactivate','reason_code.read','reason_code.manage','quality_policy.read','quality_policy.manage','receiving_tolerance_policy.read','receiving_tolerance_policy.manage')) OR
(LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff' AND p.Code IN ('putaway.read','putaway.execute','location.read','product.read','uom.read')) OR
(LOWER(LTRIM(RTRIM(r.RoleName)))='viewer' AND p.Code IN ('receipt.read','receiving_discrepancy.read','putaway.read','location.read','product.read','uom.read','partner.read','reason_code.read'));
");
            migrationBuilder.CreateIndex(
                name: "UX_Permissions_Code",
                table: "Permissions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_GrantedByUserId",
                table: "RolePermissions",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_PermissionId",
                table: "RolePermissions",
                column: "PermissionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "Permissions");
        }
    }
}
