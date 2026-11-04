using ERP.Application.Common;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PermissionOperationsService
{
    public async Task<WmsEndpointOutcome> GetSaaSSetupWizardAsync(CancellationToken ct = default)
    {
        var hasWarehouse = _db == null || await _db.Warehouses.AsNoTracking().AnyAsync(w => w.IsActive, ct);
        var hasLocations = _db == null || await _db.WarehouseLocations.AsNoTracking().AnyAsync(l => l.IsActive, ct);
        var hasProducts = _db == null || await _db.Products.AsNoTracking().AnyAsync(p => p.IsActive, ct);
        var hasOpeningStock = _db == null || await _db.InventoryStocks.AsNoTracking().AnyAsync(s => s.Quantity > 0m, ct);
        var hasMarketplaceShop = _db != null && await _db.MarketplaceShops.AsNoTracking().AnyAsync(s => s.IsActive, ct);
        var hasFirstThermalAwbOrReady = _db == null ||
            await _db.ExportReceipts.AsNoTracking().AnyAsync(r => r.AwbPrintCount > 0 || r.Status == ReceiptStatus.Dispatched, ct) ||
            await _db.SalesOrders.AsNoTracking().AnyAsync(ct) ||
            (hasOpeningStock && hasMarketplaceShop);

        var steps = new[]
        {
            new { stepNumber = 1, stepCode = "WAREHOUSE_AND_BINS", title = "1. Thiết lập Kho & Kệ Mặc Định", completed = hasWarehouse && hasLocations },
            new { stepNumber = 2, stepCode = "INDUSTRY_SKU_CATALOG", title = "2. Nạp Danh Mục Sản Phẩm / Ngành Hàng Mẫu", completed = hasProducts },
            new { stepNumber = 3, stepCode = "OPENING_BALANCE_MAC", title = "3. Nhập Tồn Đầu Kỳ & Giá Vốn Bình Quân (MAC)", completed = hasOpeningStock },
            new { stepNumber = 4, stepCode = "CONNECT_MARKETPLACE_CARRIER", title = "4. Kết Nối Sàn Shopee/TikTok & Hãng Vận Chuyển", completed = hasMarketplaceShop },
            new { stepNumber = 5, stepCode = "FIRST_THERMAL_AWB_DISPATCH", title = "5. In Thử Vận Đơn Nhiệt 100x150mm & Xuất Bàn Giao", completed = hasFirstThermalAwbOrReady }
        };

        var completedCount = steps.Count(s => s.completed);
        return WmsEndpointOutcome.Ok(new
        {
            totalSteps = steps.Length,
            completedSteps = completedCount,
            completionPct = (completedCount * 100) / steps.Length,
            steps,
            statusLabel = $"Tiến độ hướng dẫn thiết lập nhanh 5 bước cho Chủ shop mới ({completedCount}/{steps.Length} bước hoàn tất)"
        });
    }

    public async Task<WmsEndpointOutcome> SeedSaaSDemoDataAsync(
        SeedDemoDataInput? request = null,
        CancellationToken ct = default)
    {
        var template = string.IsNullOrWhiteSpace(request?.IndustryTemplate)
            ? "COSMETICS_FEFO"
            : request.IndustryTemplate.Trim().ToUpperInvariant();
        var purged = request?.PurgeDemoData == true;

        if (_db != null)
        {
            if (purged)
            {
                var demoProducts = await _db.Products
                    .Where(p => p.Code.StartsWith("DEMO-SKU-"))
                    .ToListAsync(ct);
                foreach (var dp in demoProducts)
                {
                    dp.IsActive = false;
                }
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                var categoryId = await _db.ProductCategories.AsNoTracking().Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
                var unitId = await _db.Units.AsNoTracking().Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
                if (categoryId.HasValue && unitId.HasValue)
                {
                    var demoCode = $"DEMO-SKU-{template}-01";
                    var existingDemo = await _db.Products.FirstOrDefaultAsync(p => p.Code == demoCode, ct);
                    if (existingDemo == null)
                    {
                        _db.Products.Add(new Product
                        {
                            Code = demoCode,
                            Name = $"[DEMO] Sản phẩm mẫu ngành {template}",
                            CategoryId = categoryId.Value,
                            UnitId = unitId.Value,
                            MovingAverageCost = 110000m,
                            TrackingType = template == "COSMETICS_FEFO" ? ProductTrackingType.Lot : ProductTrackingType.None,
                            ExpiryControl = template == "COSMETICS_FEFO",
                            ShelfLifeDays = template == "COSMETICS_FEFO" ? 365 : null,
                            IsActive = true
                        });
                        await _db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        existingDemo.IsActive = true;
                        await _db.SaveChangesAsync(ct);
                    }
                }
            }
        }

        return WmsEndpointOutcome.Ok(new
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

    public async Task<WmsEndpointOutcome> GetOnboardingWizardStatusAsync(CancellationToken ct = default)
    {
        var hasWarehouse = await _db.Warehouses.AsNoTracking().AnyAsync(w => w.IsActive, ct);
        var hasLocations = await _db.WarehouseLocations.AsNoTracking().AnyAsync(l => l.IsActive, ct);
        var hasProducts = await _db.Products.AsNoTracking().AnyAsync(p => p.IsActive, ct);
        var hasStock = await _db.InventoryStocks.AsNoTracking().AnyAsync(s => s.Quantity > 0m, ct);
        var hasShopOrExport = await _db.MarketplaceShops.AsNoTracking().AnyAsync(ct)
                              || await _db.ExportReceipts.AsNoTracking().AnyAsync(ct);

        var step1 = hasWarehouse && hasLocations;
        var step2 = hasProducts;
        var step3 = hasStock;
        var step4 = hasShopOrExport;
        var step5 = step1 && step2 && step3 && step4;

        var completedSteps = (step1 ? 1 : 0) + (step2 ? 1 : 0) + (step3 ? 1 : 0) + (step4 ? 1 : 0) + (step5 ? 1 : 0);
        return WmsEndpointOutcome.Ok(new
        {
            completedSteps,
            totalSteps = 5,
            statusText = completedSteps >= 5 ? "Hoàn tất 5/5 bước chuẩn hóa" : $"Đã hoàn thành {completedSteps}/5 bước thiết lập"
        });
    }

    public async Task<WmsEndpointOutcome> GetSaaSSubscriptionBillingAsync(
        int? simulatedUsageOrders = null,
        int? maxMonthlyOrders = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        TenantSubscription? sub = null;
        if (_db != null)
        {
            sub = await _db.TenantSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        }

        var resolvedPlanCode = sub?.PlanCode ?? "STARTER";
        var planSpec = SaasBillingService.ResolvePlanSpec(resolvedPlanCode);
        var quota = maxMonthlyOrders is > 0 ? maxMonthlyOrders.Value : (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : planSpec.MonthlyOrderQuota);
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

        var rawWarehouseCount = _db != null
            ? await _db.Warehouses.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(w => w.TenantId == tenantId && w.IsActive, ct)
            : 1;
        var currentWarehouseCount = rawWarehouseCount > 0 ? rawWarehouseCount : (tenantId == 1 ? 1 : 0);
        var currentUserCount = _db != null ? await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct) : 1;
        var currentSkuCount = _db != null ? await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct) : 0;
        var maxWarehouses = sub?.MaxWarehouses is > 0 ? sub.MaxWarehouses : planSpec.MaxWarehouses;
        var maxUsers = sub?.MaxUsers is > 0 ? sub.MaxUsers : planSpec.MaxUsers;
        var maxSkus = ERP.Application.Services.ProductService.ResolveMaxSkusForPlan(resolvedPlanCode);
        var (bankCode, bankName, bankBin, accountNo) = SaasBillingService.ResolveVietQrBankConfig(_configuration);
        var accountName = SaasBillingService.ResolveVietQrAccountName(_configuration);
        var (_, beneficiaryAccountName) = SaasBillingService.ResolveConfiguredBeneficiaryAccount(_configuration);
        var periodStartUtc = sub?.CurrentPeriodStartUtc ?? now.AddDays(-10);
        var periodEndUtc = sub?.CurrentPeriodEndUtc ?? now.AddDays(20);

        object[] recentInvoices = [];
        if (_db != null)
        {
            var rawInvoices = await _db.SaasBillingInvoices
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.Id)
                .Take(10)
                .ToListAsync(ct);

            recentInvoices = rawInvoices.Select(i =>
            {
                var (subTotal, vatRate, vatAmt, totalAmt) = SaasBillingService.ComputeVatBreakdownVnd(i.AmountVnd, i.SubTotalVnd, i.VatAmountVnd);
                var billingPeriodLabel = SaasBillingService.ResolveInvoiceBillingPeriodLabel(i.InvoiceCode, i.CreatedAtUtc);
                var (dueDateUtc, dueDateLabel) = SaasBillingService.ResolveInvoiceDueDate(i.InvoiceCode, i.CreatedAtUtc, i.PaidAtUtc);
                var isPaid = string.Equals(i.Status, "PAID", StringComparison.OrdinalIgnoreCase);
                return new
                {
                    id = i.Id,
                    invoiceId = i.Id,
                    invoiceCode = i.InvoiceCode,
                    invoiceNumber = i.InvoiceCode,
                    planCode = i.PlanCode,
                    planName = SaasBillingService.ResolvePlanSpec(i.PlanCode).PlanName,
                    billingPeriodLabel,
                    subTotalVnd = subTotal,
                    preTaxAmountVnd = subTotal,
                    vatRatePercent = vatRate,
                    vatAmountVnd = vatAmt,
                    amountVnd = totalAmt,
                    totalAmountVnd = totalAmt,
                    paymentProvider = i.PaymentProvider,
                    status = i.Status,
                    statusLabelVi = isPaid ? "Đã tất toán VietQR" : "Chờ thanh toán",
                    beneficiaryName = beneficiaryAccountName,
                    qrPayload = SaasBillingService.BuildVietQrPayload(totalAmt, i.InvoiceCode, bankBin, accountNo),
                    transferContent = SaasBillingService.BuildVietQrTransferContent(i.InvoiceCode),
                    createdAtUtc = i.CreatedAtUtc,
                    paidAtUtc = i.PaidAtUtc,
                    dueDateUtc,
                    dueDateLabel,
                    dueLabelVi = isPaid ? "Đã thanh toán" : $"Hạn TT: {dueDateLabel}",
                    expiresAtUtc = string.Equals(i.Status, "UNPAID", StringComparison.OrdinalIgnoreCase) && i.CreatedAtUtc.AddMinutes(30) <= now
                        ? now.AddMinutes(30)
                        : i.CreatedAtUtc.AddMinutes(30)
                };
            }).ToArray<object>();
        }

        var tenantProfile = _db != null
            ? await _db.SaasTenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            : null;
        var resolvedCompanyName = SaasBillingService.NormalizeTenantLegalName(tenantId, tenantProfile?.CompanyName, tenantProfile?.TenantName);

        return WmsEndpointOutcome.Ok(new
        {
            tenantId,
            planCode = resolvedPlanCode,
            planName = planSpec.PlanName,
            monthlyPriceVnd = planSpec.MonthlyPriceVnd,
            subscriptionStatus = writeBlocked ? "READ_ONLY" : (sub?.Status ?? "ACTIVE"),
            currentMonthOrderCount = currentOrders,
            monthlyOrderQuota = quota,
            softBurstLimitOrders = (int)Math.Floor(quota * 1.15m),
            currentWarehouseCount,
            activeWarehouses = currentWarehouseCount,
            usedWarehouses = currentWarehouseCount,
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
            taxCode = tenantProfile?.TaxCode ?? string.Empty,
            taxpayerType = tenantProfile?.TaxpayerType ?? "ENTERPRISE",
            companyName = resolvedCompanyName,
            companyLegalName = resolvedCompanyName,
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
        });
    }

    public async Task<WmsEndpointOutcome> EnforceSubscriptionQuotaAsync(
        EnforceSubscriptionQuotaInput? request = null,
        CancellationToken ct = default)
    {
        var tenantId = _db?.CurrentTenantId ?? 1;
        var sub = _db != null
            ? await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            : null;

        var fallbackSpec = SaasBillingService.ResolvePlanSpec(sub?.PlanCode);
        var quota = request?.MonthlyOrderQuota is > 0 ? request.MonthlyOrderQuota.Value : (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : fallbackSpec.MonthlyOrderQuota);
        var currentOrders = request?.CurrentMonthOrderCount ?? sub?.CurrentMonthOrderCount ?? 0;

        if (sub != null && request?.CurrentMonthOrderCount.HasValue == true)
        {
            sub.CurrentMonthOrderCount = currentOrders;
            sub.MonthlyOrderQuota = quota;
            sub.UpdatedAtUtc = DateTime.UtcNow;
            await _db!.SaveChangesAsync(ct);
        }

        var usagePct = quota > 0 ? decimal.Round((currentOrders * 100m) / quota, 2) : 0m;
        if (usagePct > 115m)
        {
            var ex = new ForbiddenException("Đã vượt 115% hạn mức đơn hàng/tháng của gói thuê bao. Hệ thống chuyển sang chế độ Chỉ Đọc (Read-Only) cho thao tác tạo mới. Vui lòng quét mã VietQR để nâng cấp gói ngay lập tức.");
            ex.Data["ErrorCode"] = "SUBSCRIPTION_QUOTA_EXCEEDED";
            throw ex;
        }

        var tierStatus = usagePct > 100m ? "SOFT_WARNING_GRACE_15PCT" : "NORMAL";
        return WmsEndpointOutcome.Ok(new
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
}
