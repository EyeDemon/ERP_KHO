using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferReturnTraceabilityTests
{
    private static ReturnStockTransferDto Reason() => new()
    { ReasonCode = "TRANSFER_DISPATCH_ERROR", Reason = "Hoàn trả về kho nguồn" };

    [SqlServerFact]
    public async Task NativeReturnLinksOriginalEvenWhenEventWindowContainsOnlyInverse()
    {
        await using var fixture = await StockTransferReturnFixture.CreateAsync();
        var id = await fixture.DispatchAsync();
        await using (var db = StockTransferReturnFixture.Db())
            await fixture.Service(db, fixture.Checker).ReturnAsync(id, Reason());

        await using var verify = StockTransferReturnFixture.Db();
        var auth = new Mock<ERP.Application.Interfaces.IWarehouseAuthorizationService>();
        auth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int> { fixture.Source, fixture.Destination });
        var trace = await new InventoryTraceabilityQueryService(verify, auth.Object)
            .TraceAsync(referenceType: "StockTransfer", referenceId: id, limit: 1);

        trace.EventsTruncated.Should().BeTrue();
        trace.Events.Should().HaveCount(2);
        var outbound = trace.Events.Single(x => x.TransactionType == nameof(TransactionType.TransferOut));
        var inverse = trace.Events.Single(x => x.TransactionType == nameof(TransactionType.TransferIn));
        outbound.IsReversed.Should().BeTrue();
        outbound.ReversalTransactionId.Should().Be(inverse.TransactionId);
        inverse.ReversalOfTransactionId.Should().Be(outbound.TransactionId);
        inverse.ReasonCode.Should().Be("TRANSFER_DISPATCH_ERROR");
    }

    [SqlServerFact]
    public async Task AlreadyLinkedOutboundRejectsReturnWithoutNewStockOrAudit()
    {
        await using var fixture = await StockTransferReturnFixture.CreateAsync();
        var id = await fixture.DispatchAsync();
        await using (var setup = StockTransferReturnFixture.Db())
        {
            var original = await setup.InventoryTransactions.SingleAsync(x =>
                x.ReferenceType == "StockTransfer" && x.ReferenceId == id &&
                x.TransactionType == TransactionType.TransferOut);
            setup.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = fixture.ProductId, WarehouseId = fixture.Source,
                TransactionType = TransactionType.Reversal, Quantity = 8,
                ReferenceType = "LegacyCorrection", ReferenceId = id,
                ReversalOfTransactionId = original.Id, CreatedBy = fixture.Checker
            });
            await setup.SaveChangesAsync();
        }

        await using (var attempt = StockTransferReturnFixture.Db())
        {
            var action = () => fixture.Service(attempt, fixture.Checker).ReturnAsync(id, Reason());
            await action.Should().ThrowAsync<ConcurrencyException>()
                .WithMessage("*đã có liên kết đảo*");
        }

        await using var verify = StockTransferReturnFixture.Db();
        (await verify.StockTransfers.SingleAsync(x => x.Id == id)).Status
            .Should().Be(StockTransferStatus.InTransit);
        (await verify.InventoryStocks.Where(x => x.ProductId == fixture.ProductId &&
            x.WarehouseId == fixture.Source).SumAsync(x => x.Quantity)).Should().Be(2);
        (await verify.InventoryTransactions.CountAsync(x =>
            x.ReferenceType == "StockTransfer" && x.ReferenceId == id)).Should().Be(1);
        (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
            x.EntityId == id && x.Action == "StockTransfer.Returned")).Should().Be(0);
    }
}
