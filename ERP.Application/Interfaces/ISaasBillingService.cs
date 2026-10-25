namespace ERP.Application.Interfaces;

public sealed record SaasSubscriptionQuotaRequest(
    string? PlanCode = "ENTERPRISE",
    int? MaxUsers = null,
    int? MaxMonthlyOrders = null,
    int? MaxStorageLocations = null,
    bool? HardBlockOnExceed = true,
    int? CurrentMonthOrderCount = null,
    int? MonthlyOrderQuota = null,
    string? OperationType = null);

public sealed record SaasCreateVietQrInvoiceRequest(
    string? PlanCode = "PRO",
    string? BillingCycle = "MONTHLY",
    decimal? CustomAmountVnd = null,
    string? TaxCode = null,
    string? CompanyName = null,
    decimal? AmountVnd = null,
    int? BillingMonths = null);

public sealed record SaasVietQrWebhookRequest(
    string? InvoiceCode = null,
    string? TransactionReference = null,
    decimal? PaidAmountVnd = null,
    string? BankCode = "VCB",
    string? ChecksumSignature = null,
    string? PlanCode = null,
    string? BankTransactionRef = null);

public interface ISaasBillingService
{
    Task<object> GetPlansAsync(CancellationToken cancellationToken = default);

    Task<object> GetSubscriptionAsync(
        int? simulatedUsageOrders = null,
        int? maxMonthlyOrders = null,
        CancellationToken cancellationToken = default);

    Task<(bool Found, object Payload)> EnforceQuotaAsync(
        SaasSubscriptionQuotaRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<object> GetVietQrSubscriptionAsync(CancellationToken cancellationToken = default);

    Task<object> CreateVietQrInvoiceAsync(
        SaasCreateVietQrInvoiceRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<(bool InvalidReason, string? ErrorMessage, object? Payload)> HandleVietQrWebhookAsync(
        SaasVietQrWebhookRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);
}
