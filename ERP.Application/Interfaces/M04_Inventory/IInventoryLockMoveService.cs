using ERP.Application.DTOs;
using ERP.Domain.Enums;

namespace ERP.Application.Interfaces;

public interface IInventoryLockService
{
    Task<IReadOnlyList<InventoryLockDto>> ListAsync(
        int? warehouseId = null,
        string? status = null,
        CancellationToken cancellationToken = default);
    Task<InventoryLockDto> GetAsync(int id, CancellationToken cancellationToken = default);
    Task<InventoryLockDto> CreateAsync(CreateInventoryLockDto request, CancellationToken cancellationToken = default);
    Task<InventoryLockDto> ReleaseAsync(int id, ReleaseInventoryLockDto request, CancellationToken cancellationToken = default);
}

public interface IInventoryLockEvaluator
{
    Task EnsureBucketUnlockedAsync(
        int warehouseId,
        int? locationId,
        int? productId,
        InventoryStatus? inventoryStatus,
        int? lotId,
        int? serialId,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveLockAsync(
        int warehouseId,
        int? locationId,
        int? productId,
        InventoryStatus? inventoryStatus,
        int? lotId,
        int? serialId,
        CancellationToken cancellationToken = default);
}

public interface IInventoryMovementService
{
    Task<InventoryMoveResultDto> MoveAsync(
        CreateInventoryMoveDto request,
        CancellationToken cancellationToken = default);

    Task<MoveHandlingUnitLpnResultDto> MoveHandlingUnitAsync(
        MoveHandlingUnitLpnRequestDto? request,
        CancellationToken cancellationToken = default);

    Task<AutoBreakBulkReplenishResultDto> AutoBreakBulkReplenishAsync(
        AutoBreakBulkReplenishRequestDto? request,
        CancellationToken cancellationToken = default);
}

public sealed record MoveHandlingUnitLpnRequestDto(
    int? HandlingUnitId = null,
    string? HandlingUnitCode = null,
    int? WarehouseId = null,
    int? SourceLocationId = null,
    int? DestinationLocationId = null,
    string? DestinationLocationCode = null,
    string? Reason = null);

public sealed record MovedHandlingUnitBucketDto(
    int MovementId,
    int ProductId,
    string ProductCode,
    string ProductName,
    int FromLocationId,
    string FromLocationCode,
    int ToLocationId,
    string ToLocationCode,
    int? LotId,
    int? SerialId,
    int? HandlingUnitId,
    decimal MovedQuantity);

public sealed record MoveHandlingUnitLpnResultDto(
    int HandlingUnitId,
    string HandlingUnitCode,
    int WarehouseId,
    int DestinationLocationId,
    string DestinationLocationCode,
    int MovedBucketCount,
    decimal TotalMovedQuantity,
    string Reason,
    IReadOnlyList<MovedHandlingUnitBucketDto> Movements,
    string StatusLabel);

public sealed record AutoBreakBulkReplenishRequestDto(
    int? WarehouseId = null,
    int? ProductId = null,
    decimal? CartonQuantity = 1m,
    decimal? MinPickFaceThresholdQty = 50m,
    bool ExecuteImmediately = false);

public sealed record BreakBulkPlanItemDto(
    int ProductId,
    string ProductCode,
    string ProductName,
    int ReserveLocationId,
    string ReserveLocationCode,
    int PickFaceLocationId,
    string PickFaceLocationCode,
    string BulkUomCode,
    string BaseUomCode,
    decimal ConversionFactor,
    decimal CartonQty,
    decimal BaseUnitQty,
    decimal ActualReplenishedBaseQty,
    bool Executed,
    int? MovementId,
    string? ReserveBinCode = null,
    string? PickFaceBinCode = null,
    string? FromLocationCode = null,
    string? ToLocationCode = null,
    string? LedgerStatus = null);

public sealed record AutoBreakBulkReplenishResultDto(
    int WarehouseId,
    bool ExecutedImmediately,
    int TotalSkuCount,
    decimal TotalCartonQty,
    decimal TotalBaseUnitQty,
    IReadOnlyList<BreakBulkPlanItemDto> BreakBulkPlan,
    string StatusLabel);
