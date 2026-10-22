using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

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
            LockType = InventoryLockType.InvestigationHold,
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

    [Fact]
    public async Task GetCurrentStockAsync_EvaluatesLowStockThresholdAgainstAggregatedWarehouseBalance()
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
        // Bucket 1 at location 1 has 10; Bucket 2 at location 2 has 3. Total available = 13.
        db.InventoryStocks.Add(new InventoryStock
        {
            ProductId = 1,
            WarehouseId = 1,
            LocationId = 2,
            Status = InventoryStatus.Available,
            Quantity = 3
        });
        await db.SaveChangesAsync();

        var service = new InventoryQueryService(db);

        // Threshold 5: total available is 13 (> 5), so product must NOT be returned as low-stock with a truncated balance of 3.
        var below5 = await service.GetCurrentStockAsync(1, 1, null, 5m);
        below5.Should().BeEmpty();

        // Threshold 15: total available is 13 (<= 15), so product IS returned with the full aggregated balance of 13.
        var below15 = (await service.GetCurrentStockAsync(1, 1, null, 15m)).Single();
        below15.AvailableQuantity.Should().Be(13m);
        below15.OnHandQuantity.Should().Be(13m);
    }

    [Fact]
    public async Task InventoryStockRepository_UntrackedLookup_IgnoresLotAndSerialBucketsAndPrefersPositiveAvailable()
    {
        await using var db = CreateContext();
        db.Units.Add(new Unit { Id = 1, Code = "EA", Name = "Each", IsActive = true });
        db.Products.Add(new Product { Id = 1, Code = "P1", Name = "Product", UnitId = 1, IsActive = true });
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "W1", Name = "Warehouse" });
        db.WarehouseLocations.AddRange(
            new WarehouseLocation { Id = 1, WarehouseId = 1, Code = "LOC-1", Name = "Loc 1", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true },
            new WarehouseLocation { Id = 2, WarehouseId = 1, Code = "LOC-2", Name = "Loc 2", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true });
        db.InventoryLots.Add(new InventoryLot { Id = 10, ProductId = 1, LotNumber = "LOT-A" });
        db.InventoryStocks.AddRange(
            new InventoryStock { Id = 1, ProductId = 1, WarehouseId = 1, LocationId = 1, LotId = 10, Status = InventoryStatus.Available, Quantity = 50 },
            new InventoryStock { Id = 2, ProductId = 1, WarehouseId = 1, LocationId = 1, Status = InventoryStatus.Available, Quantity = 0 },
            new InventoryStock { Id = 3, ProductId = 1, WarehouseId = 1, LocationId = 2, Status = InventoryStatus.Available, Quantity = 25 });
        await db.SaveChangesAsync();

        var repo = new InventoryStockRepository(db);

        // At Location 1, must match the untracked bucket (Id = 2), never the Lot bucket (Id = 1).
        var atLoc1 = await repo.GetByProductWarehouseStatusAndLocationAsync(1, 1, InventoryStatus.Available, 1);
        atLoc1.Should().NotBeNull();
        atLoc1!.Id.Should().Be(2);
        atLoc1.LotId.Should().BeNull();

        // Across Warehouse 1, must prefer the untracked bucket with positive available quantity (Id = 3, Qty = 25).
        var bestWarehouseBucket = await repo.GetByProductAndWarehouseAsync(1, 1);
        bestWarehouseBucket.Should().NotBeNull();
        bestWarehouseBucket!.Id.Should().Be(3);
        bestWarehouseBucket.Quantity.Should().Be(25m);
    }

    [Fact]
    public async Task TryPostTrackedBucketAsync_ReusesLocalStockWhenMultipleIdentitiesShareSameNewLotBeforeSaveChanges()
    {
        await using var db = CreateContext();
        db.Units.Add(new Unit { Id = 1, Code = "EA", Name = "Each", IsActive = true });
        var product = new Product
        {
            Id = 1,
            Code = "P-LOT",
            Name = "Lot Product",
            UnitId = 1,
            TrackingType = ProductTrackingType.Lot,
            IsActive = true
        };
        db.Products.Add(product);
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "W1", Name = "Warehouse" });
        var receipt = new ImportReceipt { Id = 100, Code = "IR-100", WarehouseId = 1, Status = ReceiptStatus.ReadyToPost };
        var detail = new ImportReceiptDetail
        {
            Id = 200,
            ImportReceiptId = 100,
            ProductId = 1,
            Product = product,
            BaseAcceptedQuantity = 10m
        };
        db.ImportReceipts.Add(receipt);
        db.ImportReceiptDetails.Add(detail);
        db.ImportReceiptInventoryIdentities.AddRange(
            new ImportReceiptInventoryIdentity
            {
                Id = 1,
                ImportReceiptDetailId = 200,
                ProductId = 1,
                TargetStatus = InventoryStatus.Available,
                LotNumber = "LOT-NEW-01",
                BaseQuantity = 4m
            },
            new ImportReceiptInventoryIdentity
            {
                Id = 2,
                ImportReceiptDetailId = 200,
                ProductId = 1,
                TargetStatus = InventoryStatus.Available,
                LotNumber = "LOT-NEW-01",
                BaseQuantity = 6m
            });
        await db.SaveChangesAsync();

        var service = new ReceiptInventoryIdentityService(db, Mock.Of<IWarehouseAuthorizationService>());
        var posted = await service.TryPostTrackedBucketAsync(receipt, detail, InventoryStatus.Available, 10m, 1, 10);
        posted.Should().BeTrue();

        // Must create only ONE InventoryStock in ChangeTracker for (Product 1, Warehouse 1, Location 10, Available, LOT-NEW-01) with Quantity = 10.
        db.InventoryStocks.Local.Should().ContainSingle();
        db.InventoryStocks.Local.Single().Quantity.Should().Be(10m);
        db.InventoryTransactions.Local.Should().HaveCount(2);
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
