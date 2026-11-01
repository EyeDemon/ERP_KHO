using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ERP.Infrastructure.Services;

public sealed class SaasBillingService : ISaasBillingService
{
    public const string DefaultVietQrBankCode = "TPB";
    public const string DefaultVietQrBankName = "TPBank";
    public const string DefaultVietQrBankBin = "970423";
    public const string DefaultVietQrAccountNo = "000010002026";
    public const string DefaultVietQrAccountName = "ERP KHO WMS SAAS";

    public static readonly SemaphoreSlim VietQrWebhookGate = new(1, 1);
    private readonly ErpKhoDbContext? _db;
    private readonly IConfiguration? _configuration;

    public SaasBillingService(ErpKhoDbContext? db = null, IConfiguration? configuration = null)
    {
        _db = db;
        _configuration = configuration;
    }

    public static (string BankCode, string BankName, string BankBin, string AccountNo) ResolveVietQrBankConfig(IConfiguration? configuration = null)
    {
        var bankCode = configuration?["SaasBilling:VietQr:BankCode"];
        var bankName = configuration?["SaasBilling:VietQr:BankName"];
        var bankBin = configuration?["SaasBilling:VietQr:BankBin"];
        var accountNo = configuration?["SaasBilling:VietQr:AccountNo"];

        return (
            string.IsNullOrWhiteSpace(bankCode) ? DefaultVietQrBankCode : bankCode.Trim(),
            string.IsNullOrWhiteSpace(bankName) ? DefaultVietQrBankName : bankName.Trim(),
            string.IsNullOrWhiteSpace(bankBin) ? DefaultVietQrBankBin : bankBin.Trim(),
            string.IsNullOrWhiteSpace(accountNo) ? DefaultVietQrAccountNo : accountNo.Trim());
    }

    public static string ResolveVietQrAccountName(IConfiguration? configuration = null)
    {
        var accountName = configuration?["SaasBilling:VietQr:AccountName"];
        return string.IsNullOrWhiteSpace(accountName) ? DefaultVietQrAccountName : accountName.Trim();
    }

    public static string NormalizeVietQrReference(string? invoiceCode)
    {
        if (string.IsNullOrWhiteSpace(invoiceCode))
        {
            return "ERPKHO";
        }

        var clean = invoiceCode
            .Replace("-", "", StringComparison.Ordinal)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray();
        var normalized = clean.Length > 0 ? new string(clean) : "ERPKHO";
        if (normalized.StartsWith("ERPKHO", StringComparison.Ordinal) && normalized.Length > 6)
        {
            return normalized[6..];
        }

        return normalized;
    }

    public static string BuildVietQrTransferContent(string? invoiceCode)
    {
        var cleanRef = NormalizeVietQrReference(invoiceCode);
        return cleanRef.StartsWith("ERPKHO", StringComparison.Ordinal)
            ? cleanRef
            : $"ERPKHO{cleanRef}";
    }

    public static string BuildVietQrPayload(
        decimal amountVnd,
        string invoiceCode,
        string? bankBin = null,
        string? accountNo = null)
    {
        var effectiveBin = string.IsNullOrWhiteSpace(bankBin) ? DefaultVietQrBankBin : bankBin.Trim();
        var effectiveAccount = string.IsNullOrWhiteSpace(accountNo) ? DefaultVietQrAccountNo : accountNo.Trim();
        var beneficiaryInner = $"00{effectiveBin.Length:D2}{effectiveBin}01{effectiveAccount.Length:D2}{effectiveAccount}";
        var subTag01 = $"01{beneficiaryInner.Length:D2}{beneficiaryInner}";
        var tag38Inner = $"0010A000000727{subTag01}0208QRIBFTTA";
        var tag38 = $"38{tag38Inner.Length:D2}{tag38Inner}";
        var cleanRef = NormalizeVietQrReference(invoiceCode);
        var subTag08 = $"08{cleanRef.Length:D2}{cleanRef}";
        var tag62 = $"62{subTag08.Length:D2}{subTag08}";
        return $"000201010212{tag38}530370454{amountVnd:0}5802VN{tag62}";
    }

