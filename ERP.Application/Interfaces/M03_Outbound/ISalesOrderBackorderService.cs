using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface ISalesOrderBackorderService
{
    Task<IReadOnlyList<SalesOrderListDto>> ListSalesOrdersAsync(int? warehouseId = null, string? status = null, CancellationToken cancellationToken = default);
    Task<SalesOrderDto> GetSalesOrderAsync(int id, CancellationToken cancellationToken = default);
    Task<SalesOrderDto> CreateSalesOrderAsync(CreateSalesOrderDto request, CancellationToken cancellationToken = default);
    Task<SalesOrderDto> HoldSalesOrderAsync(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken = default);
    Task<SalesOrderDto> ReleaseSalesOrderAsync(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken = default);
    Task<SalesOrderDto> CancelSalesOrderAsync(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BackorderDto>> ListBackordersAsync(int? warehouseId = null, string? status = null, CancellationToken cancellationToken = default);
    Task<BackorderDto> GetBackorderAsync(int id, CancellationToken cancellationToken = default);
    Task<BackorderDto> ReallocateBackorderAsync(int id, BackorderReallocateDto request, CancellationToken cancellationToken = default);
    Task<BackorderDto> CancelBackorderAsync(int id, BackorderCancelDto request, CancellationToken cancellationToken = default);
}
