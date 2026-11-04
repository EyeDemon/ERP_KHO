using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    [HttpPost("billing/invoices/{id:int}/reconcile")]
    [PlatformAuthorize(AppPermissions.PlatformBillingReconcile)]
    public async Task<IActionResult> ReconcileBillingInvoiceHttp(
        int id,
        [FromQuery] bool resetToPending = false,
        CancellationToken ct = default)
    {
        ReconcilePlatformInvoiceRequest? parsedRequest = null;
        IFormFile? uploadedFile = null;

        if (HttpContext?.Request?.HasFormContentType == true)
        {
            var form = await HttpContext.Request.ReadFormAsync(ct);
            uploadedFile = ResolveFormEvidenceFile(form.Files);
            parsedRequest = ParseReconcileForm(form);
        }
        else if (HttpContext?.Request?.Body is not null
            && (HttpContext.Request.ContentLength is > 0
                || HttpContext.Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true))
        {
            try
            {
                parsedRequest = await JsonSerializer.DeserializeAsync<ReconcilePlatformInvoiceRequest>(
                    HttpContext.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    ct);
            }
            catch (JsonException)
            {
                return BadRequest(new
                {
                    code = "INVALID_JSON_PAYLOAD",
                    errorCode = "INVALID_JSON_PAYLOAD",
                    message = "Dữ liệu đối soát gửi lên không đúng định dạng JSON."
                });
            }
        }

        return await ReconcileBillingInvoiceCoreAsync(id, parsedRequest, uploadedFile, resetToPending, ct);
    }

    [NonAction]
    public Task<IActionResult> ReconcilePlatformInvoice(
        int id,
        ReconcilePlatformInvoiceRequest? request = null,
        CancellationToken ct = default,
        IFormFile? evidenceFile = null,
        bool resetToPending = false) =>
        ReconcileBillingInvoice(id, request, resetToPending, ct, evidenceFile);

    [NonAction]
    public async Task<IActionResult> ReconcileBillingInvoice(
        int id,
        ReconcilePlatformInvoiceRequest? request = null,
        bool resetToPending = false,
        CancellationToken ct = default,
        IFormFile? evidenceFile = null)
    {
        var uploadedFile = evidenceFile;
        if (uploadedFile is null && HttpContext?.Request?.HasFormContentType == true)
        {
            var form = await HttpContext.Request.ReadFormAsync(ct);
            uploadedFile = ResolveFormEvidenceFile(form.Files);
            request ??= ParseReconcileForm(form);
        }

        return await ReconcileBillingInvoiceCoreAsync(id, request, uploadedFile, resetToPending, ct);
    }

    private static IFormFile? ResolveFormEvidenceFile(IFormFileCollection files) =>
        files.GetFile("file")
        ?? files.GetFile("evidenceFile")
        ?? files.GetFile("attachment")
        ?? files.GetFile("attachmentFile")
        ?? (files.Count > 0 ? files[0] : null);

    private static ReconcilePlatformInvoiceRequest ParseReconcileForm(IFormCollection form)
    {
        string? GetStr(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (form.TryGetValue(k, out var val) && !string.IsNullOrWhiteSpace(val.ToString()))
                {
                    return val.ToString().Trim();
                }
            }
            return null;
        }

        decimal? parsedAmt = decimal.TryParse(
            GetStr("paidAmountVnd", "PaidAmountVnd", "receivedAmountVnd", "ReceivedAmountVnd", "amountVnd"),
            NumberStyles.Any, CultureInfo.InvariantCulture, out var amtVal) ? amtVal : null;
        bool? allowVariance = bool.TryParse(
            GetStr("confirmUnderpaymentPartial", "ConfirmUnderpaymentPartial", "allowPartialOrOverpayment", "AllowPartialOrOverpayment"),
            out var allowVal) ? allowVal : null;
        bool? resetFlag = bool.TryParse(GetStr("resetToPending", "ResetToPending"), out var rstVal) ? rstVal : null;
        var creditTs = GetStr("bankCreditTimestamp", "BankCreditTimestamp", "bankCreditTimestampVi", "BankCreditTimestampVi");
        var reasonStr = GetStr("reason", "Reason", "reasonNote", "ReasonNote", "note", "Note");

        return new ReconcilePlatformInvoiceRequest(
            BankTransactionRef: GetStr("bankTransactionRef", "BankTransactionRef"),
            BankCreditTimestampVi: creditTs,
            ReceivedAmountVnd: parsedAmt,
            ReasonNote: reasonStr,
            AttachmentReference: GetStr("attachmentReference", "AttachmentReference"),
            ApproverCode: GetStr("approverCode", "ApproverCode"),
            AllowPartialOrOverpayment: allowVariance,
            ResetToPending: resetFlag,
            Status: GetStr("status", "Status"),
            Note: GetStr("note", "Note"),
            ApproverUsername: GetStr("approverUsername", "ApproverUsername"),
            ReconciledTimestampVi: GetStr("reconciledTimestampVi", "ReconciledTimestampVi"),
            PaidAmountVnd: parsedAmt,
            BankCreditTimestamp: creditTs,
            Reason: reasonStr,
            ConfirmUnderpaymentPartial: allowVariance);
    }

    private async Task<IActionResult> ReconcileBillingInvoiceCoreAsync(
        int id,
        ReconcilePlatformInvoiceRequest? request,
        IFormFile? uploadedFile,
        bool resetToPending,
        CancellationToken ct)
    {
        var (effectiveRole, isDowngradedPreview) = ResolveEffectivePlatformRoleWithPreview();
        var isAllowedBillingRole = effectiveRole is "SuperAdmin" or "PlatformSuperAdmin" or "PlatformOwner"
            or "PlatformAdmin" or "Admin" or "BillingAdmin" or "PlatformBillingAdmin";
        if (!isAllowedBillingRole || EnsureCallerHasPlatformPermission(AppPermissions.PlatformBillingReconcile) is not null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "BILLING_RECONCILE_FORBIDDEN",
                errorCode = "BILLING_RECONCILE_FORBIDDEN",
                message = "Tài khoản không có quyền thực hiện đối soát thanh toán (platform.billing.reconcile)."
            });
        }

        var invoice = await db.SaasBillingInvoices.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
        {
            return NotFound(new
            {
                code = "INVOICE_NOT_FOUND",
                errorCode = "INVOICE_NOT_FOUND",
                message = "Không tìm thấy hóa đơn thuê bao cần đối soát."
            });
        }

        var shouldReset = resetToPending
            || request?.ResetToPending == true
            || string.Equals(request?.Status, "UNPAID", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request?.Status, "PENDING", StringComparison.OrdinalIgnoreCase);

        var nowUtc = DateTime.UtcNow;
        var actorUsername = ResolveActorUsername();
        if (shouldReset)
        {
            return await ResetInvoiceReconciliationAsync(invoice, actorUsername, nowUtc, ct);
        }

        var bankRef = request?.BankTransactionRef?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(bankRef))
        {
            return UnprocessableEntity(new
            {
                code = "RECONCILE_BANK_REF_REQUIRED",
                errorCode = "RECONCILE_BANK_REF_REQUIRED",
                message = "Bắt buộc nhập mã giao dịch ngân hàng (tối thiểu 6 ký tự) khi xác nhận đối soát thủ công."
            });
        }

        if (bankRef.Length < 6)
        {
            return UnprocessableEntity(new
            {
                code = "INVALID_BANK_TRANSACTION_REF",
                errorCode = "INVALID_BANK_TRANSACTION_REF",
                message = "Mã giao dịch ngân hàng bắt buộc tối thiểu 6 ký tự khi xác nhận đối soát thủ công."
            });
        }

        if (await SaasBillingService.HasDuplicateBankTransactionRefAsync(db, id, bankRef, ct))
        {
            return Conflict(new
            {
                code = "BILLING_DUPLICATE_BANK_TRANSFER_REF",
                errorCode = "BILLING_DUPLICATE_BANK_TRANSFER_REF",
                message = $"409 Conflict — Mã giao dịch ngân hàng đã tồn tại ({bankRef})."
            });
        }

        var (subTotal, vatRate, vatAmt, totalAmt) = SaasBillingService.ComputeVatBreakdownVnd(invoice.AmountVnd, invoice.SubTotalVnd, invoice.VatAmountVnd);
        var paidAmount = request?.PaidAmountVnd ?? request?.ReceivedAmountVnd;
        var allowVariance = request?.ConfirmUnderpaymentPartial == true || request?.AllowPartialOrOverpayment == true;
        if (paidAmount is null || paidAmount.Value <= 0m || (paidAmount.Value != totalAmt && !allowVariance))
        {
            return UnprocessableEntity(new
            {
                code = "RECONCILE_AMOUNT_MISMATCH",
                errorCode = "RECONCILE_AMOUNT_MISMATCH",
                message = $"Số tiền thực nhận không khớp với tổng thanh toán hóa đơn ({totalAmt:N0} ₫)."
            });
        }

        var rawCreditTs = request?.BankCreditTimestamp ?? request?.BankCreditTimestampVi;
        if (!TryParseBankCreditTimestamp(rawCreditTs, out var parsedCreditUtc, out var creditTimestampVi))
        {
            return UnprocessableEntity(new
            {
                code = "RECONCILE_INVALID_CREDIT_TIMESTAMP",
                errorCode = "RECONCILE_INVALID_CREDIT_TIMESTAMP",
                message = "Thời điểm ngân hàng ghi có phải đúng định dạng dd/MM/yyyy HH:mm và không được vượt quá thời điểm hiện tại."
            });
        }

        var reason = (request?.Reason ?? request?.ReasonNote ?? request?.Note)?.Trim() ?? string.Empty;
        if (reason.Length < 10)
        {
            return UnprocessableEntity(new
            {
                code = "REASON_REQUIRED",
                errorCode = "REASON_REQUIRED",
                message = "Ghi chú kiểm toán đối soát bắt buộc tối thiểu 10 ký tự."
            });
        }

        var isSoloOwnerSuperAdmin = !isDowngradedPreview && IsTrueCallerSuperAdmin(out _);
        var rawApprover = (request?.ApproverUsername ?? request?.ApproverCode)?.Trim() ?? string.Empty;
        var hasExplicitFourEyesApprover = !string.IsNullOrWhiteSpace(rawApprover)
            && !string.Equals(rawApprover, actorUsername, StringComparison.OrdinalIgnoreCase);
        string verifiedApproverUsername;
        if (!isSoloOwnerSuperAdmin)
        {
            if (!hasExplicitFourEyesApprover)
            {
                return UnprocessableEntity(new
                {
                    code = "BILLING_SOD_SELF_APPROVAL_FORBIDDEN",
                    errorCode = "BILLING_SOD_SELF_APPROVAL_FORBIDDEN",
                    message = "Vi phạm nguyên tắc kiểm soát 4-mắt (SoD): Người phê duyệt không được trùng với người lập đối soát."
                });
            }

            var isCanonicalApproverCode = rawApprover.Equals("ktt_nguyen_mai", StringComparison.OrdinalIgnoreCase)
                || rawApprover.Equals("fin_tran_hung", StringComparison.OrdinalIgnoreCase);
            var approverExists = isCanonicalApproverCode || await db.Users.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(u => u.IsActive && u.Username == rawApprover && u.SecurityDomain == "PLATFORM", ct);
            if (!approverExists)
            {
                return UnprocessableEntity(new
                {
                    code = "BILLING_SOD_SELF_APPROVAL_FORBIDDEN",
                    errorCode = "BILLING_SOD_SELF_APPROVAL_FORBIDDEN",
                    message = "Người phê duyệt đối chiếu độc lập không tồn tại hoặc không thuộc miền quản trị PLATFORM."
                });
            }
            verifiedApproverUsername = rawApprover;
        }
        else
        {
            verifiedApproverUsername = string.IsNullOrWhiteSpace(rawApprover) ? actorUsername : rawApprover;
        }

        var fileCheck = FileUploadSecurityValidator.ValidateReconcileEvidenceFile(uploadedFile);
        if (!fileCheck.IsValid)
        {
            var errPayload = new { code = fileCheck.ErrorCode, errorCode = fileCheck.ErrorCode, message = fileCheck.Message };
            return fileCheck.StatusCode == StatusCodes.Status422UnprocessableEntity
                ? UnprocessableEntity(errPayload)
                : StatusCode(fileCheck.StatusCode, errPayload);
        }

        return await CommitManualInvoiceReconcileAsync(
            invoice, uploadedFile!, bankRef, paidAmount.Value, subTotal, vatRate, vatAmt, totalAmt,
            parsedCreditUtc, creditTimestampVi, reason, actorUsername, verifiedApproverUsername,
            isSoloOwnerSuperAdmin, hasExplicitFourEyesApprover, request?.ReconciledTimestampVi, nowUtc, ct);
    }
}
