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
