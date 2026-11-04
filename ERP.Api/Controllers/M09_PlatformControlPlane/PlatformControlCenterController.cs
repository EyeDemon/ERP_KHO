using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/v1/platform")]
[Route("api/platform")]
[Authorize]
[PlatformAuthorize]
public sealed partial class PlatformControlCenterController(
    ErpKhoDbContext db,
    IAuthService authService,
    IWebHostEnvironment environment,
    SessionSecurityOptions sessionOptions) : ControllerBase
{
    private const string PlatformRefreshCookieName = "erp_platform_refresh";
    private IConfiguration? configuration => HttpContext?.RequestServices?.GetService<IConfiguration>();

    private async Task<(int RevokedCustomerSessions, int RevokedImpersonationSessions)> RevokeTenantSessionsOnSuspensionAsync(
        int tenantId,
        string actorUsername,
        DateTime now,
        CancellationToken ct)
    {
        var activeCustomerSessions = await db.UserSessions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var session in activeCustomerSessions)
        {
            session.RevokedAt = now;
            session.RevokedBy = actorUsername;
            session.RevokeReason = $"Tenant #{tenantId} suspended";
        }

        var activeImpSessions = await db.SupportImpersonationSessions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.Status == "ACTIVE" && s.RevokedAtUtc == null)
            .ToListAsync(ct);
        foreach (var imp in activeImpSessions)
        {
            imp.Status = "REVOKED";
            imp.RevokedAtUtc = now;
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = GetActorUserId(),
            TenantId = tenantId,
            SecurityDomain = "PLATFORM",
            Action = "PLATFORM_SUPPORT_IMPERSONATION_STOPPED",
            EntityName = "SupportImpersonationSession",
            EntityId = tenantId,
            OldValues = JsonSerializer.Serialize(new { tenantId, sessionActive = activeImpSessions.Count > 0 }),
            NewValues = JsonSerializer.Serialize(new { tenantId, sessionActive = false, stopped = true, autoRevokedOnSuspension = true, revokedCount = activeImpSessions.Count, stoppedAtUtc = now }),
            Reason = $"Tự động thu hồi phiên Hỗ trợ Kỹ thuật khi tạm khóa doanh nghiệp #{tenantId}",
            Result = "Success",
            CorrelationId = ResolveCorrelationId(),
            Timestamp = now
        });

        return (activeCustomerSessions.Count, activeImpSessions.Count);
    }

    private async Task<IActionResult> ResetInvoiceReconciliationAsync(
        SaasBillingInvoice invoice,
        string actorUsername,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;

        invoice.Status = "UNPAID";
        invoice.PaidAtUtc = null;
        invoice.PaymentProvider = "VIETQR";

        db.AuditLogs.Add(new AuditLog
        {
            UserId = GetActorUserId(),
            TenantId = invoice.TenantId > 0 ? invoice.TenantId : 1,
            SecurityDomain = "PLATFORM",
            Action = "PLATFORM_BILLING_RECONCILE_RESET",
            EntityName = "SaasBillingInvoice",
            EntityId = invoice.Id,
            OldValues = JsonSerializer.Serialize(new { invoiceId = invoice.Id, invoiceCode = invoice.InvoiceCode }),
            NewValues = JsonSerializer.Serialize(new
            {
                invoiceId = invoice.Id,
                invoiceCode = invoice.InvoiceCode,
                status = "UNPAID",
                actorUsername,
                resetAtUtc = nowUtc
            }),
            Reason = "Đặt lại trạng thái hóa đơn thuê bao về Chờ thanh toán",
            Result = "Success",
            CorrelationId = ResolveCorrelationId(),
            Timestamp = nowUtc
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return Ok(new
        {
            invoiceId = invoice.Id,
            id = invoice.Id,
            invoiceNumber = invoice.InvoiceCode,
            invoiceCode = invoice.InvoiceCode,
            status = "UNPAID",
            statusLabelVi = "Chờ thanh toán",
            auditAction = "PLATFORM_BILLING_RECONCILE_RESET",
            message = "Đã đặt lại trạng thái hóa đơn về Chờ thanh toán."
        });
    }

    private async Task<IActionResult> CommitManualInvoiceReconcileAsync(
        SaasBillingInvoice invoice,
        IFormFile uploadedFile,
        string bankRef,
        decimal paidAmount,
        decimal subTotal,
        decimal vatRate,
        decimal vatAmt,
        decimal totalAmt,
        DateTime parsedCreditUtc,
        string creditTimestampVi,
        string reason,
        string actorUsername,
        string verifiedApproverUsername,
        bool isSoloOwnerSuperAdmin,
        bool hasExplicitFourEyesApprover,
        string? requestedReconciledTimestampVi,
        DateTime nowUtc,
        CancellationToken ct)
    {
        await using var rawStream = uploadedFile.OpenReadStream();
        if (rawStream.CanSeek)
        {
            rawStream.Position = 0;
        }
        using var ms = new MemoryStream();
        await rawStream.CopyToAsync(ms, ct);
        var evidenceSha256 = Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();
        var safeFileName = Path.GetFileName(uploadedFile.FileName.Trim());

        var varianceVnd = paidAmount - totalAmt;
        var targetStatus = varianceVnd < 0m ? "PARTIALLY_PAID" : "PAID";
        var remainingAmountVnd = varianceVnd < 0m ? totalAmt - paidAmount : 0m;
        var creditCarryoverVnd = varianceVnd > 0m ? varianceVnd : 0m;
        var varianceStatus = varianceVnd == 0m
            ? "EXACT_MATCH"
            : (varianceVnd < 0m ? "PARTIAL_PAYMENT" : "OVERPAYMENT_CREDIT");

        var vnNow = ConvertUtcToVietnamLocal(nowUtc);
        var reconciledTimestampVi = !string.IsNullOrWhiteSpace(requestedReconciledTimestampVi)
            ? requestedReconciledTimestampVi.Trim()
            : vnNow.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var approverDisplay = SaasBillingService.ResolveApproverDisplayName(verifiedApproverUsername);
        var ownerAuditStampVi = isSoloOwnerSuperAdmin && !hasExplicitFourEyesApprover
            ? $"Xác nhận thủ công bởi Chủ dự án lúc {vnNow:HH:mm dd/MM/yyyy} (Mã giao dịch: {bankRef})"
            : $"Xác nhận thủ công bởi {actorUsername} • Duyệt bởi: {approverDisplay} (Mã giao dịch: {bankRef})";

        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;

        invoice.SubTotalVnd = subTotal;
        invoice.VatRatePercent = vatRate;
        invoice.VatAmountVnd = vatAmt;
        invoice.AmountVnd = totalAmt;
        invoice.Status = targetStatus;
        invoice.PaymentProvider = "BANK_TRANSFER_MANUAL";
        invoice.PaidAtUtc = targetStatus == "PAID" ? parsedCreditUtc : null;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = GetActorUserId(),
            TenantId = invoice.TenantId > 0 ? invoice.TenantId : 1,
            SecurityDomain = "PLATFORM",
            Action = "PLATFORM_BILLING_MANUAL_RECONCILE",
            EntityName = "SaasBillingInvoice",
            EntityId = invoice.Id,
            OldValues = JsonSerializer.Serialize(new { invoiceId = invoice.Id, invoiceCode = invoice.InvoiceCode }),
            NewValues = JsonSerializer.Serialize(new
            {
                invoiceId = invoice.Id,
                invoiceCode = invoice.InvoiceCode,
                BankTransactionRef = bankRef,
                PaidAmountVnd = paidAmount,
                receivedAmountVnd = paidAmount,
                ExpectedAmountVnd = totalAmt,
                BankCreditTimestampUtc = parsedCreditUtc,
                bankCreditTimestampVi = creditTimestampVi,
                AttachmentReference = safeFileName,
                EvidenceSha256 = evidenceSha256,
                attachmentSha256 = evidenceSha256,
                ReconciledByUsername = actorUsername,
                actorUsername,
                ApprovedByUsername = verifiedApproverUsername,
                approverUsername = verifiedApproverUsername,
                approverCode = approverDisplay,
                ReasonNote = reason,
                ReconciledAtUtc = nowUtc,
                reconciledTimestampVi,
                TargetStatus = targetStatus,
                status = targetStatus,
                remainingAmountVnd,
                creditCarryoverVnd,
                varianceStatus,
                OwnerAuditStampVi = ownerAuditStampVi,
                settlementSummaryVi = ownerAuditStampVi
            }),
            Reason = reason,
            Result = "Success",
            CorrelationId = ResolveCorrelationId(),
            Timestamp = nowUtc
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        var (_, bankName, _, accountNo, accountName, _) = ResolveVietQrBeneficiary();
        return Ok(new
        {
            invoiceId = invoice.Id,
            id = invoice.Id,
            invoiceNumber = invoice.InvoiceCode,
            invoiceCode = invoice.InvoiceCode,
            status = targetStatus,
            currentCyclePaid = targetStatus == "PAID" && remainingAmountVnd <= 0m,
            statusLabelVi = targetStatus == "PAID" ? "Đã tất toán VietQR" : "Thanh toán một phần",
            settlementSource = "MANUAL_RECONCILIATION",
            bankTransactionRef = bankRef,
            bankCreditTimestampVi = creditTimestampVi,
            reconciledTimestampVi,
            attachmentFileName = safeFileName,
            attachmentReference = safeFileName,
            evidenceSha256,
            attachmentSha256 = evidenceSha256,
            ownerAuditStampVi,
            settlementSummaryVi = ownerAuditStampVi,
            reconciledBy = actorUsername,
            approvedBy = verifiedApproverUsername,
            approverUsername = verifiedApproverUsername,
            approverCode = approverDisplay,
            reasonNote = reason,
            paidAmountVnd = paidAmount,
            receivedAmountVnd = paidAmount,
            remainingAmountVnd,
            creditCarryoverVnd,
            varianceStatus,
            subtotalBeforeVatVnd = subTotal,
            subTotalVnd = subTotal,
            vatRatePercent = vatRate,
            vatAmountVnd = vatAmt,
            totalAmountVnd = totalAmt,
            amountVnd = totalAmt,
            paidAtUtc = invoice.PaidAtUtc,
            beneficiaryBankName = bankName,
            beneficiaryAccountNumber = accountNo,
            beneficiaryAccountName = accountName,
            auditAction = "PLATFORM_BILLING_MANUAL_RECONCILE",
            message = ownerAuditStampVi
        });
    }
}