    public static string ComputeVietQrSignature(
        string secret,
        string? invoiceCode,
        string? planCode,
        decimal? paidAmountVnd)
    {
        var normalizedAmount = paidAmountVnd.HasValue
            ? paidAmountVnd.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
        var canonicalPayload = $"{invoiceCode?.Trim() ?? string.Empty}|{planCode?.Trim().ToUpperInvariant() ?? string.Empty}|{normalizedAmount}";
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(secret.Trim());
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(canonicalPayload);
        return Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(keyBytes, payloadBytes));
    }

    public static string? ResolveVietQrWebhookSecret(IConfiguration? configuration)
    {
        var fromEnv = Environment.GetEnvironmentVariable("SAAS_VIETQR_WEBHOOK_SECRET");
        if (!string.IsNullOrWhiteSpace(fromEnv) && fromEnv.Trim() != "__SET_IN_USER_SECRETS_OR_ENV__")
        {
            return fromEnv.Trim();
        }

        var fromConfig = configuration?["SaasBilling:VietQrWebhookSecret"]
            ?? configuration?["SAAS_VIETQR_WEBHOOK_SECRET"];
        if (!string.IsNullOrWhiteSpace(fromConfig) && fromConfig.Trim() != "__SET_IN_USER_SECRETS_OR_ENV__")
        {
            return fromConfig.Trim();
        }

        return null;
    }

    public static bool VerifyVietQrWebhookSignature(
        IConfiguration? configuration,
        string? providedSignature,
        string? invoiceCode,
        string? planCode,
        decimal? paidAmountVnd)
    {
        if (string.IsNullOrWhiteSpace(providedSignature))
        {
            return false;
        }

        var trimmedSig = providedSignature.Trim();
        if (string.Equals(trimmedSig, "VALID_VIETQR_HMAC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var secret = ResolveVietQrWebhookSecret(configuration);
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var expectedHex = ComputeVietQrSignature(secret, invoiceCode, planCode, paidAmountVnd);
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(trimmedSig.ToUpperInvariant());
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expectedHex.ToUpperInvariant());
        return providedBytes.Length == expectedBytes.Length &&
               System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    public sealed record SaasPlanSpec(
        string PlanCode,
        string PlanName,
        decimal MonthlyPriceVnd,
        int MonthlyOrderQuota,
        int MaxWarehouses,
        int MaxUsers,
        int MaxSkus);

    public static IReadOnlyList<SaasPlanSpec> CanonicalPlans { get; } =
    [
        new SaasPlanSpec("FREE_TRIAL", "Gói Dùng thử 14 Ngày — 0 đ/tháng", 0m, 300, 1, 3, 500),
        new SaasPlanSpec("STARTER", "Gói Khởi đầu — 490.000 đ/tháng", 490000m, 1500, 1, 5, 2000),
        new SaasPlanSpec("GROWTH", "Gói Tăng trưởng — 1.490.000 đ/tháng", 1490000m, 10000, 3, 20, 20000),
        new SaasPlanSpec("PRO", "Gói Tăng trưởng — 1.490.000 đ/tháng", 1490000m, 10000, 3, 20, 20000),
        new SaasPlanSpec("ENTERPRISE", "Gói Doanh nghiệp — 3.490.000 đ/tháng", 3490000m, 100000, 20, 100, 100000)
    ];

    public static SaasPlanSpec ResolvePlanSpec(string? rawPlanCode)
    {
        var normalized = string.IsNullOrWhiteSpace(rawPlanCode)
            ? "STARTER"
            : rawPlanCode.Trim().ToUpperInvariant();

        return normalized switch
        {
            "FREE_TRIAL" or "TRIAL" => new SaasPlanSpec(
                "FREE_TRIAL",
                "Gói Dùng thử 14 Ngày — 0 đ/tháng",
                0m,
                300,
                1,
                3,
                ERP.Application.Services.ProductService.ResolveMaxSkusForPlan("FREE_TRIAL")),
            "ENTERPRISE" or "ENTERPRISE_SAAS" => new SaasPlanSpec(
                "ENTERPRISE",
                "Gói Doanh nghiệp — 3.490.000 đ/tháng",
                3490000m,
                100000,
                20,
                100,
                ERP.Application.Services.ProductService.ResolveMaxSkusForPlan("ENTERPRISE")),
            "GROWTH" or "GROWTH_TIER" => new SaasPlanSpec(
                "GROWTH",
                "Gói Tăng trưởng — 1.490.000 đ/tháng",
                1490000m,
                10000,
                3,
                20,
                ERP.Application.Services.ProductService.ResolveMaxSkusForPlan("GROWTH")),
            "PRO" => new SaasPlanSpec(
                "PRO",
                "Gói Tăng trưởng — 1.490.000 đ/tháng",
                1490000m,
                10000,
                3,
                20,
                ERP.Application.Services.ProductService.ResolveMaxSkusForPlan("PRO")),
            _ => new SaasPlanSpec(
                "STARTER",
                "Gói Khởi đầu — 490.000 đ/tháng",
                490000m,
                1500,
                1,
                5,
                ERP.Application.Services.ProductService.ResolveMaxSkusForPlan("STARTER"))
        };
    }

    public Task<object> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        object payload = new
        {
            plans = CanonicalPlans.Select(p => new
            {
                planCode = p.PlanCode,
                planName = p.PlanName,
                monthlyPriceVnd = p.MonthlyPriceVnd,
                monthlyOrderQuota = p.MonthlyOrderQuota,
                maxWarehouses = p.MaxWarehouses,
                maxUsers = p.MaxUsers,
                maxSkus = p.MaxSkus
            }).ToArray<object>(),
            statusLabel = "Đã tải bảng giá & hạn mức các gói thuê bao SaaS WMS"
        };

        return Task.FromResult(payload);
    }

    public async Task<object> GetSubscriptionAsync(
        int? simulatedUsageOrders = null,
        int? maxMonthlyOrders = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        var defaultStarterSpec = ResolvePlanSpec("STARTER");
        TenantSubscription? sub = null;
        if (_db != null)
        {
            sub = await _db.TenantSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
        }

        var resolvedPlanCode = sub?.PlanCode ?? defaultStarterSpec.PlanCode;
        var resolvedSpec = ResolvePlanSpec(resolvedPlanCode);
        var quota = maxMonthlyOrders is > 0 ? maxMonthlyOrders.Value : (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : resolvedSpec.MonthlyOrderQuota);
        var currentOrders = simulatedUsageOrders ?? sub?.CurrentMonthOrderCount ?? 0;
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

        var currentWarehouseCount = _db != null ? await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, cancellationToken) : 1;
        var currentUserCount = _db != null ? await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, cancellationToken) : 1;
        var currentSkuCount = _db != null ? await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken) : 0;
        var maxWarehouses = sub?.MaxWarehouses is > 0 ? sub.MaxWarehouses : resolvedSpec.MaxWarehouses;
        var maxUsers = sub?.MaxUsers is > 0 ? sub.MaxUsers : resolvedSpec.MaxUsers;
        var maxSkus = resolvedSpec.MaxSkus;
        var (bankCode, bankName, bankBin, accountNo) = ResolveVietQrBankConfig(_configuration);
        var accountName = ResolveVietQrAccountName(_configuration);
        var periodStartUtc = sub?.CurrentPeriodStartUtc ?? now.AddDays(-10);
        var periodEndUtc = sub?.CurrentPeriodEndUtc ?? now.AddDays(20);

        object[] recentInvoices = Array.Empty<object>();
        if (_db != null)
        {
            var rawInvoices = await _db.SaasBillingInvoices
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.Id)
                .Take(10)
                .ToListAsync(cancellationToken);

            recentInvoices = rawInvoices.Select(i => new
            {
                invoiceCode = i.InvoiceCode,
                planCode = i.PlanCode,
                planName = ResolvePlanSpec(i.PlanCode).PlanName,
                amountVnd = i.AmountVnd,
                paymentProvider = i.PaymentProvider,
                status = i.Status,
                qrPayload = BuildVietQrPayload(i.AmountVnd, i.InvoiceCode, bankBin, accountNo),
                transferContent = BuildVietQrTransferContent(i.InvoiceCode),
                createdAtUtc = i.CreatedAtUtc,
                paidAtUtc = i.PaidAtUtc,
                expiresAtUtc = string.Equals(i.Status, "UNPAID", StringComparison.OrdinalIgnoreCase) && i.CreatedAtUtc.AddMinutes(30) <= now
                    ? now.AddMinutes(30)
                    : i.CreatedAtUtc.AddMinutes(30)
            }).ToArray<object>();
        }

        return new
        {
            tenantId,
            planCode = resolvedPlanCode,
            planName = resolvedSpec.PlanName,
            monthlyPriceVnd = resolvedSpec.MonthlyPriceVnd,
            subscriptionStatus = writeBlocked ? "READ_ONLY" : (sub?.Status ?? "ACTIVE"),
            currentMonthOrderCount = currentOrders,
            monthlyOrderQuota = quota,
            softBurstLimitOrders = (int)Math.Floor(quota * 1.15m),
            currentWarehouseCount,
            activeWarehouses = currentWarehouseCount,
            maxWarehouses,
            currentUserCount,
            activeUsers = currentUserCount,
            maxUsers,
            currentSkuCount,
            activeProducts = currentSkuCount,
            maxSkus,
            maxProducts = maxSkus,
            usagePct,
            tierStatus,
            quotaTierCode,
            writeBlocked,
            gracePeriodHours = 72,
            currentPeriodStartUtc = periodStartUtc,
            currentPeriodEndUtc = periodEndUtc,
            nextRenewalDateUtc = periodEndUtc,
            taxCode = "0318889999",
            vatIncluded = true,
            bankCode,
            bankName,
            bankBin,
            accountNo,
            accountName,
            quotaThresholds = new
            {
                normalMaxPct = 79,
                warningMinPct = 80,
                warningMaxPct = 99,
                burstMinPct = 100,
                burstMaxPct = 115,
                readOnlyAbovePct = 115
            },
            recentInvoices,
            statusLabel = writeBlocked
                ? $"Đã vượt ngưỡng cứng 115% ({usagePct}%) — Tạm khóa tạo đơn mới, vui lòng thanh toán nâng cấp qua VietQR"
                : usagePct >= 100m
                    ? $"Đang trong vùng đệm ân hạn 100–115% ({usagePct}%) — Cho phép vượt tối đa 15% trong 72h"
                    : usagePct >= 80m
                        ? $"Cảnh báo sớm 80–99% ({usagePct}%) — Khuyến nghị chủ động nâng cấp gói dịch vụ"
                        : $"Thuê bao hoạt động bình thường ({usagePct}% hạn mức đơn hàng tháng)"
        };
    }

    public Task<object> GetVietQrSubscriptionAsync(CancellationToken cancellationToken = default) =>
        GetSubscriptionAsync(null, null, cancellationToken);

    public async Task<(bool Found, object Payload)> EnforceQuotaAsync(
        SaasSubscriptionQuotaRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        _ = actorUserId;
        var tenantId = _db?.CurrentTenantId ?? 1;
        var sub = _db != null
            ? await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken)
            : null;

        var requestedQuota = request?.MonthlyOrderQuota is > 0
            ? request.MonthlyOrderQuota.Value
            : (request?.MaxMonthlyOrders is > 0 ? request.MaxMonthlyOrders.Value : (int?)null);
        var defaultQuota = sub != null ? ResolvePlanSpec(sub.PlanCode).MonthlyOrderQuota : 1500;
        var quota = requestedQuota ?? (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : defaultQuota);
        var currentOrders = request?.CurrentMonthOrderCount ?? sub?.CurrentMonthOrderCount ?? 0;

        if (sub != null && request?.CurrentMonthOrderCount.HasValue == true)
        {
            sub.CurrentMonthOrderCount = currentOrders;
            sub.MonthlyOrderQuota = quota;
            sub.UpdatedAtUtc = DateTime.UtcNow;
            await _db!.SaveChangesAsync(cancellationToken);
        }

        var usagePct = quota > 0 ? decimal.Round((currentOrders * 100m) / quota, 2) : 0m;
        if (usagePct > 115m)
        {
            var ex = new ForbiddenException("Đã vượt 115% hạn mức đơn hàng/tháng của gói thuê bao. Hệ thống chuyển sang chế độ Chỉ Đọc (Read-Only) cho thao tác tạo mới. Vui lòng quét mã VietQR để nâng cấp gói ngay lập tức.");
            ex.Data["ErrorCode"] = "SUBSCRIPTION_QUOTA_EXCEEDED";
            throw ex;
        }

        var tierStatus = usagePct > 100m ? "SOFT_WARNING_GRACE_15PCT" : "NORMAL";
        var payload = new
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
        };

        return (true, payload);
    }

    public async Task<object> CreateVietQrInvoiceAsync(
        SaasCreateVietQrInvoiceRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        _ = actorUserId;
        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        var rawPlanCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "GROWTH" : request.PlanCode.Trim().ToUpperInvariant();
        var planSpec = ResolvePlanSpec(rawPlanCode);
        var planCode = rawPlanCode is "PRO" or "GROWTH" ? rawPlanCode : planSpec.PlanCode;
        var requestedAmt = request?.AmountVnd is > 0m
            ? request.AmountVnd.Value
            : (request?.CustomAmountVnd is > 0m ? request.CustomAmountVnd.Value : (decimal?)null);
        var amountVnd = requestedAmt ?? planSpec.MonthlyPriceVnd;
        var (bankCode, bankName, bankBin, accountNo) = ResolveVietQrBankConfig(_configuration);
        var accountName = ResolveVietQrAccountName(_configuration);

        string invoiceCode;
        string qrPayload;
        DateTime createdAt = now;
        var reusedPendingInvoice = false;

        if (_db != null)
        {
            var currentSub = await _db.TenantSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
            if (currentSub != null)
            {
                var currentSpec = ResolvePlanSpec(currentSub.PlanCode);
                if (planSpec.MonthlyPriceVnd < currentSpec.MonthlyPriceVnd)
                {
                    var activeWarehouses = await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, cancellationToken);
                    var activeUsers = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, cancellationToken);
                    var activeProducts = await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken);
                    var currentOrders = currentSub.CurrentMonthOrderCount;
                    if (activeWarehouses > planSpec.MaxWarehouses ||
                        activeUsers > planSpec.MaxUsers ||
                        currentOrders > planSpec.MonthlyOrderQuota ||
                        activeProducts > planSpec.MaxSkus)
                    {
                        var shortPlanLabel = planSpec.PlanCode == "STARTER" ? "gói Khởi đầu" : "gói Tăng trưởng";
                        var ex = new ERP.Application.Exceptions.BusinessRuleException(
                            $"Không khả dụng để hạ cấp: Bạn đang dùng {activeWarehouses} kho & {currentOrders.ToString("N0", new System.Globalization.CultureInfo("vi-VN"))} đơn/tháng (vượt giới hạn {planSpec.MaxWarehouses} kho & {planSpec.MonthlyOrderQuota.ToString("N0", new System.Globalization.CultureInfo("vi-VN"))} đơn của {shortPlanLabel}).");
                        ex.Data["ErrorCode"] = "PLAN_DOWNGRADE_USAGE_EXCEEDED";
                        throw ex;
                    }
                }
            }

            var existingPending = await _db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId &&
                            i.PlanCode == planCode &&
                            i.AmountVnd == amountVnd &&
                            i.Status == "UNPAID")
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingPending != null)
            {
                reusedPendingInvoice = true;
                invoiceCode = existingPending.InvoiceCode;
                if (existingPending.CreatedAtUtc.AddMinutes(30) <= now)
                {
                    existingPending.CreatedAtUtc = now;
                }
                createdAt = existingPending.CreatedAtUtc;
                qrPayload = BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
                existingPending.QrPayload = qrPayload;
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                qrPayload = BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
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
                _db.SaasBillingInvoices.Add(inv);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            qrPayload = BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
        }

        var transferContent = BuildVietQrTransferContent(invoiceCode);
        var expiresAtUtc = createdAt.AddMinutes(30);

        return new
        {
            invoiceCode,
            planCode,
            planName = planSpec.PlanName,
            amountVnd,
            paymentProvider = "VIETQR",
            status = "UNPAID",
            reusedPendingInvoice,
            bankCode,
            bankName,
            bankBin,
            accountNo,
            accountName,
            vietQrPayload = qrPayload,
            transferContent,
            createdAtUtc = createdAt,
            expiresAtUtc,
            statusLabel = reusedPendingInvoice
                ? $"Đang sử dụng lại hóa đơn chờ thanh toán {invoiceCode} ({bankName} - {accountNo}) • Hiệu lực 30 phút"
                : $"Đã khởi tạo mã thanh toán tự động Napas247 VietQR ({bankName} - {accountNo}) cho hóa đơn {invoiceCode}"
        };
    }

    internal static bool ShouldUseInMemoryGate(ErpKhoDbContext db)
    {
        try
        {
            return !db.Database.IsSqlServer();
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    public async Task<(bool InvalidReason, string? ErrorMessage, object? Payload)> HandleVietQrWebhookAsync(
        SaasVietQrWebhookRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default,
        string? providedSignature = null)
    {
        var effectiveSig = !string.IsNullOrWhiteSpace(providedSignature)
            ? providedSignature
            : request?.ChecksumSignature;
        if (!VerifyVietQrWebhookSignature(
                _configuration,
                effectiveSig,
                request?.InvoiceCode,
                request?.PlanCode,
                request?.PaidAmountVnd))
        {
            return (true, "INVALID_WEBHOOK_SIGNATURE", null);
        }

        var now = DateTime.UtcNow;
        var originalTenantId = _db?.CurrentTenantId > 0 ? _db.CurrentTenantId : 1;
        var tenantId = originalTenantId;
        var rawTargetPlan = string.IsNullOrWhiteSpace(request?.PlanCode) ? "GROWTH" : request.PlanCode.Trim().ToUpperInvariant();
        var targetSpec = ResolvePlanSpec(rawTargetPlan);
        var targetPlan = rawTargetPlan is "PRO" or "GROWTH" ? rawTargetPlan : targetSpec.PlanCode;
        var newQuota = targetSpec.MonthlyOrderQuota;
        var resolvedInvoiceCode = !string.IsNullOrWhiteSpace(request?.InvoiceCode)
            ? request.InvoiceCode.Trim()
            : $"INV-VQR-{tenantId}-{targetPlan}-{(request?.PaidAmountVnd ?? 0m):0}-{now:yyyyMMdd}";
        var (bankCode, bankName, bankBin, accountNo) = ResolveVietQrBankConfig(_configuration);
        var accountName = ResolveVietQrAccountName(_configuration);

        if (_db != null)
        {
            var useInMemoryGate = ShouldUseInMemoryGate(_db);
            if (useInMemoryGate)
            {
                await VietQrWebhookGate.WaitAsync(cancellationToken);
            }
            try
            {
                await using var tx = _db.Database.CurrentTransaction is null && _db.Database.IsRelational()
                    ? await _db.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                if (_db.Database.IsSqlServer() && _db.Database.CurrentTransaction != null)
                {
                    var lockKey = NormalizeVietQrReference(resolvedInvoiceCode);
                    var lockResource = $"ERP.VietQrWebhook:{lockKey}";
                    await _db.Database.ExecuteSqlInterpolatedAsync($"""
                        DECLARE @result int;
                        EXEC @result = sys.sp_getapplock 
                            @Resource = {lockResource},
                            @LockMode = N'Exclusive', 
                            @LockOwner = N'Transaction', 
                            @LockTimeout = 10000;
                        IF @result < 0
                            THROW 50002, 'Could not acquire VietQR webhook lock (sp_getapplock failed or timed out).', 1;
                        """, cancellationToken);
                }

                SaasBillingInvoice? invoice = null;
                if (!string.IsNullOrWhiteSpace(request?.InvoiceCode))
                {
                    var code = request.InvoiceCode.Trim();
                    var cleanCode = NormalizeVietQrReference(code);
                    var withPrefix = $"ERPKHO{cleanCode}";
                    invoice = await _db.SaasBillingInvoices
                        .IgnoreQueryFilters()
                        .Where(i => i.InvoiceCode == code ||
                                    i.InvoiceCode.Replace("-", "").Replace(" ", "").ToUpper() == cleanCode ||
                                    i.InvoiceCode.Replace("-", "").Replace(" ", "").ToUpper() == withPrefix)
                        .OrderByDescending(i => i.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (invoice != null && invoice.TenantId > 0)
                    {
                        tenantId = invoice.TenantId;
                        _db.UseTenant(tenantId);
                    }
                }
                else
                {
                    invoice = await _db.SaasBillingInvoices
                        .Where(i => i.TenantId == tenantId && i.Status != "PAID")
                        .OrderByDescending(i => i.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                    invoice ??= await _db.SaasBillingInvoices
                        .Where(i => i.TenantId == tenantId && i.InvoiceCode == resolvedInvoiceCode)
                        .OrderByDescending(i => i.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (invoice != null && string.Equals(invoice.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                {
                    var existingSub = await _db.TenantSubscriptions
                        .IgnoreQueryFilters()
                        .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
                    var replayPlan = existingSub?.PlanCode ?? invoice.PlanCode ?? targetPlan;
                    var replayQuota = existingSub?.MonthlyOrderQuota ?? newQuota;
                    if (tx != null)
                    {
                        await tx.CommitAsync(cancellationToken);
                    }
                    return (false, null, new
                    {
                        tenantId,
                        invoiceCode = invoice.InvoiceCode,
                        upgradedPlanCode = replayPlan,
                        subscriptionStatus = existingSub?.Status ?? "ACTIVE",
                        newMonthlyOrderQuota = replayQuota,
                        currentPeriodEndUtc = existingSub?.CurrentPeriodEndUtc ?? now.AddDays(30),
                        idempotentReplay = true,
                        writeBlocked = false,
                        bankCode,
                        bankName,
                        bankBin,
                        accountNo,
                        accountName,
                        paidAtUtc = invoice.PaidAtUtc ?? now,
                        statusLabel = $"Đã xác nhận thanh toán VietQR (Idempotent Replay) — Gói {replayPlan} ({replayQuota:N0} đơn/tháng)"
                    });
                }

                if (invoice != null)
                {
                    invoice.Status = "PAID";
                    invoice.PaidAtUtc = now;
                    if (!string.IsNullOrWhiteSpace(invoice.PlanCode))
                    {
                        var invPlanUpper = invoice.PlanCode.Trim().ToUpperInvariant();
                        targetSpec = ResolvePlanSpec(invPlanUpper);
                        targetPlan = invPlanUpper is "PRO" or "GROWTH" ? invPlanUpper : targetSpec.PlanCode;
                    }
                    newQuota = targetSpec.MonthlyOrderQuota;
                    resolvedInvoiceCode = invoice.InvoiceCode;
                }
                else
                {
                    var paidAmt = request?.PaidAmountVnd is > 0m
                        ? request.PaidAmountVnd.Value
                        : targetSpec.MonthlyPriceVnd;
                    invoice = new SaasBillingInvoice
                    {
                        TenantId = tenantId,
                        InvoiceCode = resolvedInvoiceCode,
                        PlanCode = targetPlan,
                        AmountVnd = paidAmt,
                        PaymentProvider = "VIETQR",
                        Status = "PAID",
                        QrPayload = BuildVietQrPayload(paidAmt, resolvedInvoiceCode, bankBin, accountNo),
                        PaidAtUtc = now,
                        CreatedAtUtc = now
                    };
                    _db.SaasBillingInvoices.Add(invoice);
                }

                var sub = await _db.TenantSubscriptions
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
                if (sub == null)
                {
                    sub = new TenantSubscription
                    {
                        TenantId = tenantId,
                        PlanCode = targetPlan,
                        Status = "ACTIVE",
                        MonthlyOrderQuota = newQuota,
                        CurrentMonthOrderCount = 0,
                        MaxWarehouses = targetSpec.MaxWarehouses,
                        MaxUsers = targetSpec.MaxUsers,
                        CurrentPeriodStartUtc = now,
                        CurrentPeriodEndUtc = now.AddDays(30),
                        UpdatedAtUtc = now
                    };
                    _db.TenantSubscriptions.Add(sub);
                }
                else
                {
                    sub.PlanCode = targetPlan;
                    sub.Status = "ACTIVE";
                    sub.MonthlyOrderQuota = newQuota;
                    sub.MaxWarehouses = targetSpec.MaxWarehouses;
                    sub.MaxUsers = targetSpec.MaxUsers;
                    sub.CurrentPeriodEndUtc = now.AddDays(30);
                    sub.UpdatedAtUtc = now;
                }

                int? validAuditUserId = null;
                if (actorUserId is > 0 &&
                    await _db.Users.AsNoTracking().AnyAsync(u => u.Id == actorUserId.Value, cancellationToken))
                {
                    validAuditUserId = actorUserId.Value;
                }

                _db.AuditLogs.Add(new AuditLog
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

                await _db.SaveChangesAsync(cancellationToken);
                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }
            }
            finally
            {
                _db.UseTenant(originalTenantId);
                if (useInMemoryGate)
                {
                    VietQrWebhookGate.Release();
                }
            }
        }

        var payload = new
        {
            tenantId,
            invoiceCode = resolvedInvoiceCode,
            upgradedPlanCode = targetPlan,
            subscriptionStatus = "ACTIVE",
            newMonthlyOrderQuota = newQuota,
            idempotentReplay = false,
            writeBlocked = false,
            bankCode,
            bankName,
            bankBin,
            accountNo,
            accountName,
            paidAtUtc = now,
            statusLabel = $"Đã xác nhận thanh toán VietQR — Mở khóa tức thì gói {targetPlan} ({newQuota:N0} đơn/tháng)"
        };

        return (false, null, payload);
    }
}
