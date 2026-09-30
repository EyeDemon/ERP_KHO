using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerPermissionAdministrationTests
{
    private static ErpKhoDbContext Context() => new(new DbContextOptionsBuilder<ErpKhoDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!).Options);

    [SqlServerFact]
    public async Task AggregateVersionSerializesConcurrentGrantAndRevoke()
    {
        await using var setup = Context();
        var actor = await setup.Users.SingleAsync(u => u.Username == "qa_permission_bootstrap");
        var role = new Role { RoleName = "PermissionRace_" + Guid.NewGuid().ToString("N") };
        var permissions = await setup.Permissions.Where(p => p.Code == "product.read" || p.Code == "warehouse.read").OrderBy(p => p.Code).ToArrayAsync();
        role.Permissions.Add(new RolePermission { Permission = permissions[0], GrantedByUserId = actor.Id });
        setup.Roles.Add(role); await setup.SaveChangesAsync();
        var baseline = role.RowVersion.ToArray();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Mutate(bool add)
        {
            await using var db = Context();
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await PermissionAdministrationGuard.LockAsync(db, default);
                if (add) { entered.SetResult(); await release.Task; }
                var current = await db.Roles.SingleAsync(r => r.Id == role.Id);
                if (!current.RowVersion.SequenceEqual(baseline)) throw new ConcurrencyException("Stale aggregate.");
                if (add) db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissions[1].Id, GrantedByUserId = actor.Id });
                else db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(g => g.RoleId == role.Id && g.PermissionId == permissions[0].Id));
                current.GrantRevision++;
                db.AuditLogs.Add(new AuditLog { UserId = actor.Id, EntityName = "Role", EntityId = role.Id, Action = "Permission.Race", Timestamp = DateTime.UtcNow });
                await db.SaveChangesAsync();
                await PermissionAdministrationGuard.EnsureAdministratorAsync(db, default);
                await transaction.CommitAsync(); return true;
            }
            catch (ConcurrencyException) { await transaction.RollbackAsync(); return false; }
        }
        try
        {
            var first = Mutate(true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var second = Mutate(false);
            await Task.Delay(100); Assert.False(second.IsCompleted); release.SetResult();
            Assert.Equal(new[] { true, false }, await Task.WhenAll(first, second));
            await using var verify = Context();
            Assert.Equal(2, await verify.RolePermissions.CountAsync(g => g.RoleId == role.Id));
            Assert.Equal(1, await verify.AuditLogs.CountAsync(a => a.EntityName == "Role" && a.EntityId == role.Id && a.Action == "Permission.Race"));
        }
        finally
        {
            release.TrySetResult();
            await setup.AuditLogs.Where(a => a.EntityName == "Role" && a.EntityId == role.Id).ExecuteDeleteAsync();
            await setup.RolePermissions.Where(g => g.RoleId == role.Id).ExecuteDeleteAsync();
            await setup.Roles.Where(r => r.Id == role.Id).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task SharedLockPreventsConcurrentLastAdministratorWriteSkew()
    {
        await using var setup = Context();
        var bootstrap = await setup.Users.SingleAsync(u => u.Username == "qa_permission_bootstrap");
        var second = new User { Username = "QA_ADMIN_RACE_" + Guid.NewGuid().ToString("N"), RoleId = bootstrap.RoleId, PasswordHash = "QA_FIXTURE_NOT_A_LOGIN", IsActive = true };
        setup.Users.Add(second); await setup.SaveChangesAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Disable(int id, bool wait)
        {
            await using var db = Context();
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                await PermissionAdministrationGuard.LockAsync(db, default);
                if (wait) { entered.SetResult(); await release.Task; }
                await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
                await PermissionAdministrationGuard.EnsureAdministratorAsync(db, default);
                db.AuditLogs.Add(new AuditLog { UserId = bootstrap.Id, EntityName = "User", EntityId = id, Action = "Permission.AdminRace", Timestamp = DateTime.UtcNow });
                await db.SaveChangesAsync(); await transaction.CommitAsync(); return true;
            }
            catch (ConcurrencyException) { await transaction.RollbackAsync(); return false; }
        }
        try
        {
            var a = Disable(second.Id, true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var b = Disable(bootstrap.Id, false);
            await Task.Delay(100); Assert.False(b.IsCompleted); release.SetResult();
            Assert.Equal(new[] { true, false }, await Task.WhenAll(a, b));
            await using var verify = Context();
            Assert.True(await verify.Users.AnyAsync(u => u.Id == bootstrap.Id && u.IsActive));
            Assert.Equal(1, await verify.AuditLogs.CountAsync(audit => audit.Action == "Permission.AdminRace" && (audit.EntityId == bootstrap.Id || audit.EntityId == second.Id)));
        }
        finally
        {
            release.TrySetResult();
            await setup.Users.Where(u => u.Id == bootstrap.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true));
            await setup.AuditLogs.Where(a => a.Action == "Permission.AdminRace" && (a.EntityId == bootstrap.Id || a.EntityId == second.Id)).ExecuteDeleteAsync();
            await setup.Users.Where(u => u.Id == second.Id).ExecuteDeleteAsync();
        }
    }

    [SqlServerFact]
    public async Task LockedCanonicalAdminCannotBeReplacedByManagerWithAllGrants()
    {
        await using var db = Context();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await PermissionAdministrationGuard.LockAsync(db, default);
        var admin = await db.Users.SingleAsync(u => u.Username == "qa_permission_bootstrap");
        await db.Users.Where(u => u.Id == admin.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, DateTime.UtcNow.AddHours(1)));
        var managerRole = await db.Roles.SingleAsync(r => r.RoleName == "Manager");
        var manager = new User { Username = "QA_MANAGER_GUARD", RoleId = managerRole.Id, PasswordHash = "QA_FIXTURE_NOT_A_LOGIN" };
        db.Users.Add(manager);
        var required = await db.Permissions.Where(p => PermissionAdministrationGuard.RequiredPermissions.Contains(p.Code)).ToArrayAsync();
        foreach (var permission in required) db.RolePermissions.Add(new RolePermission { RoleId = managerRole.Id, PermissionId = permission.Id });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyException>(() => PermissionAdministrationGuard.EnsureAdministratorAsync(db, default));
        await transaction.RollbackAsync();
    }
}
