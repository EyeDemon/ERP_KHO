using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261007193000_AddInventoryReversalPermission")]
public sealed class AddInventoryReversalPermission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='inventory_reversal.create')
    INSERT Permissions(Code,Description,CreatedAt)
    VALUES('inventory_reversal.create',N'Tạo controlled inventory reversal/corrective posting',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='inventory_reversal.create'
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (
      SELECT 1 FROM RolePermissions rp
      WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id
  );
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code='inventory_reversal.create';

DELETE FROM Permissions
WHERE Code='inventory_reversal.create';
""");
    }
}
