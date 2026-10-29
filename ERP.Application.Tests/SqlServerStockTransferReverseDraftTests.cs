using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferReverseDraftTests
{
    private static CreateReverseStockTransferDto Reason() => new()
    { ReasonCode = "TRANSFER_ROUTE_ERROR", Reason = "Trả về sau khi nhận sai tuyến" };

    [SqlServerFact]
    public async Task ReceivedTransferCreatesOneLinkedDraftAndUsesNativeDispatchLedger()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var originalId = await f.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
            await f.Service(db, f.Checker).ReceiveAsync(originalId,
                new ReceiveStockTransferDto { Details = [new() { ProductId = f.ProductId, ReceivedQuantity = 8 }] });

        int reverseId;
        await using (var db = StockTransferReturnFixture.Db())
        {
            var reverse = await f.Service(db, f.Checker).CreateReverseDraftAsync(originalId, Reason());
            reverseId = reverse.Id;
            reverse.Status.Should().Be(StockTransferStatus.Draft);
            reverse.ReverseOfTransferId.Should().Be(originalId);
            reverse.SourceWarehouseId.Should().Be(f.Destination);
            reverse.DestinationWarehouseId.Should().Be(f.Source);
            reverse.ReverseReasonCode.Should().Be("TRANSFER_ROUTE_ERROR");
            reverse.Details.Single().RequestedQuantity.Should().Be(8);
            (await db.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" &&
                x.ReferenceId == reverseId)).Should().Be(0);
            var duplicate = () => f.Service(db, f.Checker).CreateReverseDraftAsync(originalId, Reason());
            await duplicate.Should().ThrowAsync<ConcurrencyException>();
        }
        await using (var db = StockTransferReturnFixture.Db())
        {
            var original = await f.Service(db, f.Checker).GetByIdAsync(originalId);
            original.ReverseTransferId.Should().Be(reverseId);
            await f.Service(db, f.Creator).ApproveAsync(reverseId);
            await f.Service(db, f.Creator).DispatchAsync(reverseId);
            await f.Service(db, f.Creator).ReceiveAsync(reverseId,
                new ReceiveStockTransferDto { Details = [new() { ProductId = f.ProductId, ReceivedQuantity = 8 }] });
        }
        await using (var db = StockTransferReturnFixture.Db())
        {
            (await db.InventoryStocks.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.Source)
                .SumAsync(x => x.Quantity)).Should().Be(10);
            (await db.InventoryStocks.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.Destination)
                .SumAsync(x => x.Quantity)).Should().Be(0);
            (await db.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" &&
                x.ReferenceId == originalId)).Should().Be(2);
            (await db.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" &&
                x.ReferenceId == reverseId)).Should().Be(2);
            (await db.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                x.EntityId == reverseId && x.Action == "StockTransfer.ReverseDraftCreated")).Should().Be(1);
        }
    }

    [SqlServerFact]
    public async Task ReverseDraftRequiresReceiptAndBothWarehouseGrants()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var id = await f.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
        {
            var tooEarly = () => f.Service(db, f.Checker).CreateReverseDraftAsync(id, Reason());
            await tooEarly.Should().ThrowAsync<ConcurrencyException>();
            await f.Service(db, f.Checker).ReceiveAsync(id,
                new ReceiveStockTransferDto { Details = [new() { ProductId = f.ProductId, ReceivedQuantity = 8 }] });
            await db.UserWarehouses.Where(x => x.UserId == f.Checker && x.WarehouseId == f.Source)
                .ExecuteDeleteAsync();
        }
        await using (var db = StockTransferReturnFixture.Db())
        {
            var denied = () => f.Service(db, f.Checker).CreateReverseDraftAsync(id, Reason());
            await denied.Should().ThrowAsync<NotFoundException>();
            (await db.StockTransfers.CountAsync(x => x.ReverseOfTransferId == id)).Should().Be(0);
        }
    }

    [SqlServerFact]
    public async Task ReverseDraftRejectsInvalidReasonWithoutDocumentOrLedger()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var id = await f.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
        {
            await f.Service(db, f.Checker).ReceiveAsync(id,
                new ReceiveStockTransferDto { Details = [new() { ProductId = f.ProductId, ReceivedQuantity = 8 }] });
            var invalid = () => f.Service(db, f.Checker).CreateReverseDraftAsync(id,
                new CreateReverseStockTransferDto { ReasonCode = "UNKNOWN", Reason = "Sai" });
            await invalid.Should().ThrowAsync<BusinessRuleException>();
            (await db.StockTransfers.CountAsync(x => x.ReverseOfTransferId == id)).Should().Be(0);
        }
    }
}
