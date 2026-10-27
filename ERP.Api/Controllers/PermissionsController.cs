using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Controllers;

[ApiController, Route("api/permissions"), Route("api/v1/permissions"), Authorize]
public sealed class PermissionsController : ControllerBase
{
    private readonly IPermissionOperationsService _permissionOps;
    private readonly IWarehouseAuthorizationService? _warehouseAuth;
    private readonly ICurrentUser? _currentUser;

    [ActivatorUtilitiesConstructor]
    public PermissionsController(
        IPermissionOperationsService permissionOps,
        IWarehouseAuthorizationService? warehouseAuth = null,
        ICurrentUser? currentUser = null)
    {
        _permissionOps = permissionOps;
        _warehouseAuth = warehouseAuth;
        _currentUser = currentUser;
    }

    public PermissionsController(
        IPermissionOperationsService permissionOps,
        ICurrentUser currentUser)
        : this(permissionOps, null, currentUser)
    {
    }

    [HttpGet, PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> Catalog(CancellationToken ct) =>
        this.ToActionResult(await _permissionOps.CatalogAsync(ct));

    [HttpGet("roles"), PermissionAuthorize(AppPermissions.RoleRead)]
    public async Task<IActionResult> Roles(CancellationToken ct) =>
        this.ToActionResult(await _permissionOps.RolesAsync(ct));

    [HttpGet("users"), PermissionAuthorize(AppPermissions.UserRead)]
    public async Task<IActionResult> Users(CancellationToken ct) =>
        this.ToActionResult(await _permissionOps.UsersAsync(ct));

    [HttpGet("audit-logs"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> AuditLogs(CancellationToken ct) =>
        this.ToActionResult(await _permissionOps.AuditLogsAsync(
            User?.IsInRole(AppRoles.Admin) == true,
            _warehouseAuth,
            ct));

    [HttpGet("enterprise-governance-suite"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public IActionResult EnterpriseGovernanceSuite() =>
        this.ToActionResult(_permissionOps.EnterpriseGovernanceSuite());

    public sealed record VerifyTenantQuotasRequest(
        string? TenantCode = null,
        int? MaxWarehouses = null,
        int? MaxUsers = null,
        int? MaxSkus = null) : VerifyTenantQuotasInput(TenantCode, MaxWarehouses, MaxUsers, MaxSkus);

    [HttpGet("tenants-summary"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetTenantsSummary(CancellationToken ct = default) =>
        this.ToActionResult(await _permissionOps.GetTenantsSummaryAsync(ct));

    [HttpPost("tenants/verify-quotas")]
    [IdempotentCommand("Permissions.VerifyTenantQuotas")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> VerifyTenantQuotas(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyTenantQuotasRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        return this.ToActionResult(await _permissionOps.VerifyTenantQuotasAsync(request, GetActorUserId(), ct));
    }

    public sealed record ProvisionTenantRequest(
        string? CompanyName = null,
        string? TaxCode = null,
        string? OwnerEmail = null,
        string? IndustryTemplate = null,
        string? PlanCode = null,
        bool AllocateNewTenant = false) : ProvisionTenantInput(
            CompanyName,
            TaxCode,
            OwnerEmail,
            IndustryTemplate,
            PlanCode,
            AllocateNewTenant);

    public sealed record SeedDemoDataRequest(
        string? IndustryTemplate = null,
        bool PurgeDemoData = false,
        int? WarehouseId = null) : SeedDemoDataInput(IndustryTemplate, PurgeDemoData, WarehouseId);

    public sealed record EnforceSubscriptionQuotaRequest(
        int? CurrentMonthOrderCount = null,
        int? MonthlyOrderQuota = null,
        string? OperationType = null) : EnforceSubscriptionQuotaInput(
            CurrentMonthOrderCount,
            MonthlyOrderQuota,
            OperationType);

    public sealed record CreateVietQrInvoiceRequest(
        string? PlanCode = null,
        decimal? AmountVnd = null,
        int? BillingMonths = null) : CreateVietQrInvoiceInput(PlanCode, AmountVnd, BillingMonths);

    public sealed record VietQrWebhookRequest(
        string? InvoiceCode = null,
        string? PlanCode = null,
        decimal? PaidAmountVnd = null,
        string? BankTransactionRef = null) : VietQrWebhookInput(
            InvoiceCode,
            PlanCode,
            PaidAmountVnd,
            BankTransactionRef);

    [HttpPost("~/api/v1/saas/tenants/provision")]
    [HttpPost("~/api/saas/tenants/provision")]
    [HttpPost("saas/tenants/provision")]
    [HttpPost("saas-tenants/provision")]
    [HttpPost("~/api/v1/saas/onboarding/provision-tenant")]
    [AllowAnonymous]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.ProvisionTenant")]
    public async Task<IActionResult> ProvisionSaaSTenant(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ProvisionTenantRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        return this.ToActionResult(await _permissionOps.ProvisionSaaSTenantAsync(request, ct));
    }

    [HttpGet("~/api/v1/saas/setup-wizard")]
    [HttpGet("~/api/saas/setup-wizard")]
    [HttpGet("saas/setup-wizard")]
    [HttpGet("saas-setup-wizard")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetSaaSSetupWizard(CancellationToken ct = default) =>
        this.ToActionResult(await _permissionOps.GetSaaSSetupWizardAsync(ct));

    [HttpPost("~/api/v1/saas/demo-seed")]
    [HttpPost("~/api/saas/demo-seed")]
    [HttpPost("saas/demo-seed")]
    [HttpPost("saas/demo-data/seed")]
    [HttpPost("saas-demo-seed")]
    [HttpPost("~/api/v1/saas/onboarding/seed-industry-template")]
    [HttpPost("~/api/v1/saas/onboarding/purge-demo-data")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.SeedDemoData")]
    public async Task<IActionResult> SeedSaaSDemoData(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] SeedDemoDataRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        return this.ToActionResult(await _permissionOps.SeedSaaSDemoDataAsync(request, ct));
    }

    [HttpGet("saas/subscription-billing")]
    [HttpGet("saas-subscription-billing")]
    [HttpGet("~/api/v1/saas/subscriptions/billing")]
    [HttpGet("~/api/v1/saas/subscriptions/current")]
    [HttpGet("~/api/saas/subscriptions/current")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetSaaSSubscriptionBilling(
        [FromQuery] int? simulatedUsageOrders = null,
        [FromQuery] int? maxMonthlyOrders = null,
        CancellationToken ct = default) =>
        this.ToActionResult(await _permissionOps.GetSaaSSubscriptionBillingAsync(simulatedUsageOrders, maxMonthlyOrders, ct));

    [HttpPost("saas/subscription-billing/enforce-quota")]
    [HttpPost("saas-subscription-billing/enforce-quota")]
    [HttpPost("saas-subscription-billing/enforce-quota-check")]
    [HttpPost("~/api/v1/saas/subscriptions/enforce-quota")]
    [HttpPost("~/api/v1/saas/subscriptions/enforce-quota-check")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.EnforceQuotaCheck")]
    public async Task<IActionResult> EnforceSubscriptionQuota(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EnforceSubscriptionQuotaRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        return this.ToActionResult(await _permissionOps.EnforceSubscriptionQuotaAsync(request, ct));
    }

    [HttpPost("saas/subscription-billing/vietqr-invoice")]
    [HttpPost("saas-subscription-billing/vietqr-invoice")]
    [HttpPost("saas/subscription-billing/create-vietqr-invoice")]
    [HttpPost("saas-subscription-billing/create-vietqr-invoice")]
    [HttpPost("~/api/v1/saas/subscriptions/create-vietqr-invoice")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.CreateVietQrInvoice")]
    public async Task<IActionResult> CreateVietQrInvoice(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] CreateVietQrInvoiceRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        return this.ToActionResult(await _permissionOps.CreateVietQrInvoiceAsync(request, ct));
    }

    [HttpPost("saas/subscription-billing/webhook-vietqr")]
    [HttpPost("saas-subscription-billing/webhook-vietqr")]
    [HttpPost("~/api/v1/saas/subscriptions/webhook-vietqr")]
    [HttpPost("~/api/v1/webhooks/billing/vietqr")]
    [HttpPost("webhooks/billing/vietqr")]
    [AllowAnonymous]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    [IdempotentCommand("SaaS.VietQrBillingWebhook")]
    public async Task<IActionResult> HandleVietQrBillingWebhook(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VietQrWebhookRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        string? providedSig = null;
        if (HttpContext?.Request?.Headers != null &&
            HttpContext.Request.Headers.TryGetValue("X-VietQR-Signature", out var sigValues))
        {
            providedSig = sigValues.ToString().Trim();
        }

        return this.ToActionResult(await _permissionOps.HandleVietQrBillingWebhookAsync(
            request,
            providedSig,
            GetActorUserId(),
            ct));
    }

    [HttpGet("onboarding-wizard/status"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetOnboardingWizardStatus(CancellationToken ct = default) =>
        this.ToActionResult(await _permissionOps.GetOnboardingWizardStatusAsync(ct));

    [HttpGet("compliance-evidence-bundle"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public IActionResult ComplianceEvidenceBundle() =>
        this.ToActionResult(_permissionOps.ComplianceEvidenceBundle());

    [HttpPost("roles/{roleId:int}/grants"), IdempotentCommand("Permission.Grant"), PermissionAuthorize(AppPermissions.PermissionAssign)]
    public async Task<IActionResult> Grant(int roleId, PermissionGrantDto dto, CancellationToken ct) =>
        this.ToActionResult(await _permissionOps.ChangeRolePermissionAsync(roleId, dto.PermissionCode, dto.RowVersion, true, GetActorUserId(), ct));

    [HttpDelete("roles/{roleId:int}/grants/{permissionCode}"), IdempotentCommand("Permission.Revoke"), PermissionAuthorize(AppPermissions.PermissionAssign)]
    public async Task<IActionResult> Revoke(int roleId, string permissionCode, [FromBody] PermissionRevokeDto dto, CancellationToken ct) =>
        this.ToActionResult(await _permissionOps.ChangeRolePermissionAsync(roleId, permissionCode, dto.RowVersion, false, GetActorUserId(), ct));

    private int? GetActorUserId() =>
        int.TryParse(
            User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("Id"),
            out var uid) ? uid : (_currentUser is { UserId: > 0 } ? _currentUser.UserId : null);
}

public sealed record PermissionGrantDto(string PermissionCode, byte[] RowVersion);
public sealed record PermissionRevokeDto(byte[] RowVersion);
