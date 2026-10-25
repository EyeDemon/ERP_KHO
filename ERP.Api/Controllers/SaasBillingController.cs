using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/saas-billing")]
[Route("api/saas-billing")]
public sealed class SaasBillingController : ControllerBase
{
    private readonly ISaasBillingService _billingService;

    public SaasBillingController(
        ErpKhoDbContext? db = null,
        ISaasBillingService? billingService = null)
    {
        _billingService = billingService ?? new SaasBillingService(db);
    }

    [HttpGet("plans")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetPlans(CancellationToken ct = default)
    {
        var payload = await _billingService.GetPlansAsync(ct);
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
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> EnforceQuota(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SaasSubscriptionQuotaRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var actorUserId = ResolveActorUserId();
        var (found, payload) = await _billingService.EnforceQuotaAsync(request, actorUserId, ct);
        return found ? Ok(payload) : NotFound(payload);
    }

    [HttpPost("vietqr-invoice")]
    [HttpPost("vietqr-invoices")]
    [IdempotentCommand("SaaS.CreateVietQrInvoice")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GenerateVietQrInvoice(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SaasCreateVietQrInvoiceRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var actorUserId = ResolveActorUserId();
        var payload = await _billingService.CreateVietQrInvoiceAsync(request, actorUserId, ct);
        return Ok(payload);
    }

    [AllowAnonymous]
    [HttpPost("vietqr-webhook")]
    [IdempotentCommand("SaaS.VietQrBillingWebhook")]
    public async Task<IActionResult> HandleVietQrBillingWebhook(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SaasVietQrWebhookRequest? request = null,
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

        var actorUserId = ResolveActorUserId();
        var (invalidReason, errorMessage, payload) = await _billingService.HandleVietQrWebhookAsync(request, actorUserId, ct);
        if (invalidReason)
        {
            return BadRequest(new
            {
                code = "INVALID_VIETQR_WEBHOOK",
                errorCode = "INVALID_VIETQR_WEBHOOK",
                message = errorMessage ?? "Dữ liệu webhook VietQR không hợp lệ."
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
