using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class AccessTokenSessionValidator(ErpKhoDbContext context) : IAccessTokenSessionValidator
{
    public Task<bool> IsActiveAsync(int userId, string accessTokenJti, CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(accessTokenJti)) return Task.FromResult(false);

        var now = DateTime.UtcNow;
        return context.UserSessions.AsNoTracking().AnyAsync(
            x => x.UserId == userId
                && x.AccessTokenJti == accessTokenJti
                && x.RevokedAt == null
                && x.ExpiresAt > now
                && x.User.IsActive
                && (!x.User.LockoutEnd.HasValue || x.User.LockoutEnd <= now),
            cancellationToken);
    }
}
