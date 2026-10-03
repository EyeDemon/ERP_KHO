using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IStockTransferService
{
    Task<PagedResult<StockTransferDto>> GetAsync(StockTransferQueryDto query, CancellationToken cancellationToken = default);
    Task<StockTransferDto> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<StockTransferDto> CreateAsync(CreateStockTransferDto request, CancellationToken cancellationToken = default);
    Task UpdateAsync(int id, UpdateStockTransferDto request, CancellationToken cancellationToken = default);
    Task ApproveAsync(int id, CancellationToken cancellationToken = default);
    Task DispatchAsync(int id, CancellationToken cancellationToken = default);
    Task ReceiveAsync(int id, ReceiveStockTransferDto request, CancellationToken cancellationToken = default);
    Task CompleteAsync(int id, CancellationToken cancellationToken = default);
    Task CancelAsync(int id, CancellationToken cancellationToken = default);
}
