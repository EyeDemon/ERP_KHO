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

    [Fact]
    public async Task GetCurrentStockAsync_ExcludesStockLockedByActiveInventoryLock()
    {
        await using var db = CreateContext();
        Seed(db);
        db.WarehouseLocations.Add(new WarehouseLocation
        {
            Id = 2,
            WarehouseId = 1,
            Code = "PICK-2",
            Name = "Pick 2",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true
        });
        db.InventoryStocks.Add(new InventoryStock
        {
            ProductId = 1,
            WarehouseId = 1,
            LocationId = 2,
            Status = InventoryStatus.Available,
            Quantity = 7
        });
        var activeLock = new InventoryLock
        {
            Id = 1,
            WarehouseId = 1,
            ProductId = 1,
            LocationId = 2,
            LockType = InventoryLockType.QualityHold,
            Reason = "QC_HOLD",
            Status = InventoryLockStatus.Active,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            CreatedBy = 1
        };
        db.InventoryLocks.Add(activeLock);
        await db.SaveChangesAsync();

        var service = new InventoryQueryService(db);
        var whileLocked = (await service.GetCurrentStockAsync(1, 1, null, null)).Single();
        whileLocked.OnHandQuantity.Should().Be(10);
        whileLocked.AvailableQuantity.Should().Be(10);

        activeLock.Status = InventoryLockStatus.Released;
        activeLock.ReleasedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var afterRelease = (await service.GetCurrentStockAsync(1, 1, null, null)).Single();
        afterRelease.OnHandQuantity.Should().Be(17);
        afterRelease.AvailableQuantity.Should().Be(17);
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
