using ERP.Domain.Entities;
using ERP.Domain.Enums;

namespace ERP.Domain.Interfaces
{
    public sealed record InventoryStockConsumption(
        int LocationId,
        decimal Quantity,
        int? LotId = null,
        int? SerialId = null,
        InventoryStatus InventoryStatus = InventoryStatus.Available);

    public interface IInventoryStockRepository : IRepository<InventoryStock>
    {
        Task<InventoryStock?> GetByProductAndWarehouseAsync(int productId, int warehouseId);
        Task<InventoryStock?> GetByProductWarehouseAndStatusAsync(int productId, int warehouseId, InventoryStatus status);
        Task<InventoryStock?> GetByProductWarehouseStatusAndLocationAsync(int productId, int warehouseId, InventoryStatus status, int locationId);
        Task<int?> GetLegacyAdjustmentLocationIdAsync(int warehouseId, CancellationToken cancellationToken = default);
        Task<bool> TryDecreaseStockAsync(
            int productId,
            int warehouseId,
            decimal quantity,
            CancellationToken cancellationToken = default);
        Task<decimal> GetAvailableQuantityAsync(int productId, int warehouseId, CancellationToken cancellationToken = default);
        Task<bool> TryReserveAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default);
        Task<bool> TryConsumeReservationAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<InventoryStockConsumption>> ConsumeReservationWithBreakdownAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default);
        Task<bool> TryConsumeReservationAtLocationAsync(int productId, int warehouseId, int locationId, decimal quantity, CancellationToken cancellationToken = default);
        Task<bool> TryConsumeReservationAtBucketAsync(
            int productId,
            int warehouseId,
            int locationId,
            int? lotId,
            int? serialId,
            InventoryStatus inventoryStatus,
            decimal quantity,
            CancellationToken cancellationToken = default);
        Task<bool> TryReleaseReservationAsync(int productId, int warehouseId, decimal quantity, int? excludedReservationId, CancellationToken cancellationToken = default);
    }
}
