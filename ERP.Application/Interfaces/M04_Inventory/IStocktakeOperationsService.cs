using System.Text.Json;
using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public record VerifyStocktakeFreezePolicyInput(int? WarehouseId = null);

public record CreateAdjustmentProposalInput(
    int? WarehouseId = null,
    string? ProductCode = null,
    decimal DeltaQuantity = 0m,
    string ReasonCode = "COUNT_VARIANCE",
    string? Note = null,
    int? ProductId = null,
    int? LocationId = null,
    string? LotNumber = null,
    bool ExecuteImmediately = false,
    decimal? QuantityDelta = null,
    string? Sku = null,
    string? LocationCode = null);

public record ApproveAdjustmentProposalInput(
    int? LocationId = null,
    string? LotNumber = null,
    string? Note = null,
    string? LocationCode = null,
    decimal? DeltaQuantity = null,
    decimal? QuantityDelta = null);

public record RecountSubmissionInput(
    JsonElement? StocktakeDetailId = null,
    int? DetailId = null,
    int? ProductId = null,
    decimal? CountedQuantity = null,
    string? Note = null);

public interface IStocktakeOperationsService
{
    Task<WmsEndpointOutcome> GetReasonCodesAndAdjustmentsAsync(
        int? warehouseId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> VerifyFreezePolicyAsync(
        VerifyStocktakeFreezePolicyInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> CreateAdjustmentProposalAsync(
        CreateAdjustmentProposalInput? request = null,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ApproveAdjustmentProposalAsync(
        long proposalAuditId,
        ApproveAdjustmentProposalInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetCountAttemptsAndAutoApprovalAsync(
        string? id = null,
        decimal thresholdPct = 1.0m,
        decimal thresholdValueVnd = 500000m,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> EvaluateAutoApprovalAsync(
        string? id = null,
        decimal? thresholdPct = 1.0m,
        decimal? thresholdValueVnd = 500000m,
        IReadOnlyList<RecountSubmissionInput>? recountSubmissions = null,
        int? actorUserId = null,
        string? counterUserLabel = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);
}
