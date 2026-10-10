using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP.Application.Tests;

public sealed class InventoryReconciliationListSafetyTests
{
    [Fact]
    public async Task StatusChangeAndUnknownLedgerType_AreHandledWithoutFalseMatch()
    {
        await using var db = new ErpKhoDbContext(new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Products.Add(new Product { Id = 1, Code = "P1", Name = "Test" });
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "W1", Name = "Test" });
        db.InventoryStocks.AddRange(
            new InventoryStock { ProductId = 1, WarehouseId = 1, Quantity = 6 },
            new InventoryStock { ProductId = 1, WarehouseId = 1, Quantity = 4, Status = InventoryStatus.QcHold });
        db.InventoryTransactions.Add(new InventoryTransaction {
            ProductId = 1, WarehouseId = 1, Quantity = 10,
            TransactionType = TransactionType.Import
        });
        db.InventoryTransactions.Add(new InventoryTransaction {
            ProductId = 1, WarehouseId = 1, Quantity = 4,
            TransactionType = TransactionType.StatusChange,
            InventoryStatus = InventoryStatus.QcHold,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.QcHold
        });
        await db.SaveChangesAsync();
        var access = new Moq.Mock<ERP.Application.Interfaces.IWarehouseAuthorizationService>();
        access.Setup(x => x.GetAccessibleWarehouseIdsAsync(default))
            .Returns(Task.FromResult<IReadOnlyList<int>>(new[] { 1 }));
        var query = new InventoryReconciliationQueryService(db, access.Object);
        var matched = (await query.GetReconciliationsAsync(null, null, null)).Items.Single();
        matched.ExpectedQuantity.Should().Be(6m);
        matched.Difference.Should().Be(0m);
        matched.StatusChangeOutQuantity.Should().Be(4m);
        matched.Status.Should().Be("Match");

        db.InventoryTransactions.Add(new InventoryTransaction {
            ProductId = 1, WarehouseId = 1, Quantity = 2,
            TransactionType = TransactionType.TransferAdjustment
        });
        await db.SaveChangesAsync();
        var unknown = (await query.GetReconciliationsAsync(null, null, null)).Items.Single();
        unknown.ExpectedQuantity.Should().BeNull();
        unknown.Difference.Should().BeNull();
        unknown.Status.Should().Be("Indeterminate");
        unknown.UnclassifiedLedgerEventCount.Should().Be(1);
    }
}
