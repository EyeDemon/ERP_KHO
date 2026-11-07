using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public class WarehouseAuthorizationServiceTests
{
    [Fact]
    public async Task RegularUser_OnlyReceivesAssignedWarehouses()
    {
        await using var context = CreateContext();
        Seed(context);
        context.UserWarehouses.Add(new UserWarehouse { UserId = 10, WarehouseId = 1, CreatedBy = 99 });
        await context.SaveChangesAsync();

        var service = new WarehouseAuthorizationService(context, new TestCurrentUser(10, false));

        (await service.GetAccessibleWarehouseIdsAsync()).Should().Equal(1);
        (await service.CanAccessWarehouseAsync(2)).Should().BeFalse();
        var action = () => service.EnsureWarehouseAccessAsync(2);
        await action.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UserWithoutAssignments_ReceivesEmptyScope()
    {
        await using var context = CreateContext();
        Seed(context);

        var service = new WarehouseAuthorizationService(context, new TestCurrentUser(10, false));

        (await service.GetAccessibleWarehouseIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GlobalAdmin_ReceivesEveryWarehouse()
    {
        await using var context = CreateContext();
        Seed(context);

        var service = new WarehouseAuthorizationService(context, new TestCurrentUser(99, true));

        (await service.GetAccessibleWarehouseIdsAsync()).Should().BeEquivalentTo([1, 2]);
    }

    [SqlServerFact]
    public async Task GrantDuplicateAndRevoke_AreEnforcedAndAudited()
    {
        await using var context = new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!).Options);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var role = new Role { RoleName = "WarehouseGrant_" + Guid.NewGuid().ToString("N") };
        role.Permissions.Add(new RolePermission { Permission = await context.Permissions.SingleAsync(p => p.Code == "user_warehouse.manage") });
        role.Permissions.Add(new RolePermission { Permission = await context.Permissions.SingleAsync(p => p.Code == "user_warehouse.read") });
        var actor = new User { Username = "scope_actor_" + Guid.NewGuid().ToString("N"), PasswordHash = "QA_FIXTURE_NOT_A_LOGIN", Role = role };
        var target = new User { Username = "scope_target_" + Guid.NewGuid().ToString("N"), PasswordHash = "QA_FIXTURE_NOT_A_LOGIN", Role = role };
        var warehouse = new Warehouse { Code = "QA_SCOPE_" + Guid.NewGuid().ToString("N")[..8], Name = "Kho kiểm thử" };
        context.AddRange(actor, target, warehouse); await context.SaveChangesAsync();
        context.UserWarehouses.Add(new UserWarehouse { UserId = actor.Id, WarehouseId = warehouse.Id, CreatedBy = actor.Id });
        await context.SaveChangesAsync();
        var service = new UserWarehouseAccessService(context, new TestCurrentUser(actor.Id, false));
        var empty = await service.GetForUserAsync(target.Id);
        empty.Memberships.Should().BeEmpty();
        Convert.FromBase64String(empty.RowVersion!).Should().HaveCount(8);
        await service.GrantAsync(target.Id, warehouse.Id, empty.RowVersion);
        var stale = () => service.RevokeAsync(target.Id, warehouse.Id, empty.RowVersion);
        await stale.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>();
        var current = await service.GetForUserAsync(target.Id);
        var duplicate = () => service.GrantAsync(target.Id, warehouse.Id, current.RowVersion);
        await duplicate.Should().ThrowAsync<BusinessRuleException>();
        await service.RevokeAsync(target.Id, warehouse.Id, current.RowVersion);
        (await context.UserWarehouses.AnyAsync(w => w.UserId == target.Id)).Should().BeFalse();
        (await context.AuditLogs.Where(a => a.UserId == actor.Id).Select(x => x.Action).ToListAsync())
            .Should().Equal("UserWarehouse.Granted", "UserWarehouse.Revoked");
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task RegularUser_CannotGrantWarehouseAccess()
    {
        await using var context = CreateContext();
        Seed(context);
        var service = new UserWarehouseAccessService(context, new TestCurrentUser(10, false));

        var action = () => service.GrantAsync(10, 1, null);

        await action.Should().ThrowAsync<ForbiddenException>();
        context.UserWarehouses.Should().BeEmpty();
    }

    private static ErpKhoDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpKhoDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void Seed(ErpKhoDbContext context)
    {
        context.Warehouses.AddRange(
            new Warehouse { Id = 1, Code = "W1", Name = "Kho 1" },
            new Warehouse { Id = 2, Code = "W2", Name = "Kho 2" });
        context.Users.AddRange(
            new User { Id = 10, Username = "user", PasswordHash = "hash", FullName = "User" },
            new User { Id = 99, Username = "admin", PasswordHash = "hash", FullName = "Admin" });
        context.SaveChanges();
    }

    private sealed record TestCurrentUser(int UserId, bool IsGlobalAdmin) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
