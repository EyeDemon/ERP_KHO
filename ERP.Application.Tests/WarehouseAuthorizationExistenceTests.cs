using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class WarehouseAuthorizationExistenceTests
{
    [Fact]
    public async Task GlobalAdmin_CannotAccessNonexistentOrInvalidWarehouse()
    {
        await using var db = new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH1", Name = "Kho hiện hữu" });
        await db.SaveChangesAsync();

        var service = new WarehouseAuthorizationService(db, new CurrentUser(1, true));
        (await service.CanAccessWarehouseAsync(1)).Should().BeTrue();
        (await service.CanAccessWarehouseAsync(999)).Should().BeFalse();
        (await service.CanAccessWarehouseAsync(0)).Should().BeFalse();
        (await service.CanAccessWarehouseAsync(-1)).Should().BeFalse();
        var missing = () => service.EnsureWarehouseAccessAsync(999);
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    [SqlServerFact]
    public async Task SqlServer_GlobalAdminMustTargetExistingWarehouse_NonAdminMustBeAssigned()
    {
        await using var db = new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var warehouse = new Warehouse
        {
            Code = "AUTH" + Guid.NewGuid().ToString("N")[..10],
            Name = "Kho kiểm thử quyền"
        };
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync();

        var admin = new WarehouseAuthorizationService(db, new CurrentUser(1, true));
        (await admin.CanAccessWarehouseAsync(warehouse.Id)).Should().BeTrue();
        (await admin.CanAccessWarehouseAsync(int.MaxValue)).Should().BeFalse();
        var missing = () => admin.EnsureWarehouseAccessAsync(int.MaxValue);
        await missing.Should().ThrowAsync<NotFoundException>();

        var unassigned = new WarehouseAuthorizationService(db, new CurrentUser(-1, false));
        (await unassigned.CanAccessWarehouseAsync(warehouse.Id)).Should().BeFalse();
        var denied = () => unassigned.EnsureWarehouseAccessAsync(warehouse.Id);
        await denied.Should().ThrowAsync<NotFoundException>();
        await transaction.RollbackAsync();
    }

    private sealed record CurrentUser(int UserId, bool IsGlobalAdmin) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
