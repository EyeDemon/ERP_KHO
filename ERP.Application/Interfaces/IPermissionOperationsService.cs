using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public record VerifyTenantQuotasInput(
    string? TenantCode = null,
    int? MaxWarehouses = null,
    int? MaxUsers = null,
    int? MaxSkus = null);

public record ProvisionTenantInput(
    string? CompanyName = null,
    string? TaxCode = null,
    string? OwnerEmail = null,
    string? IndustryTemplate = null,
    string? PlanCode = null,
    bool AllocateNewTenant = false);

public record SeedDemoDataInput(
    string? IndustryTemplate = null,
    bool PurgeDemoData = false,
    int? WarehouseId = null);

public record EnforceSubscriptionQuotaInput(
    int? CurrentMonthOrderCount = null,
    int? MonthlyOrderQuota = null,
    string? OperationType = null);

public record CreateVietQrInvoiceInput(
    string? PlanCode = null,
    decimal? AmountVnd = null,
    int? BillingMonths = null);

public record VietQrWebhookInput(
    string? InvoiceCode = null,
    string? PlanCode = null,
    decimal? PaidAmountVnd = null,
    string? BankTransactionRef = null);

public record UpdateRolePermissionsRequest(IReadOnlyList<string> Permissions, byte[]? RowVersion = null);
public record UpdateUserRoleRequest(int RoleId);
public record PermissionCatalogQueryRequest(string? Search = null);
public record PermissionAuditLogQueryRequest(int? WarehouseId = null, int Limit = 20);
public record CreateUserAccountRequest(string Username, string Password, int RoleId, string? FullName = null, string? Email = null);
public record ResetUserPasswordRequest(string NewPassword);
public record ToggleUserStatusRequest(bool IsActive);

public interface IPermissionOperationsService
{
    Task<WmsEndpointOutcome> CatalogAsync(CancellationToken ct = default);
    Task<WmsEndpointOutcome> RolesAsync(CancellationToken ct = default);
    Task<WmsEndpointOutcome> UsersAsync(CancellationToken ct = default);
    Task<WmsEndpointOutcome> AuditLogsAsync(
        bool isAdmin = false,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken ct = default);
    WmsEndpointOutcome EnterpriseGovernanceSuite();
    Task<WmsEndpointOutcome> GetTenantsSummaryAsync(CancellationToken ct = default);
    Task<WmsEndpointOutcome> VerifyTenantQuotasAsync(
        VerifyTenantQuotasInput? request = null,
        int? actorUserId = null,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> ProvisionSaaSTenantAsync(
        ProvisionTenantInput? request = null,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> GetSaaSSetupWizardAsync(CancellationToken ct = default);
    Task<WmsEndpointOutcome> SeedSaaSDemoDataAsync(
        SeedDemoDataInput? request = null,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> GetSaaSSubscriptionBillingAsync(
        int? simulatedUsageOrders = null,
        int? maxMonthlyOrders = null,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> EnforceSubscriptionQuotaAsync(
        EnforceSubscriptionQuotaInput? request = null,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> CreateVietQrInvoiceAsync(
        CreateVietQrInvoiceInput? request = null,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> HandleVietQrBillingWebhookAsync(
        VietQrWebhookInput? request = null,
        string? providedSignature = null,
        int? actorUserId = null,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> GetOnboardingWizardStatusAsync(CancellationToken ct = default);
    WmsEndpointOutcome ComplianceEvidenceBundle();
    Task<WmsEndpointOutcome> ChangeRolePermissionAsync(
        int roleId,
        string input,
        byte[] version,
        bool add,
        int? actorUserId = null,
        CancellationToken ct = default);
}
