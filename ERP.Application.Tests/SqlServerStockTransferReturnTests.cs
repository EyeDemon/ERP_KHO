using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferReturnTests
{
    private static ReturnStockTransferDto Reason() => new()
    { ReasonCode = "TRANSFER_DISPATCH_ERROR", Reason = "Hoàn trả hàng chưa nhận" };

    [SqlServerFact]
    public async Task ReturnInTransitRestoresSourceAndLinksImmutableLedger()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var id = await f.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
            await f.Service(db, f.Checker).ReturnAsync(id, Reason());
        await using (var db = StockTransferReturnFixture.Db())
        {
            var detail = await f.Service(db, f.Checker).GetByIdAsync(id);
            detail.Status.Should().Be(StockTransferStatus.Returned);
            detail.ReturnReasonCode.Should().Be("TRANSFER_DISPATCH_ERROR");
            detail.Details.Single().InTransitQuantity.Should().Be(0);
            (await db.InventoryStocks.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.Source).SumAsync(x => x.Quantity)).Should().Be(10);
            (await db.InventoryStocks.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.Destination).SumAsync(x => x.Quantity)).Should().Be(0);
            var events = await db.InventoryTransactions.Where(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == id).ToListAsync();
            events.Should().HaveCount(2);
            var outbound = events.Single(x => x.TransactionType == TransactionType.TransferOut);
            var returned = events.Single(x => x.TransactionType == TransactionType.TransferIn);
            returned.ReversalOfTransactionId.Should().Be(outbound.Id);
            returned.ReasonCode.Should().Be("TRANSFER_DISPATCH_ERROR");
            returned.Quantity.Should().Be(8);
            (await db.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" && x.EntityId == id && x.Action == "StockTransfer.Returned")).Should().Be(1);
        }
        await using (var db = StockTransferReturnFixture.Db())
        {
            var again = () => f.Service(db, f.Checker).ReturnAsync(id, Reason());
            await again.Should().ThrowAsync<ConcurrencyException>();
            var receive = () => f.Service(db, f.Checker).ReceiveAsync(id,
                new ReceiveStockTransferDto { Details = [new() { ProductId = f.ProductId, ReceivedQuantity = 8 }] });
            await receive.Should().ThrowAsync<ConcurrencyException>();
        }
    }

    [SqlServerFact]
    public async Task ReturnRejectsUnknownReasonAndUnauthorizedDestination()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var id = await f.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
        {
            var invalid = () => f.Service(db, f.Checker).ReturnAsync(id,
                new ReturnStockTransferDto { ReasonCode = "UNKNOWN", Reason = "Sai" });
            await invalid.Should().ThrowAsync<BusinessRuleException>();
            var missing = () => f.Service(db, f.Checker).ReturnAsync(id,
                new ReturnStockTransferDto { ReasonCode = "TRANSFER_DISPATCH_ERROR" });
            await missing.Should().ThrowAsync<BusinessRuleException>();
            await db.UserWarehouses.Where(x => x.UserId == f.Checker && x.WarehouseId == f.Destination).ExecuteDeleteAsync();
        }
        await using (var db = StockTransferReturnFixture.Db())
        {
            var denied = () => f.Service(db, f.Checker).ReturnAsync(id, Reason());
            await denied.Should().ThrowAsync<NotFoundException>();
            (await db.StockTransfers.SingleAsync(x => x.Id == id)).Status.Should().Be(StockTransferStatus.InTransit);
            (await db.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == id)).Should().Be(1);
        }
    }

    [SqlServerFact]
    public async Task ReturnRollbackWhenSourceReceivingLocationUnavailable()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var id = await f.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
            await db.WarehouseLocations.Where(x => x.WarehouseId == f.Source && x.Code == "LEGACY")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBlocked, true));
        await using (var db = StockTransferReturnFixture.Db())
        {
            var failed = () => f.Service(db, f.Checker).ReturnAsync(id, Reason());
            await failed.Should().ThrowAsync<BusinessRuleException>();
        }
        await using (var db = StockTransferReturnFixture.Db())
        {
            (await db.StockTransfers.SingleAsync(x => x.Id == id)).Status.Should().Be(StockTransferStatus.InTransit);
            (await db.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == id)).Should().Be(1);
            (await db.InventoryStocks.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.Source).SumAsync(x => x.Quantity)).Should().Be(2);
            await db.WarehouseLocations.Where(x => x.WarehouseId == f.Source && x.Code == "LEGACY")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsBlocked, false));
        }
        await using (var db = StockTransferReturnFixture.Db())
            await f.Service(db, f.Checker).ReturnAsync(id, Reason());
    }
}
