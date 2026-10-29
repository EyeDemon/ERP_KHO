using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Domain.Entities;

namespace ERP.Application.Interfaces;

public sealed record WmsCalendarExceptionItem(
    int Id,
    int WarehouseId,
    string ExceptionType,
    string WindowSummary,
    string Reason,
    int? CreatedBy,
    DateTime CreatedAtUtc);

public sealed record WmsBarcodeResolutionResult(
    string EntityType,
    int EntityId,
    string Code,
    string? Name,
    string Barcode,
    string DisplayName,
    string? SubType = null,
    int? WarehouseId = null,
    int? ProductId = null,
    string? ProductCode = null,
    string? ProductName = null);

public sealed record WmsGs1EntityLookupResult(
    int? ProductId,
    string? ProductCode,
    string? ProductName,
    int? LotId,
    int? SerialId,
    int? HandlingUnitId);

public sealed record WmsDispatchCrossdockInput(
    int TargetWarehouseId,
    string? DockDoorCode,
    string? InboundAppointmentCode,
    string? OutboundReferenceCode,
    int? ProductId,
    string? ProductCode,
    int? SourceLocationId,
    string? SourceLocationCode,
    int? TargetLocationId,
    string? TargetLocationCode,
    string? FromLocationCode,
    string? ToLocationCode,
    decimal? Quantity,
    bool ExecuteStockMutation,
    int? ActorUserId);

