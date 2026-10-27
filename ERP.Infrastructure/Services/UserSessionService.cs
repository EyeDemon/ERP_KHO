using System.Data;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class UserSessionService(
    ErpKhoDbContext context,
    ITokenService tokenService,
    ICurrentUser currentUser,
    SessionSecurityOptions options) : IUserSessionService
{
    private const string GenericFailure = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.";

    public async Task<SessionTokenResultDto> CreateAsync(User user, SessionContextDto requestContext, CancellationToken cancellationToken = default)
    {
        var resolvedTenantId = user.TenantId > 0 ? user.TenantId : 1;
        context.UseTenant(resolvedTenantId);
        await using var transaction = context.Database.IsRelational() && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        var authenticatedHash = user.PasswordHash;
        user = await context.Users.IgnoreQueryFilters().AsNoTracking().Include(u => u.Role)
            .SingleOrDefaultAsync(u => u.Id == user.Id, cancellationToken)
            ?? throw new UnauthorizedAccessException(GenericFailure);
        if (user.TenantId > 0)
        {
            resolvedTenantId = user.TenantId;
            context.UseTenant(resolvedTenantId);
        }
        else
        {
            user.TenantId = resolvedTenantId;
        }

        var now = DateTime.UtcNow;
        if (!user.IsActive || user.LockoutEnd > now || user.PasswordHash != authenticatedHash)
            throw new UnauthorizedAccessException(GenericFailure);
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();
        var session = NewSession(user.Id, resolvedTenantId, Guid.NewGuid(), accessToken.Jti, refreshToken, requestContext, now);
        context.UserSessions.Add(session);
        await IncrementUserSecurityRevisionAsync(user.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return ToResult(accessToken, refreshToken, session.ExpiresAt);
    }

    public async Task<SessionTokenResultDto> RefreshAsync(string refreshToken, SessionContextDto requestContext, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new UnauthorizedAccessException(GenericFailure);
        var hash = tokenService.HashRefreshToken(refreshToken);
        await using var transaction = context.Database.IsRelational() && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        var current = await context.UserSessions.IgnoreQueryFilters().AsNoTracking().Include(x => x.User).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, cancellationToken);

        if (current is null || !tokenService.FixedTimeHashEquals(current.RefreshTokenHash, hash))
            throw new UnauthorizedAccessException(GenericFailure);

        var resolvedTenantId = current.TenantId > 0 ? current.TenantId : (current.User?.TenantId > 0 ? current.User.TenantId : 1);
        context.UseTenant(resolvedTenantId);
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        var now = DateTime.UtcNow;
        current = await context.UserSessions.IgnoreQueryFilters().AsNoTracking().Include(x => x.User).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, cancellationToken);

        if (current is null || !tokenService.FixedTimeHashEquals(current.RefreshTokenHash, hash))
            throw new UnauthorizedAccessException(GenericFailure);

        resolvedTenantId = current.TenantId > 0 ? current.TenantId : (current.User?.TenantId > 0 ? current.User.TenantId : resolvedTenantId);
        context.UseTenant(resolvedTenantId);

        if (current.RevokedAt.HasValue)
        {
            var revoked = await RevokeRefreshFamilyAsync(current.RefreshTokenFamilyId, "system", "Refresh token reuse detected", now, cancellationToken);
            if (revoked > 0)
            {
                await IncrementUserSecurityRevisionAsync(current.UserId, cancellationToken);
            }
            var reuseAudit = SecurityAudit(current.UserId, "Authentication.RefreshTokenReuseDetected", current.Id, now);
            reuseAudit.TenantId = resolvedTenantId;
            context.AuditLogs.Add(reuseAudit);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw new UnauthorizedAccessException(GenericFailure);
        }

        if (current.ExpiresAt <= now || !current.User!.IsActive || current.User.LockoutEnd > now)
            throw new UnauthorizedAccessException(GenericFailure);

        int updated;
        if (context.Database.IsRelational())
        {
            updated = await context.UserSessions.IgnoreQueryFilters()
                .Where(x => x.Id == current.Id && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.LastUsedAt, now)
                    .SetProperty(x => x.RevokedBy, "rotation")
                    .SetProperty(x => x.RevokeReason, "Rotated"), cancellationToken);
        }
        else
        {
            var trackedSession = await context.UserSessions.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.Id == current.Id && x.RevokedAt == null, cancellationToken);
            if (trackedSession is not null)
            {
                trackedSession.RevokedAt = now;
                trackedSession.LastUsedAt = now;
                trackedSession.RevokedBy = "rotation";
                trackedSession.RevokeReason = "Rotated";
                await context.SaveChangesAsync(cancellationToken);
                updated = 1;
            }
            else
            {
                updated = 0;
            }
        }

        if (updated != 1)
        {
            var revoked = await RevokeRefreshFamilyAsync(current.RefreshTokenFamilyId, "system", "Concurrent refresh conflict", now, cancellationToken);
            if (revoked > 0)
            {
                await IncrementUserSecurityRevisionAsync(current.UserId, cancellationToken);
            }
            var conflictAudit = SecurityAudit(current.UserId, "Authentication.RefreshTokenReuseDetected", current.Id, now);
            conflictAudit.TenantId = resolvedTenantId;
            context.AuditLogs.Add(conflictAudit);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            throw new UnauthorizedAccessException(GenericFailure);
        }

        current.User.TenantId = resolvedTenantId;
        var accessToken = tokenService.GenerateAccessToken(current.User);
        var nextRefreshToken = tokenService.GenerateRefreshToken();
        var replacement = NewSession(current.UserId, resolvedTenantId, current.RefreshTokenFamilyId, accessToken.Jti, nextRefreshToken, requestContext, now);
        context.UserSessions.Add(replacement);
        await IncrementUserSecurityRevisionAsync(current.UserId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        if (context.Database.IsRelational())
        {
            await context.UserSessions.IgnoreQueryFilters().Where(x => x.Id == current.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReplacedBySessionId, replacement.Id), cancellationToken);
        }
        else
        {
            var oldTracked = await context.UserSessions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == current.Id, cancellationToken);
            if (oldTracked is not null)
            {
                oldTracked.ReplacedBySessionId = replacement.Id;
                await context.SaveChangesAsync(cancellationToken);
            }
        }

        var rotatedAudit = SecurityAudit(current.UserId, "Authentication.RefreshRotated", replacement.Id, now);
        rotatedAudit.TenantId = resolvedTenantId;
        context.AuditLogs.Add(rotatedAudit);
        await context.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return ToResult(accessToken, nextRefreshToken, replacement.ExpiresAt);
    }

    public async Task LogoutAsync(string refreshToken, int? actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = tokenService.HashRefreshToken(refreshToken);
        var now = DateTime.UtcNow;
        var session = await context.UserSessions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, cancellationToken);
        if (session is null || (actorUserId.HasValue && session.UserId != actorUserId.Value)) return;
        var resolvedTenantId = session.TenantId > 0 ? session.TenantId : 1;
        context.UseTenant(resolvedTenantId);
        await RevokeWhereAsync(x => x.Id == session.Id, actorUserId?.ToString() ?? "cookie", "Logout", now, cancellationToken);
    }

    public async Task LogoutAllAsync(CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        await RevokeWhereAsync(x => x.UserId == currentUser.UserId, currentUser.UserId.ToString(), "Logout all", DateTime.UtcNow, cancellationToken);
    }

    public async Task<IReadOnlyList<UserSessionDto>> GetCurrentUserSessionsAsync(string? currentRefreshToken, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var currentHash = string.IsNullOrWhiteSpace(currentRefreshToken) ? null : tokenService.HashRefreshToken(currentRefreshToken);
        return await context.UserSessions.AsNoTracking().Where(x => x.UserId == currentUser.UserId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new UserSessionDto(x.Id, x.CreatedAt, x.ExpiresAt, x.LastUsedAt, x.RevokedAt, x.IpAddress, x.UserAgent, currentHash != null && x.RefreshTokenHash == currentHash))
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        var ownerId = await context.UserSessions.Where(x => x.Id == sessionId).Select(x => (int?)x.UserId).SingleOrDefaultAsync(cancellationToken);
        if (!ownerId.HasValue || ownerId.Value != currentUser.UserId)
            throw new ERP.Application.Exceptions.NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        await RevokeWhereAsync(x => x.Id == sessionId && x.UserId == currentUser.UserId, currentUser.UserId.ToString(), "Session revoked", DateTime.UtcNow, cancellationToken);
    }

    public async Task RevokeUserSessionsAsAdminAsync(int userId, string? rowVersion, CancellationToken cancellationToken = default)
    {
        await AccountSecurityState.AuthorizeAsync(context, currentUser, cancellationToken);
        await using var transaction = context.Database.IsRelational() && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        await AccountSecurityState.AuthorizeAsync(context, currentUser, cancellationToken);
        await AccountSecurityState.ValidateAsync(context, userId, rowVersion, cancellationToken);
        var now = DateTime.UtcNow;
        var revokedCount = await RevokeWhereAsync(x => x.UserId == userId, currentUser.UserId.ToString(), "Administrator revoked all sessions", now, cancellationToken);
        if (revokedCount == 0) await AccountSecurityState.AdvanceAsync(context, userId, cancellationToken);
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Authentication.AdminRevokedUserSessions",
            EntityName = "User",
            EntityId = userId,
            Timestamp = now,
            NewValues = $"RevokedSessionCount={revokedCount}"
        });
        await context.SaveChangesAsync(cancellationToken);
        await PermissionAdministrationGuard.EnsureAdministratorAsync(context, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CleanupAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        await using var transaction = context.Database.IsRelational() && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        var expired = context.UserSessions.Where(x => x.ExpiresAt < cutoffUtc || (x.RevokedAt != null && x.RevokedAt < cutoffUtc));
        var users = await expired.Select(x => x.UserId).Distinct().ToListAsync(cancellationToken);
        var count = await expired.ExecuteDeleteAsync(cancellationToken);
        foreach (var userId in users) await AccountSecurityState.AdvanceAsync(context, userId, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return count;
    }

    private UserSession NewSession(int userId, int tenantId, Guid familyId, string jti, string refreshToken, SessionContextDto requestContext, DateTime now) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, TenantId = tenantId > 0 ? tenantId : 1, RefreshTokenFamilyId = familyId,
        RefreshTokenHash = tokenService.HashRefreshToken(refreshToken), AccessTokenJti = jti,
        CreatedAt = now, ExpiresAt = now.AddDays(options.RefreshTokenDays),
        IpAddress = requestContext.IpAddress, UserAgent = Truncate(requestContext.UserAgent, 512)
    };

    private async Task<int> RevokeWhereAsync(System.Linq.Expressions.Expression<Func<UserSession, bool>> predicate, string actor, string reason, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = context.Database.IsRelational() && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        var activeTenantId = context.CurrentTenantId;
        var sessions = context.UserSessions.IgnoreQueryFilters()
            .Where(x => x.TenantId == activeTenantId)
            .Where(predicate)
            .Where(x => x.RevokedAt == null);
        var users = await sessions.Select(x => x.UserId).Distinct().ToListAsync(cancellationToken);
        int count;
        if (context.Database.IsRelational())
        {
            count = await sessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.RevokedBy, actor).SetProperty(x => x.RevokeReason, reason), cancellationToken);
        }
        else
        {
            var matched = await sessions.ToListAsync(cancellationToken);
            foreach (var s in matched)
            {
                s.RevokedAt = now;
                s.RevokedBy = actor;
                s.RevokeReason = reason;
            }
            count = matched.Count;
            if (count > 0) await context.SaveChangesAsync(cancellationToken);
        }
        foreach (var userId in users)
        {
            await IncrementUserSecurityRevisionAsync(userId, cancellationToken);
        }
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return count;
    }

    private async Task<int> RevokeRefreshFamilyAsync(Guid familyId, string actor, string reason, DateTime now, CancellationToken cancellationToken)
    {
        if (context.Database.IsRelational())
        {
            return await context.UserSessions.IgnoreQueryFilters()
                .Where(x => x.RefreshTokenFamilyId == familyId && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedBy, actor)
                    .SetProperty(x => x.RevokeReason, reason), cancellationToken);
        }

        var familySessions = await context.UserSessions.IgnoreQueryFilters()
            .Where(x => x.RefreshTokenFamilyId == familyId && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var s in familySessions)
        {
            s.RevokedAt = now;
            s.RevokedBy = actor;
            s.RevokeReason = reason;
        }
        if (familySessions.Count > 0) await context.SaveChangesAsync(cancellationToken);
        return familySessions.Count;
    }

    private async Task IncrementUserSecurityRevisionAsync(int userId, CancellationToken cancellationToken)
    {
        if (context.Database.IsRelational())
        {
            await context.Users.IgnoreQueryFilters().Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityRevision, u => u.SecurityRevision + 1), cancellationToken);
            return;
        }

        var trackedUser = await context.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (trackedUser is not null)
        {
            trackedUser.SecurityRevision += 1;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static AuditLog SecurityAudit(int userId, string action, Guid sessionId, DateTime now) => new()
        { UserId = userId, Action = action, EntityName = "UserSession", EntityId = null, Timestamp = now, NewValues = $"SessionId={sessionId}" };

    private static SessionTokenResultDto ToResult(AccessTokenDto access, string refresh, DateTime refreshExpiry) => new()
        { AccessToken = access.Token, AccessTokenExpiresAtUtc = access.ExpiresAtUtc, RefreshToken = refresh, RefreshTokenExpiresAtUtc = refreshExpiry };

    private void EnsureAuthenticated()
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedAccessException("Yêu cầu đăng nhập.");
    }

    public async Task<object?> GetAuthenticatedUserProfileAsync(int userId, CancellationToken cancellationToken = default) =>
        await context.Users.AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive && (!x.LockoutEnd.HasValue || x.LockoutEnd <= DateTime.UtcNow))
            .Select(x => new
            {
                x.Id,
                x.Username,
                x.FullName,
                Role = x.Role.RoleName,
                Permissions = x.Role.Permissions.Select(p => p.Permission.Code).OrderBy(c => c).ToArray(),
                WarehouseIds = x.WarehouseAccesses.Select(w => w.WarehouseId).OrderBy(id => id).ToArray()
            })
            .SingleOrDefaultAsync(cancellationToken);

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } ? value[..Math.Min(value.Length, max)] : null;
}
