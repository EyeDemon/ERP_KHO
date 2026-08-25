using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces
{
    public interface IInventoryStockRepository : IRepository<InventoryStock>
    {
        Task<InventoryStock?> GetByProductAndWarehouseAsync(int productId, int warehouseId);
        Task<bool> TryDecreaseStockAsync(
            int productId,
            int warehouseId,
            decimal quantity,
            CancellationToken cancellationToken = default);
        Task<bool> TryReserveAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default);
        Task<bool> TryConsumeReservationAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default);
        Task<bool> TryReleaseReservationAsync(int productId, int warehouseId, decimal quantity, CancellationToken cancellationToken = default);
    }
}
