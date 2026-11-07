using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/saas-billing")]
[Route("api/saas-billing")]
public sealed class SaasBillingController(
    ISaasBillingService billingService,
    ICurrentUser currentUser) : ControllerBase
{
    private readonly ISaasBillingService _billingService = billingService;

    [HttpGet("plans")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetPlans(CancellationToken ct = default)
    {
        var payload = await _billingService.GetPlansAsync(ct);
        return Ok(payload);
    }

    [HttpGet("invoices")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetInvoices(CancellationToken ct = default)
    {
        var payload = await _billingService.GetInvoicesAsync(ct);
        return Ok(payload);
    }

    [HttpGet("subscription")]
    [HttpGet("vietqr-subscription")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetSubscription(
        [FromQuery] int? simulatedUsageOrders = null,
        [FromQuery] int? maxMonthlyOrders = null,
        CancellationToken ct = default)
    {
        var payload = await _billingService.GetSubscriptionAsync(simulatedUsageOrders, maxMonthlyOrders, ct);
        return Ok(payload);
    }

    [HttpPost("enforce-quota")]
    [HttpPost("quota-check")]
    [IdempotentCommand("SaaS.EnforceQuotaCheck")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    public async Task<IActionResult> EnforceQuota(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SaasSubscriptionQuotaRequest? request = null,
        CancellationToken ct = default)
    {
        if (!this.HasActionPermission(currentUser, AppPermissions.UserManage))
            return Forbid();

        var actorUserId = ResolveActorUserId();
        var db = HttpContext?.RequestServices?.GetService<ERP.Infrastructure.Persistence.ErpKhoDbContext>();
        if ((request?.MonthlyOrderQuota is > 0 || request?.MaxMonthlyOrders is > 0 || request?.CurrentMonthOrderCount.HasValue == true)
            && !await PlatformAuthorizationFilter.IsPlatformCallerAsync(User, actorUserId ?? 0, db, ct))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "PLATFORM_PERMISSION_REQUIRED",
                errorCode = "PLATFORM_PERMISSION_REQUIRED",
                message = "Chỉ Quản trị viên Nền tảng (PLATFORM) mới được phép ghi đè hạn mức đơn hàng tháng (MonthlyOrderQuota)."
            });
        }

        var (found, payload) = await _billingService.EnforceQuotaAsync(request, actorUserId, ct);
        return found ? Ok(payload) : NotFound(payload);
    }

    [HttpPost("vietqr-invoice")]
    [HttpPost("vietqr-invoices")]
    [IdempotentCommand("SaaS.CreateVietQrInvoice")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    public async Task<IActionResult> GenerateVietQrInvoice(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SaasCreateVietQrInvoiceRequest? request = null,
        CancellationToken ct = default)
    {
        if (!this.HasActionPermission(currentUser, AppPermissions.UserManage))
            return Forbid();

        var actorUserId = ResolveActorUserId();
        var payload = await _billingService.CreateVietQrInvoiceAsync(request, actorUserId, ct);
        return Ok(payload);
    }

    [AllowAnonymous]
    [HttpPost("vietqr-webhook")]
    [HttpPost("webhook/vietqr")]
    [HttpPost("~/api/v1/webhooks/billing/vietqr")]
    [IdempotentCommand("SaaS.VietQrBillingWebhook")]
    public async Task<IActionResult> HandleVietQrBillingWebhook(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SaasVietQrWebhookRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.Identity?.IsAuthenticated == true && !this.HasActionPermission(currentUser, AppPermissions.UserManage))
            return Forbid();

        string? providedSig = null;
        if (HttpContext?.Request?.Headers != null &&
            HttpContext.Request.Headers.TryGetValue("X-VietQR-Signature", out var sigValues))
        {
            providedSig = sigValues.ToString().Trim();
        }
        if (string.IsNullOrWhiteSpace(providedSig) && !string.IsNullOrWhiteSpace(request?.ChecksumSignature))
        {
            providedSig = request.ChecksumSignature.Trim();
        }

        var actorUserId = ResolveActorUserId();
        var (invalidReason, errorMessage, payload) = await _billingService.HandleVietQrWebhookAsync(request, actorUserId, ct, providedSig);
        if (invalidReason)
        {
            if (string.Equals(errorMessage, "INVALID_WEBHOOK_SIGNATURE", StringComparison.Ordinal))
            {
                return Unauthorized(new
                {
                    code = "INVALID_WEBHOOK_SIGNATURE",
                    errorCode = "INVALID_WEBHOOK_SIGNATURE",
                    message = "Chữ ký HMAC-SHA256 (X-VietQR-Signature) bị thiếu hoặc không hợp lệ."
                });
            }

            if (errorMessage != null && errorMessage.Contains("INVOICE_UNDERPAID_AMOUNT_MISMATCH", StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new
                {
                    code = "INVOICE_UNDERPAID_AMOUNT_MISMATCH",
                    errorCode = "INVOICE_UNDERPAID_AMOUNT_MISMATCH",
                    message = errorMessage
                });
            }

            return BadRequest(new
            {
                code = "INVALID_VIETQR_WEBHOOK",
                errorCode = "INVALID_VIETQR_WEBHOOK",
                message = errorMessage ?? "Dữ liệu webhook VietQR không hợp lệ."
            });
        }

        return Ok(payload);
    }

    [HttpPut("tax-profile"), HttpPost("tax-profile")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    public async Task<IActionResult> UpdateTaxProfile(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SaasTaxProfileUpdateRequest? request = null,
        CancellationToken ct = default)
    {
        var actorUserId = ResolveActorUserId();
        var (valid, errorCode, errorMessage, payload) = await _billingService.UpdateTaxProfileAsync(request, actorUserId, ct);
        if (!valid)
        {
            return UnprocessableEntity(new
            {
                code = errorCode ?? "INVALID_VIETNAM_TAX_CODE",
                errorCode = errorCode ?? "INVALID_VIETNAM_TAX_CODE",
                message = errorMessage ?? "Mã số thuế không hợp lệ theo Thông tư 86/2024/TT-BTC."
            });
        }

        return Ok(payload);
    }

    private int? ResolveActorUserId()
    {
        var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id");
        return int.TryParse(raw, out var uid) && uid > 0 ? uid : null;
    }
}
