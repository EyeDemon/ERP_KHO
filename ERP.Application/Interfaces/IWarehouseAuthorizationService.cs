namespace ERP.Application.Interfaces;

public interface IWarehouseAuthorizationService
{
    Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default);
    Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default);
}
