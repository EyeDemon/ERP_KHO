using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using ERP.Infrastructure.Services;

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
            await using var transaction = _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
            await PermissionAdministrationGuard.LockAsync(_context, cancellationToken);
            await _dbSet.Where(u => u.Id == userId).ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1)
                .SetProperty(u => u.SecurityRevision, u => u.SecurityRevision + 1)
                .SetProperty(u => u.LastFailedLoginAt, nowUtc)
                .SetProperty(u => u.LockoutEnd, u => u.FailedLoginCount + 1 >= threshold ? nowUtc.Add(lockoutDuration) : u.LockoutEnd), cancellationToken);

            var result = await _dbSet.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new ValueTuple<int, DateTime?>(u.FailedLoginCount, u.LockoutEnd))
                .SingleAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }

        public async Task ResetLoginFailuresAsync(int userId, CancellationToken cancellationToken = default)
        {
            await using var transaction = _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
            await PermissionAdministrationGuard.LockAsync(_context, cancellationToken);
            var now = DateTime.UtcNow;
            await _dbSet.Where(u => u.Id == userId && (u.LockoutEnd == null || u.LockoutEnd <= now) &&
                (u.FailedLoginCount != 0 || u.LockoutEnd != null)).ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FailedLoginCount, 0)
                .SetProperty(u => u.SecurityRevision, u => u.SecurityRevision + 1)
                .SetProperty(u => u.LastFailedLoginAt, (DateTime?)null)
                .SetProperty(u => u.LockoutEnd, (DateTime?)null), cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
    }
}
