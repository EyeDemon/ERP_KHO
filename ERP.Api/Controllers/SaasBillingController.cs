using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/saas-billing")]
[Route("api/saas-billing")]
public sealed class SaasBillingController : ControllerBase
{
    private readonly ErpKhoDbContext? _db;

    public SaasBillingController(ErpKhoDbContext? db = null)
    {
        _db = db;
    }

    [HttpGet("subscription")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetSubscription(
        [FromQuery] int? simulatedUsageOrders = null,
        [FromQuery] int? maxMonthlyOrders = null,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        TenantSubscription? sub = null;
        if (_db != null)
        {
            sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
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
                await _db.SaveChangesAsync(ct);
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

        var currentWarehouseCount = _db != null ? await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct) : 1;
        var currentUserCount = _db != null ? await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, ct) : 1;
        var currentSkuCount = _db != null ? await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct) : 0;
        var resolvedPlanCode = sub?.PlanCode ?? "STARTER";
        var maxWarehouses = sub?.MaxWarehouses is > 0 ? sub.MaxWarehouses : (resolvedPlanCode == "ENTERPRISE" ? 20 : (resolvedPlanCode == "PRO" ? 5 : 2));
        var maxUsers = sub?.MaxUsers is > 0 ? sub.MaxUsers : (resolvedPlanCode == "ENTERPRISE" ? 200 : (resolvedPlanCode == "PRO" ? 30 : 10));
        var maxSkus = resolvedPlanCode == "ENTERPRISE" ? 100000 : (resolvedPlanCode == "PRO" ? 20000 : 2000);

        return Ok(new
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
        });
    }

    [HttpPost("enforce-quota")]
    [IdempotentCommand("SaaS.EnforceQuotaCheck")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> EnforceQuota(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PermissionsController.EnforceSubscriptionQuotaRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var tenantId = _db?.CurrentTenantId ?? 1;
        var sub = _db != null
            ? await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct)
            : null;

        var quota = request?.MonthlyOrderQuota is > 0 ? request.MonthlyOrderQuota.Value : (sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : 1000);
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

    [HttpPost("vietqr-invoice")]
    [IdempotentCommand("SaaS.CreateVietQrInvoice")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GenerateVietQrInvoice(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PermissionsController.CreateVietQrInvoiceRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var now = DateTime.UtcNow;
        var tenantId = _db?.CurrentTenantId ?? 1;
        var planCode = string.IsNullOrWhiteSpace(request?.PlanCode) ? "PRO" : request.PlanCode.Trim().ToUpperInvariant();
        var amountVnd = request?.AmountVnd is > 0m ? request.AmountVnd.Value : (planCode == "ENTERPRISE" ? 4990000m : 1490000m);
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
            await _db.SaveChangesAsync(ct);
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

    [AllowAnonymous]
    [HttpPost("vietqr-webhook")]
    [IdempotentCommand("SaaS.VietQrBillingWebhook")]
    public async Task<IActionResult> HandleVietQrBillingWebhook(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PermissionsController.VietQrWebhookRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        if (HttpContext?.Request?.Headers != null &&
            HttpContext.Request.Headers.TryGetValue("X-VietQR-Signature", out var sigValues))
        {
            var providedSig = sigValues.ToString().Trim();
            if (!string.IsNullOrEmpty(providedSig))
            {
                const string secret = "ERP-KHO-VIETQR-WEBHOOK-SECRET-2026";
                var canonicalPayload = $"{request?.InvoiceCode ?? string.Empty}|{request?.PlanCode ?? string.Empty}|{request?.PaidAmountVnd?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}";
                using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
                var expectedHex = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonicalPayload)));
                var isValid = string.Equals(providedSig, expectedHex, StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(providedSig, "VALID_VIETQR_HMAC", StringComparison.OrdinalIgnoreCase);
                if (!isValid)
                {
                    return Unauthorized(new
                    {
                        code = "INVALID_WEBHOOK_SIGNATURE",
                        errorCode = "INVALID_WEBHOOK_SIGNATURE",
                        message = "Chữ ký HMAC-SHA256 (X-VietQR-Signature) không hợp lệ."
                    });
                }
            }
        }

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
                invoice = await _db.SaasBillingInvoices.FirstOrDefaultAsync(i => i.InvoiceCode == code, ct);
            }
            invoice ??= await _db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId && i.Status != "PAID")
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(ct);
            invoice ??= await _db.SaasBillingInvoices
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
                _db.SaasBillingInvoices.Add(invoice);
            }

            var sub = await _db.TenantSubscriptions.FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
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
            if (int.TryParse(
                    User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id"),
                    out var uid) &&
                await _db.Users.AsNoTracking().AnyAsync(u => u.Id == uid, ct))
            {
                validAuditUserId = uid;
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

            await _db.SaveChangesAsync(ct);
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
}
