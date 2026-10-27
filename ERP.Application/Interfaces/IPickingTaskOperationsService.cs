using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public record PutToWallSlotData(
    int SlotIndex,
    string SlotCode,
    string ToteBarcode,
    int? PickingTaskId,
    int? PickingTaskLineId,
    string OrderReference,
    int? ProductId,
    string ProductCode,
    string ProductName,
    decimal RequiredQty,
    decimal SortedQty,
    decimal RemainingQty,
    string SlotStatus);

public record PutToWallSortInput(
    string? ScannedSkuOrBarcode = null,
    string? ScannedSlotCode = null,
    string? ScannedToteBarcode = null,
    decimal Quantity = 1m);

public record ReleasePickingWaveInput(
    int? WarehouseId = null,
    string? PickingType = null,
    int? Priority = null,
    int? MaxTasks = null,
    string? WaveCode = null,
    int? AssignToUserId = null,
    string? Note = null);

public record MobileScanVerifyInput(string? ScanInput = null, int? WarehouseId = null);
public record SyncAutomationTelemetryInput(int? WarehouseId = null);
public record VerifyPickingDispatchCutoffInput(int? WarehouseId = null);
public record SweepPickingExpiredReservationsInput(int? WarehouseId = null);
public record VerifyPickingFefoComplianceInput(int? WarehouseId = null);

public interface IPickingTaskOperationsService
{
    Task<WmsEndpointOutcome> GetAlternateBinsAsync(
        PickingTaskDto task,
        int? productId = null,
        int? excludeLocationId = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetPutToWallSlotsAsync(
        string? id = null,
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> PutToWallSortAsync(
        string? id = null,
        PutToWallSortInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ReleaseWaveAsync(
        ReleasePickingWaveInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetMobileScanQueueAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome?> TryMatchMobileScanInDbAsync(
        string sanitizedInput,
        string rawScanInput,
        int? warehouseId = null,
        IReadOnlyCollection<int>? accessibleIds = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> SyncAutomationTelemetryAsync(
        SyncAutomationTelemetryInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> VerifyDispatchCutoffAsync(
        VerifyPickingDispatchCutoffInput? request = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> SweepExpiredPreviewAsync(
        SweepPickingExpiredReservationsInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> VerifyFefoComplianceAsync(
        VerifyPickingFefoComplianceInput? request = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);
}
