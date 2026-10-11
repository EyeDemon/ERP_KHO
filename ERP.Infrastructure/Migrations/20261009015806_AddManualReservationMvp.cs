using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManualReservationMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @lockResult int;
                EXEC @lockResult=sys.sp_getapplock @Resource=N'ERP.PermissionAdministration',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
                IF @lockResult<0 THROW 51009, 'Permission administration lock unavailable.', 1;
                INSERT Permissions(Code,Description,CreatedAt)
                SELECT v.Code,v.Description,SYSUTCDATETIME() FROM (VALUES
                    ('reservation.read',N'Xem và đối chiếu giữ hàng'),
                    ('reservation.create',N'Tạo giữ hàng thủ công'),
                    ('reservation.release',N'Giải phóng và xử lý giữ hàng thủ công hết hạn')) v(Code,Description)
                WHERE NOT EXISTS(SELECT 1 FROM Permissions p WHERE p.Code=v.Code);
                INSERT RolePermissions(RoleId,PermissionId,GrantedAt,GrantedByUserId)
                SELECT r.Id,p.Id,SYSUTCDATETIME(),NULL FROM Roles r CROSS JOIN Permissions p
                WHERE p.Code IN ('reservation.read','reservation.create','reservation.release')
                    AND (LOWER(LTRIM(RTRIM(r.RoleName))) IN ('admin','manager')
                        OR LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff' AND p.Code IN ('reservation.read','reservation.create')
                        OR LOWER(LTRIM(RTRIM(r.RoleName)))='viewer' AND p.Code='reservation.read')
                    AND NOT EXISTS(SELECT 1 FROM RolePermissions rp WHERE rp.RoleId=r.Id AND rp.PermissionId=p.Id);
                -- Do not revoke existing administered grants, including a legitimate Staff release grant.
                UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE LOWER(LTRIM(RTRIM(RoleName))) IN ('admin','manager','viewer','warehousestaff');
                """);
            migrationBuilder.AddColumn<string>(
                name: "BaseUomCodeSnapshot",
                table: "StockReservations",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaseUomIdSnapshot",
                table: "StockReservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaseUomNameSnapshot",
                table: "StockReservations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BaseUomPrecisionSnapshot",
                table: "StockReservations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "StockReservations",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS(SELECT 1 FROM StockReservations WHERE BaseUomIdSnapshot IS NOT NULL OR BaseUomCodeSnapshot IS NOT NULL
                    OR BaseUomNameSnapshot IS NOT NULL OR BaseUomPrecisionSnapshot IS NOT NULL)
                    OR EXISTS(SELECT 1 FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId
                        WHERE p.Code IN ('reservation.read','reservation.create','reservation.release') AND rp.GrantedByUserId IS NOT NULL)
                    OR EXISTS(SELECT 1 FROM AuditLogs WHERE EntityName='Role' AND Action IN ('Permission.Grant','Permission.Revoke')
                        AND JSON_VALUE(CASE WHEN ISJSON(NewValues)=1 THEN NewValues ELSE '{}' END,'$.permissionCode') IN ('reservation.read','reservation.create','reservation.release'))
                    THROW 51014, 'Cannot downgrade reservation snapshots or administered grants without an approved backup and data plan.', 1;
                """);
            // Catalog/grants remain in the existing permission store: Down never blindly deletes grants.
            migrationBuilder.DropColumn(
                name: "BaseUomCodeSnapshot",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "BaseUomIdSnapshot",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "BaseUomNameSnapshot",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "BaseUomPrecisionSnapshot",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "StockReservations");
        }
    }
}
