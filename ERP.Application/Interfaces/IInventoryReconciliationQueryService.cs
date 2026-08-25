using System.Threading.Tasks;
using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IInventoryReconciliationQueryService
    {
        Task<PagedResult<InventoryReconciliationDto>> GetReconciliationsAsync(
            int? warehouseId,
            int? productId,
            string? keyword,
            int pageIndex = 1,
            int pageSize = 20);
    }
}
