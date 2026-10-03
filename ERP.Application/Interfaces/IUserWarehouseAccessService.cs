using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public interface IUserWarehouseAccessService
{
    Task<UserWarehouseAccessSetDto> GetForUserAsync(int userId, CancellationToken cancellationToken = default);
    Task GrantAsync(int userId, int warehouseId, string? rowVersion, CancellationToken cancellationToken = default);
    Task RevokeAsync(int userId, int warehouseId, string? rowVersion, CancellationToken cancellationToken = default);
}
