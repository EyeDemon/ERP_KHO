using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class UserWarehouseAccessService(
    ErpKhoDbContext context,
    ICurrentUser currentUser) : IUserWarehouseAccessService
{
    public async Task<IReadOnlyList<UserWarehouseAccessDto>> GetForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        EnsureGlobalAdmin();
        await EnsureUserExistsAsync(userId, cancellationToken);
        return await context.UserWarehouses.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.Warehouse.Code)
            .Select(x => new UserWarehouseAccessDto(x.WarehouseId, x.Warehouse.Code, x.Warehouse.Name, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task GrantAsync(int userId, int warehouseId, CancellationToken cancellationToken = default)
    {
        EnsureGlobalAdmin();
        await EnsureUserExistsAsync(userId, cancellationToken);
        if (!await context.Warehouses.AnyAsync(x => x.Id == warehouseId, cancellationToken))
            throw new NotFoundException("Không tìm thấy kho.");

        if (await context.UserWarehouses.AnyAsync(x => x.UserId == userId && x.WarehouseId == warehouseId, cancellationToken))
            throw new BusinessRuleException("Người dùng đã được cấp quyền kho này.");

        context.UserWarehouses.Add(new UserWarehouse
        {
            UserId = userId,
            WarehouseId = warehouseId,
            CreatedBy = currentUser.UserId,
            CreatedAt = DateTime.UtcNow
        });
        context.AuditLogs.Add(CreateAudit("UserWarehouse.Granted", userId, warehouseId));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(int userId, int warehouseId, CancellationToken cancellationToken = default)
    {
        EnsureGlobalAdmin();
        var access = await context.UserWarehouses
            .FirstOrDefaultAsync(x => x.UserId == userId && x.WarehouseId == warehouseId, cancellationToken);
        if (access is null)
            throw new NotFoundException("Không tìm thấy quyền truy cập kho.");

        context.UserWarehouses.Remove(access);
        context.AuditLogs.Add(CreateAudit("UserWarehouse.Revoked", userId, warehouseId));
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureUserExistsAsync(int userId, CancellationToken cancellationToken)
    {
        if (!await context.Users.AnyAsync(x => x.Id == userId, cancellationToken))
            throw new NotFoundException("Không tìm thấy người dùng.");
    }

    private void EnsureGlobalAdmin()
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsGlobalAdmin)
            throw new ForbiddenException("Bạn không có quyền quản lý phạm vi kho.");
    }

    private AuditLog CreateAudit(string action, int userId, int warehouseId) => new()
    {
        UserId = currentUser.UserId,
        Action = action,
        EntityName = "UserWarehouse",
        EntityId = userId,
        NewValues = $"WarehouseId: {warehouseId}",
        Timestamp = DateTime.UtcNow
    };
}
