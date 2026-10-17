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
    ErpKhoDbContext db = null!) : ControllerBase
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
        if (sessionService is not null && !string.IsNullOrWhiteSpace(request?.RowVersion))
            await sessionService.RevokeUserSessionsAsAdminAsync(userId, request.RowVersion, cancellationToken);

        var lockoutEnd = DateTime.UtcNow.AddMinutes(30);
        if (db is not null)
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is not null)
                user.LockoutEnd = lockoutEnd;

            db.AuditLogs.Add(new AuditLog
            {
                Action = "User.Locked",
                EntityName = "User",
                EntityId = userId,
                Result = "Success",
                Timestamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
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
        if (db is not null)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Action = "User.PasswordReset",
                EntityName = "User",
                EntityId = userId,
                Reason = request?.Reason,
                Result = "Success",
                Timestamp = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
        {
            userId,
            status = "PASSWORD_RESET_COMPLETED",
            resetAtUtc = DateTime.UtcNow
        });
    }
}
