using System.Data;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SaasBillingService
{
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
                    ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
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
                    var (healedSub, healedRate, healedVat, healedTot) = ComputeVatBreakdownVnd(invoice.AmountVnd, invoice.SubTotalVnd, invoice.VatAmountVnd);
                    var expectedAmount = healedTot > 0m ? healedTot : invoice.AmountVnd;
                    if (expectedAmount > 0m && (request?.PaidAmountVnd is null or <= 0m || request.PaidAmountVnd.Value < expectedAmount))
                    {
                        return BuildUnderpaidWebhookFailure(expectedAmount, request?.PaidAmountVnd ?? 0m);
                    }

                    invoice.SubTotalVnd = healedSub;
                    invoice.VatRatePercent = healedRate;
                    invoice.VatAmountVnd = healedVat;
                    invoice.AmountVnd = healedTot;
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
                    if (targetSpec.MonthlyPriceVnd > 0m && (request?.PaidAmountVnd is null or <= 0m || request.PaidAmountVnd.Value < targetSpec.MonthlyPriceVnd))
                    {
                        return BuildUnderpaidWebhookFailure(targetSpec.MonthlyPriceVnd, request?.PaidAmountVnd ?? 0m);
                    }

                    var paidAmt = request?.PaidAmountVnd is > 0m
                        ? request.PaidAmountVnd.Value
                        : targetSpec.MonthlyPriceVnd;
                    var (subAmt, vatRate, vatAmt, totalAmt) = ComputeVatBreakdownVnd(paidAmt);
                    invoice = new SaasBillingInvoice
                    {
                        TenantId = tenantId,
                        InvoiceCode = resolvedInvoiceCode,
                        PlanCode = targetPlan,
                        SubTotalVnd = subAmt,
                        VatRatePercent = vatRate,
                        VatAmountVnd = vatAmt,
                        AmountVnd = totalAmt,
                        PaymentProvider = "VIETQR",
                        Status = "PAID",
                        QrPayload = BuildVietQrPayload(totalAmt, resolvedInvoiceCode, bankBin, accountNo),
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
        else if (targetSpec.MonthlyPriceVnd > 0m && (request?.PaidAmountVnd is null or <= 0m || request.PaidAmountVnd.Value < targetSpec.MonthlyPriceVnd))
        {
            return BuildUnderpaidWebhookFailure(targetSpec.MonthlyPriceVnd, request?.PaidAmountVnd ?? 0m);
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

    private static (bool InvalidReason, string? ErrorMessage, object? Payload) BuildUnderpaidWebhookFailure(
        decimal expectedAmountVnd,
        decimal paidAmountVnd) =>
        (
            true,
            "INVOICE_UNDERPAID_AMOUNT_MISMATCH",
            new
            {
                code = "INVOICE_UNDERPAID_AMOUNT_MISMATCH",
                errorCode = "INVOICE_UNDERPAID_AMOUNT_MISMATCH",
                expectedAmountVnd,
                paidAmountVnd,
                message = "Số tiền thanh toán qua VietQR nhỏ hơn tổng tiền hóa đơn cần thanh toán."
            });
}
