using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class SaasBillingService : ISaasBillingService
{
    private readonly ErpKhoDbContext? _db;

    public SaasBillingService(ErpKhoDbContext? db = null)
    {
        _db = db;
    }

    public Task<object> GetPlansAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        object payload = new
        {
            plans = new object[]
            {
                new
                {
                    planCode = "STARTER",
                    planName = "Gói Khởi Đầu (Starter WMS)",
                    monthlyPriceVnd = 490000m,
                    monthlyOrderQuota = 1000,
                    maxWarehouses = 2,
                    maxUsers = 10,
                    maxSkus = 2000
                },
                new
                {
                    planCode = "PRO",
                    planName = "Gói Tăng Trưởng (Pro Multi-Warehouse)",
                    monthlyPriceVnd = 1490000m,
                    monthlyOrderQuota = 10000,
                    maxWarehouses = 5,
                    maxUsers = 30,
                    maxSkus = 20000
                },
                new
                {
                    planCode = "ENTERPRISE",
                    planName = "Gói Doanh Nghiệp (Enterprise 3PL & Omnichannel)",
                    monthlyPriceVnd = 4990000m,
                    monthlyOrderQuota = 100000,
                    maxWarehouses = 20,
                    maxUsers = 200,
                    maxSkus = 100000
                }
            },
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
        TenantSubscription? sub = null;
        if (_db != null)
        {
            sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
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
                _db.TenantSubscriptions.Add(sub);
                await _db.SaveChangesAsync(cancellationToken);
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

        var currentWarehouseCount = _db != null ? await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, cancellationToken) : 1;
        var currentUserCount = _db != null ? await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, cancellationToken) : 1;
        var currentSkuCount = _db != null ? await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken) : 0;
        var resolvedPlanCode = sub?.PlanCode ?? "STARTER";
        var maxWarehouses = sub?.MaxWarehouses is > 0 ? sub.MaxWarehouses : (resolvedPlanCode == "ENTERPRISE" ? 20 : (resolvedPlanCode == "PRO" ? 5 : 2));
        var maxUsers = sub?.MaxUsers is > 0 ? sub.MaxUsers : (resolvedPlanCode == "ENTERPRISE" ? 200 : (resolvedPlanCode == "PRO" ? 30 : 10));
        var maxSkus = resolvedPlanCode == "ENTERPRISE" ? 100000 : (resolvedPlanCode == "PRO" ? 20000 : 2000);

        return new
        {
            tenantId,
            planCode = resolvedPlanCode,
            subscriptionStatus = writeBlocked ? "READ_ONLY" : (sub?.Status ?? "ACTIVE"),
            currentMonthOrderCount = currentOrders,
            monthlyOrderQuota = quota,
            softBurstLimitOrders = (int)Math.Floor(quota * 1.15m),
            currentWarehouseCount,
            maxWarehouses,
            currentUserCount,
            maxUsers,
            currentSkuCount,
            maxSkus,
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
        var quota = requestedQuota ?? (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : 1000);
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
        var planCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "PRO" : request.PlanCode.Trim().ToUpperInvariant();
        var requestedAmt = request?.AmountVnd is > 0m
            ? request.AmountVnd.Value
            : (request?.CustomAmountVnd is > 0m ? request.CustomAmountVnd.Value : (decimal?)null);
        var amountVnd = requestedAmt ?? (planCode == "ENTERPRISE" ? 4990000m : 1490000m);
        var invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var qrPayload = $"00020101021238570010A00000072701270006970436011319038889999990208QRIBFTTA530370454{amountVnd:0}5802VN62{invoiceCode}";

        if (_db != null)
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
            _db.SaasBillingInvoices.Add(inv);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return new
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
        };
    }

    public async Task<(bool InvalidReason, string? ErrorMessage, object? Payload)> HandleVietQrWebhookAsync(
        SaasVietQrWebhookRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        var targetPlan = string.IsNullOrWhiteSpace(request?.PlanCode) ? "PRO" : request.PlanCode.Trim().ToUpperInvariant();
        var newQuota = targetPlan == "ENTERPRISE" ? 100000 : 10000;
        var resolvedInvoiceCode = !string.IsNullOrWhiteSpace(request?.InvoiceCode)
            ? request.InvoiceCode.Trim()
            : $"INV-VQR-{now:yyyyMMdd}-PAID";

        if (_db != null)
        {
            SaasBillingInvoice? invoice = null;
            if (!string.IsNullOrWhiteSpace(request?.InvoiceCode))
            {
                var code = request.InvoiceCode.Trim();
                invoice = await _db.SaasBillingInvoices.FirstOrDefaultAsync(i => i.InvoiceCode == code, cancellationToken);
            }
            invoice ??= await _db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId && i.Status != "PAID")
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(cancellationToken);
            invoice ??= await _db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(cancellationToken);

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
                _db.SaasBillingInvoices.Add(invoice);
            }

            var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
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
                _db.TenantSubscriptions.Add(sub);
            }
            else
            {
                sub.PlanCode = targetPlan;
                sub.Status = "ACTIVE";
                sub.MonthlyOrderQuota = newQuota;
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
        }

        var payload = new
        {
            tenantId,
            invoiceCode = resolvedInvoiceCode,
            upgradedPlanCode = targetPlan,
            subscriptionStatus = "ACTIVE",
            newMonthlyOrderQuota = newQuota,
            writeBlocked = false,
            paidAtUtc = now,
            statusLabel = $"Đã xác nhận thanh toán VietQR — Mở khóa tức thì gói {targetPlan} ({newQuota:N0} đơn/tháng)"
        };

        return (false, null, payload);
    }
}
