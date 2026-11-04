using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Wave06_SaasTenantsSecurityDomainAndTaxEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityDomain",
                table: "Users",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "CUSTOMER_TENANT");

            migrationBuilder.AddColumn<string>(
                name: "SecurityDomain",
                table: "Roles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "CUSTOMER_TENANT");

            migrationBuilder.AddColumn<int>(
                name: "ImpersonatedByUserId",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecurityDomain",
                table: "AuditLogs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankAccountNumber",
                table: "BusinessPartners",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "BusinessPartners",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxCode",
                table: "BusinessPartners",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxpayerType",
                table: "BusinessPartners",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "ENTERPRISE");

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultVatRatePercent",
                table: "Products",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 10.00m);

            migrationBuilder.AddColumn<string>(
                name: "ElectronicPxkkvcnbNo",
                table: "StockTransfers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalTransferOrderNo",
                table: "StockTransfers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransporterName",
                table: "StockTransfers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehiclePlateNumber",
                table: "StockTransfers",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerLegalName",
                table: "SaasBillingInvoices",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerTaxCode",
                table: "SaasBillingInvoices",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SubTotalVnd",
                table: "SaasBillingInvoices",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmountVnd",
                table: "SaasBillingInvoices",
                type: "decimal(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRatePercent",
                table: "SaasBillingInvoices",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 10.00m);

            migrationBuilder.CreateTable(
                name: "SaasTenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    TenantCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    TaxCode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    TaxpayerType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "ENTERPRISE"),
                    OwnerEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    LegalRegisteredAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LifecycleStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "ACTIVE"),
                    SuspensionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AllowSupportAccess = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaasTenants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_SaasTenants_TenantCode",
                table: "SaasTenants",
                column: "TenantCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_SaasTenants_TenantId",
                table: "SaasTenants",
                column: "TenantId",
                unique: true);

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM dbo.SaasTenants WHERE TenantId = 1)
BEGIN
    INSERT INTO dbo.SaasTenants (TenantId, TenantCode, CompanyName, TaxCode, TaxpayerType, OwnerEmail, ContactPhone, LegalRegisteredAddress, LifecycleStatus, AllowSupportAccess, CreatedAtUtc, UpdatedAtUtc)
    VALUES (1, N'TENANT-001', N'CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO', N'0312345678', N'ENTERPRISE', N'owner@erpkho.vn', N'0397354273', N'Khu Công Nghệ Cao, TP. Thủ Đức, TP. Hồ Chí Minh', N'ACTIVE', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
END;

INSERT INTO dbo.SaasTenants (TenantId, TenantCode, CompanyName, TaxpayerType, LifecycleStatus, AllowSupportAccess, CreatedAtUtc, UpdatedAtUtc)
SELECT ts.TenantId,
       CONCAT(N'TENANT-', FORMAT(ts.TenantId, '000')),
       CONCAT(N'Doanh nghiệp Thuê Kho #', ts.TenantId),
       N'ENTERPRISE',
       CASE WHEN ts.Status IN (N'ACTIVE', N'TRIAL', N'READ_ONLY', N'SUSPENDED') THEN ts.Status ELSE N'ACTIVE' END,
       1,
       SYSUTCDATETIME(),
       SYSUTCDATETIME()
FROM dbo.TenantSubscriptions ts
WHERE NOT EXISTS (SELECT 1 FROM dbo.SaasTenants st WHERE st.TenantId = ts.TenantId);

UPDATE dbo.Roles
SET SecurityDomain = N'PLATFORM'
WHERE RoleName IN (N'SupportAdmin', N'PlatformSuperAdmin', N'PlatformOpsAdmin', N'PlatformBillingAdmin', N'PlatformSupportAgent');

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleName = N'PlatformSuperAdmin')
    INSERT INTO dbo.Roles (RoleName, Description, SecurityDomain, GrantRevision) VALUES (N'PlatformSuperAdmin', N'Quản trị viên Tối cao Nền tảng SaaS', N'PLATFORM', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleName = N'PlatformOpsAdmin')
    INSERT INTO dbo.Roles (RoleName, Description, SecurityDomain, GrantRevision) VALUES (N'PlatformOpsAdmin', N'Quản trị Vận hành & Hạ tầng SaaS', N'PLATFORM', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleName = N'PlatformBillingAdmin')
    INSERT INTO dbo.Roles (RoleName, Description, SecurityDomain, GrantRevision) VALUES (N'PlatformBillingAdmin', N'Quản trị Doanh thu, Gói cước & Hóa đơn SaaS', N'PLATFORM', 1);
IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE RoleName = N'PlatformSupportAgent')
    INSERT INTO dbo.Roles (RoleName, Description, SecurityDomain, GrantRevision) VALUES (N'PlatformSupportAgent', N'Chuyên viên Hỗ trợ Kỹ thuật SaaS (Impersonation)', N'PLATFORM', 1);

UPDATE u
SET u.SecurityDomain = N'PLATFORM'
FROM dbo.Users u
INNER JOIN dbo.Roles r ON u.RoleId = r.Id
WHERE u.Username = N'qa_admin' OR r.SecurityDomain = N'PLATFORM';

DECLARE @PlatformPerms TABLE (Code nvarchar(100) COLLATE Latin1_General_100_BIN2, Description nvarchar(250));
INSERT INTO @PlatformPerms (Code, Description) VALUES
(N'platform.dashboard.read', N'Xem bảng điều khiển trung tâm điều hành SaaS Control Center'),
(N'platform.tenants.read', N'Xem danh sách khách hàng thuê bao (Tenants) trên toàn nền tảng'),
(N'platform.tenants.manage', N'Khởi tạo, đình chỉ, kích hoạt và quản lý vòng đời Tenant'),
(N'platform.subscriptions.manage', N'Quản lý gói cước đăng ký (Plans & Subscriptions) của các Tenant'),
(N'platform.billing.manage', N'Quản lý hóa đơn thuê bao SaaS và đối soát thanh toán toàn nền tảng'),
(N'platform.billing.reconcile', N'Đối soát và xác nhận tất toán thủ công hóa đơn SaaS trên toàn nền tảng'),
(N'platform.quotas.manage', N'Cấu hình hạn mức đơn hàng, số kho và tài khoản cho từng Tenant'),
(N'platform.integrations.manage', N'Giám sát và cấu hình kết nối đối tác vận chuyển / sàn TMĐT cấp nền tảng'),
(N'platform.monitoring.read', N'Giám sát sức khỏe hệ thống, Outbox/Inbox và tiến trình chạy ngầm'),
(N'platform.audit.read', N'Tra cứu nhật ký kiểm toán bảo mật và phiên Impersonation toàn nền tảng');

INSERT INTO dbo.Permissions (Code, Description, CreatedAt)
SELECT p.Code, p.Description, SYSUTCDATETIME()
FROM @PlatformPerms p
WHERE NOT EXISTS (SELECT 1 FROM dbo.Permissions existing WHERE existing.Code = p.Code);

INSERT INTO dbo.RolePermissions (RoleId, PermissionId, GrantedAt)
SELECT r.Id, p.Id, SYSUTCDATETIME()
FROM dbo.Roles r
CROSS JOIN dbo.Permissions p
WHERE p.Code LIKE N'platform.%'
  AND (
      (p.Code <> N'platform.billing.reconcile' AND r.RoleName IN (N'Admin', N'SupportAdmin', N'PlatformSuperAdmin', N'PlatformOpsAdmin', N'PlatformBillingAdmin', N'PlatformSupportAgent'))
      OR (p.Code = N'platform.billing.reconcile' AND r.RoleName IN (N'Admin', N'PlatformSuperAdmin', N'PlatformBillingAdmin'))
  )
  AND NOT EXISTS (
      SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
  );
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SaasTenants");

            migrationBuilder.DropColumn(
                name: "SecurityDomain",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityDomain",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "ImpersonatedByUserId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "SecurityDomain",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "BankAccountNumber",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "BankName",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "TaxCode",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "TaxpayerType",
                table: "BusinessPartners");

            migrationBuilder.DropColumn(
                name: "DefaultVatRatePercent",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ElectronicPxkkvcnbNo",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "InternalTransferOrderNo",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "TransporterName",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "VehiclePlateNumber",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "BuyerLegalName",
                table: "SaasBillingInvoices");

            migrationBuilder.DropColumn(
                name: "BuyerTaxCode",
                table: "SaasBillingInvoices");

            migrationBuilder.DropColumn(
                name: "SubTotalVnd",
                table: "SaasBillingInvoices");

            migrationBuilder.DropColumn(
                name: "VatAmountVnd",
                table: "SaasBillingInvoices");

            migrationBuilder.DropColumn(
                name: "VatRatePercent",
                table: "SaasBillingInvoices");
        }
    }
}
