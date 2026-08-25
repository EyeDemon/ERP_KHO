using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using FluentAssertions;

namespace ERP.Application.Tests;

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

    [Fact]
    public async Task GrantDuplicateAndRevoke_AreEnforcedAndAudited()
    {
        await using var context = CreateContext();
        Seed(context);
        var service = new UserWarehouseAccessService(context, new TestCurrentUser(99, true));

        await service.GrantAsync(10, 1);
        var duplicate = () => service.GrantAsync(10, 1);
        await duplicate.Should().ThrowAsync<BusinessRuleException>();
        await service.RevokeAsync(10, 1);

        context.UserWarehouses.Should().BeEmpty();
        context.AuditLogs.Select(x => x.Action).Should().Equal("UserWarehouse.Granted", "UserWarehouse.Revoked");
    }

    [Fact]
    public async Task RegularUser_CannotGrantWarehouseAccess()
    {
        await using var context = CreateContext();
        Seed(context);
        var service = new UserWarehouseAccessService(context, new TestCurrentUser(10, false));

        var action = () => service.GrantAsync(10, 1);

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
