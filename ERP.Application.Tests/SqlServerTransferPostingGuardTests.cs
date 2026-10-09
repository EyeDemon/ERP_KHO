using ERP.Application.DTOs;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerTransferPostingGuardTests
{
    [SqlServerFact]
    public async Task DuplicateDispatchKeepsOnePosting()
    {
        await using var fixture = await StockTransferReturnFixture.CreateAsync();
        int id;
        await using (var db = StockTransferReturnFixture.Db())
        {
            id = (await fixture.Service(db, fixture.Creator).CreateAsync(new CreateStockTransferDto
            {
                SourceWarehouseId = fixture.Source,
                DestinationWarehouseId = fixture.Destination,
                Details = [new() { ProductId = fixture.ProductId, Quantity = 8 }]
            })).Id;
            await fixture.Service(db, fixture.Checker).ApproveAsync(id);
            await fixture.Service(db, fixture.Checker).DispatchAsync(id);
        }
        await using (var retryDb = StockTransferReturnFixture.Db())
        {
            var retry = () => fixture.Service(retryDb, fixture.Checker).DispatchAsync(id);
            await retry.Should().ThrowAsync<ConcurrencyException>();
        }
        await using var verify = StockTransferReturnFixture.Db();
        (await verify.StockTransfers.SingleAsync(x => x.Id == id)).Status
            .Should().Be(StockTransferStatus.InTransit);
        (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "StockTransfer" &&
            x.ReferenceId == id && x.TransactionType == TransactionType.TransferOut)).Should().Be(1);
        (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
            x.EntityId == id && x.Action == "StockTransfer.Dispatched")).Should().Be(1);
    }
}
