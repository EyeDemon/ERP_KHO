using ERP.Application.DTOs;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferQuerySafetyTests
{
    [SqlServerFact]
    public async Task HugePageMustReturnEmptyWithoutOverflowAndPreserveFilteredCount()
    {
        await using var fixture = await StockTransferReturnFixture.CreateAsync();
        await using var db = StockTransferReturnFixture.Db();
        var service = fixture.Service(db, fixture.Creator);
        var created = await service.CreateAsync(new CreateStockTransferDto
        {
            SourceWarehouseId = fixture.Source,
            DestinationWarehouseId = fixture.Destination,
            Details = [new() { ProductId = fixture.ProductId, Quantity = 1 }]
        });

        var result = await service.GetAsync(new StockTransferQueryDto
        {
            PageIndex = int.MaxValue, PageSize = 100,
            SourceWarehouseId = fixture.Source
        });

        result.Items.Should().BeEmpty();
        result.TotalRecords.Should().Be(1);
        result.PageIndex.Should().Be(int.MaxValue);
        result.PageSize.Should().Be(100);
        created.Id.Should().BePositive();
    }

    [SqlServerFact]
    public async Task DateRangeAndEqualTimestampsMustReturnDeterministicPages()
    {
        await using var fixture = await StockTransferReturnFixture.CreateAsync();
        await using var db = StockTransferReturnFixture.Db();
        var service = fixture.Service(db, fixture.Creator);
        async Task<int> CreateAsync() => (await service.CreateAsync(new CreateStockTransferDto
        {
            SourceWarehouseId = fixture.Source,
            DestinationWarehouseId = fixture.Destination,
            Details = [new() { ProductId = fixture.ProductId, Quantity = 1 }]
        })).Id;
        var first = await CreateAsync();
        var second = await CreateAsync();
        var sameInstant = DateTime.UtcNow.Date.AddHours(10);
        await db.StockTransfers.Where(x => x.Id == first || x.Id == second)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.CreatedAt, sameInstant));

        var query = new StockTransferQueryDto
        {
            PageIndex = 1, PageSize = 1,
            SourceWarehouseId = fixture.Source,
            ToDate = DateTime.MaxValue
        };
        var pageOne = await service.GetAsync(query);
        query.PageIndex = 2;
        var pageTwo = await service.GetAsync(query);

        pageOne.TotalRecords.Should().Be(2);
        pageOne.Items.Select(x => x.Id).Should().Equal(second);
        pageTwo.Items.Select(x => x.Id).Should().Equal(first);

        // ToDate is a calendar date, not a midnight-only timestamp.
        query.PageIndex = 1;
        query.PageSize = 10;
        query.ToDate = sameInstant.Date;
        var inclusiveDay = await service.GetAsync(query);
        inclusiveDay.Items.Select(x => x.Id).Should().Equal(second, first);
    }
}
