using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundApprovalRejectPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO Permissions (Code,Description,CreatedAt)
VALUES ('approval.reject',N'Từ chối phê duyệt phiếu nhập',SYSUTCDATETIME());
INSERT INTO RolePermissions (RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL FROM Roles r CROSS JOIN Permissions p
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager') AND p.Code='approval.reject';
");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Production grant changes require backup and a reviewed data plan before rollback.
            migrationBuilder.Sql(@"
DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId WHERE p.Code='approval.reject';
DELETE FROM Permissions WHERE Code='approval.reject';
");

        }
    }
}
