using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class AccountAdminService(ErpKhoDbContext context, ICurrentUser currentUser) : IAccountAdminService
{
    public async Task UnlockAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsGlobalAdmin)
            throw new ForbiddenException("Bạn không có quyền mở khóa tài khoản.");

        var changed = await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(setters => setters
            .SetProperty(u => u.FailedLoginCount, 0)
            .SetProperty(u => u.LastFailedLoginAt, (DateTime?)null)
            .SetProperty(u => u.LockoutEnd, (DateTime?)null), cancellationToken);
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
    }
}
