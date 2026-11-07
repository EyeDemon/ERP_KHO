using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public sealed class Wave20PlatformEventLocalizationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static DbContextOptions<ErpKhoDbContext> CreateInMemoryOptions(string dbName) =>
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static PlatformControlCenterController CreatePlatformController(ErpKhoDbContext db)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Name, "qa_admin"),
            new(ClaimTypes.Role, AppRoles.SuperAdmin),
            new(TenantClaimTypes.TenantId, "1"),
            new(TenantClaimTypes.SecurityDomain, "PLATFORM"),
            new("permission", AppPermissions.PlatformDashboardRead),
            new("permission", AppPermissions.PlatformTenantRead),
            new("permission", AppPermissions.PlatformBillingManage),
            new("permission", AppPermissions.PlatformBillingReconcile),
            new("permission", AppPermissions.PlatformAuditRead),
            new("permission", AppPermissions.PlatformIntegrationManage)
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            TraceIdentifier = $"CORR-W20-LOC-{Guid.NewGuid():N}"[..26]
        };

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(x => x.EnvironmentName).Returns("Testing");

        return new PlatformControlCenterController(
            db,
            Mock.Of<IAuthService>(),
            envMock.Object,
            new SessionSecurityOptions())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static void SeedCanonicalTenantAndActors(ErpKhoDbContext db)
    {
        db.SaasTenants.Add(new SaasTenant
        {
            Id = 1,
            TenantId = 1,
            TenantCode = "TENANT-001",
            CompanyName = SaasBillingService.CanonicalTenant1LegalName,
            TaxCode = "0318294012",
            LifecycleStatus = "ACTIVE",
            CreatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc)
        });
        db.Warehouses.Add(new Warehouse
        {
            Id = 1,
            TenantId = 1,
            Code = "WH01",
            Name = "Kho Trung tâm Quận 7",
            Address = "TP.HCM",
            IsActive = true
        });
        db.Roles.AddRange(
            new Role { Id = 1, RoleName = "SuperAdmin", SecurityDomain = "PLATFORM", RowVersion = [1, 0, 0, 1] },
            new Role { Id = 2, RoleName = "Admin", SecurityDomain = "CUSTOMER_TENANT", RowVersion = [1, 0, 0, 2] });
        db.Users.AddRange(
            new User
            {
                Id = 1,
                TenantId = 1,
                Username = "qa_admin",
                FullName = "Quản trị viên Hệ thống",
                PasswordHash = "HASH",
                RoleId = 1,
                SecurityDomain = "PLATFORM",
                IsActive = true
            },
            new User
            {
                Id = 2,
                TenantId = 1,
                Username = "tenant_admin",
                FullName = "Quản trị viên Công ty ERP_KHO",
                PasswordHash = "HASH",
                RoleId = 2,
                SecurityDomain = "CUSTOMER_TENANT",
                IsActive = true
            });
    }

    [Fact]
    public async Task TC_W20_UX04_01_PrioritizesFiveCanonicalGovernanceEvents_CollapsesSeedRunnerLogins_And_PreservesAppendOnlyAuditLogs()
    {
        var dbName = $"{nameof(TC_W20_UX04_01_PrioritizesFiveCanonicalGovernanceEvents_CollapsesSeedRunnerLogins_And_PreservesAppendOnlyAuditLogs)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);
        SeedCanonicalTenantAndActors(db);

        var baseUtc = new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);

        // 1. Consecutive & interleaved repeated Authentication.LoginSucceeded rows of seed runner qa_admin (5 total -> collapsed into 1 "Xác thực quản trị")
        for (var i = 0; i < 3; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = 9500 + i,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Authentication.LoginSucceeded",
                EntityName = "UserSession",
                EntityId = 100 + i,
                NewValues = $"SessionId=aaaa-bbbb-{i};Entity=UserSession#{100 + i}",
                Result = "Success",
                Timestamp = baseUtc.AddMinutes(i)
            });
        }

        // 2. Pure session/token rotation & routine customer-tenant login plumbing noise (must be excluded)
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 9510,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Auth.TokenRefreshed",
                EntityName = "RefreshToken",
                EntityId = 201,
                NewValues = "TokenRotated=true",
                Result = "Success",
                Timestamp = baseUtc.AddMinutes(5)
            },
            new AuditLog
            {
                Id = 9511,
                TenantId = 1,
                UserId = 2,
                SecurityDomain = "CUSTOMER_TENANT",
                Action = "Login",
                EntityName = "UserSession",
                EntityId = 202,
                Result = "Success",
                Timestamp = baseUtc.AddMinutes(6)
            },
            new AuditLog
            {
                Id = 9512,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Session.Heartbeat",
                EntityName = "SaasTenant",
                EntityId = 1,
                Result = "Success",
                Timestamp = baseUtc.AddMinutes(7)
            },
            new AuditLog
            {
                Id = 9513,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Authentication.RefreshRotated",
                EntityName = "UserSession",
                EntityId = 203,
                Result = "Success",
                Timestamp = baseUtc.AddMinutes(8)
            });

        // 3. Duplicate warehouse operational sweep rows (4 -> 1, prioritized behind governance & admin auth)
        for (var i = 0; i < 4; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = 9520 + i,
                TenantId = 1,
                UserId = 2,
                WarehouseId = 1,
                SecurityDomain = "CUSTOMER_TENANT",
                Action = "StockReservation.SweepExpiredPreview",
                EntityName = "StockReservationSweep",
                EntityId = 1,
                NewValues = "BATCH-SWEEP-01",
                Reason = "Quét giải phóng giữ chỗ quá hạn tự động sau kiểm định WAVE-19",
                Result = "Success",
                Timestamp = baseUtc.AddMinutes(10 + i)
            });
        }

        // 4. Duplicate warehouse quota update spam rows (3 -> 1 "Cập nhật hạn mức kho")
        for (var i = 0; i < 3; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = 9530 + i,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_TENANT_QUOTA_UPDATED",
                EntityName = "TenantSubscription",
                EntityId = 1,
                OldValues = "{\"planCode\":\"GROWTH\",\"monthlyOrderQuota\":10000,\"maxWarehouses\":3}",
                NewValues = "{\"tenantCode\":\"TENANT-001\",\"planCode\":\"GROWTH\",\"monthlyOrderQuota\":15000,\"maxWarehouses\":4,\"actorUsername\":\"qa_admin\"}",
                Reason = "Điều chỉnh hạn mức kho thuê bao sau kiểm định đối kháng WAVE-18",
                Result = "Success",
                CorrelationId = $"CORR-QUOTA-{i}",
                Timestamp = baseUtc.AddMinutes(20 + i)
            });
        }

        // 5. Governance, VietQR reconciliation, tenant onboarding, lifecycle, RBAC & 9-dimension inventory integrity check
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 9540,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_BILLING_MANUAL_RECONCILE",
                EntityName = "SaasBillingInvoice",
                EntityId = 1,
                OldValues = "{\"invoiceId\":1,\"invoiceCode\":\"INV-SAAS-202610-001\"}",
                NewValues = "{\"invoiceId\":1,\"invoiceCode\":\"INV-SAAS-202610-001\",\"actorUsername\":\"qa_admin\",\"status\":\"PAID\"}",
                Reason = "Đối soát sao kê VietQR kỳ tháng 10/2026 khớp 1.490.000 đ • Công ty ERP_KHO",
                Result = "Success",
                CorrelationId = "CORR-RECON-9540",
                Timestamp = baseUtc.AddMinutes(30)
            },
            new AuditLog
            {
                Id = 9541,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_TENANT_STATUS_CHANGED",
                EntityName = "SaasTenant",
                EntityId = 1,
                OldValues = "{\"tenantId\":1,\"tenantCode\":\"TENANT-001\",\"status\":\"SUSPENDED\"}",
                NewValues = "{\"tenantId\":1,\"tenantCode\":\"TENANT-001\",\"status\":\"ACTIVE\",\"actorUsername\":\"qa_admin\"}",
                Reason = "Khôi phục trạng thái chuẩn ACTIVE sau kiểm định đối kháng WAVE-17",
                Result = "Success",
                CorrelationId = "CORR-STATUS-9541",
                Timestamp = baseUtc.AddMinutes(35)
            },
            new AuditLog
            {
                Id = 9542,
                TenantId = 1,
                UserId = 2,
                SecurityDomain = "CUSTOMER_TENANT",
                Action = "Permission.Grant",
                EntityName = "Role",
                EntityId = 2,
                OldValues = "{\"permissionCode\":\"inventory.cost.read\",\"granted\":false}",
                NewValues = "{\"permissionCode\":\"inventory.cost.read\",\"granted\":true}",
                Result = "Success",
                CorrelationId = "CORR-PERM-9542",
                Timestamp = baseUtc.AddMinutes(40)
            },
            new AuditLog
            {
                Id = 9543,
                TenantId = 1,
                UserId = null,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_SAAS_INVOICE_RECONCILED",
                EntityName = "SaasBillingInvoice",
                EntityId = 2,
                OldValues = "{\"invoiceId\":2,\"invoiceCode\":\"INV-SAAS-202611-002\",\"status\":\"UNPAID\"}",
                NewValues = "{\"invoiceId\":2,\"invoiceCode\":\"INV-SAAS-202611-002\",\"status\":\"PAID\",\"paymentMethod\":\"VIETQR_TPBANK_RECONCILED\"}",
                Reason = "Đối soát thanh toán hóa đơn INV-SAAS-202611-002 qua VietQR TPBank",
                Result = "Success",
                CorrelationId = "CORR-VIETQR-9543",
                Timestamp = baseUtc.AddMinutes(42)
            },
            new AuditLog
            {
                Id = 9544,
                TenantId = 2,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_TENANT_ONBOARDED",
                EntityName = "SaasTenant",
                EntityId = 2,
                NewValues = "{\"tenantId\":2,\"tenantCode\":\"TENANT-002\",\"status\":\"ACTIVE\",\"planCode\":\"GROWTH\"}",
                Reason = "Khởi tạo doanh nghiệp thuê bao mới TENANT-002 trên gói GROWTH",
                Result = "Success",
                CorrelationId = "CORR-ONBOARD-9544",
                Timestamp = baseUtc.AddMinutes(44)
            },
            new AuditLog
            {
                Id = 9545,
                TenantId = 1,
                UserId = null,
                SecurityDomain = "PLATFORM",
                Action = "DAILY_INVENTORY_INTEGRITY_CHECK",
                EntityName = "InventoryStock",
                NewValues = "DriftCount=0;CheckedBuckets=9",
                Reason = "Kiểm tra tự động tính toàn vẹn tồn kho 9 chiều toàn hệ thống",
                Result = "Success",
                CorrelationId = "CORR-PARITY-9545",
                Timestamp = baseUtc.AddMinutes(46)
            });

        // Second consecutive cluster of seed-runner logins (2 more -> total 5 collapsed for qa_admin)
        for (var i = 0; i < 2; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = 9550 + i,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Authentication.LoginSucceeded",
                EntityName = "UserSession",
                EntityId = 300 + i,
                NewValues = $"SessionId=cccc-dddd-{i}",
                Result = "Success",
                Timestamp = baseUtc.AddMinutes(50 + i)
            });
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var totalDbRowsBefore = await db.AuditLogs.IgnoreQueryFilters().CountAsync();
        totalDbRowsBefore.Should().Be(22);

        var platformCtrl = CreatePlatformController(db);
        var auditOk = (await platformCtrl.GetPlatformAuditLogs()).Should().BeOfType<OkObjectResult>().Subject;
        var dashOk = (await platformCtrl.GetDashboard()).Should().BeOfType<OkObjectResult>().Subject;

        // Strict AsNoTracking + Append-Only immutability on dbo.AuditLogs
        db.ChangeTracker.HasChanges().Should().BeFalse();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync()).Should().Be(totalDbRowsBefore);

        var dashJson = JsonSerializer.Serialize(dashOk.Value, JsonOpts);
        dashJson.Should().NotContain("Authentication.");
        dashJson.Should().NotContain("Auth.");
        dashJson.Should().NotContain("Session.Heartbeat");
        dashJson.Should().NotContain("UserSession");
        dashJson.Should().NotContain("RefreshToken");
        dashJson.Should().NotContain("SessionId=");

        var auditJson = JsonSerializer.Serialize(auditOk.Value, JsonOpts);
        auditJson.Should().NotContain("Authentication.");
        auditJson.Should().NotContain("Auth.");
        auditJson.Should().NotContain("Session.Heartbeat");
        auditJson.Should().NotContain("UserSession");
        auditJson.Should().NotContain("RefreshToken");
        auditJson.Should().NotContain("SessionId=");

        using var auditDoc = JsonDocument.Parse(auditJson);
        var items = auditDoc.RootElement.GetProperty("items").EnumerateArray().ToList();

        // 7 distinct Tier-0 governance events + 1 collapsed Tier-1 "Xác thực quản trị" + 1 deduplicated Tier-2 operational sweep = 9 items
        auditDoc.RootElement.GetProperty("totalCount").GetInt32().Should().Be(9);
        items.Count.Should().Be(9);

        var labelsVi = items.Select(x => x.GetProperty("actionLabelVi").GetString()!).ToList();
        labelsVi.Should().Contain([
            "Đối soát hóa đơn VietQR",
            "Cập nhật hạn mức kho",
            "Khởi tạo doanh nghiệp",
            "Kiểm tra toàn vẹn tồn kho 9 chiều",
            "Xác thực quản trị"
        ]);

        // Substantive governance rows precede collapsed "Xác thực quản trị", which precedes operational warehouse sweep
        var authIdx = items.FindIndex(x => x.GetProperty("action").GetString() == "PLATFORM_ADMIN_AUTH_VERIFIED");
        var sweepIdx = items.FindIndex(x => x.GetProperty("action").GetString() == "StockReservation.SweepExpiredPreview");
        authIdx.Should().Be(7);
        sweepIdx.Should().Be(8);

        var authRow = items[authIdx];
        authRow.GetProperty("actionLabelVi").GetString().Should().Be("Xác thực quản trị");
        authRow.GetProperty("entityName").GetString().Should().Be("AccountSecurity");
        authRow.GetProperty("targetCodeVi").GetString().Should().Be("Cổng quản trị nền tảng");
        authRow.GetProperty("actorDisplayVi").GetString().Should().Be("Chủ dự án (SuperAdmin)");
        authRow.GetProperty("collapsedCount").GetInt32().Should().Be(5);
        authRow.GetProperty("stateChangeVi").GetString().Should().Contain("5");

        foreach (var item in items)
        {
            var actorVi = item.GetProperty("actorDisplayVi").GetString()!;
            var domainVi = item.GetProperty("securityDomainLabelVi").GetString()!;
            var actionVi = item.GetProperty("actionLabelVi").GetString()!;
            var targetVi = item.GetProperty("targetCodeVi").GetString()!;
            var stateVi = item.GetProperty("stateChangeVi").GetString()!;
            var summaryVi = item.GetProperty("summaryVi").GetString()!;

            var combined = $"{actorVi} | {domainVi} | {actionVi} | {targetVi} | {stateVi} | {summaryVi}";
            combined.Should().NotContain("WAVE-");
            combined.Should().NotContain("ERP_KHO");
            combined.Should().NotContain("DriftCount=");
            Regex.IsMatch(combined, @"\b(ACTIVE|SUSPENDED|UNPAID|PAID|qa_admin|tenant_admin)\b", RegexOptions.IgnoreCase).Should().BeFalse();
            actionVi.Should().NotContain("PLATFORM_");
            actionVi.Should().NotContain(".");
        }

        using var dashDoc = JsonDocument.Parse(dashJson);
        var dashLabels = dashDoc.RootElement.GetProperty("recentEvents").EnumerateArray()
            .Select(e => e.GetProperty("eventLabelVi").GetString()!)
            .ToList();
        dashLabels.Should().Contain([
            "Đối soát hóa đơn VietQR",
            "Cập nhật hạn mức kho",
            "Khởi tạo doanh nghiệp",
            "Kiểm tra toàn vẹn tồn kho 9 chiều",
            "Xác thực quản trị"
        ]);
    }

    [Fact]
    public async Task TC_W20_UX04_02_LoginOnlyBootstrapDatabase_CollapsesConsecutiveLoginsIntoAdminAuthEvent_NeverEmpty()
    {
        var dbName = $"{nameof(TC_W20_UX04_02_LoginOnlyBootstrapDatabase_CollapsesConsecutiveLoginsIntoAdminAuthEvent_NeverEmpty)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);
        SeedCanonicalTenantAndActors(db);

        var baseUtc = new DateTime(2026, 11, 6, 3, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 4; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = 9800 + i,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Authentication.LoginSucceeded",
                EntityName = "UserSession",
                EntityId = 400 + i,
                NewValues = $"SessionId=seed-login-{i}",
                Result = "Success",
                Timestamp = baseUtc.AddSeconds(i * 15)
            });
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var platformCtrl = CreatePlatformController(db);
        var auditOk = (await platformCtrl.GetPlatformAuditLogs()).Should().BeOfType<OkObjectResult>().Subject;
        var dashOk = (await platformCtrl.GetDashboard()).Should().BeOfType<OkObjectResult>().Subject;

        db.ChangeTracker.HasChanges().Should().BeFalse();
        (await db.AuditLogs.IgnoreQueryFilters().CountAsync()).Should().Be(4);

        var auditJson = JsonSerializer.Serialize(auditOk.Value, JsonOpts);
        auditJson.Should().NotContain("Authentication.LoginSucceeded");
        auditJson.Should().NotContain("UserSession");

        using var auditDoc = JsonDocument.Parse(auditJson);
        auditDoc.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);
        var single = auditDoc.RootElement.GetProperty("items")[0];
        single.GetProperty("action").GetString().Should().Be("PLATFORM_ADMIN_AUTH_VERIFIED");
        single.GetProperty("actionLabelVi").GetString().Should().Be("Xác thực quản trị");
        single.GetProperty("entityName").GetString().Should().Be("AccountSecurity");
        single.GetProperty("targetCodeVi").GetString().Should().Be("Cổng quản trị nền tảng");
        single.GetProperty("collapsedCount").GetInt32().Should().Be(4);

        var dashJson = JsonSerializer.Serialize(dashOk.Value, JsonOpts);
        dashJson.Should().NotContain("Authentication.LoginSucceeded");
        dashJson.Should().NotContain("UserSession");
        using var dashDoc = JsonDocument.Parse(dashJson);
        var dashLabels = dashDoc.RootElement.GetProperty("recentEvents").EnumerateArray()
            .Select(e => e.GetProperty("eventLabelVi").GetString()!)
            .ToList();
        dashLabels.Should().Contain("Xác thực quản trị");
        dashLabels.Should().Contain("Khởi tạo doanh nghiệp");
        dashLabels.Should().Contain("Kiểm tra toàn vẹn tồn kho 9 chiều");
    }

    [Fact]
    public async Task TC_W20_UX04_03_SecurityAlerts_AccountLocked_And_TokenReuse_AreLocalizedWithoutLeakingRawSessionTokens()
    {
        var dbName = $"{nameof(TC_W20_UX04_03_SecurityAlerts_AccountLocked_And_TokenReuse_AreLocalizedWithoutLeakingRawSessionTokens)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);
        SeedCanonicalTenantAndActors(db);

        var nowUtc = new DateTime(2026, 11, 6, 4, 0, 0, DateTimeKind.Utc);
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 9901,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Authentication.RefreshRotated",
                EntityName = "UserSession",
                NewValues = "SessionId=rotated-secret-guid",
                Timestamp = nowUtc.AddMinutes(-5)
            },
            new AuditLog
            {
                Id = 9902,
                TenantId = 1,
                UserId = 2,
                Action = "Authentication.AccountLocked",
                EntityName = "User",
                EntityId = 2,
                Timestamp = nowUtc.AddMinutes(-2)
            },
            new AuditLog
            {
                Id = 9903,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Authentication.RefreshTokenReuseDetected",
                EntityName = "UserSession",
                NewValues = "SessionId=stolen-token-guid",
                Timestamp = nowUtc.AddMinutes(-1)
            });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var platformCtrl = CreatePlatformController(db);
        var auditOk = (await platformCtrl.GetPlatformAuditLogs()).Should().BeOfType<OkObjectResult>().Subject;
        var auditJson = JsonSerializer.Serialize(auditOk.Value, JsonOpts);

        auditJson.Should().NotContain("Authentication.");
        auditJson.Should().NotContain("UserSession");
        auditJson.Should().NotContain("stolen-token-guid");
        auditJson.Should().NotContain("rotated-secret-guid");

        using var auditDoc = JsonDocument.Parse(auditJson);
        var items = auditDoc.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Count.Should().Be(2);
        items.Select(x => x.GetProperty("actionLabelVi").GetString()!)
            .Should().Contain(["Tạm khóa tài khoản do sai mật khẩu", "Chặn nghi vấn tái sử dụng mã phiên"]);
    }
}
