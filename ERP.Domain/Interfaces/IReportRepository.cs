using ERP.Domain.Entities;
using ERP.Domain.Models;

namespace ERP.Domain.Interfaces
{
    public interface IReportRepository
    {
        Task<IEnumerable<InventoryTransaction>> GetTransactionsUpToDateAsync(DateTime asOfDate, int? warehouseId, int? productId, CancellationToken cancellationToken = default);
        Task<IEnumerable<InventoryStock>> GetCurrentStocksAsync(int? warehouseId, int? productId, CancellationToken cancellationToken = default);
        Task<IEnumerable<InventoryInOutReportModel>> GetInventoryInOutReportAsync(DateTime? fromDate, DateTime? toDate, int? warehouseId, int? productId, CancellationToken cancellationToken = default);
    }
}
