using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeparateMasterPermissionCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO Permissions (Code,Description,CreatedAt) VALUES
('product_category.read',N'Xem danh mục sản phẩm',SYSUTCDATETIME()),
('product_category.manage',N'Quản lý danh mục sản phẩm',SYSUTCDATETIME()),
('product_barcode.manage',N'Quản lý mã vạch sản phẩm',SYSUTCDATETIME()),
('warehouse.read',N'Xem kho hàng',SYSUTCDATETIME()),
('warehouse.manage',N'Quản lý kho hàng',SYSUTCDATETIME());
INSERT INTO RolePermissions (RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL FROM Roles r CROSS JOIN Permissions p WHERE
(LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager') AND p.Code IN ('product_category.read','product_category.manage','product_barcode.manage','warehouse.read','warehouse.manage')) OR
(LOWER(LTRIM(RTRIM(r.RoleName)))='viewer' AND p.Code IN ('product_category.read','warehouse.read')) OR
(LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff' AND p.Code='warehouse.read');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback after production grant administration requires backup and a data plan.
            migrationBuilder.Sql(@"
DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code IN ('product_category.read','product_category.manage','product_barcode.manage','warehouse.read','warehouse.manage');
DELETE FROM Permissions WHERE Code IN ('product_category.read','product_category.manage','product_barcode.manage','warehouse.read','warehouse.manage');
");
        }
    }
}
