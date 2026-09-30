using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Buffers.Binary;
using ERP.Domain.Exceptions;

namespace ERP.Infrastructure.Services;

public sealed class UserWarehouseAccessService(
    ErpKhoDbContext context,
    ICurrentUser currentUser) : IUserWarehouseAccessService
{
    public async Task<UserWarehouseAccessSetDto> GetForUserAsync(int userId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync("user_warehouse.read", cancellationToken);
        var scope = await AccessibleWarehousesAsync(cancellationToken);
        var target = await context.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.WarehouseAccessRevision, Memberships = u.WarehouseAccesses
                .Where(w => scope.Contains(w.WarehouseId)).OrderBy(w => w.Warehouse.Code)
                .Select(w => new UserWarehouseAccessDto(w.WarehouseId, w.Warehouse.Code, w.Warehouse.Name, w.CreatedAt)).ToList() })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy người dùng.");
        var canMutate = await context.Users.AnyAsync(u => u.Id == currentUser.UserId &&
            u.Role.RoleName.Trim().ToLower() != "viewer" &&
            u.Role.Permissions.Any(p => p.Permission.Code == "user_warehouse.manage"), cancellationToken);
        return new(target.Memberships,
            canMutate ? Token(target.WarehouseAccessRevision) : null);
    }

    public async Task GrantAsync(int userId, int warehouseId, string? rowVersion, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync("user_warehouse.manage", cancellationToken);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        if (!(await AccessibleWarehousesAsync(cancellationToken)).Contains(warehouseId))
            throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        var target = await ValidateVersionAsync(userId, rowVersion, cancellationToken);
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
        target.WarehouseAccessRevision = checked(target.WarehouseAccessRevision + 1);
        await context.SaveChangesAsync(cancellationToken);
        await PermissionAdministrationGuard.EnsureAdministratorAsync(context, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task RevokeAsync(int userId, int warehouseId, string? rowVersion, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync("user_warehouse.manage", cancellationToken);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        if (!(await AccessibleWarehousesAsync(cancellationToken)).Contains(warehouseId))
            throw new NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        var target = await ValidateVersionAsync(userId, rowVersion, cancellationToken);
        var access = await context.UserWarehouses
            .FirstOrDefaultAsync(x => x.UserId == userId && x.WarehouseId == warehouseId, cancellationToken);
        if (access is null)
            throw new NotFoundException("Không tìm thấy quyền truy cập kho.");

        context.UserWarehouses.Remove(access);
        context.AuditLogs.Add(CreateAudit("UserWarehouse.Revoked", userId, warehouseId));
        target.WarehouseAccessRevision = checked(target.WarehouseAccessRevision + 1);
        await context.SaveChangesAsync(cancellationToken);
        await PermissionAdministrationGuard.EnsureAdministratorAsync(context, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private static string Token(long revision)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, revision);
        return Convert.ToBase64String(bytes);
    }

    private async Task<User> ValidateVersionAsync(int userId, string? version, CancellationToken cancellationToken)
    {
        var target = await context.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy người dùng.");
        await context.Entry(target).ReloadAsync(cancellationToken);
        byte[] bytes;
        try { bytes = Convert.FromBase64String(version ?? ""); }
        catch (FormatException) { throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại."); }
        if (bytes.Length != 8 || BinaryPrimitives.ReadInt64BigEndian(bytes) != target.WarehouseAccessRevision)
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        return target;
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
