using ERP.Domain.Entities;

namespace ERP.Domain.Interfaces;

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByUsernameWithRoleAsync(string username, CancellationToken cancellationToken = default);
    Task<(int FailedCount, DateTime? LockoutEnd)> RecordFailedLoginAsync(int userId, int threshold, DateTime nowUtc, TimeSpan lockoutDuration, CancellationToken cancellationToken = default);
    Task ResetLoginFailuresAsync(int userId, CancellationToken cancellationToken = default);
}
