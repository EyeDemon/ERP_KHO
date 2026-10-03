using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class AccountAdminService(ErpKhoDbContext context, ICurrentUser currentUser) : IAccountAdminService
{
    public async Task<ERP.Application.DTOs.AccountSecurityDto> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        await AccountSecurityState.AuthorizeAsync(context, currentUser, cancellationToken);
        var user = await AccountSecurityState.ReadAsync(context, userId, cancellationToken);
        var now = DateTime.UtcNow;
        return new(user.IsActive, user.LockoutEnd > now, user.LockoutEnd, AccountSecurityState.Token(user, now));
    }

    public async Task UnlockAsync(int userId, string? rowVersion, CancellationToken cancellationToken = default)
    {
        await AccountSecurityState.AuthorizeAsync(context, currentUser, cancellationToken);
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await PermissionAdministrationGuard.LockAsync(context, cancellationToken);
        await AccountSecurityState.AuthorizeAsync(context, currentUser, cancellationToken);
        await AccountSecurityState.ValidateAsync(context, userId, rowVersion, cancellationToken);
        var changed = await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(setters => setters
            .SetProperty(u => u.FailedLoginCount, 0)
            .SetProperty(u => u.LastFailedLoginAt, (DateTime?)null)
            .SetProperty(u => u.LockoutEnd, (DateTime?)null)
            .SetProperty(u => u.SecurityRevision, u => u.SecurityRevision + 1), cancellationToken);
        if (changed == 0)
            throw new NotFoundException("Không tìm thấy người dùng.");

        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = "Authentication.AccountUnlocked",
            EntityName = "User",
            EntityId = userId,
            Timestamp = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        await PermissionAdministrationGuard.EnsureAdministratorAsync(context, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