public interface IWmsGovernanceOperationsService
{
    // UnitsController
    Task<WmsEndpointOutcome> VerifyUomIntegrityAsync(CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> VerifyCatchWeightColdChainAsync(int? productId, CancellationToken cancellationToken = default);

    // WarehousesController
    Task<WmsEndpointOutcome> VerifyTopologyCapacityAsync(int? warehouseId, IReadOnlyCollection<int>? accessibleIds, CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> VerifyVirtualLocationsAsync(int? warehouseId, IReadOnlyCollection<int>? accessibleIds, CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> EvaluateNetworkMheAsync(int? warehouseId, IReadOnlyCollection<int>? accessibleIds, CancellationToken cancellationToken = default);

    // WarehouseCalendarsController
    Task<WmsEndpointOutcome> VerifyShiftCoverageAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> OptimizeLaborInterleavingAsync(int warehouseId, int? actorUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WmsCalendarExceptionItem>> GetWarehouseCalendarExceptionsAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task<WmsCalendarExceptionItem> CreateWarehouseCalendarExceptionAsync(
        int warehouseId,
        string exceptionType,
        string windowSummary,
        string reason,
        int? userId,
        CancellationToken cancellationToken = default);

    // ProductBarcodeLookupController
    Task<WmsBarcodeResolutionResult?> ResolveNonGs1BarcodeAsync(string scan, int? warehouseId, CancellationToken ct = default);
    Task<(int ProductId, string Code, string Name)?> FindProductByExactBarcodeAsync(string rawBarcode, CancellationToken ct = default);
    Task<WmsGs1EntityLookupResult> ResolveGs1EntitiesAsync(
        string? gtin,
        string? normalizedBarcode,
        string? lotNumber,
        string? serialNumber,
        string? sscc,
        CancellationToken ct = default);

    // BusinessPartnersController
    Task<WmsEndpointOutcome> GetPartnerGovernanceSummaryAsync(CancellationToken ct = default);
    Task<WmsEndpointOutcome> GetCarriersAndSlaContractsAsync(CancellationToken ct = default);
    Task<bool> IsActiveBusinessPartnerAsync(int id, CancellationToken ct = default);
    Task<WmsEndpointOutcome> Get3PlBillingSummaryAsync(
        int? partnerId,
        int? warehouseId,
        string? billingPeriod,
        decimal? storageRatePerUnit,
        decimal? inboundRatePerUnit,
        decimal? outboundRatePerUnit,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> Generate3PlStatementAsync(
        int? partnerId,
        int? warehouseId,
        string? billingPeriod,
        decimal? storageRatePerUnit,
        decimal? inboundRatePerUnit,
        decimal? outboundRatePerUnit,
        bool createReceivableInvoice,
        int? actorUserId,
        CancellationToken ct = default);
    Task<int> CountActiveBusinessPartnersAsync(CancellationToken ct = default);

    // HandlingUnitsController
    Task<WmsEndpointOutcome> VerifyInTransitSealAsync(
        string? transferCode,
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);

    // StockReservationsController
    Task<WmsEndpointOutcome> GetStockReservationCommitmentSummaryAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> SweepExpiredReservationsPreviewAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    // StockAllocationsController
    Task<WmsEndpointOutcome> GetStockAllocationSummaryAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        int unallocatedReservationCount,
        decimal totalUnallocatedQuantity,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> VerifyAllocationFefoComplianceAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);

    // StockTransfersController
    Task<WmsEndpointOutcome> GetInTransitInventoryAsync(
        IReadOnlyCollection<int> allowedIds,
        int? sourceWarehouseId,
        int? destinationWarehouseId,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> SimulateNetworkRebalancingAsync(
        int? warehouseId,
        int? sourceWarehouseId,
        int? destinationWarehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);

    // ApprovalsController
    Task<WmsEndpointOutcome> EscalateOverdueApprovalsAsync(
        int? warehouseId,
        IReadOnlyList<ApprovalQueueItem> overdueItems,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    // AsnsController
    Task<WmsEndpointOutcome> PreAllocateAsnStagingAsync(
        int targetWarehouseId,
        int preAllocatedAsnCount,
        string resolvedAsnNumber,
        int? actorUserId,
        CancellationToken ct = default);

    // PurchaseOrdersController
    Task<WmsEndpointOutcome> ExpediteOverduePurchaseOrdersAsync(
        int? supplierId,
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        int openPoCount,
        int overdueCount,
        string[] expeditedPoCodes,
        int? actorUserId,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> GenerateProcurementRecommendationsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        int openPoCount,
        decimal totalIncomingQuantity,
        int? actorUserId,
        CancellationToken ct = default);

    // PutawayTasksController
    Task<WmsEndpointOutcome> GetPutawayRuleEnginePolicyAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> EvaluatePutawaySlottingAsync(
        int targetWarehouseId,
        CancellationToken ct = default);
    Task<WmsEndpointOutcome> GetDirectedPutawaySuggestionAsync(
        int? id,
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAccess,
        CancellationToken ct = default);
    Task<IReadOnlyList<WarehouseLocation>> GetActiveLocationsForBarcodeSheetAsync(
        int? warehouseId,
        string? zoneCode,
        IReadOnlyCollection<int>? locationIds,
        CancellationToken ct = default);

    // DockYardController
    Task EnrichCrossDockCodesAsync(
        int? warehouseId,
        List<string> inboundCodes,
        List<string> outboundCodes,
        CancellationToken token = default);
    Task<WmsEndpointOutcome> DispatchCrossdockAsync(
        WmsDispatchCrossdockInput input,
        CancellationToken token = default);

    // UserWarehouseAccessController
    Task EnsureTenantUserOwnershipAsync(
        int userId,
        int? warehouseId,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> VerifySodIsolationAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);

    // InventoryStatusController
    Task<WmsEndpointOutcome> VerifyBucketInvariantsAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        int? actorUserId,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> VerifyLockPrecedenceAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    // InventoryReversalTraceabilityController
    Task<WmsEndpointOutcome?> TryGetReversalSummaryFromDbAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome?> TryGetTraceabilitySummaryFromDbAsync(
        int? warehouseId,
        int? productId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> VerifyReversalChainIntegrityAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> SimulateRecallTreeAsync(
        string? batchOrSerialQuery,
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);

    // InventoryReconciliationController
    Task<int> CountInventoryStocksAsync(
        int? warehouseId,
        int? productId,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> GetInventoryValuationAsync(
        int? warehouseId,
        int? productId,
        CancellationToken cancellationToken = default);
    Task<decimal> SumReservedQuantityAsync(
        int? warehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken = default);
    Task<(int TotalTransactionsCount, int TotalAuditLogsCount, int IdempotencyRecordsCount)> GetIntegrationHubCountsAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> GetOutboxMessagesAsync(
        int? warehouseId,
        string? statusFilter,
        string? eventTypeFilter,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> DispatchPendingOutboxMessagesAsync(
        int? warehouseId,
        int? actorUserId,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> VerifySiteReadinessAsync(
        int? warehouseId,
        int checkedRows,
        int matchedCount,
        int mismatchCount,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    // AccountSecurityController
    Task<WmsEndpointOutcome> LockUserAccountAsync(
        int userId,
        string? rowVersion,
        int? actorUserId,
        IUserSessionService? sessionService,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> ResetUserPasswordAsync(
        int userId,
        string? temporaryPassword,
        string? reason,
        string? rowVersion,
        int? actorUserId,
        IUserSessionService? sessionService,
        IPasswordHasherService? passwordHasher,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> DeleteUserAccountAsync(
        int userId,
        string? rowVersion,
        int? actorUserId,
        CancellationToken cancellationToken = default);
    Task<string?> GetUsernameByIdAsync(int userId, CancellationToken cancellationToken = default);
    Task LogTotpAuditAsync(
        int userId,
        int? actorUserId,
        string action,
        string newValues,
        string reason,
        DateTime timestampUtc,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> GrantSupportImpersonationAsync(
        string? supportTicketCode,
        int? expiresInMinutes,
        bool readOnlyScope,
        int? supportAgentUserId,
        int? actorUserId,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> ListSupportImpersonationSessionsAsync(CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> RevokeSupportImpersonationAsync(
        int sessionId,
        int? actorUserId,
        CancellationToken cancellationToken = default);
    Task<WmsEndpointOutcome> AnonymizeExpiredBuyerPiiAsync(
        int? retentionDays,
        int? actorUserId,
        CancellationToken cancellationToken = default);
}
