using System.Globalization;
using ERP.Api.Authorization;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    [NonAction]
    public Task<IActionResult> GetPlatformInvoices(CancellationToken ct = default) => GetBillingInvoices(ct);

    [HttpGet("billing/invoices")]
    [PlatformAuthorize(AppPermissions.PlatformDashboardRead)]
    public async Task<IActionResult> GetBillingInvoices(CancellationToken ct = default)
    {
        var rawInvoices = await db.SaasBillingInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderByDescending(i => i.Id)
            .Take(100)
            .ToListAsync(ct);

        var saasTenantsById = await db.SaasTenants.AsNoTracking().ToDictionaryAsync(t => t.TenantId, ct);
        var (bankCode, bankName, bankBin, accountNo, accountName, displayAccountVi) = ResolveVietQrBeneficiary();

        var invoices = rawInvoices.Select(i =>
        {
            var manualSnap = SaasBillingService.GetManualReconcileSnapshot(db, i.Id);
            var (subTotal, vatRate, vatAmt, totalAmt) = SaasBillingService.ComputeVatBreakdownVnd(i.AmountVnd, i.SubTotalVnd, i.VatAmountVnd);
            var (periodLabel, billingPeriod, billingPeriodLabel, issuedAtUtc, dueAtUtc, dueDateDisplayVi, paidDateDisplayVi) =
                SaasBillingService.ResolveInvoiceScheduleDetails(i.InvoiceCode, i.CreatedAtUtc, i.PaidAtUtc, i.Status, manualSnap);
            var (settlementSource, computedSettlementSummaryVi, bankTransactionRef, bankCreditTimestampVi, reconciledTimestampVi, reconciledByActor, reconcileReasonNote) =
                SaasBillingService.ResolvePlatformInvoiceSettlementMetadata(i.InvoiceCode, i.Status, i.PaymentProvider, manualSnap);

            var ownerAuditStampVi = manualSnap?.OwnerAuditStampVi;
            var settlementSummaryVi = !string.IsNullOrWhiteSpace(ownerAuditStampVi)
                ? ownerAuditStampVi
                : computedSettlementSummaryVi;

            var isPaid = string.Equals(i.Status, "PAID", StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.Status, "PAID_WITH_CREDIT", StringComparison.OrdinalIgnoreCase);
            var isPartial = string.Equals(i.Status, "PARTIALLY_PAID", StringComparison.OrdinalIgnoreCase);
            var paidAmountVnd = manualSnap?.ReceivedAmountVnd ?? (isPaid ? totalAmt : 0m);
            var remainingAmountVnd = manualSnap?.RemainingAmountVnd ?? (isPaid ? 0m : totalAmt);
            var creditCarryoverVnd = manualSnap?.CreditCarryoverVnd ?? 0m;
            var varianceStatus = manualSnap?.VarianceStatus ?? (isPaid ? "EXACT_MATCH" : (isPartial ? "PARTIAL_PAYMENT" : "UNPAID"));
            var issuedDateDisplayVi = issuedAtUtc.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            saasTenantsById.TryGetValue(i.TenantId, out var invTenant);
            var buyerLegalName = SaasBillingService.NormalizeInvoiceBuyerLegalName(i.TenantId, i.BuyerLegalName, invTenant?.CompanyName, invTenant?.TenantName);
            var buyerTaxCode = SaasBillingService.NormalizeInvoiceBuyerTaxCode(i.TenantId, i.BuyerTaxCode, i.BuyerLegalName, invTenant?.TaxCode, invTenant?.CompanyName, invTenant?.TenantName);

            return new
            {
                id = i.Id,
                invoiceId = i.Id,
                tenantId = i.TenantId,
                tenantCode = $"TENANT-{i.TenantId:D3}",
                companyName = buyerLegalName,
                invoiceCode = i.InvoiceCode,
                invoiceNumber = i.InvoiceCode,
                planCode = i.PlanCode,
                planName = SaasBillingService.ResolvePlanSpec(i.PlanCode).PlanName,
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
                ownerAuditStampVi,
                bankTransactionRef,
                bankCreditTimestampVi,
                reconciledTimestampVi,
                reconciledByActor,
                reconcileReasonNote,
                attachmentReference = manualSnap?.AttachmentReference,
                approverCode = manualSnap?.ApproverCode,
                approverUsername = manualSnap?.ApproverUsername,
                bankCode,
                bankName,
                bankBin,
                accountNo,
                accountName,
                beneficiaryName = accountName,
                beneficiaryBankName = bankName,
                beneficiaryAccountNumber = accountNo,
                beneficiaryAccountName = accountName,
                displayAccountVi,
                transferContent = SaasBillingService.BuildVietQrTransferContent(i.InvoiceCode),
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
                dueLabelVi = isPaid ? "Đã thanh toán" : $"Hạn TT: {dueDateDisplayVi}"
            };
        }).ToList();

        var novInv = rawInvoices.FirstOrDefault(x => x.InvoiceCode.Contains("202611", StringComparison.OrdinalIgnoreCase));
        var isCurrentCyclePaid = novInv is not null
            && (string.Equals(novInv.Status, "PAID", StringComparison.OrdinalIgnoreCase)
                || string.Equals(novInv.Status, "PAID_WITH_CREDIT", StringComparison.OrdinalIgnoreCase));
        var novSnap = novInv is not null ? SaasBillingService.GetManualReconcileSnapshot(db, novInv.Id) : null;
        var currentActorUsername = ResolveActorUsername();

        var rawPlatformUsers = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.IsActive && u.SecurityDomain == "PLATFORM" && u.Username != currentActorUsername)
            .OrderBy(u => u.Id)
            .Take(10)
            .Select(u => new
            {
                u.Username,
                u.FullName,
                u.RoleId,
                RoleName = u.Role != null ? u.Role.RoleName : null
            })
            .ToListAsync(ct);

        var activePlatformUsers = rawPlatformUsers
            .Select(u =>
            {
                var humanLabel = FormatPlatformAdminActorDisplay(u.Username, u.FullName, u.RoleName, u.RoleId);
                return new
                {
                    username = u.Username,
                    fullName = humanLabel,
                    roleTitleVi = u.RoleName ?? "PlatformOperator",
                    displayLabelVi = humanLabel
                };
            })
            .ToList();

        var subs = await db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var mrrGross = subs.Where(s => s.Status is "ACTIVE" or "TRIAL" or "GRACE")
            .Sum(s => SaasBillingService.ResolvePlanSpec(s.PlanCode).MonthlyPriceVnd);
        if (mrrGross <= 0m)
        {
            mrrGross = rawInvoices.Where(x => x.Status == "PAID").Select(x => x.AmountVnd).DefaultIfEmpty(0m).Max();
        }

        var (mrrNet, _, mrrVat, mrrGrossFinal) = SaasBillingService.ComputeVatBreakdownVnd(mrrGross);
        var arrNet = decimal.Round(mrrNet * 12m, 2, MidpointRounding.AwayFromZero);
        var arrGross = decimal.Round(mrrGrossFinal * 12m, 2, MidpointRounding.AwayFromZero);

        return Ok(new
        {
            totalCount = invoices.Count,
            currentPeriodDisplayVi = "01/11/2026 – 30/11/2026 (Kỳ 11/2026)",
            dueDateDisplayVi = "05/11/2026",
            currentCyclePaid = isCurrentCyclePaid,
            currentCycleSettlementBannerVi = isCurrentCyclePaid && novInv is not null
                ? (novSnap?.OwnerAuditStampVi
                    ?? $"Hóa đơn kỳ 11/2026 ({novInv.InvoiceCode}) đã tất toán đầy đủ ({novInv.AmountVnd:N0} ₫)")
                : null,
            currentActorUsername,
            independentApprovers = activePlatformUsers,
            monthlyRecurringRevenueVnd = mrrNet,
            mrrVnd = mrrNet,
            arrVnd = arrNet,
            mrrNetBeforeVatVnd = mrrNet,
            mrrVatAmountVnd = mrrVat,
            mrrGrossWithVatVnd = mrrGrossFinal,
            arrNetBeforeVatVnd = arrNet,
            arrGrossWithVatVnd = arrGross,
            accountName,
            beneficiaryName = accountName,
            beneficiaryBankName = bankName,
            beneficiaryAccountNumber = accountNo,
            beneficiaryAccountName = accountName,
            bankCode,
            bankName,
            bankBin,
            accountNo,
            displayAccountVi,
            items = invoices
        });
    }
}
