using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboundDispatchMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ExportReceipts",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "BaseUomCodeSnapshot",
                table: "ExportReceiptDetails",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaseUomIdSnapshot",
                table: "ExportReceiptDetails",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUomNameSnapshot",
                table: "ExportReceiptDetails",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaseUomPrecisionSnapshot",
                table: "ExportReceiptDetails",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
INSERT INTO Permissions (Code,Description,CreatedAt) VALUES
('export_receipt.read',N'Xem phiếu xuất kho',SYSUTCDATETIME()),
('export_receipt.create',N'Tạo phiếu xuất kho',SYSUTCDATETIME()),
('export_receipt.update',N'Cập nhật khách hàng trên phiếu xuất nháp',SYSUTCDATETIME()),
('export_receipt.approve',N'Duyệt và giữ hàng cho phiếu xuất',SYSUTCDATETIME()),
('export_receipt.dispatch',N'Xác nhận xuất kho',SYSUTCDATETIME()),
('export_receipt.cancel',N'Hủy phiếu xuất và giải phóng hàng giữ',SYSUTCDATETIME());
INSERT INTO RolePermissions (RoleId,PermissionId,GrantedAt,GrantedByUserId)
SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL FROM Roles r CROSS JOIN Permissions p
WHERE p.Code IN ('export_receipt.read','export_receipt.create','export_receipt.update','export_receipt.approve','export_receipt.dispatch','export_receipt.cancel') AND
(LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager') OR
(LOWER(LTRIM(RTRIM(r.RoleName)))='viewer' AND p.Code='export_receipt.read') OR
(LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff' AND p.Code IN ('export_receipt.read','export_receipt.create','export_receipt.update','export_receipt.dispatch')));
UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE LOWER(LTRIM(RTRIM(RoleName))) IN ('admin','manager','viewer','warehousestaff');
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // After production grants/snapshots exist, require backup and a reviewed data plan; not a business reversal.
            migrationBuilder.Sql("""
DELETE rp FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId WHERE p.Code IN ('export_receipt.read','export_receipt.create','export_receipt.update','export_receipt.approve','export_receipt.dispatch','export_receipt.cancel');
DELETE FROM Permissions WHERE Code IN ('export_receipt.read','export_receipt.create','export_receipt.update','export_receipt.approve','export_receipt.dispatch','export_receipt.cancel');
UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE LOWER(LTRIM(RTRIM(RoleName))) IN ('admin','manager','viewer','warehousestaff');
""");
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ExportReceipts");

            migrationBuilder.DropColumn(
                name: "BaseUomCodeSnapshot",
                table: "ExportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseUomIdSnapshot",
                table: "ExportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseUomNameSnapshot",
                table: "ExportReceiptDetails");

            migrationBuilder.DropColumn(
                name: "BaseUomPrecisionSnapshot",
                table: "ExportReceiptDetails");
        }
    }
}
