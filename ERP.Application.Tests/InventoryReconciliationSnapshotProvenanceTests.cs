using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ERP.Application.Tests;

public sealed class InventoryReconciliationSnapshotProvenanceTests
{
    private static async Task<ErpKhoDbContext> SeedAsync()
    {
        var db = new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Products.Add(new Product { Id = 1, Code = "SKU", Name = "Provenance" });
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "W1", Name = "Authorized" });
        db.InventoryStocks.Add(new InventoryStock
        {
            Id = 1, WarehouseId = 1, ProductId = 1, Quantity = 10,
            Status = InventoryStatus.Available
        });
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            Id = 1, WarehouseId = 1, ProductId = 1, Quantity = 10,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            TransactionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static InventoryReconciliationQueryService Query(ErpKhoDbContext db)
    {
        var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
        auth.Setup(x => x.EnsureWarehouseAccessAsync(1, default))
            .Returns(Task.CompletedTask);
        return new InventoryReconciliationQueryService(db, auth.Object);
    }

    [Fact]
    public async Task BackdatedNewLedgerEvent_KeepsHistoricalArithmeticButPoisonsSnapshotVerdict()
    {
        using var db = await SeedAsync();
        var query = Query(db);
        var first = await query.GetInvestigationAsync(1, 1);
        first.AllStatusStatus.Should().Be("Match");
        first.SnapshotComparisonStatus.Should().Be("Match");
        first.SnapshotComparisonExpectedQuantity.Should().Be(10);
        first.EvidenceAnchorsStale.Should().BeFalse();

        db.InventoryTransactions.Add(new InventoryTransaction
        {
            Id = 2, WarehouseId = 1, ProductId = 1, Quantity = 3,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            TransactionDate = DateTime.UtcNow.AddYears(-10)
        });
        await db.SaveChangesAsync();

        var pinned = await query.GetInvestigationAsync(1, 1,
            eventAnchorId: first.EventAnchorId, bucketAnchorId: first.BucketAnchorId);
        pinned.EventAnchorId.Should().Be(first.EventAnchorId);
        pinned.Events.Select(x => x.TransactionId)
            .Should().Equal(first.Events.Select(x => x.TransactionId));
        pinned.LedgerHasEventsAfterAnchor.Should().BeTrue();
        pinned.EvidenceAnchorsStale.Should().BeTrue();
        pinned.AllStatusStatus.Should().Be("Match"); // historical arithmetic only
        pinned.SnapshotComparisonStatus.Should().Be("Indeterminate");
        pinned.SnapshotComparisonExpectedQuantity.Should().BeNull();
        pinned.SnapshotComparisonDifference.Should().BeNull();

        var refreshed = await query.GetInvestigationAsync(1, 1);
        refreshed.EvidenceAnchorsStale.Should().BeFalse();
        refreshed.SnapshotComparisonStatus.Should().Be("Mismatch");
        refreshed.SnapshotComparisonExpectedQuantity.Should().Be(13);
        refreshed.SnapshotComparisonDifference.Should().Be(-3);
    }

    [Fact]
    public async Task NewBucketAfterPinnedHighWater_RejectsStaleComparisonWithoutNewLedger()
    {
        using var db = await SeedAsync();
        var query = Query(db);
        var first = await query.GetInvestigationAsync(1, 1);
        db.InventoryStocks.Add(new InventoryStock
        {
            Id = 2, WarehouseId = 1, ProductId = 1, Quantity = 2,
            Status = InventoryStatus.Available
        });
        await db.SaveChangesAsync();

        var pinned = await query.GetInvestigationAsync(1, 1,
            eventAnchorId: first.EventAnchorId, bucketAnchorId: first.BucketAnchorId);
        pinned.BucketHasRowsAfterAnchor.Should().BeTrue();
        pinned.LedgerHasEventsAfterAnchor.Should().BeFalse();
        pinned.EvidenceAnchorsStale.Should().BeTrue();
        pinned.AllStatusStatus.Should().Be("Mismatch"); // legacy arithmetic
        pinned.SnapshotComparisonStatus.Should().Be("Indeterminate");
        pinned.SnapshotComparisonExpectedQuantity.Should().BeNull();
        pinned.SnapshotComparisonDifference.Should().BeNull();
        pinned.Buckets.Should().ContainSingle(x => x.InventoryStockId == 1);

        var refreshed = await query.GetInvestigationAsync(1, 1);
        refreshed.EvidenceAnchorsStale.Should().BeFalse();
        refreshed.SnapshotComparisonStatus.Should().Be("Mismatch");
        refreshed.SnapshotComparisonDifference.Should().Be(2);
    }

    [Fact]
    public async Task ExistingBucketEdit_InvalidatesAggregateWithoutNewIds()
    {
        using var db = await SeedAsync();
        var query = Query(db);
        var first = await query.GetInvestigationAsync(1, 1);
        first.AllStatusBucketAggregateFingerprint.Should().HaveLength(64);
        var stock = await db.InventoryStocks.SingleAsync(x => x.Id == 1);
        stock.Quantity = 12m;
        await db.SaveChangesAsync();
        var pinned = await query.GetInvestigationAsync(1, 1,
            eventAnchorId: first.EventAnchorId, bucketAnchorId: first.BucketAnchorId,
            allStatusBucketAnchorId: first.AllStatusBucketAnchorId,
            allStatusBucketAggregateFingerprint: first.AllStatusBucketAggregateFingerprint);
        pinned.AllStatusBucketAggregateChanged.Should().BeTrue();
        pinned.LedgerHasEventsAfterAnchor.Should().BeFalse();
        pinned.BucketHasRowsAfterAnchor.Should().BeFalse();
        pinned.AllStatusBucketHasRowsAfterAnchor.Should().BeFalse();
        pinned.SnapshotComparisonStatus.Should().Be("Indeterminate");
        pinned.SnapshotComparisonExpectedQuantity.Should().BeNull();
        pinned.SnapshotComparisonDifference.Should().BeNull();
        stock.Quantity = 10m;
        stock.Status = InventoryStatus.QcHold;
        await db.SaveChangesAsync();
        var changedStatus = await query.GetInvestigationAsync(1, 1,
            eventAnchorId: first.EventAnchorId,
            allStatusBucketAnchorId: first.AllStatusBucketAnchorId,
            allStatusBucketAggregateFingerprint: first.AllStatusBucketAggregateFingerprint);
        changedStatus.AllStatusBucketAggregateChanged.Should().BeTrue();
        changedStatus.EvidenceAnchorsStale.Should().BeTrue();
        var refreshed = await query.GetInvestigationAsync(1, 1,
            bucketStatus: nameof(InventoryStatus.QcHold));
        refreshed.AllStatusBucketAggregateChanged.Should().BeFalse();
        refreshed.AllStatusBucketAggregateFingerprint.Should().NotBe(first.AllStatusBucketAggregateFingerprint);
    }

    [Fact]
    public async Task NewBucketInAnotherStatus_InvalidatesPinnedAllStatusComparisonButNotSelectedPage()
    {
        using var db = await SeedAsync();
        var query = Query(db);
        var first = await query.GetInvestigationAsync(1, 1);
        first.AllStatusBucketAnchorId.Should().Be(1);
        first.AllStatusBucketHasRowsAfterAnchor.Should().BeFalse();

        // Another warehouse must not poison this authorized pair.
        db.Warehouses.Add(new Warehouse { Id = 2, Code = "W2", Name = "Other warehouse" });
        db.InventoryStocks.Add(new InventoryStock
        {
            Id = 2, WarehouseId = 2, ProductId = 1, Quantity = 7,
            Status = InventoryStatus.QcHold
        });
        await db.SaveChangesAsync();
        var isolated = await query.GetInvestigationAsync(1, 1,
            eventAnchorId: first.EventAnchorId, bucketAnchorId: first.BucketAnchorId,
            allStatusBucketAnchorId: first.AllStatusBucketAnchorId);
        isolated.EvidenceAnchorsStale.Should().BeFalse();
        isolated.SnapshotComparisonStatus.Should().Be("Match");

        db.InventoryStocks.Add(new InventoryStock
        {
            Id = 3, WarehouseId = 1, ProductId = 1, Quantity = 2,
            Status = InventoryStatus.QcHold
        });
        await db.SaveChangesAsync();
        var pinned = await query.GetInvestigationAsync(1, 1,
            eventAnchorId: first.EventAnchorId, bucketAnchorId: first.BucketAnchorId,
            allStatusBucketAnchorId: first.AllStatusBucketAnchorId);
        pinned.EventAnchorId.Should().Be(first.EventAnchorId);
        pinned.BucketAnchorId.Should().Be(first.BucketAnchorId);
        pinned.AllStatusBucketAnchorId.Should().Be(first.AllStatusBucketAnchorId);
        pinned.LedgerHasEventsAfterAnchor.Should().BeFalse();
        pinned.BucketHasRowsAfterAnchor.Should().BeFalse();
        pinned.AllStatusBucketHasRowsAfterAnchor.Should().BeTrue();
        pinned.Buckets.Should().ContainSingle(x => x.InventoryStockId == 1);
        pinned.EvidenceAnchorsStale.Should().BeTrue();
        pinned.AllStatusStatus.Should().Be("Mismatch"); // historical arithmetic
        pinned.SnapshotComparisonStatus.Should().Be("Indeterminate");
        pinned.SnapshotComparisonExpectedQuantity.Should().BeNull();
        pinned.SnapshotComparisonDifference.Should().BeNull();

        var refreshed = await query.GetInvestigationAsync(1, 1);
        refreshed.EvidenceAnchorsStale.Should().BeFalse();
        refreshed.SnapshotComparisonStatus.Should().Be("Mismatch");
    }
}
