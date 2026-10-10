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
}
