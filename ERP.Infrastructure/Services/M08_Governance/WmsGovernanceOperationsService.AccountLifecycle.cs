using System.Buffers.Binary;
using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class WmsGovernanceOperationsService
{
    // -------------------------------------------------------------------------
    // AccountSecurityController — Account Lifecycle
    // -------------------------------------------------------------------------
    internal static WmsEndpointOutcome CreateUserNotFoundOutcome(int userId) =>
        WmsEndpointOutcome.NotFound(new
        {
            userId,
            code = "USER_NOT_FOUND",
            errorCode = "USER_NOT_FOUND",
            message = "Không tìm thấy người dùng trong không gian thuê bao hiện tại."
        });

    public async Task<WmsEndpointOutcome> LockUserAccountAsync(
        int userId,
        string? rowVersion,
        int? actorUserId,
        IUserSessionService? sessionService,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        if (user is null && await db.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
            throw AccountSecurityState.CreateUserNotFoundException();
        if (user is not null && await AccountSecurityState.IsBlockedCrossPlaneUserTargetAsync(db, user, CurrentUser, actorUserId, cancellationToken))
            throw AccountSecurityState.CreateUserNotFoundException();

        if (actorUserId.HasValue && actorUserId.Value == userId)
            throw new BusinessRuleException("Không thể tự khóa hoặc đặt lại mật khẩu tài khoản của chính mình.");

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            if (db.Database.IsRelational() && db.Database.CurrentTransaction is not null)
                await PermissionAdministrationGuard.LockAsync(db, cancellationToken);

            if (user is not null)
                await EnsureRemainingActiveTenantAdminAsync(user, cancellationToken);

            var revisionBefore = user?.SecurityRevision;
            if (sessionService is not null && !string.IsNullOrWhiteSpace(rowVersion))
                await sessionService.RevokeUserSessionsAsAdminAsync(userId, rowVersion, cancellationToken);

            var now = DateTime.UtcNow;
            if (user is not null && !string.IsNullOrWhiteSpace(rowVersion) && user.SecurityRevision == revisionBefore)
                EnsureMatchingSecurityToken(user, rowVersion, now);

            var lockoutEnd = now.AddMinutes(30);
            if (user is not null)
            {
                user.LockoutEnd = lockoutEnd;
                if (user.SecurityRevision == revisionBefore)
                    user.SecurityRevision++;
            }

            var activeSessions = await db.UserSessions
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions)
            {
                session.RevokedAt = now;
                session.RevokeReason = "AdminLock";
            }

            db.AuditLogs.Add(new AuditLog
            {
                UserId = actorUserId,
                Action = "Authentication.AccountLocked",
                EntityName = "User",
                EntityId = userId,
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return WmsEndpointOutcome.Ok(new
            {
                userId,
                isLockedOut = true,
                lockoutEnd
            });
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<WmsEndpointOutcome> ResetUserPasswordAsync(
        int userId,
        string? temporaryPassword,
        string? reason,
        string? rowVersion,
        int? actorUserId,
        IUserSessionService? sessionService,
        IPasswordHasherService? passwordHasher,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        if (user is null && await db.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
            throw AccountSecurityState.CreateUserNotFoundException();
        if (user is not null && await AccountSecurityState.IsBlockedCrossPlaneUserTargetAsync(db, user, CurrentUser, actorUserId, cancellationToken))
            throw AccountSecurityState.CreateUserNotFoundException();

        if (actorUserId.HasValue && actorUserId.Value == userId)
            throw new BusinessRuleException("Không thể tự khóa hoặc đặt lại mật khẩu tài khoản của chính mình.");

        if (string.IsNullOrWhiteSpace(temporaryPassword) || temporaryPassword.Trim().Length < 8)
            throw new BusinessRuleException("Mật khẩu tạm thời phải có tối thiểu 8 ký tự.");

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        var revisionBefore = user?.SecurityRevision;
        if (sessionService is not null && !string.IsNullOrWhiteSpace(rowVersion))
            await sessionService.RevokeUserSessionsAsAdminAsync(userId, rowVersion, cancellationToken);

        var now = DateTime.UtcNow;
        if (user is not null && !string.IsNullOrWhiteSpace(rowVersion) && user.SecurityRevision == revisionBefore)
            EnsureMatchingSecurityToken(user, rowVersion, now);

        if (user is not null)
        {
            var trimmedPassword = temporaryPassword.Trim();
            user.PasswordHash = (passwordHasher ?? new PasswordHasherService()).HashPassword(trimmedPassword);
            user.FailedLoginCount = 0;
            user.LastFailedLoginAt = null;
            user.LockoutEnd = null;
            if (user.SecurityRevision == revisionBefore)
                user.SecurityRevision++;
        }

        var activeSessions = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in activeSessions)
        {
            session.RevokedAt = now;
            session.RevokeReason = "PasswordReset";
        }

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            Action = "User.PasswordReset",
            EntityName = "User",
            EntityId = userId,
            Reason = reason,
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            userId,
            status = "PASSWORD_RESET_COMPLETED",
            resetAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> DeleteUserAccountAsync(
        int userId,
        string? rowVersion,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        if (user is null || await AccountSecurityState.IsBlockedCrossPlaneUserTargetAsync(db, user, CurrentUser, actorUserId, cancellationToken))
            throw AccountSecurityState.CreateUserNotFoundException();

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            if (db.Database.IsRelational() && db.Database.CurrentTransaction is not null)
                await PermissionAdministrationGuard.LockAsync(db, cancellationToken);

            await EnsureRemainingActiveTenantAdminAsync(user, cancellationToken);

            if (actorUserId.HasValue && actorUserId.Value == userId)
                throw new BusinessRuleException("Không thể tự khóa hoặc vô hiệu hóa tài khoản của chính mình.");

            var now = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(rowVersion))
                EnsureMatchingSecurityToken(user, rowVersion, now);

            user.IsActive = false;
            user.SecurityRevision++;

            var activeSessions = await db.UserSessions
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in activeSessions)
            {
                session.RevokedAt = now;
                session.RevokeReason = "AdminDeactivate";
            }

            db.AuditLogs.Add(new AuditLog
            {
                UserId = actorUserId,
                Action = "Authentication.AccountDeactivated",
                EntityName = "User",
                EntityId = userId,
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);

            return WmsEndpointOutcome.NoContent();
        }
        catch
        {
            if (transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<string?> GetUsernameByIdAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == db.CurrentTenantId, cancellationToken);
        return !string.IsNullOrWhiteSpace(user?.Username) ? user.Username.Trim() : null;
    }

    private async Task EnsureRemainingActiveTenantAdminAsync(User targetUser, CancellationToken cancellationToken)
    {
        var roleName = targetUser.Role?.RoleName
            ?? await db.Roles.AsNoTracking()
                .Where(r => r.Id == targetUser.RoleId)
                .Select(r => r.RoleName)
                .FirstOrDefaultAsync(cancellationToken)
            ?? string.Empty;

        if (!string.Equals(roleName.Trim(), "Admin", StringComparison.OrdinalIgnoreCase))
            return;

        var now = DateTime.UtcNow;
        var activeTenantId = db.CurrentTenantId;
        var hasAnotherActiveTenantAdmin = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(u =>
                u.TenantId == activeTenantId &&
                u.Id != targetUser.Id &&
                u.IsActive &&
                (u.LockoutEnd == null || u.LockoutEnd <= now) &&
                u.Role.RoleName.Trim().ToLower() == "admin",
                cancellationToken);

        if (!hasAnotherActiveTenantAdmin)
            throw new ConcurrencyException("Không thể loại bỏ quản trị viên hợp lệ cuối cùng.");
    }

    private static void EnsureMatchingSecurityToken(User user, string rowVersion, DateTime now)
    {
        var bytes = new byte[9];
        BinaryPrimitives.WriteInt64BigEndian(bytes, user.SecurityRevision);
        bytes[8] = (byte)((user.LockoutEnd > now ? 1 : 0) | (user.IsActive ? 2 : 0));
        var expectedToken = Convert.ToBase64String(bytes);
        if (!string.Equals(rowVersion, expectedToken, StringComparison.Ordinal))
            throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
    }
}
