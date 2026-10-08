using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

internal sealed class StockTransferReturnFixture : IAsyncDisposable
{
    public int Creator { get; private set; }
    public int Checker { get; private set; }
    public int RoleId { get; private set; }
    public int UnitId { get; private set; }
    public int ProductId { get; private set; }
    public int Source { get; private set; }
    public int Destination { get; private set; }

    private sealed record TestUser(int UserId, bool IsGlobalAdmin, string Role) : ICurrentUser
    { public bool IsAuthenticated => true; }

    public static ErpKhoDbContext Db() => new(new DbContextOptionsBuilder<ErpKhoDbContext>()
        .UseSqlServer(Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!).Options);

    public StockTransferService Service(ErpKhoDbContext db, int userId)
    {
        var user = new TestUser(userId, false, "Manager");
        return new StockTransferService(db, new InventoryStockRepository(db), new WarehouseAuthorizationService(db, user), user);
    }

    public static async Task<StockTransferReturnFixture> CreateAsync()
    {
        var f = new StockTransferReturnFixture();
        await using var db = Db();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var role = new Role { RoleName = $"RRole{suffix}" };
        var creator = new User { Username = $"RCreator{suffix}", PasswordHash = "unused", FullName = "Creator", Role = role };
        var checker = new User { Username = $"RChecker{suffix}", PasswordHash = "unused", FullName = "Checker", Role = role };
        var unit = new Unit { Code = $"RU{suffix}", Name = "Unit" };
        var product = new Product { Code = $"RP{suffix}", Name = "Product", Unit = unit };
        var source = new Warehouse { Code = $"RS{suffix}", Name = "Source" };
        var destination = new Warehouse { Code = $"RD{suffix}", Name = "Destination" };
        db.AddRange(creator, checker, product, source, destination);
        await db.SaveChangesAsync();
        f.Creator = creator.Id; f.Checker = checker.Id; f.RoleId = role.Id; f.UnitId = unit.Id;
        f.ProductId = product.Id; f.Source = source.Id; f.Destination = destination.Id;
        var sourceLocation = new WarehouseLocation { WarehouseId = f.Source, Code = "LEGACY", Name = "Legacy", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, IsSystemManaged = true, CreatedBy = f.Creator };
        var destLocation = new WarehouseLocation { WarehouseId = f.Destination, Code = "LEGACY", Name = "Legacy", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, IsSystemManaged = true, CreatedBy = f.Creator };
        db.AddRange(sourceLocation, destLocation);
        await db.SaveChangesAsync();
        foreach (var userId in new[] { f.Creator, f.Checker })
        {
            db.UserWarehouses.Add(new UserWarehouse { UserId = userId, WarehouseId = f.Source, CreatedBy = f.Creator });
            db.UserWarehouses.Add(new UserWarehouse { UserId = userId, WarehouseId = f.Destination, CreatedBy = f.Creator });
        }
        db.InventoryStocks.AddRange(
            new InventoryStock { ProductId = f.ProductId, WarehouseId = f.Source, LocationId = sourceLocation.Id, Quantity = 10 },
            new InventoryStock { ProductId = f.ProductId, WarehouseId = f.Destination, LocationId = destLocation.Id, Quantity = 0 });
        db.InventoryTransactions.Add(new InventoryTransaction { ProductId = f.ProductId, WarehouseId = f.Source,
            TransactionType = TransactionType.Import, Quantity = 10, ReferenceType = "TestSetup", CreatedBy = f.Creator });
        await db.SaveChangesAsync();
        return f;
    }

    public async Task<int> DispatchAsync()
    {
        await using var db = Db();
        var id = (await Service(db, Creator).CreateAsync(new CreateStockTransferDto
        {
            SourceWarehouseId = Source, DestinationWarehouseId = Destination,
            Details = [new() { ProductId = ProductId, Quantity = 8 }]
        })).Id;
        await Service(db, Checker).ApproveAsync(id);
        await Service(db, Checker).DispatchAsync(id);
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = Db();
        var ids = new[] { Creator, Checker };
        await db.InventoryTransactions.Where(x => x.ProductId == ProductId && x.ReversalOfTransactionId != null).ExecuteDeleteAsync();
        await db.InventoryTransactions.Where(x => x.ProductId == ProductId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId.HasValue && ids.Contains(x.UserId.Value)).ExecuteDeleteAsync();
        await db.StockTransferDetails.Where(x => x.ProductId == ProductId).ExecuteDeleteAsync();
        await db.StockTransfers.Where(x => x.SourceWarehouseId == Source && x.DestinationWarehouseId == Destination).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == ProductId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => ids.Contains(x.UserId)).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == UnitId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == Source || x.WarehouseId == Destination).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == Source || x.Id == Destination).ExecuteDeleteAsync();
        await db.Users.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == RoleId).ExecuteDeleteAsync();
    }
}
