using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
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

public sealed class Wave07PlatformControlPlaneAndResidualsTests
{
    private static DbContextOptions<ErpKhoDbContext> CreateInMemoryOptions(string dbName) =>
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ControllerContext CreatePlatformControllerContext(
        int userId,
        string role,
        params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, $"platform_{role.ToLowerInvariant()}"),
            new(ClaimTypes.Role, role),
            new(TenantClaimTypes.TenantId, "1"),
            new(TenantClaimTypes.SecurityDomain, "PLATFORM")
        };
        foreach (var p in permissions)
        {
            claims.Add(new Claim("permission", p));
        }

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            TraceIdentifier = $"CORR-W7-{Guid.NewGuid():N}"[..24]
        };
        return new ControllerContext { HttpContext = httpContext };
    }

    private static IFormFile CreateSamplePdfEvidenceFile(string fileName = "UNC_VCB_20261102_1490k.pdf")
    {
        var pdfBytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF");
        var ms = new MemoryStream(pdfBytes);
        return new FormFile(ms, 0, pdfBytes.Length, "attachmentFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }

    private static PlatformControlCenterController CreatePlatformController(
        ErpKhoDbContext db,
        ControllerContext controllerContext)
    {
        var authMock = new Mock<IAuthService>();
        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(x => x.EnvironmentName).Returns("Testing");
        var controller = new PlatformControlCenterController(
            db,
            authMock.Object,
            envMock.Object,
            new SessionSecurityOptions())
        {
            ControllerContext = controllerContext
        };
        return controller;
    }

    [Fact]
    public async Task TC_W7_01_PlatformTenantStatus_RequiresReasonAndWritesAuditLog()
    {
        var dbName = $"{nameof(TC_W7_01_PlatformTenantStatus_RequiresReasonAndWritesAuditLog)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.SaasTenants.Add(new SaasTenant
        {
            Id = 1,
            TenantId = 1,
            TenantCode = "TENANT-001",
            CompanyName = "Công ty Kho vận Viking",
            LifecycleStatus = "ACTIVE",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(1, AppRoles.SuperAdmin, AppPermissions.PlatformTenantSuspend, AppPermissions.PlatformTenantWrite));

        // 1. Suspending without reason or < 10 chars -> 422 PLATFORM_SUSPENSION_REASON_REQUIRED
        var shortReasonRes = await controller.UpdateTenantStatus(
            1,
            new PlatformControlCenterController.UpdateTenantLifecycleRequest(
                Status: "SUSPENDED",
                SuspensionReason: "Ngắn"));
        var unprocessable = shortReasonRes.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        unprocessable.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        using (var errDoc = JsonSerializer.SerializeToDocument(unprocessable.Value))
        {
            errDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("PLATFORM_SUSPENSION_REASON_REQUIRED");
        }

        // 2. Suspending with valid reason (>= 10 chars) -> 200 OK + AuditLog PLATFORM_TENANT_STATUS_CHANGED
        var validReason = "Tạm khóa do quá hạn thanh toán hóa đơn SaaS kỳ tháng 11/2026 trên 72h";
        var okRes = await controller.UpdateTenantStatus(
            1,
            new PlatformControlCenterController.UpdateTenantLifecycleRequest(
                Status: "SUSPENDED",
                SuspensionReason: validReason,
                ConfirmationCode: "TENANT-001"));
        okRes.Should().BeOfType<OkObjectResult>();

        var updatedTenant = await db.SaasTenants.SingleAsync(t => t.TenantId == 1);
        updatedTenant.LifecycleStatus.Should().Be("SUSPENDED");
        updatedTenant.SuspensionReason.Should().Be(validReason);

        var auditLog = await db.AuditLogs.SingleAsync(a => a.Action == "PLATFORM_TENANT_STATUS_CHANGED" && a.EntityId == 1);
        auditLog.OldValues.Should().Contain("ACTIVE");
        auditLog.NewValues.Should().Contain("SUSPENDED");
        auditLog.Reason.Should().Be(validReason);
    }

    [Fact]
    public async Task TC_W7_02_PlatformTenantQuota_UpdatesSubscriptionAndWritesBeforeAfterAuditLog()
    {
        var dbName = $"{nameof(TC_W7_02_PlatformTenantQuota_UpdatesSubscriptionAndWritesBeforeAfterAuditLog)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = 1,
            TenantId = 1,
            PlanCode = "STARTER",
            Status = "ACTIVE",
            MonthlyOrderQuota = 1500,
            CurrentMonthOrderCount = 120,
            MaxWarehouses = 1,
            MaxUsers = 5,
            CurrentPeriodStartUtc = DateTime.UtcNow.AddDays(-5),
            CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(25),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(1, AppRoles.BillingAdmin, AppPermissions.PlatformQuotaWrite));

        var quotaRes = await controller.OverrideTenantQuota(
            1,
            new PlatformControlCenterController.OverrideTenantQuotaRequest(
                PlanCode: "ENTERPRISE",
                MonthlyOrderLimit: 50000,
                MaxWarehouses: 8,
                MaxUsers: 45,
                Reason: "Nâng hạn ngạch theo hợp đồng B2B Enterprise"));
        quotaRes.Should().BeOfType<OkObjectResult>();

        var sub = await db.TenantSubscriptions.SingleAsync(s => s.TenantId == 1);
        sub.MonthlyOrderQuota.Should().Be(50000);
        sub.MaxWarehouses.Should().Be(8);
        sub.MaxUsers.Should().Be(45);

        var auditLog = await db.AuditLogs.SingleAsync(a => a.Action == "PLATFORM_TENANT_QUOTA_UPDATED" && a.EntityId == 1);
        auditLog.OldValues.Should().Contain("1500");
        auditLog.NewValues.Should().Contain("50000");
        auditLog.Reason.Should().Contain("B2B Enterprise");
    }

    [Fact]
    public async Task TC_W7_03_PlatformDashboardAndWorkers_ReturnsCommercialKpisAndWorkerTimings()
    {
        var dbName = $"{nameof(TC_W7_03_PlatformDashboardAndWorkers_ReturnsCommercialKpisAndWorkerTimings)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.SaasTenants.Add(new SaasTenant
        {
            Id = 1,
            TenantId = 1,
            TenantCode = "TENANT-001",
            CompanyName = "Công ty Kho vận Viking",
            LifecycleStatus = "ACTIVE",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = 1,
            TenantId = 1,
            PlanCode = "GROWTH",
            Status = "ACTIVE",
            MonthlyOrderQuota = 10000,
            CurrentMonthOrderCount = 8500, // 85% >= 80% high quota usage
            MaxWarehouses = 3,
            MaxUsers = 20,
            CurrentPeriodStartUtc = DateTime.UtcNow.AddDays(-25),
            CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(5), // Expiring within 7 days
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(1, AppRoles.SuperAdmin, AppPermissions.PlatformDashboardRead));

        var dashRes = await controller.GetDashboard();
        var dashOk = dashRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var dashDoc = JsonSerializer.SerializeToDocument(dashOk.Value))
        {
            var root = dashDoc.RootElement;
            root.GetProperty("mrrVnd").GetDecimal().Should().BeGreaterThan(0m);
            root.GetProperty("arrVnd").GetDecimal().Should().Be(root.GetProperty("mrrVnd").GetDecimal() * 12m);
            root.GetProperty("expiringSoonTenantsCount").GetInt32().Should().Be(1);
            root.GetProperty("highQuotaUsageTenantsCount").GetInt32().Should().Be(1);
            root.GetProperty("driftCount").GetInt32().Should().Be(0);
            root.GetProperty("dlqCount").GetInt32().Should().Be(0);
            root.GetProperty("totpStatusLabel").GetString().Should().Contain("2FA TOTP");
        }

        var workersRes = await controller.GetWorkersHealth();
        var workersOk = workersRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var workersDoc = JsonSerializer.SerializeToDocument(workersOk.Value))
        {
            var workersArr = workersDoc.RootElement.GetProperty("workers");
            workersArr.GetArrayLength().Should().BeGreaterThanOrEqualTo(3);
            foreach (var w in workersArr.EnumerateArray())
            {
                w.GetProperty("titleVi").GetString().Should().NotBeNullOrWhiteSpace();
                w.GetProperty("descriptionVi").GetString().Should().NotBeNullOrWhiteSpace();
                w.GetProperty("lastDurationMs").GetInt32().Should().BeGreaterThan(0);
                w.GetProperty("errorCount").GetInt32().Should().Be(0);
            }
        }
    }

    [Fact]
    public async Task TC_W7_04_PlatformRoleSeparation_EnforcesGranularPermissions_And_VerifiesResiduals()
    {
        var dbName = $"{nameof(TC_W7_04_PlatformRoleSeparation_EnforcesGranularPermissions_And_VerifiesResiduals)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        // 1. SupportAdmin (only platform.support.impersonate) receives 403 Forbidden on UpdateTenantStatus & OverrideTenantQuota
        var supportController = CreatePlatformController(
            db,
            CreatePlatformControllerContext(2, AppRoles.SupportAdmin, AppPermissions.PlatformSupportImpersonate));

        var forbiddenStatusRes = await supportController.UpdateTenantStatus(
            1,
            new PlatformControlCenterController.UpdateTenantLifecycleRequest(
                Status: "SUSPENDED",
                SuspensionReason: "Thử khóa trái thẩm quyền từ SupportAdmin"));
        forbiddenStatusRes.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var forbiddenQuotaRes = await supportController.OverrideTenantQuota(
            1,
            new PlatformControlCenterController.OverrideTenantQuotaRequest(MonthlyOrderLimit: 99999));
        forbiddenQuotaRes.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // SupportAdmin IS allowed to start a Guided Support Impersonation Session with valid TicketCode & TTL
        var impRes = await supportController.StartImpersonationSession(
            new PlatformControlCenterController.StartPlatformImpersonationRequest(
                TargetTenantId: 1,
                TicketCode: "SUP-2026-777",
                Reason: "Hỗ trợ kiểm tra cấu hình in tem GHN",
                TtlMinutes: 15));
        impRes.Should().BeOfType<OkObjectResult>();
        (await db.AuditLogs.AnyAsync(a => a.Action == "PLATFORM_SUPPORT_IMPERSONATION_STARTED" && a.Reason!.Contains("SUP-2026-777")))
            .Should().BeTrue();

        // 2. CA-RES-02: Draft/Cancelled ExportReceipts do not increment CurrentMonthOrderCount, Approved does once
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = 10,
            TenantId = 1,
            PlanCode = "STARTER",
            Status = "ACTIVE",
            MonthlyOrderQuota = 1500,
            CurrentMonthOrderCount = 5,
            MaxWarehouses = 1,
            MaxUsers = 5,
            CurrentPeriodStartUtc = DateTime.UtcNow.AddDays(-2),
            CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(28),
            UpdatedAtUtc = DateTime.UtcNow
        });
        db.ExportReceipts.AddRange(
            new ExportReceipt { Id = 901, TenantId = 1, Code = "PXK-DRAFT-901", WarehouseId = 1, Status = ReceiptStatus.Draft, CreatedBy = 1 },
            new ExportReceipt { Id = 902, TenantId = 1, Code = "PXK-APPROVED-902", WarehouseId = 1, Status = ReceiptStatus.Approved, CreatedBy = 1 });
        await db.SaveChangesAsync();

        var expOps = new ExportReceiptOperationsService(db);
        await expOps.IncrementSubscriptionOrderCountAsync(default, exportReceiptId: 901);
        (await db.TenantSubscriptions.SingleAsync(s => s.Id == 10)).CurrentMonthOrderCount.Should().Be(5,
            "Draft export receipts must not inflate CurrentMonthOrderCount (CA-RES-02)");

        await expOps.IncrementSubscriptionOrderCountAsync(default, exportReceiptId: 902);
        await expOps.IncrementSubscriptionOrderCountAsync(default, exportReceiptId: 902); // Idempotent duplicate call
        (await db.TenantSubscriptions.SingleAsync(s => s.Id == 10)).CurrentMonthOrderCount.Should().Be(6,
            "Approved export receipt increments CurrentMonthOrderCount exactly once");
    }

    [Fact]
    public async Task TC_W7_05_Wave08_QuotaBelowCurrentUsageRejected_And_UnifiedVietQrAndWorkerHeartbeat()
    {
        var dbName = $"{nameof(TC_W7_05_Wave08_QuotaBelowCurrentUsageRejected_And_UnifiedVietQrAndWorkerHeartbeat)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Warehouses.AddRange(
            new Warehouse { Id = 1, TenantId = 1, Code = "WH-01", Name = "Kho trung tâm 01", Address = "TP.HCM", IsActive = true },
            new Warehouse { Id = 2, TenantId = 1, Code = "WH-02", Name = "Kho trung tâm 02", Address = "Hà Nội", IsActive = false });
        db.Users.AddRange(
            new User { Id = 11, TenantId = 1, Username = "wh_manager_1", FullName = "Quản lý Kho 1", PasswordHash = "hash", RoleId = 2, IsActive = true, SecurityDomain = "CUSTOMER_TENANT" },
            new User { Id = 12, TenantId = 1, Username = "wh_staff_1", FullName = "Nhân viên Kho 1", PasswordHash = "hash", RoleId = 4, IsActive = true, SecurityDomain = "CUSTOMER_TENANT" },
            new User { Id = 13, TenantId = 1, Username = "wh_staff_2", FullName = "Nhân viên Kho 2", PasswordHash = "hash", RoleId = 4, IsActive = true, SecurityDomain = "CUSTOMER_TENANT" });
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = 20,
            TenantId = 1,
            PlanCode = "GROWTH",
            Status = "ACTIVE",
            MonthlyOrderQuota = 10000,
            CurrentMonthOrderCount = 250,
            MaxWarehouses = 5,
            MaxUsers = 20,
            CurrentPeriodStartUtc = DateTime.UtcNow.AddDays(-10),
            CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(20),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(
                1,
                AppRoles.SuperAdmin,
                AppPermissions.PlatformDashboardRead,
                AppPermissions.PlatformTenantRead,
                AppPermissions.PlatformQuotaOverride,
                AppPermissions.PlatformQuotaWrite,
                AppPermissions.PlatformIntegrationManage));

        // 1a. MaxWarehouses = 0 (below usedWarehouses = 1) -> 422 QUOTA_BELOW_CURRENT_USAGE
        var lowWhRes = await controller.OverrideTenantQuota(
            1,
            new PlatformControlCenterController.OverrideTenantQuotaRequest(
                MaxWarehouses: 0,
                MaxUsers: 20,
                MonthlyOrderLimit: 10000));
        var lowWh422 = lowWhRes.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(lowWh422.Value))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("QUOTA_BELOW_CURRENT_USAGE");
            doc.RootElement.GetProperty("usedWarehouses").GetInt32().Should().Be(1);
        }

        // 1b. MaxUsers = 1 (below usedUsers = 3) -> 422 QUOTA_BELOW_CURRENT_USAGE
        var lowUserRes = await controller.OverrideTenantQuota(
            1,
            new PlatformControlCenterController.OverrideTenantQuotaRequest(
                MaxWarehouses: 5,
                MaxUsers: 1,
                MonthlyOrderLimit: 10000));
        var lowUser422 = lowUserRes.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(lowUser422.Value))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("QUOTA_BELOW_CURRENT_USAGE");
            doc.RootElement.GetProperty("usedUsers").GetInt32().Should().Be(3);
        }

        // 1c. MonthlyOrderLimit = 100 (below currentMonthOrders = 250) -> 422 QUOTA_BELOW_CURRENT_USAGE
        var lowOrderRes = await controller.OverrideTenantQuota(
            1,
            new PlatformControlCenterController.OverrideTenantQuotaRequest(
                MaxWarehouses: 5,
                MaxUsers: 20,
                MonthlyOrderLimit: 100));
        var lowOrder422 = lowOrderRes.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(lowOrder422.Value))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("QUOTA_BELOW_CURRENT_USAGE");
            doc.RootElement.GetProperty("currentMonthOrders").GetInt32().Should().Be(250);
        }

        // 1d. StopSupportImpersonation -> 200 OK & PLATFORM_SUPPORT_IMPERSONATION_STOPPED audit log
        var stopImpRes = await controller.StopSupportImpersonation();
        var stopImpOk = stopImpRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var stopDoc = JsonSerializer.SerializeToDocument(stopImpOk.Value))
        {
            stopDoc.RootElement.GetProperty("stopped").GetBoolean().Should().BeTrue();
            stopDoc.RootElement.GetProperty("auditAction").GetString().Should().Be("PLATFORM_SUPPORT_IMPERSONATION_STOPPED");
        }
        (await db.AuditLogs.AnyAsync(a => a.Action == "PLATFORM_SUPPORT_IMPERSONATION_STOPPED"))
            .Should().BeTrue();

        // 2. GET /api/v1/platform/integrations/health -> canonical TPBank VietQR beneficiary (98418102005 / HUYNH PHI VAN)
        var healthRes = await controller.GetIntegrationsHealth();
        var healthOk = healthRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var healthDoc = JsonSerializer.SerializeToDocument(healthOk.Value))
        {
            var vietQr = healthDoc.RootElement.GetProperty("vietQrWebhookStatus");
            vietQr.GetProperty("accountNo").GetString().Should().Be("98418102005");
            vietQr.GetProperty("bankCode").GetString().Should().Be("TPB");
            vietQr.GetProperty("bankName").GetString().Should().Be("TPBank");
            vietQr.GetProperty("bankBin").GetString().Should().Be("970423");
            vietQr.GetProperty("accountName").GetString().Should().Be("HUYNH PHI VAN");
        }

        // 3. GET /api/v1/platform/tenants -> usedWarehouses >= 1, usedUsers >= 1, maxWarehouses, maxUsers
        var tenantsRes = await controller.GetTenants();
        var tenantsOk = tenantsRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var tenantsDoc = JsonSerializer.SerializeToDocument(tenantsOk.Value))
        {
            var firstTenant = tenantsDoc.RootElement.GetProperty("items")[0];
            firstTenant.GetProperty("usedWarehouses").GetInt32().Should().BeGreaterThanOrEqualTo(1);
            firstTenant.GetProperty("usedUsers").GetInt32().Should().BeGreaterThanOrEqualTo(1);
            firstTenant.GetProperty("maxWarehouses").GetInt32().Should().Be(5);
            firstTenant.GetProperty("maxUsers").GetInt32().Should().Be(20);
            firstTenant.GetProperty("maxResourceUtilizationPct").GetDecimal().Should().BeGreaterThan(0m);
        }

        // 4. GET /api/v1/platform/monitoring/workers -> lastRunAtUtc not empty on all 3 workers
        var workersRes = await controller.GetWorkersHealth();
        var workersOk = workersRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var workersDoc = JsonSerializer.SerializeToDocument(workersOk.Value))
        {
            var workersArr = workersDoc.RootElement.GetProperty("workers");
            workersArr.GetArrayLength().Should().Be(3);
            foreach (var w in workersArr.EnumerateArray())
            {
                w.GetProperty("lastRunAtUtc").GetString().Should().NotBeNullOrWhiteSpace();
                w.GetProperty("lastRunRelativeVi").GetString().Should().NotBeNullOrWhiteSpace();
                w.GetProperty("statusLabelVi").GetString().Should().Be("Hoạt động tốt");
            }
        }
    }

    [Fact]
    public async Task TC_W9_01_Wave09_DistinctWorkerHeartbeats_DistinctIntegrationMetrics_And_QuotaContractMetadata()
    {
        var dbName = $"{nameof(TC_W9_01_Wave09_DistinctWorkerHeartbeats_DistinctIntegrationMetrics_And_QuotaContractMetadata)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Warehouses.AddRange(
            new Warehouse { Id = 1, TenantId = 1, Code = "WH01", Name = "Kho trung tâm 01", Address = "TP.HCM", IsActive = true },
            new Warehouse { Id = 2, TenantId = 1, Code = "WH02", Name = "Kho vệ tinh 02", Address = "Hà Nội", IsActive = false },
            new Warehouse { Id = 3, TenantId = 1, Code = "WH03", Name = "Kho vệ tinh 03", Address = "Đà Nẵng", IsActive = false });
        db.Shipments.AddRange(
            new Shipment { Id = 1, TenantId = 1, ShipmentCode = "SHP-W10-01", WarehouseId = 1, SourceType = "ExportReceipt", CreatedBy = 1 },
            new Shipment { Id = 2, TenantId = 1, ShipmentCode = "SHP-W10-02", WarehouseId = 1, SourceType = "ExportReceipt", CreatedBy = 1 });
        db.SalesOrders.AddRange(
            new SalesOrder { Id = 1, TenantId = 1, OrderCode = "SO-W10-01", ExternalOrderId = "SHOPEE-01", CustomerId = 1, WarehouseId = 1, CreatedBy = 1 },
            new SalesOrder { Id = 2, TenantId = 1, OrderCode = "SO-W10-02", ExternalOrderId = "SHOPEE-02", CustomerId = 1, WarehouseId = 1, CreatedBy = 1 },
            new SalesOrder { Id = 3, TenantId = 1, OrderCode = "SO-W10-03", ExternalOrderId = "TIKTOK-03", CustomerId = 1, WarehouseId = 1, CreatedBy = 1 });
        db.SaasBillingInvoices.Add(new SaasBillingInvoice
        {
            Id = 1,
            TenantId = 1,
            InvoiceCode = "INV-W10-001",
            PlanCode = "GROWTH",
            AmountVnd = 1490000m,
            PaymentProvider = "VIETQR",
            Status = "PAID",
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            PaidAtUtc = DateTime.UtcNow.AddHours(-1)
        });
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = 30,
            TenantId = 1,
            PlanCode = "GROWTH",
            Status = "ACTIVE",
            MonthlyOrderQuota = 10000,
            CurrentMonthOrderCount = 150,
            MaxWarehouses = 3,
            MaxUsers = 20,
            CurrentPeriodStartUtc = DateTime.UtcNow.AddDays(-5),
            CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(25),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(
                1,
                AppRoles.SuperAdmin,
                AppPermissions.PlatformDashboardRead,
                AppPermissions.PlatformTenantRead,
                AppPermissions.PlatformTenantSuspend,
                AppPermissions.PlatformTenantWrite,
                AppPermissions.PlatformQuotaOverride,
                AppPermissions.PlatformQuotaWrite,
                AppPermissions.PlatformSupportImpersonate,
                AppPermissions.PlatformIntegrationManage,
                AppPermissions.PlatformBillingManage));

        // 1. Distinct Integration Telemetry (Real DB Counts without Math.Max(24/31/12)) & Dynamic Clock Format
        var healthRes = await controller.GetIntegrationsHealth();
        var healthOk = healthRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var healthDoc = JsonSerializer.SerializeToDocument(healthOk.Value))
        {
            var root = healthDoc.RootElement;
            root.GetProperty("environmentBadgeLabel").GetString().Should().Be("Môi trường: Sandbox thử nghiệm");

            var ghn = root.GetProperty("ghnGatewayHealth");
            var shopee = root.GetProperty("shopeeTiktokWebhookStatus");
            var vietqr = root.GetProperty("vietQrWebhookStatus");

            var ghnSuccess = ghn.GetProperty("eventsLast24hSuccess").GetInt32();
            var shopeeSuccess = shopee.GetProperty("eventsLast24hSuccess").GetInt32();
            var vietqrSuccess = vietqr.GetProperty("eventsLast24hSuccess").GetInt32();
            ghnSuccess.Should().Be(2, "must reflect real DB Shipments + Outbox count without Math.Max(..., 24)");
            shopeeSuccess.Should().Be(3, "must reflect real DB TrackingEvents + SalesOrders count without Math.Max(..., 31)");
            vietqrSuccess.Should().Be(1, "must reflect real DB SaasBillingInvoices count without Math.Max(..., 12)");
            new[] { ghnSuccess, shopeeSuccess, vietqrSuccess }.Distinct().Count().Should().Be(3);

            var ghnLatency = ghn.GetProperty("latencyMs").GetInt32();
            var shopeeLatency = shopee.GetProperty("latencyMs").GetInt32();
            var vietqrLatency = vietqr.GetProperty("latencyMs").GetInt32();
            new[] { ghnLatency, shopeeLatency, vietqrLatency }.Distinct().Count().Should().Be(3);

            ghn.GetProperty("lastCheckedRelativeVi").GetString().Should().MatchRegex(@"^\d{2}:\d{2}:\d{2} \(\d+ giây trước\)$");
            shopee.GetProperty("lastCheckedRelativeVi").GetString().Should().MatchRegex(@"^\d{2}:\d{2}:\d{2} \(\d+ giây trước\)$");
            vietqr.GetProperty("lastCheckedRelativeVi").GetString().Should().MatchRegex(@"^\d{2}:\d{2}:\d{2} \(\d+ giây trước\)$");
            vietqr.GetProperty("displayAccountVi").GetString().Should().Be("TPBank • BIN 970423 • STK: 98418102005 (HUYNH PHI VAN)");
        }

        // 2. Distinct Worker Heartbeats & Dynamic Clock Format
        var workersRes = await controller.GetWorkersHealth();
        var workersOk = workersRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var workersDoc = JsonSerializer.SerializeToDocument(workersOk.Value))
        {
            var workers = workersDoc.RootElement.GetProperty("workers").EnumerateArray().ToArray();
            workers.Should().HaveCount(3);
            workers.Select(w => w.GetProperty("lastRunRelativeVi").GetString()).Distinct().Count().Should().Be(3);
            foreach (var w in workers)
            {
                w.GetProperty("lastRunRelativeVi").GetString().Should().MatchRegex(@"^\d{2}:\d{2}:\d{2} \(\d+ giây trước\)$");
            }
            workers.Select(w => w.GetProperty("lastDurationMs").GetInt32()).Distinct().Count().Should().Be(3);
            workers[0].GetProperty("titleVi").GetString().Should().Be("Tiến trình đồng bộ hóa đơn & sổ cái");
            workers[1].GetProperty("titleVi").GetString().Should().Be("Tiến trình đối soát bất biến tồn kho 9 chiều");
            workers[2].GetProperty("titleVi").GetString().Should().Be("Tiến trình thu hồi webhook & dọn phiên hỗ trợ");
        }

        // 3. Cross-Portal Warehouse Count Parity (1 / 3 kho) & Canonical Legal Name across /platform/tenants, SaasBillingService, and ReportOperationsService
        var tenantsRes = await controller.GetTenants();
        var tenantsOk = tenantsRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var tenantsDoc = JsonSerializer.SerializeToDocument(tenantsOk.Value))
        {
            var t1 = tenantsDoc.RootElement.GetProperty("items")[0];
            t1.GetProperty("usedWarehouses").GetInt32().Should().Be(1);
            t1.GetProperty("maxWarehouses").GetInt32().Should().Be(3);
            t1.GetProperty("companyName").GetString().Should().Be("CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO");
        }

        var saasBillingService = new SaasBillingService(db);
        var subPayload = await saasBillingService.GetSubscriptionAsync(null, null, default);
        using (var subDoc = JsonSerializer.SerializeToDocument(subPayload))
        {
            subDoc.RootElement.GetProperty("currentWarehouseCount").GetInt32().Should().Be(1);
            subDoc.RootElement.GetProperty("activeWarehouses").GetInt32().Should().Be(1);
            subDoc.RootElement.GetProperty("usedWarehouses").GetInt32().Should().Be(1);
            subDoc.RootElement.GetProperty("maxWarehouses").GetInt32().Should().Be(3);
            subDoc.RootElement.GetProperty("companyName").GetString().Should().Be("CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO");
        }

        var reportOps = new ReportOperationsService(db);
        var opDashOutcome = await reportOps.GetOperationalDashboardAsync(null, default);
        opDashOutcome.StatusCode.Should().Be(200);
        using (var opDashDoc = JsonSerializer.SerializeToDocument(opDashOutcome.Payload))
        {
            opDashDoc.RootElement.GetProperty("warehouseCount").GetInt32().Should().Be(1);
        }

        // 4. Suspend Reason Category & Audit Metadata (suspendedBy, suspendedAtVi)
        var statusRes = await controller.UpdateTenantStatus(
            1,
            new PlatformControlCenterController.UpdateTenantLifecycleRequest(
                Status: "SUSPENDED",
                SuspensionReason: "Tạm khóa do quá hạn thanh toán hóa đơn kỳ tháng 11/2026",
                ReasonCategory: "BILLING_OVERDUE",
                ConfirmationCode: "TENANT-001"));
        var statusOk = statusRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var statusDoc = JsonSerializer.SerializeToDocument(statusOk.Value))
        {
            statusDoc.RootElement.GetProperty("reasonCategory").GetString().Should().Be("BILLING_OVERDUE");
            statusDoc.RootElement.GetProperty("suspendedBy").GetString().Should().NotBeNullOrWhiteSpace();
            statusDoc.RootElement.GetProperty("suspendedAtVi").GetString().Should().Be("Vừa xong");
            statusDoc.RootElement.GetProperty("companyName").GetString().Should().Be("CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO");
        }

        // 5. Quota Override ContractReference & EffectiveUntilDate
        var quotaRes = await controller.OverrideTenantQuota(
            1,
            new PlatformControlCenterController.OverrideTenantQuotaRequest(
                PlanCode: "GROWTH",
                MaxWarehouses: 4,
                MaxUsers: 25,
                MonthlyOrderLimit: 12000,
                Reason: "Mở rộng hạn mức theo phụ lục hợp đồng quý IV",
                ContractReference: "PLHD-2026-089",
                EffectiveUntilDate: "2027-12-31"));
        var quotaOk = quotaRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var quotaDoc = JsonSerializer.SerializeToDocument(quotaOk.Value))
        {
            quotaDoc.RootElement.GetProperty("contractReference").GetString().Should().Be("PLHD-2026-089");
            quotaDoc.RootElement.GetProperty("effectiveUntilDate").GetString().Should().Be("2027-12-31");
        }

        // 6. Impersonation Active Session Query & Duplicate Session Guard (409 IMPERSONATION_SESSION_ALREADY_ACTIVE)
        var startImpRes = await controller.StartImpersonationSession(
            new PlatformControlCenterController.StartPlatformImpersonationRequest(
                TargetTenantId: 1,
                TicketCode: "SUP-2026-901",
                Reason: "Kiểm tra đối soát tồn kho 9 chiều",
                TtlMinutes: 30));
        var startImpOk = startImpRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var startImpDoc = JsonSerializer.SerializeToDocument(startImpOk.Value))
        {
            startImpDoc.RootElement.GetProperty("startedByUsername").GetString().Should().NotBeNullOrWhiteSpace();
            startImpDoc.RootElement.GetProperty("startedAtUtc").GetString().Should().NotBeNullOrWhiteSpace();
        }

        var activeImpRes = await controller.GetActiveImpersonationSessions();
        var activeImpOk = activeImpRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var activeImpDoc = JsonSerializer.SerializeToDocument(activeImpOk.Value))
        {
            activeImpDoc.RootElement.GetProperty("activeCount").GetInt32().Should().Be(1);
            activeImpDoc.RootElement.GetProperty("activeSession").GetProperty("ticketCode").GetString().Should().Be("SUP-2026-901");
        }

        var dupImpRes = await controller.StartImpersonationSession(
            new PlatformControlCenterController.StartPlatformImpersonationRequest(
                TargetTenantId: 1,
                TicketCode: "SUP-2026-901",
                Reason: "Gửi trùng phiên hỗ trợ đang active",
                TtlMinutes: 30,
                RejectIfActiveSessionExists: true));
        var dupImp409 = dupImpRes.Should().BeOfType<ConflictObjectResult>().Subject;
        using (var dupDoc = JsonSerializer.SerializeToDocument(dupImp409.Value))
        {
            dupDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("IMPERSONATION_SESSION_ALREADY_ACTIVE");
        }

        await controller.StopSupportImpersonation();
        var afterStopRes = await controller.GetActiveImpersonationSessions();
        var afterStopOk = afterStopRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var afterStopDoc = JsonSerializer.SerializeToDocument(afterStopOk.Value))
        {
            afterStopDoc.RootElement.GetProperty("activeCount").GetInt32().Should().Be(0);
        }
    }

    [Fact]
    public async Task TC_W11_01_SecondActiveWarehouse_IncrementsWarehouseCount_AcrossAllFourEndpoints()
    {
        var dbName = $"{nameof(TC_W11_01_SecondActiveWarehouse_IncrementsWarehouseCount_AcrossAllFourEndpoints)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Warehouses.AddRange(
            new Warehouse { Id = 1, TenantId = 1, Code = "WH01", Name = "Kho Trung Tâm Quận 7", Address = "TP.HCM", IsActive = true },
            new Warehouse { Id = 1403, TenantId = 1, Code = "WH02", Name = "Kho Vệ Tinh Cũ", Address = "Hà Nội", IsActive = false },
            new Warehouse { Id = 1648, TenantId = 1, Code = "WH-MAIN", Name = "Kho Khởi Tạo Cũ", Address = "TP.HCM", IsActive = false });
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = 40,
            TenantId = 1,
            PlanCode = "GROWTH",
            Status = "ACTIVE",
            MonthlyOrderQuota = 10000,
            CurrentMonthOrderCount = 6420,
            MaxWarehouses = 3,
            MaxUsers = 20,
            CurrentPeriodStartUtc = DateTime.UtcNow.AddDays(-5),
            CurrentPeriodEndUtc = DateTime.UtcNow.AddDays(25),
            UpdatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(
                1,
                AppRoles.SuperAdmin,
                AppPermissions.PlatformDashboardRead,
                AppPermissions.PlatformTenantRead,
                AppPermissions.PlatformQuotaOverride,
                AppPermissions.PlatformQuotaWrite));
        var saasBillingService = new SaasBillingService(db);
        var reportOps = new ReportOperationsService(db);

        // Baseline: 1 active warehouse (WH01 IsActive=true, WH02/WH-MAIN IsActive=false)
        var initialTenantsOk = (await controller.GetTenants()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(initialTenantsOk.Value))
        {
            doc.RootElement.GetProperty("items")[0].GetProperty("usedWarehouses").GetInt32().Should().Be(1);
        }

        var initialQuotasOk = (await controller.GetQuotas()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(initialQuotasOk.Value))
        {
            var q0 = doc.RootElement.GetProperty("items")[0];
            q0.GetProperty("usedWarehouses").GetInt32().Should().Be(1);
            q0.GetProperty("maxWarehouses").GetInt32().Should().Be(3);
            q0.GetProperty("contractAddendumCode").GetString().Should().Be("PLHD-2026-001");
            q0.GetProperty("effectiveUntilUtc").GetString().Should().BeEmpty();
            q0.GetProperty("warehouseUtilizationPct").GetDecimal().Should().Be(33.3m);
        }

        using (var subDoc = JsonSerializer.SerializeToDocument(await saasBillingService.GetSubscriptionAsync(null, null, default)))
        {
            subDoc.RootElement.GetProperty("currentWarehouseCount").GetInt32().Should().Be(1);
            subDoc.RootElement.GetProperty("usedWarehouses").GetInt32().Should().Be(1);
        }

        using (var opDoc = JsonSerializer.SerializeToDocument((await reportOps.GetOperationalDashboardAsync(null, default)).Payload))
        {
            opDoc.RootElement.GetProperty("warehouseCount").GetInt32().Should().Be(1);
        }

        // Act: Insert a 2nd active warehouse (Code = "WH-TEST-02", IsActive = true) for TenantId = 1
        db.Warehouses.Add(new Warehouse
        {
            Id = 2002,
            TenantId = 1,
            Code = "WH-TEST-02",
            Name = "Kho Thử Nghiệm 02",
            Address = "Đà Nẵng",
            IsActive = true
        });
        await db.SaveChangesAsync();

        // Assert: All 4 endpoints/services genuinely increment from 1 -> 2 (no hardcoded clamp!)
        var afterTenantsOk = (await controller.GetTenants()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(afterTenantsOk.Value))
        {
            doc.RootElement.GetProperty("items")[0].GetProperty("usedWarehouses").GetInt32().Should().Be(2);
        }

        var afterQuotasOk = (await controller.GetQuotas()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(afterQuotasOk.Value))
        {
            var q0 = doc.RootElement.GetProperty("items")[0];
            q0.GetProperty("usedWarehouses").GetInt32().Should().Be(2);
            q0.GetProperty("warehouseUtilizationPct").GetDecimal().Should().Be(66.7m);
        }

        using (var subDoc = JsonSerializer.SerializeToDocument(await saasBillingService.GetSubscriptionAsync(null, null, default)))
        {
            subDoc.RootElement.GetProperty("currentWarehouseCount").GetInt32().Should().Be(2);
            subDoc.RootElement.GetProperty("activeWarehouses").GetInt32().Should().Be(2);
            subDoc.RootElement.GetProperty("usedWarehouses").GetInt32().Should().Be(2);
        }

        using (var opDoc = JsonSerializer.SerializeToDocument((await reportOps.GetOperationalDashboardAsync(null, default)).Payload))
        {
            opDoc.RootElement.GetProperty("warehouseCount").GetInt32().Should().Be(2);
        }
    }

    [Fact]
    public async Task TC_W12_01_InvoiceVatMath_SingleOwner_AutoRevokeImpersonationOnSuspend_And_GrossProfitIdentity()
    {
        var dbName = $"{nameof(TC_W12_01_InvoiceVatMath_SingleOwner_AutoRevokeImpersonationOnSuspend_And_GrossProfitIdentity)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Warehouses.Add(new Warehouse { Id = 1, TenantId = 1, Code = "WH01", Name = "Kho Trung Tâm Quận 7", Address = "TP.HCM", IsActive = true });
        db.SaasBillingInvoices.AddRange(
            new SaasBillingInvoice
            {
                Id = 174,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202610-001",
                PlanCode = "GROWTH",
                SubTotalVnd = 0m,
                VatRatePercent = 0m,
                VatAmountVnd = 0m,
                AmountVnd = 1490000m,
                PaymentProvider = "VIETQR",
                Status = "PAID",
                CreatedAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PaidAtUtc = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc)
            },
            new SaasBillingInvoice
            {
                Id = 175,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202611-002",
                PlanCode = "GROWTH",
                SubTotalVnd = 0m,
                VatRatePercent = 0m,
                VatAmountVnd = 0m,
                AmountVnd = 1490000m,
                PaymentProvider = "VIETQR",
                Status = "UNPAID",
                CreatedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        var prod1 = new Product { Id = 101, TenantId = 1, Code = "SKU-W12-01", Name = "Bàn phím Cơ Không Dây", MovingAverageCost = 500000m, IsActive = true };
        var prod2 = new Product { Id = 102, TenantId = 1, Code = "SKU-W12-02", Name = "Tai nghe Chống Ồn", MovingAverageCost = 335000m, IsActive = true };
        db.Products.AddRange(prod1, prod2);

        var expReceipt = new ExportReceipt
        {
            Id = 501,
            TenantId = 1,
            Code = "EXP-W12-001",
            WarehouseId = 1,
            Status = ReceiptStatus.Dispatched,
            CarrierCode = "SHOPEE",
            TrackingNumber = "SPXVN061234567",
            CreatedBy = 1,
            Details =
            [
                new ExportReceiptDetail { Id = 1, ExportReceiptId = 501, ProductId = 101, Product = prod1, Quantity = 10m, UnitPrice = 3000000m },
                new ExportReceiptDetail { Id = 2, ExportReceiptId = 501, ProductId = 102, Product = prod2, Quantity = 10m, UnitPrice = 2400000m }
            ]
        };
        db.ExportReceipts.Add(expReceipt);
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(
                1,
                AppRoles.SuperAdmin,
                AppPermissions.PlatformDashboardRead,
                AppPermissions.PlatformTenantRead,
                AppPermissions.PlatformTenantSuspend,
                AppPermissions.PlatformTenantWrite,
                AppPermissions.PlatformBillingManage,
                AppPermissions.PlatformBillingWrite,
                AppPermissions.PlatformBillingReconcile,
                AppPermissions.PlatformSupportImpersonate,
                AppPermissions.PlatformIntegrationManage));
        var saasBillingService = new SaasBillingService(db);

        // 1. Invoice VAT Math (1,354,545 + 135,455 == 1,490,000) & Canonical VietQR Beneficiary
        var platInvoicesOk = (await controller.GetBillingInvoices()).Should().BeOfType<OkObjectResult>().Subject;
        var platInvoicesJson = JsonSerializer.Serialize(platInvoicesOk.Value);
        platInvoicesJson.Should().NotContain("NGUYEN THUY TRANG");
        platInvoicesJson.Should().Contain("HUYNH PHI VAN");
        platInvoicesJson.Should().Contain("98418102005");
        using (var doc = JsonSerializer.SerializeToDocument(platInvoicesOk.Value))
        {
            var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
            items.Should().HaveCount(2);
            foreach (var inv in items)
            {
                var sub = inv.GetProperty("subTotalVnd").GetDecimal();
                var vat = inv.GetProperty("vatAmountVnd").GetDecimal();
                var tot = inv.GetProperty("totalAmountVnd").GetDecimal();
                sub.Should().Be(1354545m);
                vat.Should().Be(135455m);
                tot.Should().Be(1490000m);
                (sub + vat).Should().Be(tot);
                inv.GetProperty("billingPeriodLabel").GetString().Should().NotBeNullOrWhiteSpace();
                inv.GetProperty("dueDateLabel").GetString().Should().NotBeNullOrWhiteSpace();
            }
        }

        var saasInvoicesPayload = await saasBillingService.GetInvoicesAsync(default);
        var saasInvoicesJson = JsonSerializer.Serialize(saasInvoicesPayload);
        saasInvoicesJson.Should().NotContain("NGUYEN THUY TRANG");
        saasInvoicesJson.Should().Contain("HUYNH PHI VAN");
        saasInvoicesJson.Should().Contain("98418102005");
        using (var doc = JsonSerializer.SerializeToDocument(saasInvoicesPayload))
        {
            var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
            items.Should().HaveCount(2);
            foreach (var inv in items)
            {
                inv.GetProperty("subTotalVnd").GetDecimal().Should().Be(1354545m);
                inv.GetProperty("vatAmountVnd").GetDecimal().Should().Be(135455m);
                inv.GetProperty("totalAmountVnd").GetDecimal().Should().Be(1490000m);
            }
        }

        // 1b. Reconcile endpoint marks invoice PAID and supports resetToPending
        var recOk = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Đã kiểm tra sao kê TPBank khớp mã giao dịch FT2630698418102"),
            evidenceFile: CreateSamplePdfEvidenceFile())).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(recOk.Value))
        {
            doc.RootElement.GetProperty("status").GetString().Should().Be("PAID");
            doc.RootElement.GetProperty("statusLabelVi").GetString().Should().Be("Đã tất toán VietQR");
            doc.RootElement.GetProperty("subTotalVnd").GetDecimal().Should().Be(1354545m);
            doc.RootElement.GetProperty("vatAmountVnd").GetDecimal().Should().Be(135455m);
            doc.RootElement.GetProperty("totalAmountVnd").GetDecimal().Should().Be(1490000m);
        }
        var resetOk = (await controller.ReconcileBillingInvoice(175, resetToPending: true)).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(resetOk.Value))
        {
            doc.RootElement.GetProperty("status").GetString().Should().Be("UNPAID");
        }

        // 1c. Per-Channel Marketplace Webhook Status & Configurable Tax Label
        var healthOk = (await controller.GetIntegrationsHealth()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(healthOk.Value))
        {
            var mkt = doc.RootElement.GetProperty("shopeeTiktokWebhookStatus");
            mkt.GetProperty("taxPolicyLabelVi").GetString().Should().Be("Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)");
            mkt.GetProperty("channels").GetArrayLength().Should().Be(2);
        }

        // 2. Auto-Revoke Active Support Impersonation Sessions when Tenant is Suspended
        await controller.StartImpersonationSession(new PlatformControlCenterController.StartPlatformImpersonationRequest(
            TargetTenantId: 1,
            TicketCode: "SUP-2026-999",
            Reason: "Kiểm tra tự động thu hồi phiên hỗ trợ khi tạm khóa doanh nghiệp",
            TtlMinutes: 30));

        var beforeSuspendImpOk = (await controller.GetActiveImpersonationSessions()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(beforeSuspendImpOk.Value))
        {
            doc.RootElement.GetProperty("activeCount").GetInt32().Should().Be(1);
        }

        var suspendOk = (await controller.UpdateTenantStatus(
            1,
            new PlatformControlCenterController.UpdateTenantLifecycleRequest(
                Status: "SUSPENDED",
                SuspensionReason: "Tạm khóa doanh nghiệp để kiểm định tự động thu hồi phiên Impersonation",
                ReasonCategory: "SECURITY_REVIEW"))).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(suspendOk.Value))
        {
            doc.RootElement.GetProperty("revokedImpersonationSessionsCount").GetInt32().Should().Be(1);
        }

        var afterSuspendImpOk = (await controller.GetActiveImpersonationSessions()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(afterSuspendImpOk.Value))
        {
            doc.RootElement.GetProperty("activeCount").GetInt32().Should().Be(0);
        }

        await controller.UpdateTenantStatus(
            1,
            new PlatformControlCenterController.UpdateTenantLifecycleRequest(
                Status: "ACTIVE",
                Reason: "Khôi phục trạng thái ACTIVE sau kiểm thử"));

        // 3. OwnerGrossProfit Exact Arithmetic Identity: TotalRevenueVnd - TotalCogsVnd == TotalGrossProfitVnd
        var reportOps = new ReportOperationsService(db);
        var gpOutcome = await reportOps.GetOwnerGrossProfitAsync(1, canReadCost: true);
        gpOutcome.StatusCode.Should().Be(200);
        using (var doc = JsonSerializer.SerializeToDocument(gpOutcome.Payload))
        {
            var root = doc.RootElement;
            var totalRev = root.GetProperty("totalRevenueVnd").GetDecimal();
            var totalCogs = root.GetProperty("totalCogsVnd").GetDecimal();
            var totalGp = root.GetProperty("totalGrossProfitVnd").GetDecimal();
            var feeDeduction = root.GetProperty("totalPlatformAndShippingFeeVnd").GetDecimal();
            var taxDeduction = root.GetProperty("totalWithholdingTaxAndFixedFeeVnd").GetDecimal();
            var netOp = root.GetProperty("totalNetOperatingProfitVnd").GetDecimal();

            totalRev.Should().Be(54000000m);
            totalCogs.Should().Be(8350000m);
            totalGp.Should().Be(45650000m);
            (totalRev - totalCogs - totalGp).Should().Be(0m);
            (totalGp - feeDeduction - taxDeduction - netOp).Should().Be(0m);
            root.GetProperty("taxPolicyReference").GetString().Should().Be("Khấu trừ thuế Sàn TMĐT 1,5% (1% GTGT + 0,5% TNCN — Cấu hình chính sách thuế TMĐT)");

            foreach (var row in root.GetProperty("skuBreakdown").EnumerateArray())
            {
                var rRev = row.GetProperty("revenueVnd").GetDecimal();
                var rCogs = row.GetProperty("cogsVnd").GetDecimal();
                var rGp = row.GetProperty("grossProfitVnd").GetDecimal();
                (rRev - rCogs - rGp).Should().Be(0m);
            }
        }
    }

    [Fact]
    public async Task TC_W13_01_ManualReconcileSafetyNet_RoleGuard_AmountMismatch422_And_BillingScheduleParity()
    {
        var dbName = $"{nameof(TC_W13_01_ManualReconcileSafetyNet_RoleGuard_AmountMismatch422_And_BillingScheduleParity)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Warehouses.Add(new Warehouse { Id = 1, TenantId = 1, Code = "WH01", Name = "Kho Trung Tâm Quận 7", Address = "TP.HCM", IsActive = true });
        db.SaasBillingInvoices.AddRange(
            new SaasBillingInvoice
            {
                Id = 174,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202610-001",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10.00m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                PaymentProvider = "VIETQR",
                Status = "PAID",
                CreatedAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PaidAtUtc = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)
            },
            new SaasBillingInvoice
            {
                Id = 175,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202611-002",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10.00m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                PaymentProvider = "VIETQR",
                Status = "UNPAID",
                CreatedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(
                1,
                AppRoles.SuperAdmin,
                AppPermissions.PlatformDashboardRead,
                AppPermissions.PlatformTenantRead,
                AppPermissions.PlatformBillingManage,
                AppPermissions.PlatformBillingWrite,
                AppPermissions.PlatformBillingReconcile));

        // 1. Role Guard: SupportAdmin / SecurityAuditor must be blocked with 403 BILLING_RECONCILE_FORBIDDEN
        controller.ControllerContext.HttpContext.Request.Headers["X-Platform-Role-Preview"] = "SupportAdmin";
        var forbidSupport = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Đối soát sao kê TPBank kỳ 11/2026"))).Should().BeOfType<ObjectResult>().Subject;
        forbidSupport.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        using (var doc = JsonSerializer.SerializeToDocument(forbidSupport.Value))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("BILLING_RECONCILE_FORBIDDEN");
        }

        controller.ControllerContext.HttpContext.Request.Headers["X-Platform-Role-Preview"] = "SecurityAuditor";
        var forbidAuditor = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Đối soát sao kê TPBank kỳ 11/2026"))).Should().BeOfType<ObjectResult>().Subject;
        forbidAuditor.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // Restore BillingAdmin role
        controller.ControllerContext.HttpContext.Request.Headers["X-Platform-Role-Preview"] = "BillingAdmin";

        // 2. Missing BankTransactionRef -> 422 RECONCILE_BANK_REF_REQUIRED
        var missingBankRef = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "",
                ReceivedAmountVnd: 1490000m,
                ReasonNote: "Đối soát sao kê TPBank kỳ 11/2026"))).Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(missingBankRef.Value))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("RECONCILE_BANK_REF_REQUIRED");
        }

        // 3. Mismatched ReceivedAmountVnd -> 422 RECONCILE_AMOUNT_MISMATCH
        var mismatchAmt = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                ReceivedAmountVnd: 1000000m,
                ReasonNote: "Đối soát sao kê TPBank kỳ 11/2026"))).Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(mismatchAmt.Value))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("RECONCILE_AMOUNT_MISMATCH");
        }

        // 4. Valid manual reconciliation -> 200 OK + AuditLog PLATFORM_BILLING_MANUAL_RECONCILE + MANUAL_RECONCILIATION summary
        var validRec = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Đã kiểm tra sao kê TPBank khớp mã giao dịch FT2630698418102"),
            evidenceFile: CreateSamplePdfEvidenceFile())).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(validRec.Value))
        {
            doc.RootElement.GetProperty("status").GetString().Should().Be("PAID");
            doc.RootElement.GetProperty("settlementSource").GetString().Should().Be("MANUAL_RECONCILIATION");
            doc.RootElement.GetProperty("settlementSummaryVi").GetString().Should().Be("Xác nhận thủ công bởi platform_superadmin • Duyệt bởi: Nguyễn Thị Mai (Kế toán trưởng) (Mã giao dịch: FT2630698418102)");
        }

        var manualAudit = await db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == "PLATFORM_BILLING_MANUAL_RECONCILE")
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();
        manualAudit.Should().NotBeNull();
        manualAudit!.NewValues.Should().Contain("FT2630698418102");

        // Reset back to UNPAID so both portals show UNSETTLED / Chờ thanh toán / Đối soát for INV-SAAS-202611-002
        await controller.ReconcileBillingInvoice(175, resetToPending: true);

        // 5. Cross-portal Billing Schedule & VAT Breakdown Parity between /platform/billing and /saas-billing
        var platformInvoicesOk = (await controller.GetBillingInvoices()).Should().BeOfType<OkObjectResult>().Subject;
        var saasBillingSvc = new SaasBillingService(db);
        var customerInvoicesObj = await saasBillingSvc.GetInvoicesAsync();

        using var platDoc = JsonSerializer.SerializeToDocument(platformInvoicesOk.Value);
        using var custDoc = JsonSerializer.SerializeToDocument(customerInvoicesObj);

        var platItems = platDoc.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var custItems = custDoc.RootElement.GetProperty("items").EnumerateArray().ToArray();

        platItems.Should().HaveCount(2);
        custItems.Should().HaveCount(2);

        var platNov = platItems.Single(x => x.GetProperty("invoiceCode").GetString() == "INV-SAAS-202611-002");
        var custNov = custItems.Single(x => x.GetProperty("invoiceCode").GetString() == "INV-SAAS-202611-002");

        platNov.GetProperty("dueDateDisplayVi").GetString().Should().Be("05/11/2026");
        custNov.GetProperty("dueDateDisplayVi").GetString().Should().Be("05/11/2026");
        platNov.GetProperty("subTotalVnd").GetDecimal().Should().Be(1354545m);
        custNov.GetProperty("subTotalVnd").GetDecimal().Should().Be(1354545m);
        platNov.GetProperty("vatAmountVnd").GetDecimal().Should().Be(135455m);
        custNov.GetProperty("vatAmountVnd").GetDecimal().Should().Be(135455m);
        platNov.GetProperty("totalAmountVnd").GetDecimal().Should().Be(1490000m);
        custNov.GetProperty("totalAmountVnd").GetDecimal().Should().Be(1490000m);
        platNov.GetProperty("settlementSource").GetString().Should().Be("UNSETTLED");

        var platOct = platItems.Single(x => x.GetProperty("invoiceCode").GetString() == "INV-SAAS-202610-001");
        platOct.GetProperty("settlementSource").GetString().Should().Be("AUTO_VIETQR_WEBHOOK");
        platOct.GetProperty("settlementSummaryVi").GetString().Should().Be("Tự động qua VietQR (Mã giao dịch: FT2627690123)");
    }

    [Fact]
    public async Task TC_W14_01_ManualReconcileDuplicateRef409_PartialAndOverpayment_InvoiceChronology_And_NetMrr()
    {
        var dbName = $"{nameof(TC_W14_01_ManualReconcileDuplicateRef409_PartialAndOverpayment_InvoiceChronology_And_NetMrr)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Warehouses.Add(new Warehouse { Id = 1, TenantId = 1, Code = "WH01", Name = "Kho Trung Tâm Quận 7", Address = "TP.HCM", IsActive = true });
        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = 1,
            TenantId = 1,
            PlanCode = "GROWTH",
            Status = "ACTIVE",
            MonthlyOrderQuota = 10000,
            CurrentMonthOrderCount = 6420,
            MaxWarehouses = 3,
            MaxUsers = 20,
            CurrentPeriodStartUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc),
            UpdatedAtUtc = DateTime.UtcNow
        });
        db.SaasBillingInvoices.AddRange(
            new SaasBillingInvoice
            {
                Id = 1,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202610-001",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10.00m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                PaymentProvider = "VIETQR",
                Status = "PAID",
                CreatedAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PaidAtUtc = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc) // Will be healed to 2026-10-03
            },
            new SaasBillingInvoice
            {
                Id = 2,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202611-002",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10.00m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                PaymentProvider = "VIETQR",
                Status = "UNPAID",
                CreatedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(
                1,
                AppRoles.SuperAdmin,
                AppPermissions.PlatformDashboardRead,
                AppPermissions.PlatformTenantRead,
                AppPermissions.PlatformBillingManage,
                AppPermissions.PlatformBillingWrite,
                AppPermissions.PlatformBillingReconcile));

        // 1. GET /api/v1/platform/dashboard returns Net MRR (1,354,545), Net ARR (16,254,540), and Gross MRR (1,490,000)
        var dashOk = (await controller.GetDashboard()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(dashOk.Value))
        {
            var root = doc.RootElement;
            root.GetProperty("mrrNetBeforeVatVnd").GetDecimal().Should().Be(1354545m);
            root.GetProperty("arrNetBeforeVatVnd").GetDecimal().Should().Be(16254540m);
            root.GetProperty("mrrGrossWithVatVnd").GetDecimal().Should().Be(1490000m);
            root.GetProperty("mrrVnd").GetDecimal().Should().Be(1354545m);
            root.GetProperty("arrVnd").GetDecimal().Should().Be(16254540m);
        }

        // 2. GET /api/v1/platform/billing/invoices verifies chronological dates (Issued 1st, Paid 3rd <= Due 5th)
        var invOk = (await controller.GetBillingInvoices()).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(invOk.Value))
        {
            var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var oct = items.Single(x => x.GetProperty("invoiceCode").GetString() == "INV-SAAS-202610-001");
            oct.GetProperty("issuedDateDisplayVi").GetString().Should().Be("01/10/2026");
            oct.GetProperty("paidDateDisplayVi").GetString().Should().Be("03/10/2026 16:15");
            oct.GetProperty("bankCreditTimestampVi").GetString().Should().Be("03/10/2026 16:00");
            oct.GetProperty("dueDateDisplayVi").GetString().Should().Be("05/10/2026");

            var nov = items.Single(x => x.GetProperty("invoiceCode").GetString() == "INV-SAAS-202611-002");
            nov.GetProperty("issuedDateDisplayVi").GetString().Should().Be("01/11/2026");
            nov.GetProperty("dueDateDisplayVi").GetString().Should().Be("05/11/2026");
        }

        // 3. Duplicate BankTransactionRef ("FT2627690123" from October invoice) -> 409 Conflict BILLING_DUPLICATE_BANK_TRANSFER_REF
        var dupConflict = (await controller.ReconcileBillingInvoice(
            2,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2627690123",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Đối soát thử mã giao dịch trùng kỳ trước"))).Should().BeOfType<ConflictObjectResult>().Subject;
        dupConflict.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        using (var doc = JsonSerializer.SerializeToDocument(dupConflict.Value))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("BILLING_DUPLICATE_BANK_TRANSFER_REF");
        }

        // 4. Partial payment (allowPartialOrOverpayment = true, receivedAmountVnd = 1354545m) -> status = "PARTIALLY_PAID", remainingAmountVnd = 135455m
        var partialOk = (await controller.ReconcileBillingInvoice(
            2,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418101",
                BankCreditTimestampVi: "02/11/2026 18:10",
                ReceivedAmountVnd: 1354545m,
                AttachmentReference: "UNC_VCB_20261102_1354k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                AllowPartialOrOverpayment: true,
                ReasonNote: "Khách hàng chuyển thiếu phần thuế GTGT 10%"),
            evidenceFile: CreateSamplePdfEvidenceFile("UNC_VCB_20261102_1354k.pdf"))).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(partialOk.Value))
        {
            doc.RootElement.GetProperty("status").GetString().Should().Be("PARTIALLY_PAID");
            doc.RootElement.GetProperty("remainingAmountVnd").GetDecimal().Should().Be(135455m);
            doc.RootElement.GetProperty("varianceStatus").GetString().Should().Be("PARTIAL_PAYMENT");
        }

        // 5. Full payment with binary PDF evidence & independent approver -> status = "PAID" and GET /api/v1/saas-billing/subscription returns currentCyclePaid == true
        var fullOk = (await controller.ReconcileBillingInvoice(
            2,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReconciledTimestampVi: "02/11/2026 18:30",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Đã kiểm tra sao kê TPBank khớp đủ 1.490.000 ₫"),
            evidenceFile: CreateSamplePdfEvidenceFile("UNC_VCB_20261102_1490k.pdf"))).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(fullOk.Value))
        {
            doc.RootElement.GetProperty("status").GetString().Should().Be("PAID");
            doc.RootElement.GetProperty("attachmentReference").GetString().Should().Be("UNC_VCB_20261102_1490k.pdf");
            doc.RootElement.GetProperty("approverCode").GetString().Should().Be("Nguyễn Thị Mai (Kế toán trưởng)");
            doc.RootElement.GetProperty("currentCyclePaid").GetBoolean().Should().BeTrue();
        }

        var saasBillingSvc = new SaasBillingService(db);
        var subPayload = await saasBillingSvc.GetSubscriptionAsync();
        using (var subDoc = JsonSerializer.SerializeToDocument(subPayload))
        {
            subDoc.RootElement.GetProperty("currentCyclePaid").GetBoolean().Should().BeTrue();
            subDoc.RootElement.GetProperty("currentCycleSettlementBannerVi").GetString()
                .Should().Be("Hóa đơn kỳ 11/2026 (INV-SAAS-202611-002) đã tất toán đầy đủ (1.490.000 đ) • Xác nhận lúc: 02/11/2026 18:30");
        }

        // 6. Reset invoice #2 back to UNPAID at the end of the test
        var resetOk = (await controller.ReconcileBillingInvoice(2, resetToPending: true)).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(resetOk.Value))
        {
            doc.RootElement.GetProperty("status").GetString().Should().Be("UNPAID");
        }
    }

    [Fact]
    public async Task TC_W15_01_FourEyesSoD_MultipartReconcile_CustomerPrivacyZeroLeak_QuotaAddendum_SalesOrderCode_And_KitCostSnapshot()
    {
        var dbName = $"{nameof(TC_W15_01_FourEyesSoD_MultipartReconcile_CustomerPrivacyZeroLeak_QuotaAddendum_SalesOrderCode_And_KitCostSnapshot)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        var unitBo = new Unit { Id = 1, TenantId = 1, Code = "Bộ", Name = "Bộ", DecimalPlaces = 0, IsActive = true };
        var unitCai = new Unit { Id = 2, TenantId = 1, Code = "Cái", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        db.Units.AddRange(unitBo, unitCai);

        var wh1 = new Warehouse { Id = 1, TenantId = 1, Code = "WH01", Name = "Kho Trung tâm (WH01)", Address = "Quận 7, TP.HCM", IsActive = true };
        var wh2 = new Warehouse { Id = 2, TenantId = 1, Code = "WH02", Name = "Kho Chi Nhánh Hà Nội", Address = "Hà Nội", IsActive = false };
        db.Warehouses.AddRange(wh1, wh2);

        var customer = new BusinessPartner { Id = 10, TenantId = 1, Code = "CUST-W15", Name = "Khách hàng B2B Đại Phát", IsCustomer = true, IsActive = true };
        db.BusinessPartners.Add(customer);

        var kitProduct = new Product { Id = 1, TenantId = 1, Code = "KIT-GIFT-01", Name = "Bộ Quà Doanh Nghiệp Cao Cấp", UnitId = 1, MovingAverageCost = 500000m, IsActive = true };
        var compProduct1 = new Product { Id = 2, TenantId = 1, Code = "COMP-MOUSE-01", Name = "Chuột Không Dây Cao Cấp", UnitId = 2, MovingAverageCost = 420000m, IsActive = true };
        var compProduct2 = new Product { Id = 3, TenantId = 1, Code = "COMP-BOX-01", Name = "Hộp Quà Nam Châm", UnitId = 2, MovingAverageCost = 80000m, IsActive = true };
        db.Products.AddRange(kitProduct, compProduct1, compProduct2);

        var bin = new WarehouseLocation { Id = 10, TenantId = 1, WarehouseId = 1, Code = "BIN-A01-01", Name = "Kệ lưu trữ tiêu chuẩn A01-01", LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage, IsActive = true };
        db.WarehouseLocations.Add(bin);

        var stock1 = new InventoryStock { Id = 1001, TenantId = 1, WarehouseId = 1, ProductId = 2, LocationId = 10, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 50m, MovingAverageCost = 420000m };
        stock1.RefreshGrainHash();
        var stock2 = new InventoryStock { Id = 1002, TenantId = 1, WarehouseId = 1, ProductId = 3, LocationId = 10, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 50m, MovingAverageCost = 80000m };
        stock2.RefreshGrainHash();
        db.InventoryStocks.AddRange(stock1, stock2);

        var kwoCompleted = new KitWorkOrder
        {
            Id = 501,
            TenantId = 1,
            WarehouseId = 1,
            WorkOrderCode = "KWO-2026-001",
            KitProductId = 1,
            WorkOrderType = KitWorkOrderType.Assemble,
            Status = "Completed",
            PlannedKitQty = 20m,
            CompletedKitQty = 20m,
            BaseUomCode = "Bộ",
            ConversionRateSnapshot = 1m,
            TargetLocationId = 10,
            RolledUpUnitCost = 500000m,
            EarliestComponentExpiryDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            CreatedAtUtc = new DateTime(2026, 10, 28, 8, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 10, 28, 10, 30, 0, DateTimeKind.Utc),
            ComponentLines =
            [
                new KitWorkOrderComponentLine { Id = 5001, TenantId = 1, ComponentProductId = 2, ComponentRatioQty = 1m, RequiredBaseQty = 20m, ConsumedBaseQty = 20m, BaseUomCode = "Cái", ComponentUnitCost = 420000m },
                new KitWorkOrderComponentLine { Id = 5002, TenantId = 1, ComponentProductId = 3, ComponentRatioQty = 1m, RequiredBaseQty = 20m, ConsumedBaseQty = 20m, BaseUomCode = "Vỏ hộp (Cái)", ComponentUnitCost = 80000m }
            ]
        };
        db.KitWorkOrders.Add(kwoCompleted);

        db.TenantSubscriptions.Add(new TenantSubscription
        {
            Id = 1,
            TenantId = 1,
            PlanCode = "GROWTH",
            Status = "ACTIVE",
            MonthlyOrderQuota = 10000,
            CurrentMonthOrderCount = 6420,
            MaxWarehouses = 3,
            MaxUsers = 20,
            CurrentPeriodStartUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrentPeriodEndUtc = new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc),
            UpdatedAtUtc = DateTime.UtcNow
        });

        db.SaasBillingInvoices.AddRange(
            new SaasBillingInvoice
            {
                Id = 174,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202610-001",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10.00m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                PaymentProvider = "VIETQR",
                Status = "PAID",
                CreatedAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PaidAtUtc = new DateTime(2026, 10, 3, 9, 15, 0, DateTimeKind.Utc)
            },
            new SaasBillingInvoice
            {
                Id = 175,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202611-002",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10.00m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                PaymentProvider = "VIETQR",
                Status = "UNPAID",
                CreatedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
            });

        await db.SaveChangesAsync();

        var controller = CreatePlatformController(
            db,
            CreatePlatformControllerContext(
                1,
                AppRoles.SuperAdmin,
                AppPermissions.PlatformDashboardRead,
                AppPermissions.PlatformTenantRead,
                AppPermissions.PlatformQuotaOverride,
                AppPermissions.PlatformBillingManage,
                AppPermissions.PlatformBillingReconcile));

        // (a) 403 BILLING_RECONCILE_FORBIDDEN for Viewer / SupportReadOnly / SupportAdmin / SecurityAuditor
        foreach (var restrictedRole in new[] { "Viewer", "SupportReadOnly", "SupportAdmin", "SecurityAuditor" })
        {
            controller.ControllerContext.HttpContext.Request.Headers["X-Platform-Role-Preview"] = restrictedRole;
            var forbidRes = (await controller.ReconcileBillingInvoice(
                175,
                new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                    BankTransactionRef: "FT2630698418102",
                    BankCreditTimestampVi: "02/11/2026 18:15",
                    ReceivedAmountVnd: 1490000m,
                    AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                    ApproverUsername: "ktt_nguyen_mai",
                    ReasonNote: "Kiểm tra phân quyền đối soát thanh toán"))).Should().BeOfType<ObjectResult>().Subject;
            forbidRes.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            using var fDoc = JsonSerializer.SerializeToDocument(forbidRes.Value);
            fDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("BILLING_RECONCILE_FORBIDDEN");
        }
        controller.ControllerContext.HttpContext.Request.Headers.Remove("X-Platform-Role-Preview");

        // Also verify PlatformAuthorizationFilter returns 403 BILLING_RECONCILE_FORBIDDEN when SupportReadOnly calls endpoint filter
        var filterCtx = CreatePlatformControllerContext(99, "SupportReadOnly", AppPermissions.PlatformDashboardRead);
        var authFilterCtx = new Microsoft.AspNetCore.Mvc.Filters.AuthorizationFilterContext(
            new ActionContext(filterCtx.HttpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            []);
        var reconcileFilter = new PlatformAuthorizationFilter(db, AppPermissions.PlatformBillingReconcile);
        await reconcileFilter.OnAuthorizationAsync(authFilterCtx);
        var filterObjResult = authFilterCtx.Result.Should().BeOfType<ObjectResult>().Subject;
        filterObjResult.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        using (var filterDoc = JsonSerializer.SerializeToDocument(filterObjResult.Value))
        {
            filterDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("BILLING_RECONCILE_FORBIDDEN");
        }

        // (b) 422 BILLING_SOD_SELF_APPROVAL_FORBIDDEN when BillingAdmin tries to self-approve or omits approver
        controller.ControllerContext.HttpContext.Request.Headers["X-Platform-Role-Preview"] = "BillingAdmin";
        var sodSelfRes = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "platform_superadmin",
                ReasonNote: "Tự duyệt đối soát trái nguyên tắc 4 mắt"))).Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var sodDoc = JsonSerializer.SerializeToDocument(sodSelfRes.Value))
        {
            sodDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("BILLING_SOD_SELF_APPROVAL_FORBIDDEN");
        }
        controller.ControllerContext.HttpContext.Request.Headers.Remove("X-Platform-Role-Preview");

        // (c) 409 BILLING_DUPLICATE_BANK_TRANSFER_REF when reusing FT26276110988 or FT2627690123
        var dupBankRes = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT26276110988",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Kiểm tra chặn trùng mã giao dịch ngân hàng"))).Should().BeOfType<ConflictObjectResult>().Subject;
        using (var dupDoc = JsonSerializer.SerializeToDocument(dupBankRes.Value))
        {
            dupDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("BILLING_DUPLICATE_BANK_TRANSFER_REF");
        }

        // (d) 422 RECONCILE_INVALID_CREDIT_TIMESTAMP for future timestamp & 422 RECONCILE_ATTACHMENT_REQUIRED when missing file
        var futureTs = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).AddHours(3).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var futureRes = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                BankCreditTimestampVi: futureTs,
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "UNC_VCB_20261102_1490k.pdf",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Kiểm tra chặn giờ ghi có vượt quá hiện tại"))).Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var futDoc = JsonSerializer.SerializeToDocument(futureRes.Value))
        {
            futDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("RECONCILE_INVALID_CREDIT_TIMESTAMP");
        }

        var missingFileRes = (await controller.ReconcileBillingInvoice(
            175,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(
                BankTransactionRef: "FT2630698418102",
                BankCreditTimestampVi: "02/11/2026 18:15",
                ReceivedAmountVnd: 1490000m,
                AttachmentReference: "",
                ApproverUsername: "ktt_nguyen_mai",
                ReasonNote: "Kiểm tra bắt buộc tệp chứng từ thanh toán"))).Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var missFileDoc = JsonSerializer.SerializeToDocument(missingFileRes.Value))
        {
            missFileDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("RECONCILE_ATTACHMENT_REQUIRED");
        }

        // Multipart FormData binary upload (%PDF-1.4) via ReconcileBillingInvoiceHttp
        var pdfBytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF");
        await using var pdfStream = new MemoryStream(pdfBytes);
        var formFile = new FormFile(pdfStream, 0, pdfBytes.Length, "attachmentFile", "UNC_VCB_20261102_1490k.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
        var formFiles = new FormFileCollection { formFile };
        var formFields = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["bankTransactionRef"] = "FT2630698418102",
            ["bankCreditTimestampVi"] = "02/11/2026 18:15",
            ["reconciledTimestampVi"] = "02/11/2026 18:30",
            ["receivedAmountVnd"] = "1490000",
            ["approverUsername"] = "ktt_nguyen_mai",
            ["reasonNote"] = "Đối soát sao kê TPBank khớp mã giao dịch FT2630698418102"
        };
        controller.ControllerContext.HttpContext.Request.ContentType = "multipart/form-data; boundary=----Wave15Boundary";
        controller.ControllerContext.HttpContext.Request.Form = new FormCollection(formFields, formFiles);

        var unescapedJsonOpts = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var multipartOk = (await controller.ReconcileBillingInvoiceHttp(175)).Should().BeOfType<OkObjectResult>().Subject;
        var multipartJson = JsonSerializer.Serialize(multipartOk.Value, unescapedJsonOpts);
        multipartJson.Should().NotContain("Mã UNC");
        multipartJson.Should().NotContain("UNC:");
        using (var mpDoc = JsonSerializer.SerializeToDocument(multipartOk.Value))
        {
            var root = mpDoc.RootElement;
            root.GetProperty("status").GetString().Should().Be("PAID");
            root.GetProperty("attachmentReference").GetString().Should().Be("UNC_VCB_20261102_1490k.pdf");
            root.GetProperty("attachmentSha256").GetString().Should().NotBeNullOrWhiteSpace();
            root.GetProperty("bankCreditTimestampVi").GetString().Should().Be("02/11/2026 18:15");
            root.GetProperty("reconciledTimestampVi").GetString().Should().Be("02/11/2026 18:30");
            root.GetProperty("settlementSummaryVi").GetString().Should().Be("Xác nhận thủ công bởi platform_superadmin • Duyệt bởi: Nguyễn Thị Mai (Kế toán trưởng) (Mã giao dịch: FT2630698418102)");
        }
        controller.ControllerContext.HttpContext.Request.ContentType = "application/json";

        // (e) Customer Privacy: 0 internal actor & 0 "Xác nhận thủ công bởi" & 0 "UNC" in customer billing APIs!
        var saasBillingSvc = new SaasBillingService(db);
        var custInvoicesJson = JsonSerializer.Serialize(await saasBillingSvc.GetInvoicesAsync(), unescapedJsonOpts);
        var custSubJson = JsonSerializer.Serialize(await saasBillingSvc.GetSubscriptionAsync(), unescapedJsonOpts);
        custInvoicesJson.Should().NotContain("qa_admin");
        custInvoicesJson.Should().NotContain("platform_superadmin");
        custInvoicesJson.Should().NotContain("Xác nhận thủ công bởi");
        custInvoicesJson.Should().NotContain("UNC");
        custSubJson.Should().NotContain("qa_admin");
        custSubJson.Should().NotContain("platform_superadmin");
        custSubJson.Should().NotContain("Xác nhận thủ công bởi");
        custSubJson.Should().NotContain("UNC");
        custInvoicesJson.Should().Contain("Đã xác nhận thanh toán (Mã giao dịch: FT2630698418102)");
        custSubJson.Should().Contain("Hóa đơn kỳ 11/2026 (INV-SAAS-202611-002) đã tất toán đầy đủ (1.490.000 đ) • Xác nhận lúc: 02/11/2026 18:30");

        // Reset back to UNPAID and check dynamic dashboard due label
        await controller.ReconcileBillingInvoice(175, resetToPending: true);
        var dashRes = (await controller.GetDashboard()).Should().BeOfType<OkObjectResult>().Subject;
        using (var dDoc = JsonSerializer.SerializeToDocument(dashRes.Value))
        {
            var dueLabel = dDoc.RootElement.GetProperty("unpaidInvoiceDueLabelVi").GetString();
            dueLabel.Should().StartWith("Hạn thanh toán: 05/11/2026 (");
            dueLabel.Should().NotBe("Hạn xử lý: Hôm nay");
        }

        // (f) Quota exceeding plan ceiling requires ContractReference & EffectiveUntilDate + Immutable OrderCode + Immutable KitWorkOrder Cost
        var missingAddendum = (await controller.OverrideTenantQuota(
            1,
            new PlatformControlCenterController.OverrideTenantQuotaRequest(
                MaxWarehouses: 4,
                MaxUsers: 25,
                MonthlyOrderQuota: 12000,
                Reason: "Nâng hạn mức kho nhưng thiếu mã phụ lục"))).Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var qaDoc = JsonSerializer.SerializeToDocument(missingAddendum.Value))
        {
            qaDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("QUOTA_CONTRACT_ADDENDUM_REQUIRED");
        }

        var missingEffectiveUntil = (await controller.OverrideTenantQuota(
            1,
            new PlatformControlCenterController.OverrideTenantQuotaRequest(
                MaxWarehouses: 4,
                MaxUsers: 25,
                MonthlyOrderQuota: 12000,
                ContractReference: "PLHD-2026-115",
                Reason: "Nâng hạn mức kho nhưng thiếu ngày hiệu lực đến"))).Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using (var qeDoc = JsonSerializer.SerializeToDocument(missingEffectiveUntil.Value))
        {
            qeDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("QUOTA_EFFECTIVE_UNTIL_REQUIRED");
        }

        // Immutable SalesOrder.OrderCode ("SO-YYYYMM-001", "SO-YYYYMM-002")
        var currentUserMock = new Moq.Mock<ICurrentUser>();
        currentUserMock.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUserMock.SetupGet(u => u.UserId).Returns(1);
        currentUserMock.SetupGet(u => u.IsGlobalAdmin).Returns(true);
        currentUserMock.Setup(u => u.HasPermission(Moq.It.IsAny<string>())).Returns(true);

        var whAuthMock = new Mock<IWarehouseAuthorizationService>();
        whAuthMock.Setup(w => w.EnsureWarehouseAccessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        whAuthMock.Setup(w => w.CanAccessWarehouseAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        whAuthMock.Setup(w => w.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { 1 });

        var soService = new SalesOrderBackorderService(
            db,
            Moq.Mock.Of<ERP.Domain.Interfaces.IInventoryStockRepository>(),
            Moq.Mock.Of<IStockReservationService>(),
            Moq.Mock.Of<IStockAllocationService>(),
            whAuthMock.Object,
            currentUserMock.Object);

        var octSo = new SalesOrder
        {
            Id = 901,
            TenantId = 1,
            OrderCode = "SO-202609-009",
            ExternalOrderId = "EXT-SEP-009",
            CustomerId = 10,
            WarehouseId = 1,
            Status = SalesOrderStatus.Released,
            Currency = "VND",
            CreatedAt = new DateTime(2026, 9, 29, 9, 30, 0, DateTimeKind.Utc),
            ReleasedAt = new DateTime(2026, 9, 29, 9, 30, 0, DateTimeKind.Utc),
            CreatedBy = 1,
            Lines =
            [
                new SalesOrderLine
                {
                    Id = 9001,
                    TenantId = 1,
                    SalesOrderId = 901,
                    ExternalLineId = "SEP-L1",
                    ProductId = 1,
                    OrderedQuantity = 1m,
                    UnitPrice = 500000m,
                    UomCode = "Bộ"
                }
            ]
        };
        db.SalesOrders.Add(octSo);
        await db.SaveChangesAsync();

        var so1 = await soService.CreateSalesOrderAsync(new CreateSalesOrderDto
        {
            ExternalOrderId = "EXT-W15-001",
            CustomerId = 10,
            WarehouseId = 1,
            Lines = [new CreateSalesOrderLineDto { ExternalLineId = "L1", ProductId = 1, OrderedQuantity = 2m, UnitPrice = 1500000m }]
        });
        var so2 = await soService.CreateSalesOrderAsync(new CreateSalesOrderDto
        {
            ExternalOrderId = "EXT-W15-002",
            CustomerId = 10,
            WarehouseId = 1,
            Lines = [new CreateSalesOrderLineDto { ExternalLineId = "L1", ProductId = 1, OrderedQuantity = 3m, UnitPrice = 1500000m }]
        });
        so1.OrderCode.Should().MatchRegex(@"^SO-\d{6}-001$");
        so2.OrderCode.Should().MatchRegex(@"^SO-\d{6}-002$");
        (await soService.GetSalesOrderAsync(901)).OrderCode.Should().Be("SO-202609-009");
        (await soService.ListSalesOrdersAsync(1)).Should().Contain(x => x.Id == 901 && x.OrderCode == "SO-202609-009");
        (await soService.GetSalesOrderAsync(so1.Id)).OrderCode.Should().Be(so1.OrderCode);

        // Mutate live MovingAverageCost of products & stocks -> Completed KitWorkOrder cost MUST remain 100% immutable!
        kitProduct.MovingAverageCost = 999999m;
        compProduct1.MovingAverageCost = 888888m;
        compProduct2.MovingAverageCost = 777777m;
        stock1.MovingAverageCost = 888888m;
        stock2.MovingAverageCost = 777777m;
        await db.SaveChangesAsync();

        var prodOps = new ProductOperationsService(db, currentUserMock.Object);
        var kwoListOutcome = await prodOps.GetKitWorkOrdersAsync(1, null, null);
        using (var kwoDoc = JsonSerializer.SerializeToDocument(kwoListOutcome.Payload))
        {
            var kwoItem = kwoDoc.RootElement.GetProperty("items").EnumerateArray()
                .Single(x => x.GetProperty("workOrderCode").GetString() == "KWO-2026-001");
            kwoItem.GetProperty("rolledUpUnitCost").GetDecimal().Should().Be(500000m);
            kwoItem.GetProperty("unitCost").GetDecimal().Should().Be(500000m);
            kwoItem.GetProperty("totalCost").GetDecimal().Should().Be(10000000m);
            kwoItem.GetProperty("baseUomCode").GetString().Should().Be("Bộ");
            foreach (var compLine in kwoItem.GetProperty("componentLines").EnumerateArray())
            {
                compLine.GetProperty("baseUomCode").GetString().Should().Be("Cái");
            }
        }

        // (g) BE-FIX-1: Inactive warehouse WH02 stocks are excluded from default /cost-valuation skuValuations
        var binWh2 = new WarehouseLocation { Id = 20, TenantId = 1, WarehouseId = 2, Code = "BIN-HN-01", Name = "Kệ chi nhánh Hà Nội", LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage, IsActive = true };
        db.WarehouseLocations.Add(binWh2);
        var stockWh2 = new InventoryStock { Id = 1003, TenantId = 1, WarehouseId = 2, ProductId = 2, LocationId = 20, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 30m, MovingAverageCost = 420000m };
        stockWh2.RefreshGrainHash();
        db.InventoryStocks.Add(stockWh2);
        await db.SaveChangesAsync();

        var reportOps = new ReportOperationsService(db, null, currentUserMock.Object);
        var gpOutcome = await reportOps.GetOwnerGrossProfitAsync(null, canReadCost: true);
        using (var gpDoc = JsonSerializer.SerializeToDocument(gpOutcome.Payload))
        {
            var valRows = gpDoc.RootElement.GetProperty("skuValuations").EnumerateArray().ToList();
            valRows.Should().NotBeEmpty();
            foreach (var vr in valRows)
            {
                vr.GetProperty("warehouseName").GetString().Should().Be("Kho Trung tâm (WH01)");
            }
            valRows.Single(x => x.GetProperty("productId").GetInt32() == 2)
                .GetProperty("onHandQuantity").GetDecimal().Should().Be(50m);
        }
    }

    [Fact]
    public async Task TC_W16_01_TenantRolesCatalog_ExcludesPlatformRolesAndProvidesDistinctVietnameseLabels()
    {
        var dbName = $"{nameof(TC_W16_01_TenantRolesCatalog_ExcludesPlatformRolesAndProvidesDistinctVietnameseLabels)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        var permRead = new Permission(AppPermissions.RoleRead, "Đọc danh mục vai trò") { Id = 1 };
        var permStock = new Permission(AppPermissions.PermissionRead, "Đọc danh mục quyền") { Id = 2 };
        db.Permissions.AddRange(permRead, permStock);

        byte[] rv = [0, 0, 0, 0, 0, 0, 0, 1];
        db.Roles.AddRange(
            new Role { Id = 1, RoleName = "Admin", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 2, RoleName = "Manager", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 3, RoleName = "Accountant", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 4, RoleName = "WarehouseStaff", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 5, RoleName = "Supervisor", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 6, RoleName = "Receiver", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 7, RoleName = "PickerPacker", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 8, RoleName = "Viewer", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 9, RoleName = "ShiftLead", SecurityDomain = "CUSTOMER_TENANT", RowVersion = rv },
            new Role { Id = 10, RoleName = "PlatformSuperAdmin", SecurityDomain = "PLATFORM", RowVersion = rv },
            new Role { Id = 11, RoleName = "PlatformBillingAdmin", SecurityDomain = "PLATFORM", RowVersion = rv },
            new Role { Id = 12, RoleName = "PlatformOpsAdmin", SecurityDomain = "PLATFORM", RowVersion = rv },
            new Role { Id = 13, RoleName = "PlatformSupportAgent", SecurityDomain = "PLATFORM", RowVersion = rv });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 1 },
            new RolePermission { RoleId = 1, PermissionId = 2 },
            new RolePermission { RoleId = 2, PermissionId = 2 });
        await db.SaveChangesAsync();

        static PermissionsController BuildPermissionsController(ErpKhoDbContext context, IEnumerable<Claim> claims)
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            };
            var accessor = new HttpContextAccessor { HttpContext = httpContext };
            var currentUser = new HttpCurrentUser(accessor);
            var warehouseAuthMock = new Mock<IWarehouseAuthorizationService>();
            var permOps = new PermissionOperationsService(context, currentUser, warehouseAuthMock.Object);
            return new PermissionsController(permOps, warehouseAuthMock.Object, currentUser)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };
        }

        var webJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        // 1. Customer Tenant Manager caller: zero Platform* roles, all 9 tenant roles have distinct Vietnamese labels
        var tenantManagerController = BuildPermissionsController(db,
        [
            new Claim(ClaimTypes.NameIdentifier, "11"),
            new Claim(ClaimTypes.Name, "wh_manager_1"),
            new Claim(ClaimTypes.Role, "Manager"),
            new Claim(TenantClaimTypes.TenantId, "1"),
            new Claim(TenantClaimTypes.SecurityDomain, "CUSTOMER_TENANT"),
            new Claim("permission", AppPermissions.RoleRead)
        ]);
        var tenantRes = await tenantManagerController.Roles(CancellationToken.None);
        var tenantOk = tenantRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(tenantOk.Value, webJsonOptions))
        {
            var items = doc.RootElement.EnumerateArray().ToList();
            items.Should().HaveCount(9);

            var roleCodes = items.Select(x => x.GetProperty("roleCode").GetString()!).ToList();
            roleCodes.Should().NotContain(c => c.StartsWith("Platform", StringComparison.OrdinalIgnoreCase));
            items.Select(x => x.GetProperty("roleName").GetString()!).Should().Equal(roleCodes);

            var labelsByCode = items.ToDictionary(
                x => x.GetProperty("roleCode").GetString()!,
                x => x.GetProperty("displayNameVi").GetString()!);

            labelsByCode["Admin"].Should().Be("Quản trị viên công ty");
            labelsByCode["Manager"].Should().Be("Quản lý kho");
            labelsByCode["Accountant"].Should().Be("Kế toán kho");
            labelsByCode["WarehouseStaff"].Should().Be("Nhân viên kho");
            labelsByCode["Supervisor"].Should().Be("Giám sát & QC");
            labelsByCode["Receiver"].Should().Be("Nhân viên nhận hàng");
            labelsByCode["PickerPacker"].Should().Be("Nhân viên lấy/đóng gói");
            labelsByCode["Viewer"].Should().Be("Chỉ xem / Kiểm toán");
            labelsByCode["ShiftLead"].Should().Be("Vai trò ShiftLead");

            labelsByCode.Values.Should().OnlyHaveUniqueItems();
            labelsByCode.Values.Should().NotContain("Vai trò khác");
            items.All(x => !string.IsNullOrWhiteSpace(x.GetProperty("rowVersion").GetString())).Should().BeTrue();
            items.Single(x => x.GetProperty("roleCode").GetString() == "Admin")
                .GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Contain(AppPermissions.RoleRead);
        }

        // 2. Dual-hat qa_admin caller on Customer Tenant /api/v1/permissions/roles: still excludes internal Platform* roles
        var qaAdminController = BuildPermissionsController(db,
        [
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, "qa_admin"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(TenantClaimTypes.TenantId, "1"),
            new Claim(TenantClaimTypes.SecurityDomain, "PLATFORM"),
            new Claim("permission", AppPermissions.RoleRead)
        ]);
        var qaAdminRes = await qaAdminController.Roles(CancellationToken.None);
        var qaAdminOk = qaAdminRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(qaAdminOk.Value, webJsonOptions))
        {
            var roleCodes = doc.RootElement.EnumerateArray().Select(x => x.GetProperty("roleCode").GetString()!).ToList();
            roleCodes.Should().HaveCount(9);
            roleCodes.Should().NotContain(c => c.StartsWith("Platform", StringComparison.OrdinalIgnoreCase));
        }

        // 3. Dedicated Platform Control Plane operator calling Customer /api/v1/permissions/roles: strictly excludes Platform roles (FND-W18-02)
        var platformOpController = BuildPermissionsController(db,
        [
            new Claim(ClaimTypes.NameIdentifier, "99"),
            new Claim(ClaimTypes.Name, "platform_super_admin"),
            new Claim(ClaimTypes.Role, "PlatformSuperAdmin"),
            new Claim(TenantClaimTypes.TenantId, "1"),
            new Claim(TenantClaimTypes.SecurityDomain, "PLATFORM"),
            new Claim("permission", AppPermissions.RoleRead)
        ]);
        var platformRes = await platformOpController.Roles(CancellationToken.None);
        var platformOk = platformRes.Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(platformOk.Value, webJsonOptions))
        {
            var roleCodes = doc.RootElement.EnumerateArray().Select(x => x.GetProperty("roleCode").GetString()!).ToList();
            roleCodes.Should().HaveCount(9);
            roleCodes.Should().NotContain(c => c.StartsWith("Platform", StringComparison.OrdinalIgnoreCase));
            roleCodes.Take(6).Should().Equal("Admin", "Manager", "Supervisor", "Accountant", "WarehouseStaff", "Viewer");
        }
    }

    [Fact]
    public async Task TC_W16_02_PlatformDashboardRecentEvents_And_EinvoiceBuyerTaxCodeValidation()
    {
        var dbName = $"{nameof(TC_W16_02_PlatformDashboardRecentEvents_And_EinvoiceBuyerTaxCodeValidation)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        // --- PART 1: UIX-20 Recent Platform Reconciliation & Activity Summary on GET /api/v1/platform/dashboard ---
        db.SaasTenants.Add(new SaasTenant
        {
            Id = 1,
            TenantId = 1,
            TenantCode = "TENANT-001",
            CompanyName = "Công ty TNHH Tiếp Vận Kho Vận Việt Nam",
            LifecycleStatus = "ACTIVE",
            CreatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        db.Users.Add(new User
        {
            Id = 1,
            TenantId = 1,
            Username = "qa_admin",
            FullName = "Quản trị viên Hệ thống",
            PasswordHash = "hash",
            RoleId = 1,
            IsActive = true,
            SecurityDomain = "PLATFORM"
        });
        db.SaasBillingInvoices.AddRange(
            new SaasBillingInvoice
            {
                Id = 1,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202610-001",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10.00m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                BuyerTaxCode = "0312345678",
                BuyerLegalName = "Công ty TNHH Tiếp Vận Kho Vận Việt Nam",
                PaymentProvider = "VIETQR",
                Status = "PAID",
                CreatedAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PaidAtUtc = new DateTime(2026, 10, 3, 9, 15, 0, DateTimeKind.Utc)
            },
            new SaasBillingInvoice
            {
                Id = 2,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202611-002",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10.00m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                BuyerTaxCode = "0312345678",
                BuyerLegalName = "Công ty TNHH Tiếp Vận Kho Vận Việt Nam",
                PaymentProvider = "VIETQR",
                Status = "UNPAID",
                CreatedAtUtc = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Utc)
            });
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 501,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_BILLING_MANUAL_RECONCILE",
                EntityName = "SaasBillingInvoice",
                EntityId = 1,
                NewValues = "{\"invoiceCode\":\"INV-SAAS-202610-001\",\"actorUsername\":\"qa_admin\",\"status\":\"PAID\"}",
                Reason = "Đối soát sao kê VietQR kỳ tháng 10/2026 khớp 1.490.000 đ",
                Result = "Success",
                Timestamp = new DateTime(2026, 11, 2, 8, 30, 0, DateTimeKind.Utc)
            },
            new AuditLog
            {
                Id = 502,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_TENANT_QUOTA_UPDATED",
                EntityName = "TenantSubscription",
                EntityId = 1,
                NewValues = "{\"tenantCode\":\"TENANT-001\",\"monthlyOrderQuota\":15000}",
                Reason = "Nâng hạn ngạch đơn hàng tháng 11/2026 lên 15.000 đơn",
                Result = "Success",
                Timestamp = new DateTime(2026, 11, 3, 9, 15, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var auditCountBeforeGet = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().CountAsync();
        var platformController = CreatePlatformController(
            db,
            CreatePlatformControllerContext(1, AppRoles.SuperAdmin, AppPermissions.PlatformDashboardRead));

        var dashRes = await platformController.GetDashboard();
        var dashOk = dashRes.Should().BeOfType<OkObjectResult>().Subject;
        db.ChangeTracker.HasChanges().Should().BeFalse("GET /api/v1/platform/dashboard must use AsNoTracking and never mutate DB state");
        (await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().CountAsync()).Should().Be(auditCountBeforeGet);

        using (var dashDoc = JsonSerializer.SerializeToDocument(dashOk.Value))
        {
            var recentEvents = dashDoc.RootElement.GetProperty("recentEvents").EnumerateArray().ToList();
            recentEvents.Should().NotBeEmpty();
            recentEvents.Count.Should().BeLessThanOrEqualTo(8);

            // Sorted descending by timestampUtc
            var timestamps = recentEvents.Select(e => e.GetProperty("timestampUtc").GetDateTime()).ToList();
            timestamps.Should().BeInDescendingOrder();

            // Verify top audit event (+07:00 timestampVi = 03/11/2026 16:15, clean admin actor display)
            var topEvent = recentEvents[0];
            topEvent.GetProperty("id").GetString().Should().Be("AUD-502");
            topEvent.GetProperty("eventTypeCode").GetString().Should().Be("PLATFORM_TENANT_QUOTA_UPDATED");
            topEvent.GetProperty("eventLabelVi").GetString().Should().Be("Cập nhật hạn ngạch doanh nghiệp");
            topEvent.GetProperty("targetCode").GetString().Should().Be("TENANT-001");
            topEvent.GetProperty("timestampVi").GetString().Should().Be("03/11/2026 16:15");
            topEvent.GetProperty("actorDisplay").GetString().Should().Be("Quản trị viên Cấp cao (qa_admin)");
            topEvent.GetProperty("summaryVi").GetString().Should().Contain("15.000 đơn");
            topEvent.GetProperty("tone").GetString().Should().Be("info");

            // Because fewer than 5 audit rows exist, invoice milestones supplement the timeline without duplicating covered paid invoice
            var eventCodes = recentEvents.Select(e => e.GetProperty("eventTypeCode").GetString()!).ToList();
            eventCodes.Should().Contain("PLATFORM_BILLING_MANUAL_RECONCILE");
            eventCodes.Should().Contain("BILLING_INVOICE_PENDING");
            eventCodes.Should().NotContain("BILLING_INVOICE_PAID",
                "INV-SAAS-202610-001 payment is already represented by PLATFORM_BILLING_MANUAL_RECONCILE audit entry");
        }

        // --- PART 2: UIX-05 Server-Side Validation for buyerTaxCode on POST /api/v1/export-receipts/einvoice-nd70-draft ---
        var wh = new Warehouse { Id = 1, TenantId = 1, Code = "WH-W16-01", Name = "Kho Kiểm định HĐĐT", IsActive = true };
        var prod = new Product { Id = 601, TenantId = 1, Code = "SKU-W16-601", Name = "Thiết bị kiểm kho PDA", UnitId = 1, MovingAverageCost = 1000000m, DefaultVatRatePercent = 10.00m, IsActive = true };
        var receipt = new ExportReceipt
        {
            Id = 1601,
            TenantId = 1,
            Code = "PXK-W16-1601",
            WarehouseId = 1,
            Status = ReceiptStatus.Dispatched,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail { Id = 16011, ProductId = 601, Product = prod, Quantity = 2m, UnitPrice = 1500000m }
            ]
        };
        db.Warehouses.Add(wh);
        db.Products.Add(prod);
        db.ExportReceipts.Add(receipt);
        await db.SaveChangesAsync();

        var expServiceMock = new Mock<IExportReceiptService>();
        var whAuthMock = new Mock<IWarehouseAuthorizationService>();
        var handoverMock = new Mock<IHandoverManifestService>();
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUserMock.SetupGet(x => x.UserId).Returns(1);
        currentUserMock.SetupGet(x => x.TenantId).Returns(1);
        currentUserMock.SetupGet(x => x.Role).Returns("Accountant");
        currentUserMock.Setup(x => x.HasPermission(It.IsAny<string>())).Returns(true);

        var expOps = new ExportReceiptOperationsService(db);
        var expController = new ExportReceiptsController(
            expServiceMock.Object,
            expOps,
            whAuthMock.Object,
            currentUserMock.Object,
            handoverMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "1"),
                        new Claim(ClaimTypes.Role, "Accountant"),
                        new Claim("permission", AppPermissions.ExportReceiptUpdate)
                    ], "TestAuth"))
                }
            }
        };

        const string expectedTaxErrorMessage =
            "Mã số thuế người mua không hợp lệ (bắt buộc gồm 10 chữ số hoặc 13 chữ số chi nhánh theo chuẩn Tổng cục Thuế).";

        foreach (var invalidTaxCode in new[] { "ABC1234567", "0312345", "03123456789", "0312345678-12" })
        {
            var badRes = await expController.IssueEinvoiceNd70Draft(
                new ExportReceiptsController.IssueEinvoiceNd70DraftRequest(
                    ExportReceiptId: 1601,
                    WarehouseId: 1,
                    BuyerTaxCode: invalidTaxCode,
                    BuyerName: "Công ty TNHH Thử Nghiệm"));
            var bad422 = badRes.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
            bad422.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
            using var errDoc = JsonSerializer.SerializeToDocument(bad422.Value);
            errDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_BUYER_TAX_CODE");
            errDoc.RootElement.GetProperty("message").GetString().Should().Be(expectedTaxErrorMessage);

            // Also verify direct service boundary defense-in-depth
            var svcOutcome = await expOps.IssueEinvoiceNd70DraftAsync(
                new IssueEinvoiceNd70DraftInput(ExportReceiptId: 1601, WarehouseId: 1, BuyerTaxCode: invalidTaxCode),
                actorUserId: 1,
                isAuthenticated: true,
                currentUserOverride: currentUserMock.Object);
            svcOutcome.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);
        }

        foreach (var validTaxCode in new string?[] { "0312345678", "0312345678-001", "0312345678001", "KHACH_LE_KHONG_LAY_HOA_DON", null, "" })
        {
            var okRes = await expController.IssueEinvoiceNd70Draft(
                new ExportReceiptsController.IssueEinvoiceNd70DraftRequest(
                    ExportReceiptId: 1601,
                    WarehouseId: 1,
                    BuyerTaxCode: validTaxCode,
                    BuyerName: "Công ty TNHH Hợp Lệ"));
            okRes.Should().BeOfType<OkObjectResult>();
        }
    }
}
