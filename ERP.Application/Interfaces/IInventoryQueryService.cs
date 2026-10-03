using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IInventoryQueryService
    {
        Task<IEnumerable<InventoryStockDto>> GetCurrentStockAsync(int? warehouseId, int? productId, string? keyword, decimal? lowStockThreshold);
    }
}