using ERP.Api.Authorization;
using ERP.Application.Interfaces;
using ERP.Application.DTOs;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed record ResetPasswordAdminRequest(
    string? TemporaryPassword = null,
    string? Reason = null,
    string? RowVersion = null);

[ApiController]
[Route("api/users/{userId:int}/security")]
[Authorize]
[PermissionAuthorize(AppPermissions.UserManage)]
public sealed class AccountSecurityController(
    IAccountAdminService service,
    IUserSessionService sessionService,
    ErpKhoDbContext db = null!,
    IPasswordHasherService? passwordHasher = null) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int userId, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(userId, cancellationToken));

    [HttpPost("lock")]
    [IdempotentCommand("User.Lock")]
    public async Task<IActionResult> Lock(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = ResolveActorUserId();
        if (actorUserId.HasValue && actorUserId.Value == userId)
            throw new ERP.Application.Exceptions.BusinessRuleException("Không thể tự khóa hoặc đặt lại mật khẩu tài khoản của chính mình.");

        var user = db is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            : null;
        if (db is not null && user is null && await db.Users.AnyAsync(cancellationToken))
            throw new ERP.Application.Exceptions.NotFoundException("Không tìm thấy người dùng.");

        await using var transaction = db is not null && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var revisionBefore = user?.SecurityRevision;
        if (sessionService is not null && !string.IsNullOrWhiteSpace(request?.RowVersion))
            await sessionService.RevokeUserSessionsAsAdminAsync(userId, request.RowVersion, cancellationToken);

        var now = DateTime.UtcNow;
        if (user is not null && !string.IsNullOrWhiteSpace(request?.RowVersion) && user.SecurityRevision == revisionBefore)
            EnsureMatchingSecurityToken(user, request.RowVersion, now);

        var lockoutEnd = now.AddMinutes(30);
        if (db is not null)
        {
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
        }

        return Ok(new
        {
            userId,
            isLockedOut = true,
            lockoutEnd
        });
    }

    [HttpPost("unlock")]
    [IdempotentCommand("User.Unlock")]
    public async Task<IActionResult> Unlock(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null, CancellationToken cancellationToken = default)
    {
        await service.UnlockAsync(userId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("revoke-sessions")]
    [IdempotentCommand("User.RevokeSessions")]
    public async Task<IActionResult> RevokeSessions(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null, CancellationToken cancellationToken = default)
    {
        await sessionService.RevokeUserSessionsAsAdminAsync(userId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [IdempotentCommand("User.ResetPassword")]
    public async Task<IActionResult> ResetPassword(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ResetPasswordAdminRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var actorUserId = ResolveActorUserId();
        if (actorUserId.HasValue && actorUserId.Value == userId)
            throw new ERP.Application.Exceptions.BusinessRuleException("Không thể tự khóa hoặc đặt lại mật khẩu tài khoản của chính mình.");

        if (string.IsNullOrWhiteSpace(request?.TemporaryPassword) || request.TemporaryPassword.Trim().Length < 8)
            throw new ERP.Application.Exceptions.BusinessRuleException("Mật khẩu tạm thời phải có tối thiểu 8 ký tự.");

        var user = db is not null
            ? await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            : null;
        if (db is not null && user is null && await db.Users.AnyAsync(cancellationToken))
            throw new ERP.Application.Exceptions.NotFoundException("Không tìm thấy người dùng.");

        await using var transaction = db is not null && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var revisionBefore = user?.SecurityRevision;
        if (sessionService is not null && !string.IsNullOrWhiteSpace(request.RowVersion))
            await sessionService.RevokeUserSessionsAsAdminAsync(userId, request.RowVersion, cancellationToken);

        var now = DateTime.UtcNow;
        if (user is not null && !string.IsNullOrWhiteSpace(request.RowVersion) && user.SecurityRevision == revisionBefore)
            EnsureMatchingSecurityToken(user, request.RowVersion, now);

        if (db is not null)
        {
            if (user is not null)
            {
                var trimmedPassword = request.TemporaryPassword.Trim();
                user.PasswordHash = (passwordHasher ?? new ERP.Infrastructure.Services.PasswordHasherService()).HashPassword(trimmedPassword);
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
                Reason = request.Reason,
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
                await transaction.CommitAsync(cancellationToken);
        }

        return Ok(new
        {
            userId,
            status = "PASSWORD_RESET_COMPLETED",
            resetAtUtc = now
        });
    }

    private int? ResolveActorUserId() =>
        int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;

    private static void EnsureMatchingSecurityToken(User user, string rowVersion, DateTime now)
    {
        var bytes = new byte[9];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, user.SecurityRevision);
        bytes[8] = (byte)((user.LockoutEnd > now ? 1 : 0) | (user.IsActive ? 2 : 0));
        var expectedToken = Convert.ToBase64String(bytes);
        if (!string.Equals(rowVersion, expectedToken, StringComparison.Ordinal))
            throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
    }
}
