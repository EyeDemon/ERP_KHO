using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileOutboundDispatchMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @lockResult int;
                EXEC @lockResult=sys.sp_getapplock @Resource=N'ERP.PermissionAdministration',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
                IF @lockResult<0 THROW 51009, 'Permission administration lock unavailable.', 1;
                -- Runtime grants carry actor provenance; preserve those and audited explicit grants.
                DELETE rp FROM RolePermissions rp
                JOIN Roles r ON r.Id=rp.RoleId JOIN Permissions p ON p.Id=rp.PermissionId
                WHERE LOWER(LTRIM(RTRIM(r.RoleName)))='warehousestaff' AND p.Code='export_receipt.cancel'
                    AND rp.GrantedByUserId IS NULL
                    AND NOT EXISTS(SELECT 1 FROM AuditLogs a WHERE a.EntityName='Role' AND a.EntityId=r.Id
                        AND a.Action='Permission.Grant' AND a.Timestamp>=rp.GrantedAt AND JSON_VALUE(CASE WHEN ISJSON(a.NewValues)=1 THEN a.NewValues ELSE '{}' END,'$.permissionCode') COLLATE Latin1_General_100_BIN2=p.Code);
                -- The public seed did not advance the role aggregate; invalidate pre-cutover snapshots.
                UPDATE Roles SET GrantRevision=GrantRevision+1 WHERE LOWER(LTRIM(RTRIM(RoleName))) IN ('admin','manager','viewer','warehousestaff');
                """);
            // The historical imperative index is absent from the old EF snapshot; replace it explicitly.
            migrationBuilder.DropIndex(name: "IX_InventoryTransactions_ExportReceiptReference", table: "InventoryTransactions");

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

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_ExportReceiptLocationReference",
                table: "InventoryTransactions",
                columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId", "LocationId" },
                unique: true,
                filter: "[ReferenceType] = 'ExportReceipt'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DECLARE @lockResult int;
                EXEC @lockResult=sys.sp_getapplock @Resource=N'ERP.PermissionAdministration',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
                IF @lockResult<0 THROW 51009, 'Permission administration lock unavailable.', 1;
                IF EXISTS(SELECT 1 FROM InventoryTransactions WHERE ReferenceType='ExportReceipt'
                    GROUP BY ReferenceType,ReferenceId,TransactionType,ProductId,WarehouseId HAVING COUNT(*)>1)
                    THROW 51012, 'Cannot downgrade location export ledger without an approved backup and data plan.', 1;
                IF EXISTS(SELECT 1 FROM ExportReceiptDetails WHERE BaseUomIdSnapshot IS NOT NULL OR BaseUomCodeSnapshot IS NOT NULL
                    OR BaseUomNameSnapshot IS NOT NULL OR BaseUomPrecisionSnapshot IS NOT NULL)
                    OR EXISTS(SELECT 1 FROM RolePermissions rp JOIN Permissions p ON p.Id=rp.PermissionId
                        WHERE p.Code LIKE 'export_receipt.%' AND rp.GrantedByUserId IS NOT NULL)
                    OR EXISTS(SELECT 1 FROM AuditLogs WHERE EntityName='Role' AND Action IN ('Permission.Grant','Permission.Revoke')
                        AND JSON_VALUE(CASE WHEN ISJSON(NewValues)=1 THEN NewValues ELSE '{}' END,'$.permissionCode') LIKE 'export_receipt.%')
                    THROW 51013, 'Cannot downgrade outbound snapshots or administered grants without an approved backup and data plan.', 1;
                """);
            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_ExportReceiptLocationReference",
                table: "InventoryTransactions");

            migrationBuilder.CreateIndex(name: "IX_InventoryTransactions_ExportReceiptReference", table: "InventoryTransactions",
                columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "ProductId", "WarehouseId" },
                unique: true, filter: "[ReferenceType] = 'ExportReceipt'");
            // Catalog ownership stays with the public seed. Never restore the erroneous implicit Staff grant.

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
