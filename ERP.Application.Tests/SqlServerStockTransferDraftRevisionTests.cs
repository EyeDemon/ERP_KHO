using Xunit;
using System.Text.Json;
using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockTransferDraftRevisionTests
{
    [Fact]
    public void UpdateJsonRequiresExplicitRevision()
    {
        var missing = "{\"sourceWarehouseId\":1,\"destinationWarehouseId\":2,\"details\":[]}";
        Action act = () => { _ = JsonSerializer.Deserialize<UpdateStockTransferDto>(missing); };
        act.Should().Throw<JsonException>();
    }

    [SqlServerFact]
    public async Task StaleDraftEditCannotOverwriteNewerLinesOrAudit()
    {
        await using var fixture = await StockTransferReturnFixture.CreateAsync();
        int id;
        await using (var db = StockTransferReturnFixture.Db())
        {
            var created = await fixture.Service(db, fixture.Creator).CreateAsync(new CreateStockTransferDto
            {
                SourceWarehouseId = fixture.Source, DestinationWarehouseId = fixture.Destination,
                Details = [new() { ProductId = fixture.ProductId, Quantity = 8 }]
            });
            created.DraftRevision.Should().Be(1);
            id = created.Id;
        }

        UpdateStockTransferDto Edit(int revision, decimal quantity, string note) => new()
        {
            ExpectedDraftRevision = revision, SourceWarehouseId = fixture.Source,
            DestinationWarehouseId = fixture.Destination, Note = note,
            Details = [new() { ProductId = fixture.ProductId, Quantity = quantity }]
        };

        await using (var first = StockTransferReturnFixture.Db())
            await fixture.Service(first, fixture.Creator).UpdateAsync(id, Edit(1, 7, "Người thứ nhất"));

        await using (var stale = StockTransferReturnFixture.Db())
        {
            var act = () => fixture.Service(stale, fixture.Creator)
                .UpdateAsync(id, Edit(1, 3, "Người thứ hai"));
            await act.Should().ThrowAsync<ConcurrencyException>()
                .WithMessage("*Phiên bản phiếu nháp đã thay đổi*");
        }
        await using (var missing = StockTransferReturnFixture.Db())
        {
            var act = () => fixture.Service(missing, fixture.Creator)
                .UpdateAsync(id, Edit(0, 3, "Thiếu phiên bản"));
            await act.Should().ThrowAsync<ConcurrencyException>();
        }
        await using (var verify = StockTransferReturnFixture.Db())
        {
            var current = await verify.StockTransfers.AsNoTracking().Include(x => x.Details)
                .SingleAsync(x => x.Id == id);
            current.Status.Should().Be(StockTransferStatus.Draft);
            current.DraftRevision.Should().Be(2);
            current.Note.Should().Be("Người thứ nhất");
            current.Details.Single().RequestedQuantity.Should().Be(7);
            (await verify.InventoryTransactions.CountAsync(x =>
                x.ReferenceType == "StockTransfer" && x.ReferenceId == id)).Should().Be(0);
            (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                x.EntityId == id && x.Action == "StockTransfer.Updated")).Should().Be(1);
        }

        await using (var refreshed = StockTransferReturnFixture.Db())
            await fixture.Service(refreshed, fixture.Creator).UpdateAsync(id,
                Edit(2, 6, "Người thứ hai sau tải lại"));
        await using (var verify = StockTransferReturnFixture.Db())
        {
            var current = await verify.StockTransfers.AsNoTracking().Include(x => x.Details)
                .SingleAsync(x => x.Id == id);
            current.DraftRevision.Should().Be(3);
            current.Details.Single().RequestedQuantity.Should().Be(6);
            (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                x.EntityId == id && x.Action == "StockTransfer.Updated")).Should().Be(2);
        }
    }

    [SqlServerFact]
    public async Task AuditFailureRollsBackDraftRevisionAndDetails()
    {
        await using var fixture = await StockTransferReturnFixture.CreateAsync();
        int id;
        await using (var db = StockTransferReturnFixture.Db())
            id = (await fixture.Service(db, fixture.Creator).CreateAsync(new CreateStockTransferDto
            {
                SourceWarehouseId = fixture.Source, DestinationWarehouseId = fixture.Destination,
                Details = [new() { ProductId = fixture.ProductId, Quantity = 8 }]
            })).Id;
        var edit = new UpdateStockTransferDto
        {
            ExpectedDraftRevision = 1, SourceWarehouseId = fixture.Source,
            DestinationWarehouseId = fixture.Destination,
            Details = [new() { ProductId = fixture.ProductId, Quantity = 4 }]
        };
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!)
            .AddInterceptors(new RejectEditAudit()).Options;
        await using (var failing = new ErpKhoDbContext(options))
        {
            var act = () => fixture.Service(failing, fixture.Creator).UpdateAsync(id, edit);
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Chặn lưu audit thử nghiệm*");
        }
        await using (var verify = StockTransferReturnFixture.Db())
        {
            var current = await verify.StockTransfers.AsNoTracking().Include(x => x.Details)
                .SingleAsync(x => x.Id == id);
            current.DraftRevision.Should().Be(1);
            current.Details.Single().RequestedQuantity.Should().Be(8);
            (await verify.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" &&
                x.EntityId == id && x.Action == "StockTransfer.Updated")).Should().Be(0);
        }
        await using (var retry = StockTransferReturnFixture.Db())
            await fixture.Service(retry, fixture.Creator).UpdateAsync(id, edit);
        await using (var verify = StockTransferReturnFixture.Db())
            (await verify.StockTransfers.AsNoTracking().Where(x => x.Id == id)
                .Select(x => x.DraftRevision).SingleAsync()).Should().Be(2);
    }

    private sealed class RejectEditAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AuditLog>().Any(entry =>
                entry.State == EntityState.Added && entry.Entity.Action == "StockTransfer.Updated") == true)
                throw new InvalidOperationException("Chặn lưu audit thử nghiệm.");
            return new ValueTask<InterceptionResult<int>>(result);
        }
    }
}
