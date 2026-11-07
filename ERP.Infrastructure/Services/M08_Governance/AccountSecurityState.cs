using System.Buffers.Binary;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

internal static class AccountSecurityState
{
    internal const string Conflict = "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.";

    internal static NotFoundException CreateUserNotFoundException(string message = "Không tìm thấy người dùng.")
    {
        var ex = new NotFoundException(message);
        ex.Data["ErrorCode"] = "USER_NOT_FOUND";
        return ex;
    }

    internal static bool IsPlatformTargetUser(string? userSecurityDomain, string? roleName, string? roleSecurityDomain) =>
        string.Equals(userSecurityDomain?.Trim(), "PLATFORM", StringComparison.OrdinalIgnoreCase)
        || string.Equals(roleSecurityDomain?.Trim(), "PLATFORM", StringComparison.OrdinalIgnoreCase)
        || ERP.Domain.Security.AppPermissions.IsPlatformRole(roleName);

    internal static bool IsPlatformTargetUser(User user) =>
        IsPlatformTargetUser(user.SecurityDomain, user.Role?.RoleName, user.Role?.SecurityDomain);

    internal static async Task<bool> IsPlatformCallerAsync(
        ErpKhoDbContext db,
        ICurrentUser? currentUser,
        int? actorUserId,
        CancellationToken ct)
    {
        if (currentUser is ISecurityDomainCurrentUser secUser
            && string.Equals(secUser.SecurityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (currentUser is { IsAuthenticated: true } && ERP.Domain.Security.AppPermissions.IsPlatformRole(currentUser.Role))
        {
            return true;
        }

        var effectiveActorId = actorUserId ?? (currentUser is { IsAuthenticated: true } ? currentUser.UserId : null);
        if (effectiveActorId is not > 0)
        {
            return true;
        }

        var actorProjection = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == effectiveActorId.Value)
            .Select(u => new
            {
                u.SecurityDomain,
                RoleName = u.Role != null ? u.Role.RoleName : null,
                RoleSecurityDomain = u.Role != null ? u.Role.SecurityDomain : null
            })
            .FirstOrDefaultAsync(ct);

        return actorProjection is not null
               && IsPlatformTargetUser(actorProjection.SecurityDomain, actorProjection.RoleName, actorProjection.RoleSecurityDomain);
    }

    internal static async Task<bool> IsBlockedCrossPlaneUserTargetAsync(
        ErpKhoDbContext db,
        User targetUser,
        ICurrentUser? currentUser,
        int? actorUserId,
        CancellationToken ct)
    {
        var selfActorId = actorUserId ?? (currentUser is { IsAuthenticated: true } ? currentUser.UserId : null);
        if (selfActorId.HasValue && selfActorId.Value > 0 && selfActorId.Value == targetUser.Id)
        {
            return false;
        }

        var roleName = targetUser.Role?.RoleName;
        var roleSecurityDomain = targetUser.Role?.SecurityDomain;
        if (targetUser.Role is null && targetUser.RoleId > 0)
        {
            var roleInfo = await db.Roles.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.Id == targetUser.RoleId)
                .Select(r => new { r.RoleName, r.SecurityDomain })
                .FirstOrDefaultAsync(ct);
            roleName = roleInfo?.RoleName;
            roleSecurityDomain = roleInfo?.SecurityDomain;
        }

        if (!IsPlatformTargetUser(targetUser.SecurityDomain, roleName, roleSecurityDomain))
        {
            return false;
        }

        return !await IsPlatformCallerAsync(db, currentUser, actorUserId, ct);
    }

    internal static string Token(User user, DateTime now)
    {
        // Include natural lockout expiry without writing on reads.
        var bytes = new byte[9];
        BinaryPrimitives.WriteInt64BigEndian(bytes, user.SecurityRevision);
        bytes[8] = (byte)((user.LockoutEnd > now ? 1 : 0) | (user.IsActive ? 2 : 0));
        return Convert.ToBase64String(bytes);
    }

    internal static async Task<User> ReadAsync(
        ErpKhoDbContext db,
        int userId,
        CancellationToken ct,
        ICurrentUser? actor = null)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw CreateUserNotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        if (user.RoleId > 0 && await db.Roles.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == user.RoleId, ct) is { } role)
        {
            user.Role = role;
        }

        if (actor is not null && await IsBlockedCrossPlaneUserTargetAsync(db, user, actor, null, ct))
        {
            throw CreateUserNotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        }

        return user;
    }

    internal static async Task<User> ValidateAsync(
        ErpKhoDbContext db,
        int userId,
        string? token,
        CancellationToken ct,
        ICurrentUser? actor = null)
    {
        var user = await ReadAsync(db, userId, ct, actor);
        if (!string.Equals(token, Token(user, DateTime.UtcNow), StringComparison.Ordinal))
            throw new ConcurrencyException(Conflict);
        return user;
    }

    internal static Task AdvanceAsync(ErpKhoDbContext db, int userId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s =>
            s.SetProperty(u => u.SecurityRevision, u => u.SecurityRevision + 1), ct);

    internal static async Task AuthorizeAsync(ErpKhoDbContext db, ICurrentUser actor, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (!actor.IsAuthenticated || !await db.Users.AnyAsync(u => u.Id == actor.UserId && u.IsActive &&
            (u.LockoutEnd == null || u.LockoutEnd <= now) && u.Role.Permissions.Any(p => p.Permission.Code == "user.manage"), ct))
            throw new ForbiddenException("Bạn không có quyền thực hiện thao tác này.");
    }
}
