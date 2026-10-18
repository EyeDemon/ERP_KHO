using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class WarehouseAuthorizationService(
    ErpKhoDbContext context,
    ICurrentUser currentUser) : IWarehouseAuthorizationService
{
    private readonly Dictionary<int, IReadOnlyList<int>> _accessibleWarehouseCache = new();

    public int? CurrentUserId => currentUser.IsAuthenticated && currentUser.UserId > 0 ? currentUser.UserId : null;

    public void InvalidateWarehouseAccessCache(int? userId = null)
    {
        if (userId.HasValue)
        {
            _accessibleWarehouseCache.Remove(userId.Value);
            return;
        }

        _accessibleWarehouseCache.Clear();
    }

    public async Task<IReadOnlyList<int>> GetAccessibleWarehouseIdsAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Người dùng chưa xác thực.");

        if (currentUser.IsGlobalAdmin)
            return await context.Warehouses.AsNoTracking()
                .Where(x => x.IsActive)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

        var warehouseIds = await context.UserWarehouses.AsNoTracking()
            .Where(x => x.UserId == currentUser.UserId && !context.Warehouses.Any(w => w.Id == x.WarehouseId && !w.IsActive))
            .Select(x => x.WarehouseId)
            .ToListAsync(cancellationToken);

        _accessibleWarehouseCache[currentUser.UserId] = warehouseIds;
        return warehouseIds;
    }

    public async Task<bool> CanAccessWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            return false;

        if (warehouseId <= 0)
            return false;

        // Global admin bypasses membership, never resource existence or active state.
        // An unknown or inactive warehouse must fail closed like an unassigned one.
        if (currentUser.IsGlobalAdmin)
            return await context.Warehouses.AsNoTracking()
                .AnyAsync(x => x.Id == warehouseId && x.IsActive, cancellationToken);

        var allowed = await context.UserWarehouses.AsNoTracking()
            .AnyAsync(x =>
                x.UserId == currentUser.UserId &&
                x.WarehouseId == warehouseId &&
                !context.Warehouses.Any(w => w.Id == x.WarehouseId && !w.IsActive),
                cancellationToken);

        if (!allowed)
            _accessibleWarehouseCache.Remove(currentUser.UserId);

        return allowed;
    }

    public async Task EnsureWarehouseAccessAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessWarehouseAsync(warehouseId, cancellationToken))
            throw new NotFoundException("Không tìm thấy tài nguyên.");
    }
}
