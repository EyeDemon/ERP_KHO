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
        await EnsurePermissionAsync("user_warehouse.read", cancellationToken);
        await EnsureUserExistsAsync(userId, cancellationToken);
        var scope = await AccessibleWarehousesAsync(cancellationToken);
        return await context.UserWarehouses.AsNoTracking()
            .Where(x => x.UserId == userId && scope.Contains(x.WarehouseId))
            .OrderBy(x => x.Warehouse.Code)
            .Select(x => new UserWarehouseAccessDto(x.WarehouseId, x.Warehouse.Code, x.Warehouse.Name, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task GrantAsync(int userId, int warehouseId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync("user_warehouse.manage", cancellationToken);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        if (!(await AccessibleWarehousesAsync(cancellationToken)).Contains(warehouseId))
            throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
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
        await PermissionAdministrationGuard.EnsureAdministratorAsync(context, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeAsync(int userId, int warehouseId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync("user_warehouse.manage", cancellationToken);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        if (!(await AccessibleWarehousesAsync(cancellationToken)).Contains(warehouseId))
            throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        var access = await context.UserWarehouses
            .FirstOrDefaultAsync(x => x.UserId == userId && x.WarehouseId == warehouseId, cancellationToken);
        if (access is null)
            throw new NotFoundException("Không tìm thấy quyền truy cập kho.");

        context.UserWarehouses.Remove(access);
        context.AuditLogs.Add(CreateAudit("UserWarehouse.Revoked", userId, warehouseId));
        await context.SaveChangesAsync(cancellationToken);
        await PermissionAdministrationGuard.EnsureAdministratorAsync(context, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureUserExistsAsync(int userId, CancellationToken cancellationToken)
    {
        if (!await context.Users.AnyAsync(x => x.Id == userId, cancellationToken))
            throw new NotFoundException("Không tìm thấy người dùng.");
    }

    private async Task EnsurePermissionAsync(string permission, CancellationToken token)
    {
        var now = DateTime.UtcNow;
        if (!currentUser.IsAuthenticated || !await context.Users.AnyAsync(u => u.Id == currentUser.UserId && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= now) && u.Role.Permissions.Any(p => p.Permission.Code == permission), token))
            throw new ForbiddenException("Bạn không có quyền quản lý phạm vi kho.");
    }

    private async Task<int[]> AccessibleWarehousesAsync(CancellationToken token)
    {
        var admin = await context.Users.AnyAsync(u => u.Id == currentUser.UserId && u.Role.RoleName.Trim().ToLower() == "admin", token);
        return admin ? await context.Warehouses.Select(w => w.Id).ToArrayAsync(token)
            : await context.UserWarehouses.Where(w => w.UserId == currentUser.UserId).Select(w => w.WarehouseId).ToArrayAsync(token);
    }

    private AuditLog CreateAudit(string action, int userId, int warehouseId) => new()
    {
        UserId = currentUser.UserId,
        Action = action,
        EntityName = "UserWarehouse",
        EntityId = userId,
        WarehouseId = warehouseId,
        NewValues = $"WarehouseId: {warehouseId}",
        Timestamp = DateTime.UtcNow
    };
}
