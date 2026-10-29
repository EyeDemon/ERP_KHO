using ERP.Application.DTOs;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferReturnConcurrencyTests
{
    [SqlServerFact]
    public async Task TwoIndependentReturnsIntoSameSourceBucketSerializeWithoutDoublePosting()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var ids = new List<int>();
        await using (var setup = StockTransferReturnFixture.Db())
        {
            var maker = f.Service(setup, f.Creator);
            var checker = f.Service(setup, f.Checker);
            for (var i = 0; i < 2; i++)
            {
                var id = (await maker.CreateAsync(new CreateStockTransferDto
                {
                    SourceWarehouseId = f.Source,
                    DestinationWarehouseId = f.Destination,
                    Details = [new() { ProductId = f.ProductId, Quantity = 3 }]
                })).Id;
                await checker.ApproveAsync(id);
                await checker.DispatchAsync(id);
                ids.Add(id);
            }
        }

        async Task ReturnAsync(int id)
        {
            await using var db = StockTransferReturnFixture.Db();
            await f.Service(db, f.Checker).ReturnAsync(id,
                new ReturnStockTransferDto
                {
                    ReasonCode = "TRANSFER_DISPATCH_ERROR",
                    Reason = "Chưa nhận, hoàn trả theo phiếu gốc"
                });
        }

        await Task.WhenAll(ids.Select(ReturnAsync));
        await using var verify = StockTransferReturnFixture.Db();
        (await verify.InventoryStocks.Where(x => x.ProductId == f.ProductId &&
            x.WarehouseId == f.Source).SumAsync(x => x.Quantity)).Should().Be(10);
        (await verify.InventoryStocks.Where(x => x.ProductId == f.ProductId &&
            x.WarehouseId == f.Destination).SumAsync(x => x.Quantity)).Should().Be(0);
        (await verify.StockTransfers.CountAsync(x => ids.Contains(x.Id) &&
            x.Status == StockTransferStatus.Returned)).Should().Be(2);
        var journal = await verify.InventoryTransactions.AsNoTracking()
            .Where(x => x.ReferenceType == "StockTransfer" &&
                x.ReferenceId.HasValue && ids.Contains(x.ReferenceId.Value))
            .ToListAsync();
        journal.Count(x => x.TransactionType == TransactionType.TransferOut).Should().Be(2);
        var returns = journal.Where(x => x.TransactionType == TransactionType.TransferIn).ToList();
        returns.Should().HaveCount(2);
        returns.All(x => x.ReversalOfTransactionId.HasValue &&
            journal.Any(outbound => outbound.Id == x.ReversalOfTransactionId &&
                outbound.TransactionType == TransactionType.TransferOut &&
                outbound.ReferenceId == x.ReferenceId)).Should().BeTrue();
        (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
            x.EntityId.HasValue && ids.Contains(x.EntityId.Value) && x.Action == "StockTransfer.Returned")).Should().Be(2);
    }

    [SqlServerFact]
    public async Task ReceiveAndReturnCompeteForOneInTransitClaim()
    {
        await using var f = await StockTransferReturnFixture.CreateAsync();
        var id = await f.DispatchAsync();
        async Task<bool> ReturnAsync()
        {
            await using var db = StockTransferReturnFixture.Db();
            try
            {
                await f.Service(db, f.Checker).ReturnAsync(id,
                    new ReturnStockTransferDto { ReasonCode = "TRANSFER_DISPATCH_ERROR", Reason = "Sai tuyến" });
                return true;
            }
            catch (Exception ex) when (ex is ConcurrencyException or DbUpdateException) { return false; }
        }
        async Task<bool> ReceiveAsync()
        {
            await using var db = StockTransferReturnFixture.Db();
            try
            {
                await f.Service(db, f.Checker).ReceiveAsync(id,
                    new ReceiveStockTransferDto { Details = [new() { ProductId = f.ProductId, ReceivedQuantity = 8 }] });
                return true;
            }
            catch (Exception ex) when (ex is ConcurrencyException or DbUpdateException) { return false; }
        }
        var outcomes = await Task.WhenAll(ReturnAsync(), ReceiveAsync());
        outcomes.Count(x => x).Should().Be(1);
        await using var verify = StockTransferReturnFixture.Db();
        var status = (await verify.StockTransfers.SingleAsync(x => x.Id == id)).Status;
        status.Should().BeOneOf(StockTransferStatus.Returned, StockTransferStatus.Received);
        (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" && x.ReferenceId == id)).Should().Be(2);
        var source = await verify.InventoryStocks.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.Source).SumAsync(x => x.Quantity);
        var destination = await verify.InventoryStocks.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.Destination).SumAsync(x => x.Quantity);
        (source + destination).Should().Be(10);
    }
}
