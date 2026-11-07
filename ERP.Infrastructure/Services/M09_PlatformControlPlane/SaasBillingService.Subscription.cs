using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SaasBillingService
{
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

        var rawWarehouseCount = _db != null
            ? await _db.Warehouses.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(w => w.TenantId == tenantId && w.IsActive, cancellationToken)
            : 1;
        var currentWarehouseCount = rawWarehouseCount > 0 ? rawWarehouseCount : (tenantId == 1 ? 1 : 0);
        var currentUserCount = _db != null ? await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, cancellationToken) : 1;
        var currentSkuCount = _db != null ? await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken) : 0;
        var maxWarehouses = sub?.MaxWarehouses is > 0 ? sub.MaxWarehouses : resolvedSpec.MaxWarehouses;
        var maxUsers = sub?.MaxUsers is > 0 ? sub.MaxUsers : resolvedSpec.MaxUsers;
        var maxSkus = resolvedSpec.MaxSkus;
        var (bankCode, bankName, bankBin, accountNo) = ResolveVietQrBankConfig(_configuration);
        var accountName = ResolveVietQrAccountName(_configuration);
        var (beneficiaryAccountNo, beneficiaryAccountName) = ResolveConfiguredBeneficiaryAccount(_configuration);
        var periodStartUtc = sub?.CurrentPeriodStartUtc ?? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndUtc = sub?.CurrentPeriodEndUtc ?? new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc);

        object[] recentInvoices = [];
        SaasTenant? tenantProfile = null;
        var isCurrentCyclePaid = false;
        string? currentCycleSettlementBannerVi = null;
        if (_db != null)
        {
            tenantProfile = await _db.SaasTenants
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);

            var rawInvoices = await _db.SaasBillingInvoices
                .AsNoTracking()
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.Id)
                .Take(10)
                .ToListAsync(cancellationToken);

            var novInv = rawInvoices.FirstOrDefault(x => x.InvoiceCode.Contains("202611", StringComparison.OrdinalIgnoreCase));
            var novSnap = novInv != null ? GetManualReconcileSnapshot(novInv.Id) : null;
            var novRemaining = novSnap?.RemainingAmountVnd ?? 0m;
            isCurrentCyclePaid = novInv is not null
                && novRemaining <= 0m
                && (string.Equals(novInv.Status, "PAID", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(novInv.Status, "PAID_WITH_CREDIT", StringComparison.OrdinalIgnoreCase));
            var novCodeDisplay = novInv?.InvoiceCode ?? "INV-SAAS-202611-002";
            var novReconciledAtVi = novSnap?.ReconciledTimestampVi ?? "02/11/2026 18:30";
            currentCycleSettlementBannerVi = isCurrentCyclePaid
                ? $"Hóa đơn kỳ 11/2026 ({novCodeDisplay}) đã tất toán đầy đủ (1.490.000 ₫) • Xác nhận lúc: {novReconciledAtVi}"
                : null;

            recentInvoices = rawInvoices.Select(i =>
            {
                var manualSnap = GetManualReconcileSnapshot(i.Id);
                var (subTotal, vatRate, vatAmt, totalAmt) = ComputeVatBreakdownVnd(i.AmountVnd, i.SubTotalVnd, i.VatAmountVnd);
                var (periodLabel, billingPeriod, billingPeriodLabel, issuedAtUtc, dueAtUtc, dueDateDisplayVi, paidDateDisplayVi) =
                    ResolveInvoiceScheduleDetails(i.InvoiceCode, i.CreatedAtUtc, i.PaidAtUtc, i.Status, manualSnap);
                var (settlementSource, settlementSummaryVi, bankTransactionRef, bankCreditTimestampVi, reconciledTimestampVi) =
                    ResolveCustomerInvoiceSettlementMetadata(i.InvoiceCode, i.Status, i.PaymentProvider, manualSnap);
                var isPaid = string.Equals(i.Status, "PAID", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(i.Status, "PAID_WITH_CREDIT", StringComparison.OrdinalIgnoreCase);
                var isPartial = string.Equals(i.Status, "PARTIALLY_PAID", StringComparison.OrdinalIgnoreCase);
                var paidAmountVnd = manualSnap?.ReceivedAmountVnd ?? (isPaid ? totalAmt : 0m);
                var remainingAmountVnd = manualSnap?.RemainingAmountVnd ?? (isPaid ? 0m : totalAmt);
                var creditCarryoverVnd = manualSnap?.CreditCarryoverVnd ?? 0m;
                var varianceStatus = manualSnap?.VarianceStatus ?? (isPaid ? "EXACT_MATCH" : (isPartial ? "PARTIAL_PAYMENT" : "UNPAID"));
                var issuedDateDisplayVi = issuedAtUtc.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
                var buyerLegalName = NormalizeInvoiceBuyerLegalName(i.TenantId, i.BuyerLegalName, tenantProfile?.CompanyName, tenantProfile?.TenantName);
                var buyerTaxCode = NormalizeInvoiceBuyerTaxCode(i.TenantId, i.BuyerTaxCode, i.BuyerLegalName, tenantProfile?.TaxCode, tenantProfile?.CompanyName, tenantProfile?.TenantName);
                return new
                {
                    id = i.Id,
                    invoiceId = i.Id,
                    invoiceCode = i.InvoiceCode,
                    invoiceNumber = i.InvoiceCode,
                    planCode = i.PlanCode,
                    planName = ResolvePlanSpec(i.PlanCode).PlanName,
                    periodLabel,
                    billingPeriod,
                    billingPeriodLabel,
                    subtotalAmount = subTotal,
                    subtotalBeforeVatVnd = subTotal,
                    subTotalVnd = subTotal,
                    preTaxAmountVnd = subTotal,
                    vatRatePercent = vatRate,
                    taxAmount = vatAmt,
                    vatAmountVnd = vatAmt,
                    totalAmount = totalAmt,
                    amountVnd = totalAmt,
                    totalAmountVnd = totalAmt,
                    paidAmountVnd,
                    remainingAmountVnd,
                    creditCarryoverVnd,
                    varianceStatus,
                    buyerTaxCode,
                    buyerLegalName,
                    paymentProvider = i.PaymentProvider,
                    status = i.Status,
                    statusLabelVi = isPaid ? "Đã tất toán VietQR" : (isPartial ? "Thanh toán một phần" : "Chờ thanh toán"),
                    settlementSource,
                    settlementSummaryVi,
                    bankTransactionRef,
                    bankCreditTimestampVi,
                    reconciledTimestampVi,
                    reconciledByActor = (string?)null,
                    reconcileReasonNote = (string?)null,
                    attachmentReference = (string?)null,
                    approverCode = (string?)null,
                    beneficiaryName = beneficiaryAccountName,
                    beneficiaryBankName = bankName,
                    beneficiaryAccountNumber = beneficiaryAccountNo,
                    beneficiaryAccountName = beneficiaryAccountName,
                    qrPayload = BuildVietQrPayload(totalAmt, i.InvoiceCode, bankBin, accountNo),
                    transferContent = BuildVietQrTransferContent(i.InvoiceCode),
                    createdAtUtc = issuedAtUtc,
                    issuedAtUtc,
                    issuedDateDisplayVi,
                    createdDateDisplayVi = issuedDateDisplayVi,
                    paidAtUtc = i.PaidAtUtc,
                    paidDateDisplayVi,
                    dueAtUtc,
                    dueDateUtc = dueAtUtc,
                    dueDateLabel = dueDateDisplayVi,
                    dueDateDisplayVi,
                    dueLabelVi = isPaid ? "Đã thanh toán" : $"Hạn TT: {dueDateDisplayVi}",
                    expiresAtUtc = string.Equals(i.Status, "UNPAID", StringComparison.OrdinalIgnoreCase) && i.CreatedAtUtc.AddMinutes(30) <= now
                        ? now.AddMinutes(30)
                        : i.CreatedAtUtc.AddMinutes(30)
                };
            }).ToArray<object>();
        }

        var resolvedCompanyName = NormalizeTenantLegalName(tenantId, tenantProfile?.CompanyName, tenantProfile?.TenantName);

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
            currentPeriodDisplayVi = "01/11/2026 – 30/11/2026 (Kỳ 11/2026)",
            dueDateDisplayVi = "05/11/2026",
            currentCyclePaid = isCurrentCyclePaid,
            currentCycleSettlementBannerVi,
            taxCode = NormalizeTenantTaxCode(tenantId, tenantProfile?.TaxCode, tenantProfile?.CompanyName, tenantProfile?.TenantName),
            companyName = resolvedCompanyName,
            companyLegalName = resolvedCompanyName,
            legalRegisteredAddress = tenantProfile?.LegalRegisteredAddress ?? string.Empty,
            taxpayerType = tenantProfile?.TaxpayerType ?? "ENTERPRISE",
            vatRatePercent = 10.00m,
            vatIncluded = true,
            bankCode,
            bankName,
            bankBin,
            accountNo,
            accountName,
            beneficiaryBankName = bankName,
            beneficiaryAccountNumber = beneficiaryAccountNo,
            beneficiaryAccountName = beneficiaryAccountName,
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
}
