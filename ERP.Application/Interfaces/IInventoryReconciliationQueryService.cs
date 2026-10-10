using System.Threading.Tasks;
using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IInventoryReconciliationQueryService
    {
        Task<IReadOnlyList<InventoryReconciliationWarehouseDto>> GetAccessibleWarehousesAsync();

        Task<InventoryReconciliationInvestigationDto> GetInvestigationAsync(
            int warehouseId, int productId, int? eventAnchorId = null, int limit = 50,
            int? eventBeforeId = null, int? bucketAnchorId = null,
            int? bucketAfterId = null, string bucketStatus = "Available",
            string eventStatus = "Available");

        Task<PagedResult<InventoryReconciliationDto>> GetReconciliationsAsync(
            int? warehouseId,
            int? productId,
            string? keyword,
            int pageIndex = 1,
            int pageSize = 20);
    }
}
