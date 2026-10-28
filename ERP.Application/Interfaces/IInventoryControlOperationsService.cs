using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public record ExecuteReplenishmentInput(
    int? WarehouseId = null,
    int? ProductId = null,
    string? ProductCode = null,
    int? FromLocationId = null,
    string? FromLocationCode = null,
    int? ToLocationId = null,
    string? ToLocationCode = null,
    decimal Quantity = 10m,
    bool ExecuteStockMutation = true,
    string? LotNumber = null,
    string? Note = null,
    int? SourceLocationId = null,
    string? SourceLocationCode = null,
    int? TargetLocationId = null,
    string? TargetLocationCode = null);

public record WhatIfSimulationInput(
    int? WarehouseId = null,
    decimal DemandMultiplier = 1.5m,
    int HorizonDays = 14,
    decimal? DemandShockPct = null,
    int? LeadTimeDelayDays = null);

public record EvaluateDecisionPolicyInput(int? WarehouseId = null);

public record ExecuteBreakBulkReplenishmentInput(
    int? WarehouseId = null,
    int? ProductId = null,
    string? ProductCode = null,
    int? SourceLocationId = null,
    string? SourceLocationCode = null,
    int? TargetLocationId = null,
    string? TargetLocationCode = null,
    int? SourceUomId = null,
    string? SourceUomCode = null,
    decimal SourcePackQty = 1m,
    decimal? ConversionFactor = null,
    int? SourceHandlingUnitId = null,
    int? SourceLotId = null,
    bool Reverse = false,
    int? OriginalReferenceId = null,
    string? Note = null);

public record EvaluateAbcSlottingInput(
    int? WarehouseId = null,
    int LookbackDays = 90,
    int? ProductId = null,
    bool PersistAuditAndOutbox = true,
    bool Reverse = false,
    string? OriginalEvaluationId = null);

public record ThreePlDepositorPortalInput(
    int? OwnerId = null,
    int? RequestedOwnerId = null,
    int? AuthenticatedOwnerId = null,
    int? WarehouseId = null,
    string? BillingPeriod = null,
    decimal StorageRatePerPalletDay = 15000m,
    decimal InboundHandlingRatePerUnit = 8000m,
    decimal OutboundHandlingRatePerUnit = 9000m,
    decimal VasKittingRatePerUnit = 12000m,
    bool PersistSnapshotAndStatement = false,
    bool Reverse = false,
    int? OriginalStatementId = null);

public interface IInventoryControlOperationsService
{
    Task<WmsEndpointOutcome> ReplenishmentSuggestionsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ExecuteReplenishmentAsync(
        ExecuteReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ExecuteBreakBulkReplenishmentAsync(
        ExecuteBreakBulkReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> EvaluateAbcSlottingAndTaskInterleavingAsync(
        EvaluateAbcSlottingInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetThreePlDepositorPortalAsync(
        ThreePlDepositorPortalInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> AdvancedPlanningSuiteAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> RunWhatIfSimulationAsync(
        WhatIfSimulationInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> EvaluateDecisionPolicyGovernanceAsync(
        EvaluateDecisionPolicyInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);
}
