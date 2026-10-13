using System.Data;
using System.Data.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerInventoryReconciliationSnapshotProvenanceTests
{
    private static ErpKhoDbContext CreateContext(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(Environment.GetEnvironmentVariable(
                SqlServerFactAttribute.ConnectionVariable)!);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new ErpKhoDbContext(options.Options);
    }

    private sealed class SqlIsolationProbe : DbCommandInterceptor
    {
        public List<IsolationLevel?> Observed { get; } = [];
        public bool PauseBeforeLedgerAggregate { get; set; }
        public TaskCompletionSource<bool> ReaderPaused { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ResumeReader { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Observed.Add(command.Transaction?.IsolationLevel);
            // This command follows materialization of the all-status stock
            // GROUP BY. Pausing here holds the prior SQL SERIALIZABLE stock
            // read locks while a second DbContext attempts a physical UPDATE.
            if (PauseBeforeLedgerAggregate &&
                command.CommandText.Contains("InventoryTransactions", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase))
            {
                PauseBeforeLedgerAggregate = false;
                ReaderPaused.TrySetResult(true);
                await ResumeReader.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }

    [SqlServerFact]
    public async Task BackdatedLedgerAfterPinnedAnchor_LeavesHistoryStableButDisablesSnapshotVerdict()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var role = new Role { RoleName = $"ReconRole{suffix}" };
        var user = new User
        {
            Username = $"ReconUser{suffix}", PasswordHash = "not-used",
            FullName = "Snapshot provenance test", Role = role
        };
        var unit = new Unit { Code = $"RSU{suffix}", Name = "Snapshot unit", DecimalPlaces = 4 };
        var product = new Product { Code = $"RSP{suffix}", Name = "Snapshot product", Unit = unit };
        var unrelatedProduct = new Product
        {
            Code = $"RSO{suffix}", Name = "Unrelated concurrent product", Unit = unit
        };
        var warehouse = new Warehouse { Code = $"RSW{suffix}", Name = "Snapshot warehouse" };
        var location = new WarehouseLocation
        {
            Code = $"RSL{suffix}", Name = "Snapshot location",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true, IsPickable = true
        };
        await using (var setup = CreateContext())
        {
            setup.AddRange(user, product, unrelatedProduct, warehouse);
            await setup.SaveChangesAsync();
            location.WarehouseId = warehouse.Id;
            location.CreatedBy = user.Id;
            setup.WarehouseLocations.Add(location);
            await setup.SaveChangesAsync();
            setup.UserWarehouses.Add(new UserWarehouse
            {
                UserId = user.Id, WarehouseId = warehouse.Id, CreatedBy = user.Id
            });
            setup.InventoryStocks.Add(new InventoryStock
            {
                WarehouseId = warehouse.Id, ProductId = product.Id,
                LocationId = location.Id,
                Status = InventoryStatus.Available, Quantity = 10
            });
            setup.InventoryStocks.Add(new InventoryStock
            {
                WarehouseId = warehouse.Id, ProductId = unrelatedProduct.Id,
                LocationId = location.Id,
                Status = InventoryStatus.Available, Quantity = 7
            });
            setup.InventoryTransactions.Add(new InventoryTransaction
            {
                WarehouseId = warehouse.Id, ProductId = product.Id,
                LocationId = location.Id,
                CreatedBy = user.Id, InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import, Quantity = 10,
                TransactionDate = DateTime.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        try
        {
            var probe = new SqlIsolationProbe();
            await using var read = CreateContext(probe);
            var query = new InventoryReconciliationQueryService(
                read, new WarehouseAuthorizationService(read, new CurrentUser(user.Id)));
            var first = await query.GetInvestigationAsync(warehouse.Id, product.Id);
            // Every SQL read — including access checks and the final evidence
            // keyset pages — must share a serializable transaction. It must
            // be disposed before the next HTTP investigation begins.
            probe.Observed.Should().NotBeEmpty()
                .And.OnlyContain(x => x == IsolationLevel.Serializable);
            read.Database.CurrentTransaction.Should().BeNull();
            first.EvidenceAnchorsStale.Should().BeFalse();
            first.SnapshotComparisonStatus.Should().Be("Match");
            first.SnapshotComparisonExpectedQuantity.Should().Be(10m);
            var originalIds = first.Events.Select(x => x.TransactionId).ToArray();

            await using (var writer = CreateContext())
            {
                writer.InventoryTransactions.Add(new InventoryTransaction
                {
                    WarehouseId = warehouse.Id, ProductId = product.Id,
                    CreatedBy = user.Id, InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Import, Quantity = 3,
                    TransactionDate = DateTime.UtcNow.AddYears(-5)
                });
                await writer.SaveChangesAsync();
            }

            var pinned = await query.GetInvestigationAsync(warehouse.Id, product.Id,
                eventAnchorId: first.EventAnchorId, bucketAnchorId: first.BucketAnchorId);
            pinned.LedgerHasEventsAfterAnchor.Should().BeTrue();
            pinned.EvidenceAnchorsStale.Should().BeTrue();
            pinned.Events.Select(x => x.TransactionId).Should().Equal(originalIds);
            pinned.AllStatusStatus.Should().Be("Match"); // historical arithmetic, not a snapshot
            pinned.SnapshotComparisonStatus.Should().Be("Indeterminate");
            pinned.SnapshotComparisonExpectedQuantity.Should().BeNull();
            pinned.SnapshotComparisonDifference.Should().BeNull();

            var fresh = await query.GetInvestigationAsync(warehouse.Id, product.Id);
            fresh.EvidenceAnchorsStale.Should().BeFalse();
            fresh.SnapshotComparisonStatus.Should().Be("Mismatch");
            fresh.SnapshotComparisonExpectedQuantity.Should().Be(13m);
            fresh.SnapshotComparisonDifference.Should().Be(-3m);

            // QC insert alone: no new Ledger or AVAILABLE bucket.
            await using (var writer = CreateContext())
            {
                writer.InventoryStocks.Add(new InventoryStock
                {
                    WarehouseId = warehouse.Id, ProductId = product.Id,
                    LocationId = location.Id, Status = InventoryStatus.QcHold,
                    Quantity = 2m
                });
                await writer.SaveChangesAsync();
            }
            var qcPinned = await query.GetInvestigationAsync(warehouse.Id, product.Id,
                eventAnchorId: fresh.EventAnchorId, bucketAnchorId: fresh.BucketAnchorId,
                allStatusBucketAnchorId: fresh.AllStatusBucketAnchorId);
            qcPinned.LedgerHasEventsAfterAnchor.Should().BeFalse();
            qcPinned.BucketHasRowsAfterAnchor.Should().BeFalse();
            qcPinned.AllStatusBucketHasRowsAfterAnchor.Should().BeTrue();
            qcPinned.EvidenceAnchorsStale.Should().BeTrue();
            qcPinned.SnapshotComparisonStatus.Should().Be("Indeterminate");
            qcPinned.SnapshotComparisonExpectedQuantity.Should().BeNull();
            qcPinned.SnapshotComparisonDifference.Should().BeNull();
            var freshQc = await query.GetInvestigationAsync(warehouse.Id, product.Id);
            await using (var writer = CreateContext())
            {
                var stock = await writer.InventoryStocks.SingleAsync(x =>
                    x.WarehouseId == warehouse.Id && x.ProductId == product.Id &&
                    x.Status == InventoryStatus.QcHold);
                stock.Quantity = 5m;
                await writer.SaveChangesAsync();
            }
            var pinnedEdit = await query.GetInvestigationAsync(warehouse.Id, product.Id,
                eventAnchorId: freshQc.EventAnchorId,
                allStatusBucketAnchorId: freshQc.AllStatusBucketAnchorId,
                allStatusBucketAggregateFingerprint: freshQc.AllStatusBucketAggregateFingerprint);
            pinnedEdit.AllStatusBucketAggregateChanged.Should().BeTrue();
            pinnedEdit.LedgerHasEventsAfterAnchor.Should().BeFalse();
            pinnedEdit.AllStatusBucketHasRowsAfterAnchor.Should().BeFalse();
            pinnedEdit.SnapshotComparisonStatus.Should().Be("Indeterminate");
            pinnedEdit.SnapshotComparisonDifference.Should().BeNull();

            // A real second SQL connection must not be able to change a stock
            // bucket between the detail's materialized Balance GROUP BY and
            // its subsequent Ledger GROUP BY. Otherwise this single HTTP
            // evidence response could mix two database instants.
            probe.PauseBeforeLedgerAggregate = true;
            var inFlightInvestigation = query.GetInvestigationAsync(warehouse.Id, product.Id);
            try
            {
                await probe.ReaderPaused.Task.WaitAsync(TimeSpan.FromSeconds(30));

                // A different product at the SAME warehouse must remain
                // writable. This detects an accidental table/warehouse-wide
                // reader lock that would serialize otherwise unrelated posts.
                await using (var unrelatedWriter = CreateContext())
                {
                    var unrelatedStock = await unrelatedWriter.InventoryStocks.SingleAsync(x =>
                        x.WarehouseId == warehouse.Id && x.ProductId == unrelatedProduct.Id);
                    unrelatedStock.Quantity += 1m;
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    await unrelatedWriter.SaveChangesAsync(timeout.Token);
                }

                await using var competingWriter = CreateContext();
                var availableStock = await competingWriter.InventoryStocks.SingleAsync(x =>
                    x.WarehouseId == warehouse.Id && x.ProductId == product.Id &&
                    x.Status == InventoryStatus.Available);
                availableStock.Quantity += 1m;
                var save = competingWriter.SaveChangesAsync();
                try
                {
                    // The update needs an exclusive lock on the same stock row.
                    // SQL SERIALIZABLE retains the reader's shared lock until
                    // its transaction commits, so this write must not finish.
                    var earliest = await Task.WhenAny(save, Task.Delay(500));
                    earliest.Should().NotBe(save,
                        "a writer must not change physical stock during a scoped reconciliation read");
                }
                finally
                {
                    // The blocked writer must be released before waiting for it.
                    probe.ResumeReader.TrySetResult(true);
                }

                var stableResponse = await inFlightInvestigation.WaitAsync(TimeSpan.FromSeconds(30));
                stableResponse.AllStatusCurrentQuantity.Should().Be(freshQc.AllStatusCurrentQuantity + 3m);
                await save.WaitAsync(TimeSpan.FromSeconds(30));
            }
            finally
            {
                // Also release the reader when the unrelated write, setup,
                // or any earlier assertion unexpectedly fails.
                probe.ResumeReader.TrySetResult(true);
            }
            read.Database.CurrentTransaction.Should().BeNull();
            var afterWriter = await query.GetInvestigationAsync(warehouse.Id, product.Id);
            afterWriter.AllStatusCurrentQuantity.Should().Be(freshQc.AllStatusCurrentQuantity + 4m);
            afterWriter.SnapshotComparisonStatus.Should().Be("Mismatch");
            await using var verifyOther = CreateContext();
            (await verifyOther.InventoryStocks.SingleAsync(x =>
                x.WarehouseId == warehouse.Id && x.ProductId == unrelatedProduct.Id))
                .Quantity.Should().Be(8m);
        }
        finally
        {
            await using var cleanup = CreateContext();
            await cleanup.InventoryTransactions.Where(x =>
                x.ProductId == product.Id && x.WarehouseId == warehouse.Id).ExecuteDeleteAsync();
            await cleanup.InventoryStocks.Where(x =>
                (x.ProductId == product.Id || x.ProductId == unrelatedProduct.Id) &&
                x.WarehouseId == warehouse.Id).ExecuteDeleteAsync();
            await cleanup.UserWarehouses.Where(x =>
                x.UserId == user.Id && x.WarehouseId == warehouse.Id).ExecuteDeleteAsync();
            await cleanup.Products.Where(x =>
                x.Id == product.Id || x.Id == unrelatedProduct.Id).ExecuteDeleteAsync();
            await cleanup.Units.Where(x => x.Id == unit.Id).ExecuteDeleteAsync();
            await cleanup.WarehouseLocations.Where(x => x.Id == location.Id).ExecuteDeleteAsync();
            await cleanup.Warehouses.Where(x => x.Id == warehouse.Id).ExecuteDeleteAsync();
            await cleanup.Users.Where(x => x.Id == user.Id).ExecuteDeleteAsync();
            await cleanup.Roles.Where(x => x.Id == role.Id).ExecuteDeleteAsync();
        }
    }
}
