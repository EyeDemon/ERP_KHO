using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SaasBillingService
{
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
            var currentSub = await _db.TenantSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);
            if (currentSub != null)
            {
                var currentSpec = ResolvePlanSpec(currentSub.PlanCode);
                if (planSpec.MonthlyPriceVnd < currentSpec.MonthlyPriceVnd)
                {
                    var activeWarehouses = await _db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, cancellationToken);
                    var activeUsers = await _db.Users.AsNoTracking().CountAsync(u => u.IsActive, cancellationToken);
                    var activeProducts = await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken);
                    var currentOrders = currentSub.CurrentMonthOrderCount;
                    if (activeWarehouses > planSpec.MaxWarehouses || activeUsers > planSpec.MaxUsers ||
                        currentOrders > planSpec.MonthlyOrderQuota || activeProducts > planSpec.MaxSkus)
                    {
                        var shortPlanLabel = planSpec.PlanCode == "STARTER" ? "gói Khởi đầu" : "gói Tăng trưởng";
                        var ex = new ERP.Application.Exceptions.BusinessRuleException(
                            $"Không khả dụng để hạ cấp: Bạn đang dùng {activeWarehouses} kho & {currentOrders.ToString("N0", new System.Globalization.CultureInfo("vi-VN"))} đơn/tháng (vượt giới hạn {planSpec.MaxWarehouses} kho & {planSpec.MonthlyOrderQuota.ToString("N0", new System.Globalization.CultureInfo("vi-VN"))} đơn của {shortPlanLabel}).");
                        ex.Data["ErrorCode"] = "PLAN_DOWNGRADE_USAGE_EXCEEDED";
                        throw ex;
                    }
                }
            }

            var tenantProfile = await _db.SaasTenants.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
            var buyerLegalName = !string.IsNullOrWhiteSpace(request?.CompanyName)
                ? request.CompanyName.Trim()
                : NormalizeTenantLegalName(tenantId, tenantProfile?.CompanyName, tenantProfile?.TenantName);
            var buyerTaxCode = !string.IsNullOrWhiteSpace(request?.TaxCode)
                ? request.TaxCode.Trim()
                : NormalizeTenantTaxCode(tenantId, tenantProfile?.TaxCode, tenantProfile?.CompanyName, tenantProfile?.TenantName);
            var (subTotalVnd, vatRatePercent, vatAmountVnd, normalizedTotalVnd) = ComputeVatBreakdownVnd(amountVnd);
            amountVnd = normalizedTotalVnd;

            var existingPending = await _db.SaasBillingInvoices
                .Where(i => i.TenantId == tenantId && i.PlanCode == planCode && i.AmountVnd == amountVnd && i.Status == "UNPAID")
                .OrderByDescending(i => i.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingPending != null)
            {
                reusedPendingInvoice = true;
                invoiceCode = existingPending.InvoiceCode;
                if (existingPending.CreatedAtUtc.AddMinutes(30) <= now) existingPending.CreatedAtUtc = now;
                createdAt = existingPending.CreatedAtUtc;
                qrPayload = BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
                existingPending.QrPayload = qrPayload;
                existingPending.SubTotalVnd = subTotalVnd;
                existingPending.VatRatePercent = vatRatePercent;
                existingPending.VatAmountVnd = vatAmountVnd;
                existingPending.BuyerTaxCode = buyerTaxCode ?? existingPending.BuyerTaxCode;
                existingPending.BuyerLegalName = buyerLegalName ?? existingPending.BuyerLegalName;
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                qrPayload = BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
                _db.SaasBillingInvoices.Add(new SaasBillingInvoice
                {
                    TenantId = tenantId,
                    InvoiceCode = invoiceCode,
                    PlanCode = planCode,
                    SubTotalVnd = subTotalVnd,
                    VatRatePercent = vatRatePercent,
                    VatAmountVnd = vatAmountVnd,
                    AmountVnd = amountVnd,
                    BuyerTaxCode = buyerTaxCode,
                    BuyerLegalName = buyerLegalName,
                    PaymentProvider = "VIETQR",
                    Status = "UNPAID",
                    QrPayload = qrPayload,
                    CreatedAtUtc = now
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            invoiceCode = $"INV-VQR-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            qrPayload = BuildVietQrPayload(amountVnd, invoiceCode, bankBin, accountNo);
        }

        var (computedSubTotalVnd, _, computedVatAmountVnd, computedTotalVnd) = ComputeVatBreakdownVnd(amountVnd);
        var transferContent = BuildVietQrTransferContent(invoiceCode);
        var expiresAtUtc = createdAt.AddMinutes(30);

        return new
        {
            invoiceCode,
            planCode,
            planName = planSpec.PlanName,
            subTotalVnd = computedSubTotalVnd,
            vatRatePercent = 10.00m,
            vatAmountVnd = computedVatAmountVnd,
            amountVnd = computedTotalVnd,
            totalAmountVnd = computedTotalVnd,
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

    public static bool IsValidVietnamTaxCode(string? rawTaxCode)
    {
        if (string.IsNullOrWhiteSpace(rawTaxCode)) return false;
        // Thông tư 86/2024/TT-BTC: MST doanh nghiệp 10/13 số hoặc CCCD định danh cá nhân 12 số
        return System.Text.RegularExpressions.Regex.IsMatch(rawTaxCode.Trim(), @"^(\d{10}|\d{10}-\d{3}|\d{13}|\d{12})$");
    }

    public async Task<(bool Valid, string? ErrorCode, string? ErrorMessage, object? Payload)> UpdateTaxProfileAsync(
        SaasTaxProfileUpdateRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var taxCode = request?.TaxCode?.Trim() ?? string.Empty;
        if (!IsValidVietnamTaxCode(taxCode))
        {
            return (
                false,
                "INVALID_VIETNAM_TAX_CODE",
                "Mã số thuế không hợp lệ theo Thông tư 86/2024/TT-BTC. Vui lòng nhập MST doanh nghiệp (10 hoặc 13 số) hoặc Số định danh cá nhân / CCCD (12 số).",
                null);
        }

        var tenantId = _db?.CurrentTenantId ?? 1;
        var companyName = string.IsNullOrWhiteSpace(request?.CompanyName) ? $"Doanh nghiệp Thuê bao #{tenantId}" : request.CompanyName.Trim();
        var legalAddress = request?.LegalRegisteredAddress?.Trim();
        var taxpayerType = string.IsNullOrWhiteSpace(request?.TaxpayerType)
            ? (taxCode.Length == 12 ? "HOUSEHOLD_INDIVIDUAL" : "ENTERPRISE")
            : request.TaxpayerType.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;

        if (_db != null)
        {
            var tenant = await _db.SaasTenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == tenantId, cancellationToken);
            if (tenant == null)
            {
                _db.SaasTenants.Add(new SaasTenant
                {
                    TenantId = tenantId,
                    TenantCode = $"TENANT-{tenantId:D3}",
                    CompanyName = companyName,
                    TenantName = companyName,
                    TaxCode = taxCode,
                    TaxpayerType = taxpayerType,
                    LegalRegisteredAddress = legalAddress,
                    LifecycleStatus = "ACTIVE",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
            else
            {
                tenant.CompanyName = companyName;
                tenant.TenantName = companyName;
                tenant.TaxCode = taxCode;
                tenant.TaxpayerType = taxpayerType;
                tenant.LegalRegisteredAddress = legalAddress ?? tenant.LegalRegisteredAddress;
                tenant.UpdatedAtUtc = now;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                TenantId = tenantId,
                UserId = actorUserId,
                EntityName = "SaasTenant",
                EntityId = tenantId,
                Action = "SaaS.UpdateTaxProfile",
                NewValues = $"TaxCode={taxCode}; CompanyName={companyName}; TaxpayerType={taxpayerType}",
                Reason = "Cập nhật hồ sơ thuế thuê bao SaaS theo Thông tư 86/2024/TT-BTC",
                Result = "Success",
                Timestamp = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return (true, null, null, new
        {
            tenantId,
            taxCode,
            companyName,
            legalRegisteredAddress = legalAddress ?? string.Empty,
            taxpayerType,
            updatedAtUtc = now,
            statusLabel = $"Đã lưu hồ sơ thuế ({taxCode} — {companyName}) tuân thủ Thông tư 86/2024/TT-BTC"
        });
    }
}
