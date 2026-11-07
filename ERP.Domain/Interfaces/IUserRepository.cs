using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<bool> HasPermissionAsync(int userId, string permission, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameWithRoleAsync(string username, CancellationToken cancellationToken = default);
    Task<User?> GetByUsernameWithRoleAsync(string username, int? tenantId, CancellationToken cancellationToken = default) =>
        GetByUsernameWithRoleAsync(username, cancellationToken);
    Task<(int FailedCount, DateTime? LockoutEnd)> RecordFailedLoginAsync(int userId, int threshold, DateTime nowUtc, TimeSpan lockoutDuration, CancellationToken cancellationToken = default);
    Task ResetLoginFailuresAsync(int userId, CancellationToken cancellationToken = default);
}
