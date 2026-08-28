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
        var now = DateTime.UtcNow;
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();
        var session = NewSession(user.Id, Guid.NewGuid(), accessToken.Jti, refreshToken, requestContext, now);
        context.UserSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        return ToResult(accessToken, refreshToken, session.ExpiresAt);
    }

    public async Task<SessionTokenResultDto> RefreshAsync(string refreshToken, SessionContextDto requestContext, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new UnauthorizedAccessException(GenericFailure);
        var hash = tokenService.HashRefreshToken(refreshToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var now = DateTime.UtcNow;
        var current = await context.UserSessions.Include(x => x.User).ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, cancellationToken);

        if (current is null || !tokenService.FixedTimeHashEquals(current.RefreshTokenHash, hash))
            throw new UnauthorizedAccessException(GenericFailure);

        if (current.RevokedAt.HasValue)
        {
            await context.UserSessions.Where(x => x.RefreshTokenFamilyId == current.RefreshTokenFamilyId && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.RevokedAt, now)
                    .SetProperty(x => x.RevokedBy, "system")
                    .SetProperty(x => x.RevokeReason, "Refresh token reuse detected"), cancellationToken);
            context.AuditLogs.Add(SecurityAudit(current.UserId, "Authentication.RefreshTokenReuseDetected", current.Id, now));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new UnauthorizedAccessException(GenericFailure);
        }

        if (current.ExpiresAt <= now || !current.User.IsActive || current.User.LockoutEnd > now)
            throw new UnauthorizedAccessException(GenericFailure);

        var updated = await context.UserSessions.Where(x => x.Id == current.Id && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.LastUsedAt, now)
                .SetProperty(x => x.RevokedBy, "rotation")
                .SetProperty(x => x.RevokeReason, "Rotated"), cancellationToken);
        if (updated != 1)
        {
            await context.UserSessions.Where(x => x.RefreshTokenFamilyId == current.RefreshTokenFamilyId && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedBy, "system").SetProperty(x => x.RevokeReason, "Concurrent refresh conflict"), cancellationToken);
            context.AuditLogs.Add(SecurityAudit(current.UserId, "Authentication.RefreshTokenReuseDetected", current.Id, now));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new UnauthorizedAccessException(GenericFailure);
        }

        var accessToken = tokenService.GenerateAccessToken(current.User);
        var nextRefreshToken = tokenService.GenerateRefreshToken();
        var replacement = NewSession(current.UserId, current.RefreshTokenFamilyId, accessToken.Jti, nextRefreshToken, requestContext, now);
        context.UserSessions.Add(replacement);
        await context.SaveChangesAsync(cancellationToken);
        await context.UserSessions.Where(x => x.Id == current.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReplacedBySessionId, replacement.Id), cancellationToken);

        context.AuditLogs.Add(SecurityAudit(current.UserId, "Authentication.RefreshRotated", replacement.Id, now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToResult(accessToken, nextRefreshToken, replacement.ExpiresAt);
    }

    public async Task LogoutAsync(string refreshToken, int? actorUserId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return;
        var hash = tokenService.HashRefreshToken(refreshToken);
        var now = DateTime.UtcNow;
        var session = await context.UserSessions.SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, cancellationToken);
        if (session is null || (actorUserId.HasValue && session.UserId != actorUserId.Value)) return;
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
        if (!ownerId.HasValue) return;
        if (ownerId.Value != currentUser.UserId && !currentUser.IsGlobalAdmin) throw new UnauthorizedAccessException("Bạn không có quyền thu hồi phiên này.");
        await RevokeWhereAsync(x => x.Id == sessionId, currentUser.UserId.ToString(), "Session revoked", DateTime.UtcNow, cancellationToken);
    }

    public async Task RevokeUserSessionsAsAdminAsync(int userId, CancellationToken cancellationToken = default)
    {
        EnsureAuthenticated();
        if (!currentUser.IsGlobalAdmin) throw new UnauthorizedAccessException("Chỉ quản trị viên toàn cục được thu hồi phiên người dùng.");
        await using var transaction = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;
        var now = DateTime.UtcNow;
        var revokedCount = await RevokeWhereAsync(x => x.UserId == userId, currentUser.UserId.ToString(), "Administrator revoked all sessions", now, cancellationToken);
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
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public Task<int> CleanupAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default) =>
        context.UserSessions.Where(x => x.ExpiresAt < cutoffUtc || (x.RevokedAt != null && x.RevokedAt < cutoffUtc)).ExecuteDeleteAsync(cancellationToken);

    private UserSession NewSession(int userId, Guid familyId, string jti, string refreshToken, SessionContextDto requestContext, DateTime now) => new()
    {
        Id = Guid.NewGuid(), UserId = userId, RefreshTokenFamilyId = familyId,
        RefreshTokenHash = tokenService.HashRefreshToken(refreshToken), AccessTokenJti = jti,
        CreatedAt = now, ExpiresAt = now.AddDays(options.RefreshTokenDays),
        IpAddress = requestContext.IpAddress, UserAgent = Truncate(requestContext.UserAgent, 512)
    };

    private async Task<int> RevokeWhereAsync(System.Linq.Expressions.Expression<Func<UserSession, bool>> predicate, string actor, string reason, DateTime now, CancellationToken cancellationToken)
    {
        return await context.UserSessions.Where(predicate).Where(x => x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedBy, actor).SetProperty(x => x.RevokeReason, reason), cancellationToken);
    }

    private static AuditLog SecurityAudit(int userId, string action, Guid sessionId, DateTime now) => new()
        { UserId = userId, Action = action, EntityName = "UserSession", EntityId = null, Timestamp = now, NewValues = $"SessionId={sessionId}" };

    private static SessionTokenResultDto ToResult(AccessTokenDto access, string refresh, DateTime refreshExpiry) => new()
        { AccessToken = access.Token, AccessTokenExpiresAtUtc = access.ExpiresAtUtc, RefreshToken = refresh, RefreshTokenExpiresAtUtc = refreshExpiry };

    private void EnsureAuthenticated()
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedAccessException("Yêu cầu đăng nhập.");
    }

    private static string? Truncate(string? value, int max) => value is { Length: > 0 } ? value[..Math.Min(value.Length, max)] : null;
}
