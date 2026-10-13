using ERP.Application.DTOs;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferReverseDraftLedgerGuardTests
{
    private static CreateReverseStockTransferDto Reason() => new()
    { ReasonCode = "TRANSFER_ROUTE_ERROR", Reason = "Đối soát phiếu điều chuyển ngược" };

    [SqlServerFact]
    public async Task ReverseDraftRejectsMismatchedReceiptLedgerWithoutDocumentOrAudit()
    {
        await AssertMismatchRejectedAsync(TransactionType.TransferIn);
    }

    [SqlServerFact]
    public async Task ReverseDraftRejectsMismatchedDispatchLedgerWithoutDocumentOrAudit()
    {
        await AssertMismatchRejectedAsync(TransactionType.TransferOut);
    }

    private static async Task AssertMismatchRejectedAsync(TransactionType transactionType)
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var id = await f.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
            await f.Service(db, f.Checker).ReceiveAsync(id,
                new ReceiveStockTransferDto { Details = [new() { ProductId = f.ProductId, ReceivedQuantity = 8 }] });

        await using (var db = StockTransferReturnFixture.Db())
            await db.InventoryTransactions.Where(x => x.ReferenceType == "StockTransfer" &&
                x.ReferenceId == id && x.TransactionType == transactionType)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, 7m));

        await using var verify = StockTransferReturnFixture.Db();
        var action = () => f.Service(verify, f.Checker).CreateReverseDraftAsync(id, Reason());
        await action.Should().ThrowAsync<ConcurrencyException>().WithMessage("*Sổ cái xuất/nhận*");
        (await verify.StockTransfers.SingleAsync(x => x.Id == id)).Status.Should().Be(StockTransferStatus.Received);
        (await verify.StockTransfers.CountAsync(x => x.ReverseOfTransferId == id)).Should().Be(0);
        (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
            x.Action == "StockTransfer.ReverseDraftCreated")).Should().Be(0);
        (await verify.InventoryStocks.Where(x => x.ProductId == f.ProductId &&
            x.WarehouseId == f.Destination).SumAsync(x => x.Quantity)).Should().Be(8);
    }

    [SqlServerFact]
    public async Task PartialReceiptCreatesReverseDraftForActualReceivedQuantityOnly()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var id = await f.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
            await f.Service(db, f.Checker).ReceiveAsync(id,
                new ReceiveStockTransferDto { Details = [new()
                {
                    ProductId = f.ProductId, ReceivedQuantity = 5, MissingQuantity = 3
                }] });

        await using var verify = StockTransferReturnFixture.Db();
        var draft = await f.Service(verify, f.Checker).CreateReverseDraftAsync(id, Reason());
        draft.Status.Should().Be(StockTransferStatus.Draft);
        draft.Details.Single().RequestedQuantity.Should().Be(5);
        draft.SourceWarehouseId.Should().Be(f.Destination);
        draft.DestinationWarehouseId.Should().Be(f.Source);
        (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" &&
            x.ReferenceId == draft.Id)).Should().Be(0);
        (await verify.InventoryStocks.Where(x => x.ProductId == f.ProductId &&
            x.WarehouseId == f.Destination).SumAsync(x => x.Quantity)).Should().Be(5);
    }
}
