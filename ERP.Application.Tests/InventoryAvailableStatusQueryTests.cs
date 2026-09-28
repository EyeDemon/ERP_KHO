using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

public sealed class InventoryAvailableStatusQueryTests
{
    [Fact]
    public async Task OperationalQueries_ExcludeDamagedAndRejectedBuckets()
    {
        await using var db = CreateContext();
        Seed(db);
        await db.SaveChangesAsync();

        var current = (await new InventoryQueryService(db).GetCurrentStockAsync(null, null, null, null)).ToList();
        current.Should().ContainSingle().Which.AvailableQuantity.Should().Be(10);

        var reconciliation = await new InventoryReconciliationQueryService(db).GetReconciliationsAsync(null, null, null);
        reconciliation.Items.Should().ContainSingle();
        reconciliation.Items.Single().CurrentQuantity.Should().Be(10);
        reconciliation.Items.Single().ExpectedQuantity.Should().Be(10);

        var reports = new ReportRepository(db);
        (await reports.GetCurrentStocksAsync(null, null)).Should().ContainSingle(x => x.Status == InventoryStatus.Available && x.Quantity == 10);
        (await reports.GetTransactionsUpToDateAsync(DateTime.UtcNow.AddMinutes(1), null, null)).Should().ContainSingle(x => x.InventoryStatus == InventoryStatus.Available && x.Quantity == 10);
    }

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void Seed(ErpKhoDbContext db)
    {
        db.Units.Add(new Unit { Id = 1, Code = "EA", Name = "Each" });
        db.Products.Add(new Product { Id = 1, Code = "P1", Name = "Product", UnitId = 1 });
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "W1", Name = "Warehouse" });
        db.WarehouseLocations.Add(new WarehouseLocation { Id = 1, WarehouseId = 1, Code = "LEGACY", Name = "Legacy", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, IsSystemManaged = true });
        db.InventoryStocks.AddRange(
            new InventoryStock { ProductId = 1, WarehouseId = 1, LocationId = 1, Status = InventoryStatus.Available, Quantity = 10 },
            new InventoryStock { ProductId = 1, WarehouseId = 1, LocationId = 1, Status = InventoryStatus.Damaged, Quantity = 4 },
            new InventoryStock { ProductId = 1, WarehouseId = 1, LocationId = 1, Status = InventoryStatus.Rejected, Quantity = 3 });
        db.InventoryTransactions.AddRange(
            new InventoryTransaction { ProductId = 1, WarehouseId = 1, InventoryStatus = InventoryStatus.Available, TransactionType = TransactionType.Import, Quantity = 10, TransactionDate = DateTime.UtcNow },
            new InventoryTransaction { ProductId = 1, WarehouseId = 1, InventoryStatus = InventoryStatus.Damaged, TransactionType = TransactionType.Import, Quantity = 4, TransactionDate = DateTime.UtcNow },
            new InventoryTransaction { ProductId = 1, WarehouseId = 1, InventoryStatus = InventoryStatus.Rejected, TransactionType = TransactionType.Import, Quantity = 3, TransactionDate = DateTime.UtcNow });
    }
}
