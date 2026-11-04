namespace ERP.Application.Interfaces;

public interface IWarehouseAuthorizationService
{
    int? CurrentUserId => null;
    Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default);
    Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default);
    Task EnsureWarehouseCommandAccessAsync(int warehouseId, CancellationToken cancellationToken = default) =>
        EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
    void InvalidateWarehouseAccessCache(int? userId = null)
    {
    }
}
