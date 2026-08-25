using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Repositories
{
    public class UserRepository : Repository<User>, IUserRepository
    {
        public UserRepository(ErpKhoDbContext context) : base(context)
        {
        }

        public async Task<User?> GetByUsernameWithRoleAsync(string username, CancellationToken cancellationToken = default)
        {
            return await _dbSet
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
        }

        public async Task<(int FailedCount, DateTime? LockoutEnd)> RecordFailedLoginAsync(int userId, int threshold, DateTime nowUtc, TimeSpan lockoutDuration, CancellationToken cancellationToken = default)
        {
            await _dbSet.Where(u => u.Id == userId).ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1)
                .SetProperty(u => u.LastFailedLoginAt, nowUtc)
                .SetProperty(u => u.LockoutEnd, u => u.FailedLoginCount + 1 >= threshold ? nowUtc.Add(lockoutDuration) : u.LockoutEnd), cancellationToken);

            return await _dbSet.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new ValueTuple<int, DateTime?>(u.FailedLoginCount, u.LockoutEnd))
                .SingleAsync(cancellationToken);
        }

        public Task ResetLoginFailuresAsync(int userId, CancellationToken cancellationToken = default) =>
            _dbSet.Where(u => u.Id == userId).ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.LastFailedLoginAt, (DateTime?)null)
                .SetProperty(u => u.LockoutEnd, (DateTime?)null), cancellationToken);
    }
}
