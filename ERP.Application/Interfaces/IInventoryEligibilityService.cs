using ERP.Domain.Enums;

namespace ERP.Application.Interfaces;

public interface IInventoryEligibilityService
{
    Task EnsureBucketEligibleAsync(
        int productId,
        int warehouseId,
        int locationId,
        int? lotId,
        int? serialId,
        InventoryEligibilityOperation operation,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveLockAsync(
        int warehouseId,
        int? locationId = null,
        int? productId = null,
        int? lotId = null,
        int? serialId = null,
        int? handlingUnitId = null,
        CancellationToken cancellationToken = default);
}
