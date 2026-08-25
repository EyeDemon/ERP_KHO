using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IUserWarehouseAccessService
{
    Task<IReadOnlyList<UserWarehouseAccessDto>> GetForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task GrantAsync(int userId, int warehouseId, CancellationToken cancellationToken = default);
    Task RevokeAsync(int userId, int warehouseId, CancellationToken cancellationToken = default);
}
