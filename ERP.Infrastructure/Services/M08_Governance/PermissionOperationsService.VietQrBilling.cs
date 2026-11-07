using System.Data;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PermissionOperationsService
{
    public async Task<WmsEndpointOutcome> CreateVietQrInvoiceAsync(
        CreateVietQrInvoiceInput? request = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        var rawPlanCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "GROWTH" : request.PlanCode.Trim().ToUpperInvariant();
        var planSpec = SaasBillingService.ResolvePlanSpec(rawPlanCode);
        var planCode = rawPlanCode is "PRO" or "GROWTH" ? rawPlanCode : planSpec.PlanCode;
        var amountVnd = request?.AmountVnd is > 0m ? request.AmountVnd.Value : planSpec.MonthlyPriceVnd;
        var (bankCode, bankName, bankBin, accountNo) = SaasBillingService.ResolveVietQrBankConfig(_configuration);
        var accountName = SaasBillingService.ResolveVietQrAccountName(_configuration);

        string invoiceCode;
        string qrPayload;
        DateTime createdAt = now;
        var reusedPendingInvoice = false;

        if (_db != null)
        {
            var currentSub = await _db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
            if (currentSub != null)
            {
                var currentSpec = SaasBillingService.ResolvePlanSpec(currentSub.PlanCode);
                if (planSpec.MonthlyPriceVnd < currentSpec.MonthlyPriceVnd)
                {
                    var activeWarehouses = await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
                    var activeUsers = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct);
                    var activeProducts = await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
                    var currentOrders = currentSub.CurrentMonthOrderCount;
                    if (activeWarehouses > planSpec.MaxWarehouses || activeUsers > planSpec.MaxUsers ||
                        currentOrders > planSpec.MonthlyOrderQuota || activeProducts > planSpec.MaxSkus)
                    {
                        var shortPlanLabel = planSpec.PlanCode == "STARTER" ? "gói Khởi đầu" : "gói Tăng trưởng";
                        var ex = new BusinessRuleException(
                            $"Không khả dụng để hạ cấp: Bạn đang dùng {activeWarehouses} kho & {currentOrders.ToString("N0", new System.Globalization.CultureInfo("vi-VN"))} đơn/tháng (vượt giới hạn {planSpec.MaxWarehouses} kho & {planSpec.MonthlyOrderQuota.ToString("N0", new System.Globalization.CultureInfo("vi-VN"))} đơn của {shortPlanLabel}).");
                        ex.Data["ErrorCode"] = "PLAN_DOWNGRADE_USAGE_EXCEEDED";
                        throw ex;
                    }
                }
            }

            var existingPending = await _db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId && i.PlanCode == planCode && i.AmountVnd == amountVnd && i.Status == "UNPAID")
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(ct);

            if (existingPending != null)
            {
                reusedPendingInvoice = true;
                invoiceCode = existingPending.InvoiceCode;
                if (existingPending.CreatedAtUtc.AddMinutes(30) <= now) existingPending.CreatedAtUtc = now;
                createdAt = existingPending.CreatedAtUtc;
                var (subAmt, vatRate, vatAmt, totalAmt) = SaasBillingService.ComputeVatBreakdownVnd(amountVnd);
                amountVnd = totalAmt;
                qrPayload = SaasBillingService.BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
                existingPending.SubTotalVnd = subAmt;
                existingPending.VatRatePercent = vatRate;
                existingPending.VatAmountVnd = vatAmt;
                existingPending.AmountVnd = totalAmt;
                existingPending.QrPayload = qrPayload;
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                var (subAmt, vatRate, vatAmt, totalAmt) = SaasBillingService.ComputeVatBreakdownVnd(amountVnd);
                amountVnd = totalAmt;
                invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                qrPayload = SaasBillingService.BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
                _db.SaasBillingInvoices.Add(new SaasBillingInvoice
                {
                    TenantId = tenantId, InvoiceCode = invoiceCode, PlanCode = planCode,
                    SubTotalVnd = subAmt, VatRatePercent = vatRate, VatAmountVnd = vatAmt, AmountVnd = amountVnd,
                    PaymentProvider = "VIETQR", Status = "UNPAID", QrPayload = qrPayload, CreatedAtUtc = now
                });
                await _db.SaveChangesAsync(ct);
            }
        }
        else
        {
            invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            qrPayload = SaasBillingService.BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
        }

        var (computedSubTotalVnd, _, computedVatAmountVnd, computedTotalVnd) = SaasBillingService.ComputeVatBreakdownVnd(amountVnd);
        var transferContent = SaasBillingService.BuildVietQrTransferContent(invoiceCode);
        var expiresAtUtc = createdAt.AddMinutes(30);

        return WmsEndpointOutcome.Ok(new
        {
            invoiceCode, planCode, planName = planSpec.PlanName,
            subTotalVnd = computedSubTotalVnd, vatRatePercent = 10.00m, vatAmountVnd = computedVatAmountVnd,
            amountVnd = computedTotalVnd, totalAmountVnd = computedTotalVnd, paymentProvider = "VIETQR", status = "UNPAID",
            reusedPendingInvoice, bankCode, bankName, bankBin, accountNo, accountName, vietQrPayload = qrPayload,
            transferContent, createdAtUtc = createdAt, expiresAtUtc,
            statusLabel = reusedPendingInvoice
                ? $"Đang sử dụng lại hóa đơn chờ thanh toán {invoiceCode} ({bankName} - {accountNo}) • Hiệu lực 30 phút"
                : $"Đã khởi tạo mã thanh toán tự động Napas247 VietQR ({bankName} - {accountNo}) cho hóa đơn {invoiceCode}"
        });
    }

    public async Task<WmsEndpointOutcome> HandleVietQrBillingWebhookAsync(
        VietQrWebhookInput? request = null,
        string? providedSignature = null,
        int? actorUserId = null,
        CancellationToken ct = default)
    {
        if (!SaasBillingService.VerifyVietQrWebhookSignature(_configuration, providedSignature, request?.InvoiceCode, request?.PlanCode, request?.PaidAmountVnd))
        {
            return WmsEndpointOutcome.Unauthorized(new
            {
                code = "INVALID_WEBHOOK_SIGNATURE",
                errorCode = "INVALID_WEBHOOK_SIGNATURE",
                message = "Chữ ký HMAC-SHA256 (X-VietQR-Signature) bị thiếu hoặc không hợp lệ."
            });
        }

        var now = DateTime.UtcNow;
        var originalTenantId = _db?.CurrentTenantId > 0 ? _db.CurrentTenantId : 1;
        var tenantId = originalTenantId;
        var targetPlan = string.IsNullOrWhiteSpace(request?.PlanCode) ? "GROWTH" : request.PlanCode.Trim().ToUpperInvariant();
        var targetSpec = SaasBillingService.ResolvePlanSpec(targetPlan);
        var newQuota = targetSpec.MonthlyOrderQuota;
        var resolvedInvoiceCode = !string.IsNullOrWhiteSpace(request?.InvoiceCode)
            ? request.InvoiceCode.Trim()
            : $"INV-VQR-{tenantId}-{targetPlan}-{(request?.PaidAmountVnd ?? 0m):0}-{now:yyyyMMdd}";
        var (bankCode, bankName, bankBin, accountNo) = SaasBillingService.ResolveVietQrBankConfig(_configuration);
        var accountName = SaasBillingService.ResolveVietQrAccountName(_configuration);

        if (_db != null)
        {
            var useInMemoryGate = SaasBillingService.ShouldUseInMemoryGate(_db);
            if (useInMemoryGate) await SaasBillingService.VietQrWebhookGate.WaitAsync(ct);
            try
            {
                await using var tx = _db.Database.CurrentTransaction is null && _db.Database.IsRelational()
                    ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
                    : null;

                if (_db.Database.IsSqlServer() && _db.Database.CurrentTransaction != null)
                {
                    var lockResource = $"ERP.VietQrWebhook:{SaasBillingService.NormalizeVietQrReference(resolvedInvoiceCode)}";
                    await _db.Database.ExecuteSqlInterpolatedAsync($"""
                        DECLARE @result int;
                        EXEC @result = sys.sp_getapplock @Resource = {lockResource}, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;
                        IF @result < 0 THROW 50002, 'Could not acquire VietQR webhook lock (sp_getapplock failed or timed out).', 1;
                        """, ct);
                }

                SaasBillingInvoice? invoice = null;
                if (!string.IsNullOrWhiteSpace(request?.InvoiceCode))
                {
                    var code = request.InvoiceCode.Trim();
                    var cleanCode = SaasBillingService.NormalizeVietQrReference(code);
                    var withPrefix = $"ERPKHO{cleanCode}";
                    invoice = await _db.SaasBillingInvoices.IgnoreQueryFilters()
                        .Where(i => i.InvoiceCode == code ||
                                    i.InvoiceCode.Replace("-", "").Replace(" ", "").ToUpper() == cleanCode ||
                                    i.InvoiceCode.Replace("-", "").Replace(" ", "").ToUpper() == withPrefix)
                        .OrderByDescending(i => i.Id).FirstOrDefaultAsync(ct);
                    if (invoice != null && invoice.TenantId > 0)
                    {
                        tenantId = invoice.TenantId;
                        _db.UseTenant(tenantId);
                    }
                }
                else
                {
                    invoice = await _db.SaasBillingInvoices.Where(i => i.TenantId == tenantId && i.Status != "PAID").OrderByDescending(i => i.Id).FirstOrDefaultAsync(ct)
                        ?? await _db.SaasBillingInvoices.Where(i => i.TenantId == tenantId && i.InvoiceCode == resolvedInvoiceCode).OrderByDescending(i => i.Id).FirstOrDefaultAsync(ct);
                }

                if (invoice != null && string.Equals(invoice.Status, "PAID", StringComparison.OrdinalIgnoreCase))
                {
                    var existingSub = await _db.TenantSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
                    var replayPlan = existingSub?.PlanCode ?? invoice.PlanCode ?? targetPlan;
                    var replayQuota = existingSub?.MonthlyOrderQuota ?? newQuota;
                    if (tx != null) await tx.CommitAsync(ct);
                    return WmsEndpointOutcome.Ok(new
                    {
                        tenantId, invoiceCode = invoice.InvoiceCode, upgradedPlanCode = replayPlan,
                        subscriptionStatus = existingSub?.Status ?? "ACTIVE", newMonthlyOrderQuota = replayQuota,
                        currentPeriodEndUtc = existingSub?.CurrentPeriodEndUtc ?? now.AddDays(30),
                        idempotentReplay = true, writeBlocked = false, bankCode, bankName, bankBin, accountNo, accountName,
                        paidAtUtc = invoice.PaidAtUtc ?? now,
                        statusLabel = $"Đã xác nhận thanh toán VietQR (Idempotent Replay) — Gói {replayPlan} ({replayQuota:N0} đơn/tháng)"
                    });
                }

                if (invoice != null)
                {
                    var (_, _, _, healedTot) = SaasBillingService.ComputeVatBreakdownVnd(invoice.AmountVnd, invoice.SubTotalVnd, invoice.VatAmountVnd);
                    var expectedAmount = healedTot > 0m ? healedTot : invoice.AmountVnd;
                    if (expectedAmount > 0m && (request?.PaidAmountVnd is null or <= 0m || request.PaidAmountVnd.Value < expectedAmount))
                        return BuildUnderpaidOutcome(expectedAmount, request?.PaidAmountVnd ?? 0m);

                    invoice.Status = "PAID";
                    invoice.PaidAtUtc = now;
                    if (!string.IsNullOrWhiteSpace(invoice.PlanCode)) targetPlan = invoice.PlanCode.Trim().ToUpperInvariant();
                    targetSpec = SaasBillingService.ResolvePlanSpec(targetPlan);
                    newQuota = targetSpec.MonthlyOrderQuota;
                    resolvedInvoiceCode = invoice.InvoiceCode;
                }
                else
                {
                    if (targetSpec.MonthlyPriceVnd > 0m && (request?.PaidAmountVnd is null or <= 0m || request.PaidAmountVnd.Value < targetSpec.MonthlyPriceVnd))
                        return BuildUnderpaidOutcome(targetSpec.MonthlyPriceVnd, request?.PaidAmountVnd ?? 0m);

                    var paidAmt = request?.PaidAmountVnd is > 0m ? request.PaidAmountVnd.Value : targetSpec.MonthlyPriceVnd;
                    invoice = new SaasBillingInvoice
                    {
                        TenantId = tenantId, InvoiceCode = resolvedInvoiceCode, PlanCode = targetPlan, AmountVnd = paidAmt,
                        PaymentProvider = "VIETQR", Status = "PAID",
                        QrPayload = SaasBillingService.BuildVietQrPayload(paidAmt, resolvedInvoiceCode, bankBin, accountNo),
                        PaidAtUtc = now, CreatedAtUtc = now
                    };
                    _db.SaasBillingInvoices.Add(invoice);
                }

                var sub = await _db.TenantSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
                if (sub == null)
                {
                    _db.TenantSubscriptions.Add(new TenantSubscription
                    {
                        TenantId = tenantId, PlanCode = targetPlan, Status = "ACTIVE", MonthlyOrderQuota = newQuota,
                        CurrentMonthOrderCount = 0, MaxWarehouses = targetSpec.MaxWarehouses, MaxUsers = targetSpec.MaxUsers,
                        CurrentPeriodStartUtc = now, CurrentPeriodEndUtc = now.AddDays(30), UpdatedAtUtc = now
                    });
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
                var candidateId = actorUserId ?? (_currentUser is { IsAuthenticated: true, UserId: > 0 } ? _currentUser.UserId : null);
                if (candidateId is > 0 && await _db.Users.AsNoTracking().AnyAsync(u => u.Id == candidateId.Value, ct))
                    validAuditUserId = candidateId;

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = validAuditUserId, EntityName = "TenantSubscription", EntityId = tenantId,
                    Action = "SaaS.VietQrBillingWebhook.Paid",
                    NewValues = $"Plan={targetPlan}; Status=ACTIVE; Quota={newQuota}; MaxWarehouses={targetSpec.MaxWarehouses}; MaxUsers={targetSpec.MaxUsers}",
                    Reason = $"Webhook thanh toán VietQR tự động kích hoạt gói {targetPlan} & mở khóa hạn mức tức thì",
                    Result = "Success", Timestamp = now
                });

                await _db.SaveChangesAsync(ct);
                if (tx != null) await tx.CommitAsync(ct);
            }
            finally
            {
                _db.UseTenant(originalTenantId);
                if (useInMemoryGate) SaasBillingService.VietQrWebhookGate.Release();
            }
        }
        else if (targetSpec.MonthlyPriceVnd > 0m && (request?.PaidAmountVnd is null or <= 0m || request.PaidAmountVnd.Value < targetSpec.MonthlyPriceVnd))
        {
            return BuildUnderpaidOutcome(targetSpec.MonthlyPriceVnd, request?.PaidAmountVnd ?? 0m);
        }

        return WmsEndpointOutcome.Ok(new
        {
            tenantId, invoiceCode = resolvedInvoiceCode, upgradedPlanCode = targetPlan, subscriptionStatus = "ACTIVE",
            newMonthlyOrderQuota = newQuota, currentPeriodEndUtc = now.AddDays(30), idempotentReplay = false, writeBlocked = false,
            bankCode, bankName, bankBin, accountNo, accountName, paidAtUtc = now,
            statusLabel = $"Đã xác nhận thanh toán VietQR — Mở khóa tức thì gói {targetPlan} ({newQuota:N0} đơn/tháng)"
        });
    }

    private static WmsEndpointOutcome BuildUnderpaidOutcome(decimal expectedAmountVnd, decimal paidAmountVnd) =>
        WmsEndpointOutcome.Json(422, new
        {
            code = "INVOICE_UNDERPAID_AMOUNT_MISMATCH",
            errorCode = "INVOICE_UNDERPAID_AMOUNT_MISMATCH",
            expectedAmountVnd,
            paidAmountVnd,
            message = "Số tiền thanh toán qua VietQR nhỏ hơn tổng tiền hóa đơn cần thanh toán."
        });
}
