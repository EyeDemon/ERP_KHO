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

        public Task<User?> GetByUsernameWithRoleAsync(string username, CancellationToken cancellationToken = default) =>
            GetByUsernameWithRoleAsync(username, null, cancellationToken);

        public async Task<User?> GetByUsernameWithRoleAsync(string username, int? tenantId, CancellationToken cancellationToken = default)
        {
            var (normalizedUsername, parsedTenantId, hasInvalidTenantSpecifier) =
                ERP.Application.Common.TenantContext.ResolveLoginIdentifier(username, tenantId, null);
            if (hasInvalidTenantSpecifier || string.IsNullOrWhiteSpace(normalizedUsername))
            {
                return null;
            }

            if (parsedTenantId.HasValue && parsedTenantId.Value > 0)
            {
                var explicitUser = await _dbSet
                    .IgnoreQueryFilters()
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.TenantId == parsedTenantId.Value && u.Username == normalizedUsername, cancellationToken);
                if (explicitUser is not null)
                {
                    _context.UseTenant(explicitUser.TenantId > 0 ? explicitUser.TenantId : 1);
                }

                return explicitUser;
            }

            var activeTenantId = _context.CurrentTenantId;
            var matches = await _dbSet
                .IgnoreQueryFilters()
                .Include(u => u.Role)
                .Where(u => u.Username == normalizedUsername)
                .OrderBy(u => u.TenantId == activeTenantId ? 0 : u.TenantId == 1 ? 1 : 2)
                .ThenBy(u => u.TenantId)
                .Take(2)
                .ToListAsync(cancellationToken);

            if (matches.Count == 1)
            {
                var single = matches[0];
                _context.UseTenant(single.TenantId > 0 ? single.TenantId : 1);
                return single;
            }

            if (matches.Count > 1)
            {
                var preferred = matches.FirstOrDefault(u => u.TenantId == activeTenantId)
                    ?? matches.FirstOrDefault(u => u.TenantId == 1);
                if (preferred is not null)
                {
                    _context.UseTenant(preferred.TenantId > 0 ? preferred.TenantId : 1);
                    return preferred;
                }
            }

            return null;
        }

        public Task<bool> HasPermissionAsync(int userId, string permission, CancellationToken cancellationToken = default)
            => _dbSet.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive &&
                (u.LockoutEnd == null || u.LockoutEnd <= DateTime.UtcNow) &&
                u.Role.Permissions.Any(p => p.Permission.Code == permission), cancellationToken);

        public async Task<(int FailedCount, DateTime? LockoutEnd)> RecordFailedLoginAsync(int userId, int threshold, DateTime nowUtc, TimeSpan lockoutDuration, CancellationToken cancellationToken = default)
        {
            await using var transaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
            var targetTenantId = await _dbSet.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (int?)u.TenantId)
                .SingleOrDefaultAsync(cancellationToken);
            if (targetTenantId is > 0)
            {
                _context.UseTenant(targetTenantId.Value);
            }

            await PermissionAdministrationGuard.LockAsync(_context, cancellationToken);
            if (_context.Database.IsRelational())
            {
                await _dbSet.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1)
                    .SetProperty(u => u.SecurityRevision, u => u.SecurityRevision + 1)
                    .SetProperty(u => u.LastFailedLoginAt, nowUtc)
                    .SetProperty(u => u.LockoutEnd, u => u.FailedLoginCount + 1 >= threshold ? nowUtc.Add(lockoutDuration) : u.LockoutEnd), cancellationToken);
            }
            else
            {
                var tracked = await _dbSet.IgnoreQueryFilters().SingleAsync(u => u.Id == userId, cancellationToken);
                tracked.FailedLoginCount += 1;
                tracked.SecurityRevision += 1;
                tracked.LastFailedLoginAt = nowUtc;
                if (tracked.FailedLoginCount >= threshold)
                    tracked.LockoutEnd = nowUtc.Add(lockoutDuration);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var result = await _dbSet.IgnoreQueryFilters().AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new ValueTuple<int, DateTime?>(u.FailedLoginCount, u.LockoutEnd))
                .SingleAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }

        public async Task ResetLoginFailuresAsync(int userId, CancellationToken cancellationToken = default)
        {
            await using var transaction = _context.Database.IsRelational() && _context.Database.CurrentTransaction is null
                ? await _context.Database.BeginTransactionAsync(cancellationToken) : null;
            var targetTenantId = await _dbSet.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => (int?)u.TenantId)
                .SingleOrDefaultAsync(cancellationToken);
            if (targetTenantId is > 0)
            {
                _context.UseTenant(targetTenantId.Value);
            }

            await PermissionAdministrationGuard.LockAsync(_context, cancellationToken);
            var now = DateTime.UtcNow;
            if (_context.Database.IsRelational())
            {
                await _dbSet.IgnoreQueryFilters().Where(u => u.Id == userId && (u.LockoutEnd == null || u.LockoutEnd <= now) &&
                    (u.FailedLoginCount != 0 || u.LockoutEnd != null)).ExecuteUpdateAsync(setters => setters
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.SecurityRevision, u => u.SecurityRevision + 1)
                    .SetProperty(u => u.LastFailedLoginAt, (DateTime?)null)
                    .SetProperty(u => u.LockoutEnd, (DateTime?)null), cancellationToken);
            }
            else
            {
                var tracked = await _dbSet.IgnoreQueryFilters().SingleOrDefaultAsync(
                    u => u.Id == userId && (u.LockoutEnd == null || u.LockoutEnd <= now) && (u.FailedLoginCount != 0 || u.LockoutEnd != null),
                    cancellationToken);
                if (tracked is not null)
                {
                    tracked.FailedLoginCount = 0;
                    tracked.SecurityRevision += 1;
                    tracked.LastFailedLoginAt = null;
                    tracked.LockoutEnd = null;
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
    }
}
