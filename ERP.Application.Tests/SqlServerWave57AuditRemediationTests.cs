using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerWave57AuditRemediationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    private static ErpKhoDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

    [SqlServerFact]
    public async Task F002_ImmutableLedgerTrigger_BlocksRawSqlUpdateAndDeleteOnInventoryTransactions()
    {
        var fx = await CreateFixtureAsync();
        try
        {
            int txId;
            await using (var db = CreateContext())
            {
                var tx = new InventoryTransaction
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    LocationId = fx.LocationAId,
                    LotId = fx.LotId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Import,
                    Quantity = 25m,
                    UnitCost = 10000m,
                    ReferenceType = "ImportReceipt",
                    ReferenceId = 1,
                    TransactionDate = DateTime.UtcNow,
                    ActualOccurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
                    PostedAtUtc = DateTime.UtcNow,
                    CreatedBy = fx.UserId,
                    Note = "Wave57 trigger verification"
                };
                db.InventoryTransactions.Add(tx);
                await db.SaveChangesAsync();
                txId = tx.Id;
            }

            int reversalTxId;
            await using (var revDb = CreateContext())
            {
                var revTx = new InventoryTransaction
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    LocationId = fx.LocationAId,
                    LotId = fx.LotId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Reversal,
                    Quantity = 25m,
                    UnitCost = 10000m,
                    ReferenceType = "ImportReceipt",
                    ReferenceId = 1,
                    ReversalOfTransactionId = txId,
                    ReasonCode = "QTY_INPUT_ERROR",
                    TransactionDate = DateTime.UtcNow,
                    ActualOccurredAtUtc = DateTime.UtcNow,
                    PostedAtUtc = DateTime.UtcNow,
                    CreatedBy = fx.UserId,
                    Note = "Wave57 reversal marker"
                };
                revDb.InventoryTransactions.Add(revTx);
                await revDb.SaveChangesAsync();
                reversalTxId = revTx.Id;
            }

            await using (var rawDb = CreateContext())
            {
                var updateAct = () => rawDb.Database.ExecuteSqlRawAsync(
                    "UPDATE dbo.InventoryTransactions SET Quantity = 999.0000 WHERE Id = {0};", txId);
                await updateAct.Should().ThrowAsync<SqlException>()
                    .WithMessage("*INV_LEDGER_IMMUTABLE*InventoryTransactions is append-only.*");

                // Piggybacking a Quantity/ReferenceId mutation onto CorrectiveTransactionId NULL -> NOT NULL must also be blocked
                var piggybackAct = () => rawDb.Database.ExecuteSqlRawAsync(
                    "UPDATE dbo.InventoryTransactions SET CorrectiveTransactionId = {0}, Quantity = 999.0000 WHERE Id = {1};",
                    txId, reversalTxId);
                await piggybackAct.Should().ThrowAsync<SqlException>()
                    .WithMessage("*INV_LEDGER_IMMUTABLE*InventoryTransactions is append-only.*");

                // One-time CorrectiveTransactionId link (NULL -> NOT NULL) with no other column changes is permitted
                var validLinkRows = await rawDb.Database.ExecuteSqlRawAsync(
                    "UPDATE dbo.InventoryTransactions SET CorrectiveTransactionId = {0} WHERE Id = {1};",
                    txId, reversalTxId);
                validLinkRows.Should().Be(1);

                // Second update to CorrectiveTransactionId (NOT NULL -> NOT NULL) must be blocked
                var secondLinkAct = () => rawDb.Database.ExecuteSqlRawAsync(
                    "UPDATE dbo.InventoryTransactions SET CorrectiveTransactionId = {0} WHERE Id = {1};",
                    txId, reversalTxId);
                await secondLinkAct.Should().ThrowAsync<SqlException>()
                    .WithMessage("*INV_LEDGER_IMMUTABLE*InventoryTransactions is append-only.*");

                var deleteAct = () => rawDb.Database.ExecuteSqlRawAsync(
                    "DELETE FROM dbo.InventoryTransactions WHERE Id = {0};", reversalTxId);
                await deleteAct.Should().ThrowAsync<SqlException>()
                    .WithMessage("*INV_LEDGER_IMMUTABLE*InventoryTransactions is append-only.*");
            }

            await using (var verifyDb = CreateContext())
            {
                var persisted = await verifyDb.InventoryTransactions.AsNoTracking().SingleAsync(x => x.Id == txId);
                persisted.Quantity.Should().Be(25m);
                persisted.ActualOccurredAtUtc.Should().BeBefore(persisted.PostedAtUtc.AddSeconds(1));
            }
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task F001_RebuildBalancesAsync_DiscoversMissing9DBuckets_AndRejectsNegativeLedgerBuckets()
    {
        var fx = await CreateFixtureAsync();
        try
        {
            await using (var db = CreateContext())
            {
                // Intentionally delete pre-existing stock rows so RebuildBalancesAsync must discover 9D buckets from ledger
                await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId && x.ProductId == fx.ProductId).ExecuteDeleteAsync();

                db.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        TenantId = 1,
                        WarehouseId = fx.WarehouseId,
                        ProductId = fx.ProductId,
                        LocationId = fx.LocationAId,
                        LotId = fx.LotId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Import,
                        Quantity = 55m, // +10 from fixture setup + 55 = 65 at LocationA
                        ReferenceType = "ImportReceipt",
                        ReferenceId = 101,
                        TransactionDate = DateTime.UtcNow.AddMinutes(-10),
                        CreatedBy = fx.UserId
                    },
                    new InventoryTransaction
                    {
                        TenantId = 1,
                        WarehouseId = fx.WarehouseId,
                        ProductId = fx.ProductId,
                        LocationId = fx.LocationBId,
                        FromLocationId = fx.LocationAId,
                        ToLocationId = fx.LocationBId,
                        LotId = fx.LotId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Move,
                        Quantity = 5m,
                        ReferenceType = "InternalMove",
                        ReferenceId = 102,
                        TransactionDate = DateTime.UtcNow.AddMinutes(-5),
                        CreatedBy = fx.UserId
                    });
                await db.SaveChangesAsync();

                var rsvService = CreateReservationService(db, fx.UserId);
                var rebuiltCount = await rsvService.RebuildBalancesAsync(fx.WarehouseId, fx.ProductId);
                rebuiltCount.Should().BeGreaterThanOrEqualTo(2);
            }

            await using (var verifyDb = CreateContext())
            {
                var stocks = await verifyDb.InventoryStocks.AsNoTracking()
                    .Where(x => x.WarehouseId == fx.WarehouseId && x.ProductId == fx.ProductId)
                    .ToListAsync();

                stocks.Should().HaveCount(2);
                var locAStock = stocks.Single(x => x.LocationId == fx.LocationAId && x.LotId == fx.LotId);
                var locBStock = stocks.Single(x => x.LocationId == fx.LocationBId && x.LotId == fx.LotId);

                locAStock.Quantity.Should().Be(60m);
                locBStock.Quantity.Should().Be(5m);
                locAStock.GrainHash.Should().NotBeNull();
                locAStock.GrainHash!.Length.Should().Be(32);
                locBStock.GrainHash.Should().NotBeNull();
                locBStock.GrainHash!.Length.Should().Be(32);
            }

            // Now add an unbacked Export (-10) on LocationB (which only has +5) so net ledger becomes -5 < 0
            await using (var negDb = CreateContext())
            {
                negDb.InventoryTransactions.Add(new InventoryTransaction
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    LocationId = fx.LocationBId,
                    LotId = fx.LotId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Export,
                    Quantity = 10m,
                    ReferenceType = "ExportReceipt",
                    ReferenceId = 103,
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = fx.UserId
                });
                await negDb.SaveChangesAsync();

                var rsvService = CreateReservationService(negDb, fx.UserId);
                var act = () => rsvService.RebuildBalancesAsync(fx.WarehouseId, fx.ProductId);
                var ex = await act.Should().ThrowAsync<UnprocessableDomainException>();
                ex.Which.ErrorCode.Should().Be("INV_NEGATIVE_LEDGER_BUCKET_REBUILD");
            }
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task F003_RelationalPersistence_StocktakeRecount_PickingWave_PutToWallSlot_And3PlTables()
    {
        var fx = await CreateFixtureAsync();
        int stocktakeId = 0;
        int waveId = 0;
        int slotId = 0;
        int snapId = 0;
        int stmtId = 0;
        try
        {
            await using (var db = CreateContext())
            {
                var stocktake = new Stocktake
                {
                    TenantId = 1,
                    Code = $"ST-W57-{Guid.NewGuid():N}"[..24],
                    WarehouseId = fx.WarehouseId,
                    Status = StocktakeStatus.PendingRecount,
                    CreatedBy = fx.UserId,
                    CreatedAt = DateTime.UtcNow,
                    Details =
                    [
                        new StocktakeDetail
                        {
                            TenantId = 1,
                            ProductId = fx.ProductId,
                            LocationId = fx.LocationAId,
                            SystemQuantity = 60m,
                            ActualQuantity = 58m,
                            DifferenceQuantity = -2m,
                            CountAttempt1Qty = 57m,
                            CountAttempt2Qty = 58m,
                            CountAttemptCount = 2,
                            RecountReason = "Chênh lệch lần 1 cần đếm lại mù lần 2",
                            LastCountedByUserId = fx.UserId
                        }
                    ]
                };
                db.Stocktakes.Add(stocktake);

                var wave = new PickingWave
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    WaveCode = $"WAVE-W57-{Guid.NewGuid():N}"[..24],
                    PickingType = "FEFO_ZONE_CLUSTER",
                    Status = "RELEASED",
                    ReleasedTasksCount = 2,
                    TotalLinesCount = 4,
                    TotalRequestedQuantity = 10m,
                    ReleasedByUserId = fx.UserId,
                    ReleasedAtUtc = DateTime.UtcNow
                };
                db.PickingWaves.Add(wave);

                var slot = new PutToWallSlot
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    PickingTaskId = 1,
                    SlotIndex = 1,
                    SlotCode = "SLOT-A01",
                    ToteBarcode = "TOTE-W57-01",
                    ProductId = fx.ProductId,
                    ProductCode = "SKU-W57",
                    RequiredQuantity = 5m,
                    SortedQuantity = 5m,
                    SlotStatus = "COMPLETED",
                    LastSortedByUserId = fx.UserId,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                db.PutToWallSlots.Add(slot);

                var snap = new ThreePlStorageSnapshot
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    OwnerCode = "OWN-W57",
                    OwnerName = "3PL Owner W57",
                    SnapshotCode = $"SNAP-W57-{Guid.NewGuid():N}"[..24],
                    SnapshotDateUtc = DateTime.UtcNow.Date,
                    DistinctSkuCount = 1,
                    OccupiedBinCount = 2,
                    TotalOnHandQty = 65m,
                    EstimatedDailyStorageFeeVnd = 32500m,
                    EstimatedMonthlyStorageFeeVnd = 975000m,
                    CapturedByUserId = fx.UserId,
                    CapturedAtUtc = DateTime.UtcNow
                };
                db.ThreePlStorageSnapshots.Add(snap);

                var partner = new BusinessPartner
                {
                    TenantId = 1,
                    Code = $"BP-W57-{Guid.NewGuid():N}"[..20],
                    Name = "3PL Owner Partner",
                    IsCustomer = true,
                    IsActive = true
                };
                db.BusinessPartners.Add(partner);
                await db.SaveChangesAsync();

                var stmt = new ThreePlBillingStatement
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    PartnerId = partner.Id,
                    StatementCode = $"3PL-STMT-{Guid.NewGuid():N}"[..24],
                    PeriodFromUtc = DateTime.UtcNow.Date.AddDays(-30),
                    PeriodToUtc = DateTime.UtcNow.Date,
                    TotalOnHandQty = 65m,
                    TotalInboundQty = 65m,
                    TotalOutboundQty = 5m,
                    StorageFeeAmount = 32500m,
                    InboundHandlingFeeAmount = 15000m,
                    OutboundHandlingFeeAmount = 5000m,
                    TotalBillingAmount = 52500m,
                    Status = "ISSUED",
                    CreatedByUserId = fx.UserId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.ThreePlBillingStatements.Add(stmt);
                await db.SaveChangesAsync();

                stocktakeId = stocktake.Id;
                waveId = wave.Id;
                slotId = slot.Id;
                snapId = snap.Id;
                stmtId = stmt.Id;
            }

            await using (var verifyDb = CreateContext())
            {
                var st = await verifyDb.Stocktakes.Include(x => x.Details).AsNoTracking().SingleAsync(x => x.Id == stocktakeId);
                st.Status.Should().Be(StocktakeStatus.PendingRecount);
                st.Details.Single().CountAttempt1Qty.Should().Be(57m);
                st.Details.Single().CountAttempt2Qty.Should().Be(58m);
                st.Details.Single().CountAttemptCount.Should().Be(2);

                (await verifyDb.PickingWaves.AsNoTracking().AnyAsync(x => x.Id == waveId && x.Status == "RELEASED")).Should().BeTrue();
                (await verifyDb.PutToWallSlots.AsNoTracking().AnyAsync(x => x.Id == slotId && x.SortedQuantity == 5m)).Should().BeTrue();
                (await verifyDb.ThreePlStorageSnapshots.AsNoTracking().AnyAsync(x => x.Id == snapId && x.TotalOnHandQty == 65m)).Should().BeTrue();
                (await verifyDb.ThreePlBillingStatements.AsNoTracking().AnyAsync(x => x.Id == stmtId && x.TotalBillingAmount == 52500m)).Should().BeTrue();
            }
        }
        finally
        {
            await using var cleanDb = CreateContext();
            if (stmtId > 0)
            {
                var partnerId = await cleanDb.ThreePlBillingStatements.Where(x => x.Id == stmtId).Select(x => x.PartnerId).FirstOrDefaultAsync();
                await cleanDb.ThreePlBillingStatements.Where(x => x.Id == stmtId).ExecuteDeleteAsync();
                if (partnerId > 0) await cleanDb.BusinessPartners.Where(x => x.Id == partnerId).ExecuteDeleteAsync();
            }
            if (snapId > 0) await cleanDb.ThreePlStorageSnapshots.Where(x => x.Id == snapId).ExecuteDeleteAsync();
            if (slotId > 0) await cleanDb.PutToWallSlots.Where(x => x.Id == slotId).ExecuteDeleteAsync();
            if (waveId > 0) await cleanDb.PickingWaves.Where(x => x.Id == waveId).ExecuteDeleteAsync();
            if (stocktakeId > 0)
            {
                await cleanDb.StocktakeDetails.Where(x => x.StocktakeId == stocktakeId).ExecuteDeleteAsync();
                await cleanDb.Stocktakes.Where(x => x.Id == stocktakeId).ExecuteDeleteAsync();
            }
            await CleanupAsync(fx);
        }
    }

    [SqlServerFact]
    public async Task F009_OpeningBalance_CustomerReturnRma_AndVasDekit_FourScenarios_HappyPath_Idempotency_Concurrency_Reversal()
    {
        var fx = await CreateFixtureAsync();
        try
        {
            // 1. Opening Balance + RMA Return + VAS_DEKIT Happy Path & Idempotency-Key deduplication
            var idempotencyKeyHash = $"W57-IDEMP-{Guid.NewGuid():N}";

            await using (var db = CreateContext())
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.AcquireInventoryAllocationLocksAsync(fx.WarehouseId, [fx.ProductId]);

                var stock = await db.InventoryStocks.SingleAsync(x =>
                    x.WarehouseId == fx.WarehouseId && x.ProductId == fx.ProductId &&
                    x.LocationId == fx.LocationAId && x.LotId == fx.LotId);

                // Opening Balance (+20) + CustomerReturn (+5) + VAS_DEKIT TransferOut (-2) / TransferIn (+2 on same LotId!)
                stock.Quantity += 25m;
                stock.RefreshGrainHash();

                var now = DateTime.UtcNow;
                db.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        TenantId = 1,
                        WarehouseId = fx.WarehouseId,
                        ProductId = fx.ProductId,
                        LocationId = fx.LocationAId,
                        LotId = fx.LotId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Import,
                        Quantity = 20m,
                        ReferenceType = "OpeningBalanceImport",
                        ReferenceId = 5001,
                        TransactionDate = now,
                        ActualOccurredAtUtc = now,
                        PostedAtUtc = now,
                        CreatedBy = fx.UserId
                    },
                    new InventoryTransaction
                    {
                        TenantId = 1,
                        WarehouseId = fx.WarehouseId,
                        ProductId = fx.ProductId,
                        LocationId = fx.LocationAId,
                        LotId = fx.LotId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Import,
                        Quantity = 5m,
                        ReferenceType = "CustomerReturn",
                        ReferenceId = 5002,
                        TransactionDate = now,
                        ActualOccurredAtUtc = now,
                        PostedAtUtc = now,
                        CreatedBy = fx.UserId
                    },
                    new InventoryTransaction
                    {
                        TenantId = 1,
                        WarehouseId = fx.WarehouseId,
                        ProductId = fx.ProductId,
                        LocationId = fx.LocationAId,
                        LotId = fx.LotId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.TransferOut,
                        Quantity = 2m,
                        ReferenceType = "VAS_DEKIT",
                        ReferenceId = 5003,
                        TransactionDate = now,
                        ActualOccurredAtUtc = now,
                        PostedAtUtc = now,
                        CreatedBy = fx.UserId
                    },
                    new InventoryTransaction
                    {
                        TenantId = 1,
                        WarehouseId = fx.WarehouseId,
                        ProductId = fx.ProductId,
                        LocationId = fx.LocationAId,
                        LotId = fx.LotId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.TransferIn,
                        Quantity = 2m,
                        ReferenceType = "VAS_DEKIT",
                        ReferenceId = 5003,
                        TransactionDate = now,
                        ActualOccurredAtUtc = now,
                        PostedAtUtc = now,
                        CreatedBy = fx.UserId
                    });

                db.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    TenantId = 1,
                    UserId = fx.UserId,
                    CommandScope = "DataImports.OpeningBalances",
                    KeyHash = idempotencyKeyHash,
                    RequestFingerprint = idempotencyKeyHash,
                    Status = IdempotencyStatus.Completed,
                    ResponseStatusCode = 200,
                    ResponseBody = "{\"importedRows\":1}",
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    WarehouseId = fx.WarehouseId,
                    CreatedAtUtc = now,
                    CompletedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(1)
                });

                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            // 2. Duplicate Idempotency-Key check: unique index blocks duplicate (TenantId, UserId, CommandScope, KeyHash)
            await using (var dupDb = CreateContext())
            {
                dupDb.IdempotencyRecords.Add(new IdempotencyRecord
                {
                    TenantId = 1,
                    UserId = fx.UserId,
                    CommandScope = "DataImports.OpeningBalances",
                    KeyHash = idempotencyKeyHash,
                    RequestFingerprint = idempotencyKeyHash,
                    Status = IdempotencyStatus.Processing,
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    WarehouseId = fx.WarehouseId,
                    CreatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
                });
                var dupAct = () => dupDb.SaveChangesAsync();
                await dupAct.Should().ThrowAsync<DbUpdateException>();
            }

            // 3. Parallel Concurrency scenario: 2 concurrent workers locking & adding +3 each via 9D lock
            var tasks = Enumerable.Range(0, 2).Select(async workerIdx =>
            {
                await using var workerDb = CreateContext();
                await using var tx = await workerDb.Database.BeginTransactionAsync();
                await workerDb.AcquireInventoryAllocationLocksAsync(fx.WarehouseId, [fx.ProductId]);
                var stock = await workerDb.InventoryStocks.SingleAsync(x =>
                    x.WarehouseId == fx.WarehouseId && x.ProductId == fx.ProductId &&
                    x.LocationId == fx.LocationAId && x.LotId == fx.LotId);
                stock.Quantity += 3m;
                workerDb.InventoryTransactions.Add(new InventoryTransaction
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    LocationId = fx.LocationAId,
                    LotId = fx.LotId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Import,
                    Quantity = 3m,
                    ReferenceType = "CustomerReturn",
                    ReferenceId = 6000 + workerIdx,
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = fx.UserId
                });
                await workerDb.SaveChangesAsync();
                await tx.CommitAsync();
            });
            await Task.WhenAll(tasks);

            // 4. Reversal scenario: append-only Reversal entry (-5) referencing original CustomerReturn transaction
            await using (var revDb = CreateContext())
            {
                var origRmaTx = await revDb.InventoryTransactions.AsNoTracking()
                    .FirstAsync(x => x.WarehouseId == fx.WarehouseId && x.ReferenceType == "CustomerReturn" && x.ReferenceId == 5002);

                await using var tx = await revDb.Database.BeginTransactionAsync();
                await revDb.AcquireInventoryAllocationLocksAsync(fx.WarehouseId, [fx.ProductId]);
                var stock = await revDb.InventoryStocks.SingleAsync(x =>
                    x.WarehouseId == fx.WarehouseId && x.ProductId == fx.ProductId &&
                    x.LocationId == fx.LocationAId && x.LotId == fx.LotId);
                stock.Quantity -= 5m;

                revDb.InventoryTransactions.Add(new InventoryTransaction
                {
                    TenantId = 1,
                    WarehouseId = fx.WarehouseId,
                    ProductId = fx.ProductId,
                    LocationId = fx.LocationAId,
                    LotId = fx.LotId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Export,
                    Quantity = 5m,
                    ReferenceType = "CustomerReturnReversal",
                    ReferenceId = 5002,
                    ReversalOfTransactionId = origRmaTx.Id,
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = fx.UserId,
                    Note = "Đảo bút toán RMA #5002"
                });
                await revDb.SaveChangesAsync();
                await tx.CommitAsync();
            }

            // Final verification: RebuildBalancesAsync confirms 100% 9D parity (10 initial + 20 OB + 5 RMA + 6 concurrent - 5 reversal = 36)
            await using (var verifyDb = CreateContext())
            {
                var stock = await verifyDb.InventoryStocks.AsNoTracking().SingleAsync(x =>
                    x.WarehouseId == fx.WarehouseId && x.ProductId == fx.ProductId &&
                    x.LocationId == fx.LocationAId && x.LotId == fx.LotId);
                stock.Quantity.Should().Be(36m);

                var rsvService = CreateReservationService(verifyDb, fx.UserId);
                await rsvService.RebuildBalancesAsync(fx.WarehouseId, fx.ProductId);

                var rebuilt = await verifyDb.InventoryStocks.AsNoTracking().SingleAsync(x =>
                    x.WarehouseId == fx.WarehouseId && x.ProductId == fx.ProductId &&
                    x.LocationId == fx.LocationAId && x.LotId == fx.LotId);
                rebuilt.Quantity.Should().Be(36m);
            }
        }
        finally
        {
            await CleanupAsync(fx);
        }
    }

    private static StockReservationService CreateReservationService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new StockReservationService(
            db,
            new InventoryStockRepository(db),
            new UnitOfWork(db),
            new WarehouseAuthorizationService(db, current),
            current,
            new StockReservationOptions());
    }

    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }

    private sealed record Fixture(int UserId, int RoleId, int UnitId, int ProductId, int WarehouseId, int LocationAId, int LocationBId, int LotId);

    private static async Task<Fixture> CreateFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var role = new Role { RoleName = $"W57Role{suffix}" };
        var user = new User { Username = $"W57User{suffix}", PasswordHash = "hash", FullName = "Wave57 Auditor", Role = role };
        var unit = new Unit { Code = $"U{suffix}"[..10], Name = "Cái" };
        var product = new Product { Code = $"SKU-W57-{suffix}", Name = "Wave57 Product", Unit = unit };
        var warehouse = new Warehouse { Code = $"WH57-{suffix}"[..12], Name = "Wave57 Warehouse" };
        db.AddRange(role, user, unit, product, warehouse);
        await db.SaveChangesAsync();

        var locA = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"LOC-A-{suffix}"[..16],
            Name = "Receiving Stage A",
            LocationType = WarehouseLocationType.Staging,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var locB = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"LOC-B-{suffix}"[..16],
            Name = "Storage Bin B",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var lot = new InventoryLot
        {
            ProductId = product.Id,
            LotNumber = $"LOT-W57-{suffix}",
            ManufactureDate = DateTime.UtcNow.Date.AddMonths(-1),
            ExpiryDate = DateTime.UtcNow.Date.AddMonths(11),
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.AddRange(locA, locB, lot);
        await db.SaveChangesAsync();

        db.UserWarehouses.Add(new UserWarehouse { UserId = user.Id, WarehouseId = warehouse.Id, CreatedBy = user.Id });
        var initialStock = new InventoryStock
        {
            TenantId = 1,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            LocationId = locA.Id,
            LotId = lot.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m
        };
        initialStock.RefreshGrainHash();
        db.InventoryStocks.Add(initialStock);
        db.InventoryTransactions.Add(new InventoryTransaction
        {
            TenantId = 1,
            WarehouseId = warehouse.Id,
            ProductId = product.Id,
            LocationId = locA.Id,
            LotId = lot.Id,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Import,
            Quantity = 10m,
            ReferenceType = "ImportReceipt",
            ReferenceId = 1,
            TransactionDate = DateTime.UtcNow.AddMinutes(-30),
            CreatedBy = user.Id
        });
        await db.SaveChangesAsync();

        return new Fixture(user.Id, role.Id, unit.Id, product.Id, warehouse.Id, locA.Id, locB.Id, lot.Id);
    }

    private static async Task CleanupAsync(Fixture fx)
    {
        await using var db = CreateContext();
        await db.IdempotencyRecords.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == fx.UserId || x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try
        {
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId && x.ReversalOfTransactionId != null).ExecuteDeleteAsync();
            await db.InventoryTransactions.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        }
        await db.InventoryStocks.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(x => x.Id == fx.LotId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => x.UserId == fx.UserId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fx.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fx.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fx.UnitId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fx.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fx.RoleId).ExecuteDeleteAsync();
    }
}
