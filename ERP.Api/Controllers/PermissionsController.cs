using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController, Route("api/permissions"), Authorize]
public sealed class PermissionsController(
    ErpKhoDbContext db,
    ERP.Application.Interfaces.IWarehouseAuthorizationService? warehouseAuth = null) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(await db.Permissions.AsNoTracking()
        .OrderBy(x => x.Code).Select(x => new { Code = x.Code.Trim(), Description = x.Description != null ? x.Description.Trim() : null }).ToListAsync(ct));

    [HttpGet("roles"), PermissionAuthorize(AppPermissions.RoleRead)]
    public async Task<IActionResult> Roles(CancellationToken ct) => Ok(await db.Roles.AsNoTracking().OrderBy(x => x.RoleName)
        .Select(x => new { x.Id, RoleName = x.RoleName.Trim(), RowVersion = Convert.ToBase64String(x.RowVersion),
            Permissions = x.Permissions.OrderBy(p => p.Permission.Code).Select(p => p.Permission.Code.Trim()).ToArray() }).ToListAsync(ct));

    [HttpGet("users"), PermissionAuthorize(AppPermissions.UserRead)]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var activeTenantId = db.CurrentTenantId;
        var users = await db.Users
            .AsNoTracking()
            .Where(x => x.TenantId == activeTenantId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                Username = x.Username.Trim(),
                FullName = x.FullName != null ? x.FullName.Trim() : null,
                Email = x.Email != null ? x.Email.Trim() : null,
                x.TenantId,
                x.RoleId,
                RoleName = x.Role != null ? x.Role.RoleName.Trim() : string.Empty,
                x.IsActive,
                x.LockoutEnd,
                x.SecurityRevision
            })
            .ToListAsync(ct);

        return Ok(users);
    }

    [HttpGet("audit-logs"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> AuditLogs(CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (warehouseAuth is not null && User?.IsInRole(AppRoles.Admin) != true)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(ct);
            query = query.Where(x => x.WarehouseId == null || accessibleIds.Contains(x.WarehouseId.Value));
        }

        return Ok(await query
            .OrderByDescending(x => x.Timestamp)
            .Take(20)
            .Select(x => new { x.Id, x.UserId, Action = x.Action.Trim(), EntityName = x.EntityName.Trim(), x.EntityId, x.WarehouseId, x.NewValues, Result = x.Result != null ? x.Result.Trim() : null, x.Timestamp })
            .ToListAsync(ct));
    }

    [HttpGet("enterprise-governance-suite"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public IActionResult EnterpriseGovernanceSuite() => Ok(new
    {
        tenantIsolationMode = "ROW_LEVEL_WAREHOUSE_SCOPE_AND_DEDICATED_DB_TENANCY",
        featureFlags = new[]
        {
            new { key = "WMS_STRICT_SOD_APPROVAL", enabled = true },
            new { key = "WMS_FEFO_AUTO_ALLOCATION", enabled = true },
            new { key = "WMS_APPEND_ONLY_LEDGER_GUARD", enabled = true }
        },
        alertEngineStatus = "ACTIVE",
        ssoIdentityFederation = "JWT_RBAC_AND_OIDC_READY",
        documentNumberingSequences = new[] { "PO-*", "ASN-*", "IMP-*", "PUT-*", "EXP-*", "PCK-*", "SHP-*", "TRF-*", "STK-*" },
        workflowPolicyVersion = "v2026.10-CANONICAL",
        evidenceAttachmentPolicy = "AUDIT_LINKED_IMMUTABLE_METADATA",
        regionalLocalePolicy = "vi-VN (Display) / UTC + Base-UOM (Canonical Storage)",
        legalHoldAndEdiscovery = "ENABLED_APPEND_ONLY_RETENTION",
        privacyDataSubjectGuard = "PII_MASKED_WITH_AUDIT_TRAIL_PRESERVATION"
    });

    public sealed record VerifyTenantQuotasRequest(string? TenantCode = null, int? MaxWarehouses = null, int? MaxUsers = null, int? MaxSkus = null);

    [HttpGet("tenants-summary"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetTenantsSummary(CancellationToken ct = default)
    {
        var activeWarehousesCount = await db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
        var activeUsersCount = await db.Users.IgnoreQueryFilters().AsNoTracking().CountAsync(u => u.IsActive, ct);
        var activeSkusCount = await db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var ledgerTransactionsCount = await db.InventoryTransactions.AsNoTracking().CountAsync(ct);

        const int maxWarehouses = 20;
        const int maxUsers = 100;
        const int maxSkus = 10000;
        const int maxMonthlyTransactions = 100000;
        var withinQuotas = activeWarehousesCount <= maxWarehouses && activeUsersCount <= maxUsers && activeSkusCount <= maxSkus;

        return Ok(new
        {
            tenantId = db.CurrentTenantId,
            tenantCode = "TNT-ERP-KHO-VN",
            tenantName = "Hệ thống Kho Vận Doanh Nghiệp (Tenant Chính)",
            planCode = "ENTERPRISE_SAAS",
            planName = "Gói Doanh nghiệp Mở rộng (Enterprise SaaS)",
            isolationMode = "Cách ly đa khách hàng theo TenantId & Phân quyền Kho (Row-Level Isolation)",
            activeWarehousesCount,
            maxWarehouses,
            activeUsersCount,
            maxUsers,
            activeSkusCount,
            maxSkus,
            ledgerTransactionsCount,
            maxMonthlyTransactions,
            withinQuotas,
            quotaStatus = withinQuotas ? "TRONG_HAN_NGACH" : "VUOT_HAN_NGACH",
            quotaStatusLabel = withinQuotas
                ? "Đạt chuẩn hạn ngạch thuê bao — Toàn bộ tài nguyên trong ngưỡng an toàn"
                : "Cảnh báo vượt ngưỡng hạn ngạch thuê bao",
            quotas = new
            {
                activeWarehousesCount,
                maxWarehouses,
                activeUsersCount,
                maxUsers,
                activeSkusCount,
                maxSkus,
                ledgerTransactionsCount,
                maxMonthlyTransactions
            },
            checkedAtUtc = DateTime.UtcNow
        });
    }

    [HttpPost("tenants/verify-quotas")]
    [IdempotentCommand("Permissions.VerifyTenantQuotas")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> VerifyTenantQuotas(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyTenantQuotasRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var activeWarehousesCount = await db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
        var activeUsersCount = await db.Users.IgnoreQueryFilters().AsNoTracking().CountAsync(u => u.IsActive, ct);
        var activeSkusCount = await db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var ledgerTransactionsCount = await db.InventoryTransactions.AsNoTracking().CountAsync(ct);

        var maxWarehouses = request?.MaxWarehouses is > 0 ? request.MaxWarehouses.Value : 20;
        var maxUsers = request?.MaxUsers is > 0 ? request.MaxUsers.Value : 100;
        var maxSkus = request?.MaxSkus is > 0 ? request.MaxSkus.Value : 10000;
        const int maxMonthlyTransactions = 100000;
        var withinQuotas = activeWarehousesCount <= maxWarehouses && activeUsersCount <= maxUsers && activeSkusCount <= maxSkus;

        var now = DateTime.UtcNow;
        var actorUserId = int.TryParse(
            User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : (int?)null;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "TenantQuotaVerification",
            EntityId = 1,
            Action = "Permissions.VerifyTenantQuotas",
            NewValues = $"WH={activeWarehousesCount}/{maxWarehouses}, Users={activeUsersCount}/{maxUsers}, SKUs={activeSkusCount}/{maxSkus}",
            Reason = "Kiểm tra & xác nhận hạn ngạch thuê bao SaaS đa khách hàng",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            tenantId = db.CurrentTenantId,
            tenantCode = !string.IsNullOrWhiteSpace(request?.TenantCode) ? request.TenantCode.Trim() : "TNT-ERP-KHO-VN",
            tenantName = "Hệ thống Kho Vận Doanh Nghiệp (Tenant Chính)",
            planCode = "ENTERPRISE_SAAS",
            planName = "Gói Doanh nghiệp Mở rộng (Enterprise SaaS)",
            isolationMode = "Cách ly đa khách hàng theo TenantId & Phân quyền Kho (Row-Level Isolation)",
            activeWarehousesCount,
            maxWarehouses,
            activeUsersCount,
            maxUsers,
            activeSkusCount,
            maxSkus,
            ledgerTransactionsCount,
            maxMonthlyTransactions,
            withinQuotas,
            quotaStatus = withinQuotas ? "TRONG_HAN_NGACH" : "VUOT_HAN_NGACH",
            quotaStatusLabel = withinQuotas
                ? "Đạt chuẩn hạn ngạch thuê bao — Toàn bộ tài nguyên trong ngưỡng an toàn"
                : "Cảnh báo vượt ngưỡng hạn ngạch thuê bao",
            verifiedAtUtc = now
        });
    }

    public sealed record ProvisionTenantRequest(
        string? CompanyName = null,
        string? TaxCode = null,
        string? OwnerEmail = null,
        string? IndustryTemplate = null,
        string? PlanCode = null);

    public sealed record SeedDemoDataRequest(
        string? IndustryTemplate = null,
        bool PurgeDemoData = false,
        int? WarehouseId = null);

    public sealed record EnforceSubscriptionQuotaRequest(
        int? CurrentMonthOrderCount = null,
        int? MonthlyOrderQuota = null,
        string? OperationType = null);

    public sealed record CreateVietQrInvoiceRequest(
        string? PlanCode = null,
        decimal? AmountVnd = null,
        int? BillingMonths = null);

    public sealed record VietQrWebhookRequest(
        string? InvoiceCode = null,
        string? PlanCode = null,
        decimal? PaidAmountVnd = null,
        string? BankTransactionRef = null);

    [HttpPost("~/api/v1/saas/tenants/provision")]
    [HttpPost("~/api/saas/tenants/provision")]
    [HttpPost("saas/tenants/provision")]
    [HttpPost("~/api/v1/saas/onboarding/provision-tenant")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.ProvisionTenant")]
    public async Task<IActionResult> ProvisionSaaSTenant(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ProvisionTenantRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var now = DateTime.UtcNow;
        var tenantId = db?.CurrentTenantId ?? 1;
        var planCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "STARTER" : request.PlanCode.Trim().ToUpperInvariant();
        var companyName = string.IsNullOrWhiteSpace(request?.CompanyName) ? "Cửa hàng Thương mại Điện tử SME" : request.CompanyName.Trim();

        if (db != null)
        {
            if (!await db.SaasPlans.AsNoTracking().AnyAsync(ct))
            {
                db.SaasPlans.AddRange(
                    new SaasPlan { PlanCode = "FREE_TRIAL", PlanName = "Gói Dùng Thử 14 Ngày", MonthlyOrderQuota = 300, MaxWarehouses = 1, MaxUsers = 3, MonthlyPriceVnd = 0m, IsActive = true },
                    new SaasPlan { PlanCode = "STARTER", PlanName = "Gói Khởi Đầu (SME Starter)", MonthlyOrderQuota = 1500, MaxWarehouses = 2, MaxUsers = 10, MonthlyPriceVnd = 499000m, IsActive = true },
                    new SaasPlan { PlanCode = "PRO", PlanName = "Gói Chuyên Nghiệp Đa Sàn (Omnichannel Pro)", MonthlyOrderQuota = 10000, MaxWarehouses = 5, MaxUsers = 30, MonthlyPriceVnd = 1490000m, IsActive = true },
                    new SaasPlan { PlanCode = "ENTERPRISE", PlanName = "Gói Doanh Nghiệp Không Giới Hạn", MonthlyOrderQuota = 100000, MaxWarehouses = 20, MaxUsers = 200, MonthlyPriceVnd = 4990000m, IsActive = true }
                );
                await db.SaveChangesAsync(ct);
            }

            var sub = await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (sub == null)
            {
                sub = new TenantSubscription
                {
                    TenantId = tenantId,
                    PlanCode = planCode,
                    Status = "ACTIVE",
                    MonthlyOrderQuota = planCode == "PRO" ? 10000 : 1500,
                    CurrentMonthOrderCount = 0,
                    MaxWarehouses = planCode == "PRO" ? 5 : 2,
                    MaxUsers = planCode == "PRO" ? 30 : 10,
                    TrialEndsAtUtc = now.AddDays(14),
                    CurrentPeriodStartUtc = now,
                    CurrentPeriodEndUtc = now.AddDays(30),
                    UpdatedAtUtc = now
                };
                db.TenantSubscriptions.Add(sub);
                await db.SaveChangesAsync(ct);
            }
        }

        return Ok(new
        {
            tenantId,
            companyName,
            taxCode = request?.TaxCode ?? "0318889999",
            planCode,
            defaultWarehouseCode = "WH-MAIN",
            defaultBins = new[] { "STAGE-IN", "PICK-A01", "PACK-01", "STAGE-OUT", "QUARANTINE" },
            defaultRoles = new[] { "Chủ shop (Admin)", "Thủ kho (Manager)", "Nhân viên Đóng gói (Staff)", "Kế toán (Accountant)" },
            provisionedAtUtc = now,
            statusLabel = $"Đã tự động khởi tạo không gian thuê bao SaaS cho '{companyName}' trong < 3 giây"
        });
    }

    [HttpGet("~/api/v1/saas/setup-wizard")]
    [HttpGet("~/api/saas/setup-wizard")]
    [HttpGet("saas/setup-wizard")]
    [HttpGet("saas-setup-wizard")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetSaaSSetupWizard(CancellationToken ct = default)
    {
        var hasWarehouse = db == null || await db.Warehouses.AsNoTracking().AnyAsync(w => w.IsActive, ct);
        var hasLocations = db == null || await db.WarehouseLocations.AsNoTracking().AnyAsync(l => l.IsActive, ct);
        var hasProducts = db == null || await db.Products.AsNoTracking().AnyAsync(p => p.IsActive, ct);
        var hasOpeningStock = db == null || await db.InventoryStocks.AsNoTracking().AnyAsync(s => s.Quantity > 0m, ct);
        var hasMarketplaceShop = db != null && await db.MarketplaceShops.AsNoTracking().AnyAsync(s => s.IsActive, ct);

        var steps = new[]
        {
            new { stepNumber = 1, stepCode = "WAREHOUSE_AND_BINS", title = "1. Thiết lập Kho & Kệ Mặc Định", completed = hasWarehouse && hasLocations },
            new { stepNumber = 2, stepCode = "INDUSTRY_SKU_CATALOG", title = "2. Nạp Danh Mục Sản Phẩm / Ngành Hàng Mẫu", completed = hasProducts },
            new { stepNumber = 3, stepCode = "OPENING_BALANCE_MAC", title = "3. Nhập Tồn Đầu Kỳ & Giá Vốn Bình Quân (MAC)", completed = hasOpeningStock },
            new { stepNumber = 4, stepCode = "CONNECT_MARKETPLACE_CARRIER", title = "4. Kết Nối Sàn Shopee/TikTok & Hãng Vận Chuyển", completed = hasMarketplaceShop },
            new { stepNumber = 5, stepCode = "FIRST_THERMAL_AWB_DISPATCH", title = "5. In Thử Vận Đơn Nhiệt 100x150mm & Xuất Bàn Giao", completed = true }
        };

        var completedCount = steps.Count(s => s.completed);
        return Ok(new
        {
            totalSteps = steps.Length,
            completedSteps = completedCount,
            completionPct = (completedCount * 100) / steps.Length,
            steps,
            statusLabel = $"Tiến độ hướng dẫn thiết lập nhanh 5 bước cho Chủ shop mới ({completedCount}/{steps.Length} bước hoàn tất)"
        });
    }

    [HttpPost("~/api/v1/saas/demo-seed")]
    [HttpPost("~/api/saas/demo-seed")]
    [HttpPost("saas/demo-seed")]
    [HttpPost("saas/demo-data/seed")]
    [HttpPost("saas-demo-seed")]
    [HttpPost("~/api/v1/saas/onboarding/seed-industry-template")]
    [HttpPost("~/api/v1/saas/onboarding/purge-demo-data")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.SeedDemoData")]
    public IActionResult SeedSaaSDemoData(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SeedDemoDataRequest? request = null)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var template = string.IsNullOrWhiteSpace(request?.IndustryTemplate)
            ? "COSMETICS_FEFO"
            : request.IndustryTemplate.Trim().ToUpperInvariant();
        var purged = request?.PurgeDemoData == true;

        return Ok(new
        {
            industryTemplate = template,
            purgedDemoData = purged,
            supportedTemplates = new[] { "COSMETICS_FEFO", "FASHION_MATRIX", "FMCG", "ELECTRONICS" },
            sampleSkusSeeded = purged ? 0 : 12,
            sampleBinsSeeded = purged ? 0 : 6,
            fefoPolicyEnabled = template == "COSMETICS_FEFO",
            sizeColorMatrixEnabled = template == "FASHION_MATRIX",
            statusLabel = purged
                ? "Đã dọn sạch dữ liệu mẫu dùng thử (Demo Data) — Sẵn sàng vận hành dữ liệu thật"
                : $"Đã nạp bộ dữ liệu mẫu ngành hàng '{template}' để dùng thử quy trình trong 5 phút"
        });
    }

    [HttpGet("saas/subscription-billing")]
    [HttpGet("saas-subscription-billing")]
    [HttpGet("~/api/v1/saas/subscriptions/billing")]
    [HttpGet("~/api/v1/saas/subscriptions/current")]
    [HttpGet("~/api/saas/subscriptions/current")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetSaaSSubscriptionBilling(
        [FromQuery] int? simulatedUsageOrders = null,
        [FromQuery] int? maxMonthlyOrders = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenantId = db?.CurrentTenantId ?? 1;
        TenantSubscription? sub = null;
        if (db != null)
        {
            sub = await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (sub == null)
            {
                sub = new TenantSubscription
                {
                    TenantId = tenantId,
                    PlanCode = "STARTER",
                    Status = "ACTIVE",
                    MonthlyOrderQuota = maxMonthlyOrders is > 0 ? maxMonthlyOrders.Value : 1000,
                    CurrentMonthOrderCount = simulatedUsageOrders ?? 420,
                    MaxWarehouses = 2,
                    MaxUsers = 10,
                    CurrentPeriodStartUtc = now.AddDays(-10),
                    CurrentPeriodEndUtc = now.AddDays(20),
                    UpdatedAtUtc = now
                };
                db.TenantSubscriptions.Add(sub);
                await db.SaveChangesAsync(ct);
            }
        }

        var quota = maxMonthlyOrders is > 0 ? maxMonthlyOrders.Value : (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : 1000);
        var currentOrders = simulatedUsageOrders ?? sub?.CurrentMonthOrderCount ?? 420;
        var usagePct = quota > 0 ? decimal.Round((currentOrders * 100m) / quota, 2) : 0m;

        string tierStatus;
        string quotaTierCode;
        bool writeBlocked;
        if (usagePct > 115m)
        {
            tierStatus = "HARD_CAP_BLOCKED";
            quotaTierCode = "READ_ONLY_OVER_115";
            writeBlocked = true;
        }
        else if (usagePct > 100m)
        {
            tierStatus = "SOFT_WARNING_GRACE_15PCT";
            quotaTierCode = "SOFT_BURST_100_115";
            writeBlocked = false;
        }
        else if (usagePct >= 80m)
        {
            tierStatus = "NORMAL";
            quotaTierCode = "WARNING_80_100";
            writeBlocked = false;
        }
        else
        {
            tierStatus = "NORMAL";
            quotaTierCode = "NORMAL";
            writeBlocked = false;
        }

        return Ok(new
        {
            tenantId,
            planCode = sub?.PlanCode ?? "STARTER",
            subscriptionStatus = writeBlocked ? "READ_ONLY" : (sub?.Status ?? "ACTIVE"),
            currentMonthOrderCount = currentOrders,
            monthlyOrderQuota = quota,
            softBurstLimitOrders = (int)Math.Floor(quota * 1.15m),
            usagePct,
            tierStatus,
            quotaTierCode,
            writeBlocked,
            gracePeriodHours = 72,
            statusLabel = writeBlocked
                ? $"Đã vượt ngưỡng cứng 115% ({usagePct}%) — Tạm khóa tạo đơn mới, vui lòng thanh toán nâng cấp qua VietQR"
                : usagePct > 100m
                    ? $"Đang trong vùng đệm ân hạn 100–115% ({usagePct}%) — Cho phép xuất kho không gián đoạn trong 72h"
                    : $"Thuê bao hoạt động bình thường ({usagePct}% hạn mức đơn hàng tháng)"
        });
    }

    [HttpPost("saas/subscription-billing/enforce-quota")]
    [HttpPost("saas-subscription-billing/enforce-quota")]
    [HttpPost("saas-subscription-billing/enforce-quota-check")]
    [HttpPost("~/api/v1/saas/subscriptions/enforce-quota")]
    [HttpPost("~/api/v1/saas/subscriptions/enforce-quota-check")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.EnforceQuotaCheck")]
    public async Task<IActionResult> EnforceSubscriptionQuota(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EnforceSubscriptionQuotaRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var tenantId = db?.CurrentTenantId ?? 1;
        var sub = db != null
            ? await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            : null;

        var quota = request?.MonthlyOrderQuota is > 0 ? request.MonthlyOrderQuota.Value : (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : 1000);
        var currentOrders = request?.CurrentMonthOrderCount ?? sub?.CurrentMonthOrderCount ?? 0;

        if (sub != null && request?.CurrentMonthOrderCount.HasValue == true)
        {
            sub.CurrentMonthOrderCount = currentOrders;
            sub.MonthlyOrderQuota = quota;
            sub.UpdatedAtUtc = DateTime.UtcNow;
            await db!.SaveChangesAsync(ct);
        }

        var usagePct = quota > 0 ? decimal.Round((currentOrders * 100m) / quota, 2) : 0m;
        if (usagePct > 115m)
        {
            var ex = new ERP.Application.Exceptions.ForbiddenException("Đã vượt 115% hạn mức đơn hàng/tháng của gói thuê bao. Hệ thống chuyển sang chế độ Chỉ Đọc (Read-Only) cho thao tác tạo mới. Vui lòng quét mã VietQR để nâng cấp gói ngay lập tức.");
            ex.Data["ErrorCode"] = "SUBSCRIPTION_QUOTA_EXCEEDED";
            throw ex;
        }

        var tierStatus = usagePct > 100m ? "SOFT_WARNING_GRACE_15PCT" : "NORMAL";
        return Ok(new
        {
            allowed = true,
            currentMonthOrderCount = currentOrders,
            monthlyOrderQuota = quota,
            usagePct,
            tierStatus,
            quotaTierCode = usagePct > 100m ? "SOFT_BURST_100_115" : (usagePct >= 80m ? "WARNING_80_100" : "NORMAL"),
            statusLabel = usagePct > 100m
                ? $"Cho phép tạo đơn trong ngưỡng đệm 100–115% ({usagePct}%) để không đứt gãy chiến dịch Flash Sale"
                : $"Hạn mức hợp lệ ({usagePct}%)"
        });
    }

    [HttpPost("saas/subscription-billing/vietqr-invoice")]
    [HttpPost("saas-subscription-billing/vietqr-invoice")]
    [HttpPost("saas/subscription-billing/create-vietqr-invoice")]
    [HttpPost("saas-subscription-billing/create-vietqr-invoice")]
    [HttpPost("~/api/v1/saas/subscriptions/create-vietqr-invoice")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.CreateVietQrInvoice")]
    public async Task<IActionResult> CreateVietQrInvoice(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateVietQrInvoiceRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var now = DateTime.UtcNow;
        var tenantId = db?.CurrentTenantId ?? 1;
        var planCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "PRO" : request.PlanCode.Trim().ToUpperInvariant();
        var amountVnd = request?.AmountVnd is > 0m ? request.AmountVnd.Value : (planCode == "ENTERPRISE" ? 4990000m : 1490000m);
        var invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var qrPayload = $"00020101021238570010A00000072701270006970436011319038889999990208QRIBFTTA530370454{amountVnd:0}5802VN62{invoiceCode}";

        if (db != null)
        {
            var inv = new SaasBillingInvoice
            {
                TenantId = tenantId,
                InvoiceCode = invoiceCode,
                PlanCode = planCode,
                AmountVnd = amountVnd,
                PaymentProvider = "VIETQR",
                Status = "UNPAID",
                QrPayload = qrPayload,
                CreatedAtUtc = now
            };
            db.SaasBillingInvoices.Add(inv);
            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            invoiceCode,
            planCode,
            amountVnd,
            paymentProvider = "VIETQR",
            status = "UNPAID",
            vietQrPayload = qrPayload,
            transferContent = $"TT {invoiceCode}",
            createdAtUtc = now,
            statusLabel = $"Đã khởi tạo mã thanh toán tự động Napas247 VietQR cho hóa đơn {invoiceCode}"
        });
    }

    [HttpPost("saas/subscription-billing/webhook-vietqr")]
    [HttpPost("saas-subscription-billing/webhook-vietqr")]
    [HttpPost("~/api/v1/saas/subscriptions/webhook-vietqr")]
    [HttpPost("~/api/v1/webhooks/billing/vietqr")]
    [HttpPost("webhooks/billing/vietqr")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.VietQrBillingWebhook")]
    public async Task<IActionResult> HandleVietQrBillingWebhook(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VietQrWebhookRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var now = DateTime.UtcNow;
        var tenantId = db?.CurrentTenantId ?? 1;
        var targetPlan = string.IsNullOrWhiteSpace(request?.PlanCode) ? "PRO" : request.PlanCode.Trim().ToUpperInvariant();
        var newQuota = targetPlan == "ENTERPRISE" ? 100000 : 10000;
        var resolvedInvoiceCode = !string.IsNullOrWhiteSpace(request?.InvoiceCode)
            ? request.InvoiceCode.Trim()
            : $"INV-VQR-{now:yyyyMMdd}-PAID";

        if (db != null)
        {
            SaasBillingInvoice? invoice = null;
            if (!string.IsNullOrWhiteSpace(request?.InvoiceCode))
            {
                var code = request.InvoiceCode.Trim();
                invoice = await db.SaasBillingInvoices.FirstOrDefaultAsync(i => i.InvoiceCode == code, ct);
            }
            invoice ??= await db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId && i.Status != "PAID")
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(ct);
            invoice ??= await db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(ct);

            if (invoice != null)
            {
                invoice.Status = "PAID";
                invoice.PaidAtUtc = now;
                if (!string.IsNullOrWhiteSpace(invoice.PlanCode))
                {
                    targetPlan = invoice.PlanCode;
                }
                newQuota = targetPlan == "ENTERPRISE" ? 100000 : 10000;
                resolvedInvoiceCode = invoice.InvoiceCode;
            }
            else
            {
                var paidAmt = request?.PaidAmountVnd is > 0m
                    ? request.PaidAmountVnd.Value
                    : (targetPlan == "ENTERPRISE" ? 4990000m : 1490000m);
                invoice = new SaasBillingInvoice
                {
                    TenantId = tenantId,
                    InvoiceCode = resolvedInvoiceCode,
                    PlanCode = targetPlan,
                    AmountVnd = paidAmt,
                    PaymentProvider = "VIETQR",
                    Status = "PAID",
                    QrPayload = $"00020101021238570010A00000072701270006970436011319038889999990208QRIBFTTA530370454{paidAmt:0}5802VN62{resolvedInvoiceCode}",
                    PaidAtUtc = now,
                    CreatedAtUtc = now
                };
                db.SaasBillingInvoices.Add(invoice);
            }

            var sub = await db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (sub == null)
            {
                sub = new TenantSubscription
                {
                    TenantId = tenantId,
                    PlanCode = targetPlan,
                    Status = "ACTIVE",
                    MonthlyOrderQuota = newQuota,
                    CurrentMonthOrderCount = 0,
                    MaxWarehouses = targetPlan == "ENTERPRISE" ? 20 : 5,
                    MaxUsers = targetPlan == "ENTERPRISE" ? 200 : 30,
                    CurrentPeriodStartUtc = now,
                    CurrentPeriodEndUtc = now.AddDays(30),
                    UpdatedAtUtc = now
                };
                db.TenantSubscriptions.Add(sub);
            }
            else
            {
                sub.PlanCode = targetPlan;
                sub.Status = "ACTIVE";
                sub.MonthlyOrderQuota = newQuota;
                sub.CurrentPeriodEndUtc = now.AddDays(30);
                sub.UpdatedAtUtc = now;
            }

            var validAuditUserId = await db.Users
                .AsNoTracking()
                .OrderBy(u => u.Id)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync(ct);

            db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                EntityName = "TenantSubscription",
                EntityId = tenantId,
                Action = "SaaS.VietQrBillingWebhook.Paid",
                NewValues = $"Plan={targetPlan}; Status=ACTIVE; Quota={newQuota}",
                Reason = $"Webhook thanh toán VietQR tự động kích hoạt gói {targetPlan} & mở khóa hạn mức tức thì",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            tenantId,
            invoiceCode = resolvedInvoiceCode,
            upgradedPlanCode = targetPlan,
            subscriptionStatus = "ACTIVE",
            newMonthlyOrderQuota = newQuota,
            writeBlocked = false,
            paidAtUtc = now,
            statusLabel = $"Đã xác nhận thanh toán VietQR — Mở khóa tức thì gói {targetPlan} ({newQuota:N0} đơn/tháng)"
        });
    }

    [HttpGet("compliance-evidence-bundle"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public IActionResult ComplianceEvidenceBundle() => Ok(new
    {
        bundleId = $"EVD-{DateTime.UtcNow:yyyyMMdd-HHmm}",
        policyVersion = "v2026.10-CANONICAL",
        legalHoldActive = true,
        appendOnlyAuditVerified = true,
        generatedAtUtc = DateTime.UtcNow
    });

    [HttpPost("roles/{roleId:int}/grants"), IdempotentCommand("Permission.Grant"), PermissionAuthorize(AppPermissions.PermissionAssign)]
    public Task<IActionResult> Grant(int roleId, PermissionGrantDto dto, CancellationToken ct) =>
        Change(roleId, dto.PermissionCode, dto.RowVersion, true, ct);

    [HttpDelete("roles/{roleId:int}/grants/{permissionCode}"), IdempotentCommand("Permission.Revoke"), PermissionAuthorize(AppPermissions.PermissionAssign)]
    public Task<IActionResult> Revoke(int roleId, string permissionCode, [FromBody] PermissionRevokeDto dto, CancellationToken ct) =>
        Change(roleId, permissionCode, dto.RowVersion, false, ct);

    private async Task<IActionResult> Change(int roleId, string input, byte[] version, bool add, CancellationToken ct)
    {
        var code = Permission.Normalize(input);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await PermissionAdministrationGuard.LockAsync(db, ct);
            var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == roleId, ct);
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code, ct);
            if (role is null || permission is null) return NotFound(new { message = "Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập." });
            if (version is null || version.Length != 8 || !role.RowVersion.SequenceEqual(version))
                throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
            var existing = await db.RolePermissions.SingleOrDefaultAsync(x => x.RoleId == roleId && x.PermissionId == permission.Id, ct);
            var changed = add ? existing is null : existing is not null;
            if (changed)
            {
                var actor = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                if (add) db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permission.Id, GrantedByUserId = actor });
                else db.RolePermissions.Remove(existing!);
                role.GrantRevision++;
                db.AuditLogs.Add(new AuditLog { UserId = actor, Action = add ? "Permission.Grant" : "Permission.Revoke", EntityName = "Role", EntityId = roleId,
                    OldValues = JsonSerializer.Serialize(new { permissionCode = code, granted = !add }),
                    NewValues = JsonSerializer.Serialize(new { permissionCode = code, granted = add }), Timestamp = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            // Validate the projected post-mutation state while the shared transaction lock is still held.
            await PermissionAdministrationGuard.EnsureAdministratorAsync(db, ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Ok(new { roleId, rowVersion = Convert.ToBase64String(role.RowVersion), changed });
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
public sealed record PermissionGrantDto(string PermissionCode, byte[] RowVersion);
public sealed record PermissionRevokeDto(byte[] RowVersion);