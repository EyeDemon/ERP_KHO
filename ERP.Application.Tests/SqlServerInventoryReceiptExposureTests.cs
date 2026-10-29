using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerInventoryReceiptExposureTests
{
    [SqlServerFact]
    public async Task PostedReceiptEvidence_ExcludesDraftAndOrphan_AndRespectsAnchor()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)).Options;
        await using var db = new ErpKhoDbContext(options);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var role = new Role { RoleName = "ReceiptEvidence" + suffix };
            var user = new User { Username = "ReceiptEvidence" + suffix, FullName = "Test user", Role = role };
            var unit = new Unit { Code = "U" + suffix, Name = "Unit" };
            var product = new Product { Code = "P" + suffix, Name = "Product", Unit = unit, TrackingType = ProductTrackingType.Lot };
            var warehouse = new Warehouse { Code = "W" + suffix, Name = "Warehouse" };
            db.AddRange(user, product, warehouse);
            await db.SaveChangesAsync();
            db.UserWarehouses.Add(new UserWarehouse { UserId = user.Id, WarehouseId = warehouse.Id, CreatedBy = user.Id });
            var lot = new InventoryLot { ProductId = product.Id, LotNumber = "L" + suffix, ReceivedAt = DateTime.UtcNow };
            var posted = new ImportReceipt { Code = "POST" + suffix, WarehouseId = warehouse.Id,
                Status = ReceiptStatus.Posted, CreatedBy = user.Id };
            var draft = new ImportReceipt { Code = "DRAFT" + suffix, WarehouseId = warehouse.Id,
                Status = ReceiptStatus.Draft, CreatedBy = user.Id };
            db.AddRange(lot, posted, draft);
            await db.SaveChangesAsync();
            InventoryTransaction Entry(int reference, decimal quantity) => new()
            {
                ProductId = product.Id, WarehouseId = warehouse.Id, LotId = lot.Id,
                CreatedBy = user.Id, TransactionType = TransactionType.Import,
                InventoryStatus = InventoryStatus.Available, Quantity = quantity,
                ReferenceType = "ImportReceipt", ReferenceId = reference
            };
            db.InventoryTransactions.AddRange(Entry(posted.Id, 4m), Entry(draft.Id, 99m), Entry(int.MaxValue, 99m));
            await db.SaveChangesAsync();

            var query = new InventoryTraceabilityQueryService(db,
                new WarehouseAuthorizationService(db, new TestUser(user.Id)));
            var first = await query.TraceAsync(productId: product.Id, lotNumber: lot.LotNumber);
            first.ReceiptExposures.Should().ContainSingle().Which.PostedQuantity.Should().Be(4m);
            first.ReceiptExposures[0].ReceiptId.Should().Be(posted.Id);
            first.ReceiptExposuresTruncated.Should().BeFalse();
            var later = Entry(posted.Id, 2m);
            later.TransactionDate = DateTime.UtcNow.AddDays(-10);
            db.InventoryTransactions.Add(later);
            await db.SaveChangesAsync();
            var anchored = await query.TraceAsync(productId: product.Id,
                lotNumber: lot.LotNumber, eventAnchorId: first.EventAnchorId);
            anchored.ReceiptExposures.Single().PostedQuantity.Should().Be(4m);
            var refreshed = await query.TraceAsync(productId: product.Id, lotNumber: lot.LotNumber);
            refreshed.ReceiptExposures.Single().PostedQuantity.Should().Be(6m);
            refreshed.ReceiptExposures.Single().LedgerEventCount.Should().Be(2);
            (await query.TraceAsync(warehouseId: warehouse.Id)).ReceiptExposures.Should().BeEmpty();
        }
        finally { await tx.RollbackAsync(); }
    }

    private sealed record TestUser(int UserId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }
}
