using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/v1/saas-billing")]
[Route("api/saas-billing")]
public sealed class SaasBillingController : ControllerBase
{
    private readonly ErpKhoDbContext _db;

    public SaasBillingController(ErpKhoDbContext db)
    {
        _db = db;
    }

    private PermissionsController CreatePermissionsForwarder() => new(_db)
    {
        ControllerContext = ControllerContext
    };

    [HttpGet("subscription")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public Task<IActionResult> GetSubscription(
        [FromQuery] int? simulatedUsageOrders = null,
        [FromQuery] int? maxMonthlyOrders = null,
        CancellationToken ct = default)
        => CreatePermissionsForwarder().GetSaaSSubscriptionBilling(simulatedUsageOrders, maxMonthlyOrders, ct);

    [HttpPost("enforce-quota")]
    [IdempotentCommand("SaaS.EnforceQuotaCheck")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public Task<IActionResult> EnforceQuota(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PermissionsController.EnforceSubscriptionQuotaRequest? request = null,
        CancellationToken ct = default)
        => CreatePermissionsForwarder().EnforceSubscriptionQuota(request, ct);

    [HttpPost("vietqr-invoice")]
    [IdempotentCommand("SaaS.CreateVietQrInvoice")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public Task<IActionResult> GenerateVietQrInvoice(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PermissionsController.CreateVietQrInvoiceRequest? request = null,
        CancellationToken ct = default)
        => CreatePermissionsForwarder().CreateVietQrInvoice(request, ct);

    [AllowAnonymous]
    [HttpPost("vietqr-webhook")]
    [IdempotentCommand("SaaS.VietQrBillingWebhook")]
    public Task<IActionResult> HandleVietQrBillingWebhook(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PermissionsController.VietQrWebhookRequest? request = null,
        CancellationToken ct = default)
        => CreatePermissionsForwarder().HandleVietQrBillingWebhook(request, ct);
}
