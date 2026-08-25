using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ERP.Application.Interfaces;
using ERP.Application.Exceptions;
using ERP.Infrastructure.Services;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerAccountLockoutConcurrencyTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task ConcurrentFailures_AreCountedWithoutLostUpdates()
    {
        var username = $"QALOCK_{Guid.NewGuid():N}";
        int userId;
        int roleId;
        await using (var setup = CreateContext())
        {
            var role = new Role { RoleName = $"LockoutRole_{Guid.NewGuid():N}", Description = "Temporary SQL integration-test role" };
            setup.Roles.Add(role);
            var user = new User { Username = username, PasswordHash = "test", FullName = "Lockout concurrency", Role = role };
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
            userId = user.Id;
            roleId = role.Id;
        }

        try
        {
            var attempts = Enumerable.Range(0, 20).Select(async _ =>
            {
                await using var context = CreateContext();
                var repository = new UserRepository(context);
                await repository.RecordFailedLoginAsync(userId, 5, DateTime.UtcNow, TimeSpan.FromMinutes(15));
            });
            await Task.WhenAll(attempts);

            await using var verify = CreateContext();
            var user = await verify.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            user.FailedLoginCount.Should().Be(20);
            user.LockoutEnd.Should().BeAfter(DateTime.UtcNow);
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
            await cleanup.Roles.Where(r => r.Id == roleId).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task OnlyGlobalAdmin_CanUnlockAndResetAccount()
    {
        await using var context = CreateContext();
        var adminRole = new Role { RoleName = $"AdminTest_{Guid.NewGuid():N}" };
        var targetRole = new Role { RoleName = $"TargetTest_{Guid.NewGuid():N}" };
        var admin = new User { Username = $"QAADMIN_{Guid.NewGuid():N}", PasswordHash = "test", FullName = "Test admin", Role = adminRole };
        var target = new User { Username = $"QAUNLOCK_{Guid.NewGuid():N}", PasswordHash = "test", FullName = "Unlock target", Role = targetRole, FailedLoginCount = 5, LockoutEnd = DateTime.UtcNow.AddMinutes(15) };
        context.Users.AddRange(admin, target);
        await context.SaveChangesAsync();

        try
        {
            var denied = new AccountAdminService(context, new TestCurrentUser(target.Id, false));
            var deniedAction = () => denied.UnlockAsync(target.Id);
            await deniedAction.Should().ThrowAsync<ForbiddenException>();

            var allowed = new AccountAdminService(context, new TestCurrentUser(admin.Id, true));
            await allowed.UnlockAsync(target.Id);
            await context.Entry(target).ReloadAsync();
            target.FailedLoginCount.Should().Be(0);
            target.LockoutEnd.Should().BeNull();
        }
        finally
        {
            await context.Users.Where(u => u.Id == target.Id).ExecuteDeleteAsync();
            await context.Users.Where(u => u.Id == admin.Id).ExecuteDeleteAsync();
            await context.Roles.Where(r => r.Id == adminRole.Id || r.Id == targetRole.Id).ExecuteDeleteAsync();
        }
    }

    private static ErpKhoDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpKhoDbContext>()
        .UseSqlServer(ConnectionString).Options);

    private sealed record TestCurrentUser(int UserId, bool IsGlobalAdmin) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
