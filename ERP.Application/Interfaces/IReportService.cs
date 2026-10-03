using ERP.Application.DTOs;

namespace ERP.Application.Interfaces
{
    public interface IReportService
    {
        Task<IEnumerable<InventoryReportDto>> GetInventoryReportAsync(DateTime? asOfDate, int? warehouseId, int? productId, CancellationToken cancellationToken = default);
        Task<IEnumerable<InventoryInOutReportDto>> GetInventoryInOutReportAsync(DateTime? fromDate, DateTime? toDate, int? warehouseId, int? productId, CancellationToken cancellationToken = default);
    }
}
