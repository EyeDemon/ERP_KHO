using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class WarehouseAuthorizationService(
    ErpKhoDbContext context,
    ICurrentUser currentUser) : IWarehouseAuthorizationService
{
    public async Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Người dùng chưa xác thực.");

        if (currentUser.IsGlobalAdmin)
            return await context.Warehouses.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);

        return await context.UserWarehouses.AsNoTracking()
            .Where(x => x.UserId == currentUser.UserId)
            .Select(x => x.WarehouseId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            return false;

        return currentUser.IsGlobalAdmin || await context.UserWarehouses.AsNoTracking()
            .AnyAsync(x => x.UserId == currentUser.UserId && x.WarehouseId == warehouseId, cancellationToken);
    }

    public async Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessWarehouseAsync(warehouseId, cancellationToken))
            throw new NotFoundException("Không tìm thấy tài nguyên.");
    }
}
