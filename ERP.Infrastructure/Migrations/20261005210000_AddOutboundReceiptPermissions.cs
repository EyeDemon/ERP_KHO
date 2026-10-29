using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations;

[DbContext(typeof(ErpKhoDbContext))]
[Migration("20261005210000_AddOutboundReceiptPermissions")]
public sealed class AddOutboundReceiptPermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='export_receipt.read')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('export_receipt.read',N'Xem phiếu xuất kho',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='export_receipt.create')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('export_receipt.create',N'Tạo phiếu xuất kho',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='export_receipt.update')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('export_receipt.update',N'Cập nhật phiếu xuất kho nháp',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='export_receipt.approve')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('export_receipt.approve',N'Duyệt và giữ hàng cho phiếu xuất kho',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='export_receipt.dispatch')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('export_receipt.dispatch',N'Xác nhận hàng đã rời kho',SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code='export_receipt.cancel')
    INSERT Permissions(Code,Description,CreatedAt) VALUES('export_receipt.cancel',N'Hủy hoặc từ chối phiếu xuất kho',SYSUTCDATETIME());

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN (
    'export_receipt.read','export_receipt.create','export_receipt.update',
    'export_receipt.approve','export_receipt.dispatch','export_receipt.cancel')
WHERE LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code IN (
    'export_receipt.read','export_receipt.create','export_receipt.update',
    'export_receipt.dispatch')
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);

INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL
FROM Roles r
JOIN Permissions p ON p.Code='export_receipt.read'
WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='viewer'
  AND NOT EXISTS (SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Production grant changes require backup and a reviewed data plan before rollback.
        migrationBuilder.Sql("""
DELETE rp
FROM RolePermissions rp
JOIN Permissions p ON p.Id=rp.PermissionId
WHERE p.Code IN (
    'export_receipt.read','export_receipt.create','export_receipt.update',
    'export_receipt.approve','export_receipt.dispatch','export_receipt.cancel');
DELETE FROM Permissions WHERE Code IN (
    'export_receipt.read','export_receipt.create','export_receipt.update',
    'export_receipt.approve','export_receipt.dispatch','export_receipt.cancel');
""");
    }
}
