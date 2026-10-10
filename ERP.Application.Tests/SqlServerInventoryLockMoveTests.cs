using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerInventoryLockMoveTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task OverlappingLocks_ReleaseIsIndependent_AndLastReleaseUnlocksBucket()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            InventoryLockDto first;
            InventoryLockDto second;
            await using (var db = CreateContext())
            {
                var service = CreateLockService(db, fixture.UserId);
                first = await service.CreateAsync(new()
                {
                    LockType = nameof(InventoryLockType.ManualOperationalLock),
                    WarehouseId = fixture.WarehouseId,
                    ProductId = fixture.ProductId,
                    Reason = "manual freeze"
                });
                second = await service.CreateAsync(new()
                {
                    LockType = nameof(InventoryLockType.QualityHold),
                    WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.SourceLocationId,
                    ProductId = fixture.ProductId,
                    InventoryStatus = "AVAILABLE",
                    LotId = fixture.LotId,
                    Reason = "quality investigation"
                });
            }

            await using (var releaseFirst = CreateContext())
            {
                var service = CreateLockService(releaseFirst, fixture.UserId);
                await service.ReleaseAsync(first.Id, new()
                {
                    Reason = "manual freeze cleared",
                    RowVersion = first.RowVersion
                });
            }

            await using (var stillLocked = CreateContext())
            {
                var service = CreateLockService(stillLocked, fixture.UserId);
                await FluentActions.Awaiting(() => service.EnsureBucketUnlockedAsync(
                        fixture.WarehouseId,
                        fixture.SourceLocationId,
                        fixture.ProductId,
                        InventoryStatus.Available,
                        fixture.LotId,
                        null))
                    .Should().ThrowAsync<BusinessRuleException>()
                    .Where(ex => Equals(ex.Data["ErrorCode"], "INV_STOCK_LOCKED"));
            }

            await using (var releaseSecond = CreateContext())
            {
                var service = CreateLockService(releaseSecond, fixture.UserId);
                var current = await service.GetAsync(second.Id);
                await service.ReleaseAsync(second.Id, new()
                {
                    Reason = "quality hold released",
                    RowVersion = current.RowVersion
                });
            }

            await using (var unlocked = CreateContext())
                await CreateLockService(unlocked, fixture.UserId).EnsureBucketUnlockedAsync(
                    fixture.WarehouseId,
                    fixture.SourceLocationId,
                    fixture.ProductId,
                    InventoryStatus.Available,
                    fixture.LotId,
                    null);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ActiveLock_BlocksReservationStatusChangeAndInternalMove()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using (var lockDb = CreateContext())
            {
                await CreateLockService(lockDb, fixture.UserId).CreateAsync(new()
                {
                    LockType = nameof(InventoryLockType.QualityHold),
                    WarehouseId = fixture.WarehouseId,
                    ProductId = fixture.ProductId,
                    Reason = "quality hold"
                });
            }

            await using (var reserveDb = CreateContext())
            {
                var current = new CurrentUser(fixture.UserId);
                var service = new StockReservationService(
                    reserveDb,
                    new InventoryStockRepository(reserveDb),
                    new UnitOfWork(reserveDb),
                    new WarehouseAuthorizationService(reserveDb, current),
                    current,
                    new StockReservationOptions());
                await FluentActions.Awaiting(() => service.CreateAsync(new()
                    {
                        ProductId = fixture.ProductId,
                        WarehouseId = fixture.WarehouseId,
                        Quantity = 1
                    }))
                    .Should().ThrowAsync<ConcurrencyException>();
            }

            await using (var statusDb = CreateContext())
            {
                var current = new CurrentUser(fixture.UserId);
                var locks = CreateLockService(statusDb, fixture.UserId);
                var service = new InventoryStatusService(
                    statusDb,
                    new WarehouseAuthorizationService(statusDb, current),
                    current,
                    locks);
                var act = () => service.ChangeAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    Quantity = 1,
                    ToStatus = "QC_HOLD",
                    Reason = "must be blocked"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_STOCK_LOCKED");
            }

            await using (var moveDb = CreateContext())
            {
                var service = CreateMovementService(moveDb, fixture.UserId);
                var act = () => service.MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 1,
                    Reason = "must be blocked"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_STOCK_LOCKED");
            }

            await using var verify = CreateContext();
            var source = await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.Id == fixture.SourceStockId);
            source.Quantity.Should().Be(10);
            source.ReservedQuantity.Should().Be(0);
            (await verify.StockReservations.CountAsync(x => x.ProductId == fixture.ProductId)).Should().Be(0);
            (await verify.InventoryLocationMovements.CountAsync(x => x.ReferenceType == "InventoryMove")).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task InternalMove_DestinationScopedLock_BlocksWithoutMutation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using (var lockDb = CreateContext())
            {
                await CreateLockService(lockDb, fixture.UserId).CreateAsync(new()
                {
                    LockType = nameof(InventoryLockType.ManualOperationalLock),
                    WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.DestinationLocationId,
                    ProductId = fixture.ProductId,
                    Reason = "destination freeze"
                });
            }

            await using (var moveDb = CreateContext())
            {
                var service = CreateMovementService(moveDb, fixture.UserId);
                var act = () => service.MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 1,
                    Reason = "must respect destination lock"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_STOCK_LOCKED");
            }

            await using var verify = CreateContext();
            (await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.Id == fixture.SourceStockId))
                .Quantity.Should().Be(10);
            (await verify.InventoryLocationMovements.CountAsync(x => x.ReferenceType == "InventoryMove")).Should().Be(0);
            (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "InventoryMove")).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task InternalMove_DestinationCapacityExceeded_BlocksWithoutMutation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using (var setup = CreateContext())
            {
                await setup.Products.Where(x => x.Id == fixture.ProductId)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.UnitWeightKg, 2m));
                await setup.WarehouseLocations.Where(x => x.Id == fixture.DestinationLocationId)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.MaxWeightKg, 5m));
            }

            await using (var moveDb = CreateContext())
            {
                var service = CreateMovementService(moveDb, fixture.UserId);
                var act = () => service.MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 3,
                    Reason = "would exceed destination capacity"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_BUCKET_CONFLICT");
                thrown.Which.Message.Should().Contain("capacity");
            }

            await using var verify = CreateContext();
            (await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.Id == fixture.SourceStockId))
                .Quantity.Should().Be(10);
            (await verify.InventoryLocationMovements.CountAsync(x => x.ReferenceType == "InventoryMove")).Should().Be(0);
            (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "InventoryMove")).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task InternalMove_PreservesWarehouseOnHandStatusAndLot_AndWritesTraceableLedger()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            InventoryMoveResultDto moved;
            await using (var db = CreateContext())
            {
                moved = await CreateMovementService(db, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 4,
                    Reason = "slotting optimization"
                });
            }

            await using var verify = CreateContext();
            var stocks = await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .OrderBy(x => x.LocationId)
                .ToListAsync();
            stocks.Sum(x => x.Quantity).Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.SourceLocationId).Quantity.Should().Be(6);
            var destination = stocks.Single(x => x.LocationId == fixture.DestinationLocationId);
            destination.Quantity.Should().Be(4);
            destination.Status.Should().Be(InventoryStatus.Available);
            destination.LotId.Should().Be(fixture.LotId);
            destination.SerialId.Should().BeNull();

            var movement = await verify.InventoryLocationMovements.AsNoTracking()
                .SingleAsync(x => x.Id == moved.MovementId);
            movement.ReferenceType.Should().Be("InventoryMove");
            movement.ReferenceId.Should().Be(movement.Id);
            movement.FromLocationId.Should().Be(fixture.SourceLocationId);
            movement.ToLocationId.Should().Be(fixture.DestinationLocationId);
            movement.LotId.Should().Be(fixture.LotId);
            movement.BaseQuantity.Should().Be(4);

            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .SingleAsync(x => x.Id == moved.TransactionId);
            ledger.TransactionType.Should().Be(TransactionType.Move);
            ledger.Quantity.Should().Be(4);
            ledger.InventoryStatus.Should().Be(InventoryStatus.Available);
            ledger.FromLocationId.Should().Be(fixture.SourceLocationId);
            ledger.ToLocationId.Should().Be(fixture.DestinationLocationId);
            ledger.LotId.Should().Be(fixture.LotId);
            ledger.ReferenceType.Should().Be("InventoryMove");
            ledger.ReferenceId.Should().Be(movement.Id);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task InternalMove_CannotMoveReservedQuantity()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using (var setup = CreateContext())
                await setup.InventoryStocks.Where(x => x.Id == fixture.SourceStockId)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.ReservedQuantity, 3m));

            await using (var db = CreateContext())
            {
                var service = CreateMovementService(db, fixture.UserId);
                var act = () => service.MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 8,
                    Reason = "reserved stock cannot move"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_INSUFFICIENT_AVAILABLE");
            }

            await using var verify = CreateContext();
            var source = await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.Id == fixture.SourceStockId);
            source.Quantity.Should().Be(10);
            source.ReservedQuantity.Should().Be(3);
            (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "InventoryMove")).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [Fact]
    public void InternalLedgerEvents_AreWarehouseNeutralForAsOfReporting()
    {
        TransactionType.Move.ApplySign(7m).Should().Be(0);
        TransactionType.StatusChange.ApplySign(7m).Should().Be(0);
        TransactionType.Reversal.ApplySign(7m).Should().Be(0);
    }

    [SqlServerFact]
    public async Task InternalMove_ReversalCreatesCorrectiveMoveAndMarker_WithoutChangingWarehouseTotal()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            InventoryMoveResultDto moved;
            await using (var moveDb = CreateContext())
            {
                moved = await CreateMovementService(moveDb, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 4,
                    Reason = "reversal fixture move"
                });
            }

            InventoryReversalResultDto reversed;
            await using (var reversalDb = CreateContext())
            {
                reversed = await CreateReversalService(reversalDb, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = moved.TransactionId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "operator correction"
                });
            }

            await using var verify = CreateContext();
            var stocks = await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .ToListAsync();
            stocks.Sum(x => x.Quantity).Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.SourceLocationId).Quantity.Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.DestinationLocationId).Quantity.Should().Be(0);

            var correction = await verify.InventoryTransactions.AsNoTracking()
                .SingleAsync(x => x.Id == reversed.CorrectiveTransactionId);
            correction.TransactionType.Should().Be(TransactionType.Move);
            correction.FromLocationId.Should().Be(fixture.DestinationLocationId);
            correction.ToLocationId.Should().Be(fixture.SourceLocationId);
            correction.Quantity.Should().Be(4);

            var marker = await verify.InventoryTransactions.AsNoTracking()
                .SingleAsync(x => x.Id == reversed.ReversalTransactionId);
            marker.TransactionType.Should().Be(TransactionType.Reversal);
            marker.ReferenceType.Should().Be("InventoryReversal");
            marker.ReferenceId.Should().Be(moved.TransactionId);
            marker.ReversalOfTransactionId.Should().Be(moved.TransactionId);
            marker.CorrectiveTransactionId.Should().Be(reversed.CorrectiveTransactionId);
            marker.ReasonCode.Should().Be("OPERATION_CORRECTION");
            reversed.ReasonCode.Should().Be("OPERATION_CORRECTION");
            marker.Note.Should().Be("operator correction");
            marker.Quantity.Should().Be(4);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task DownstreamMovement_InsufficientStockRejectsReversalAndRollsBackMarker()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int originalId;
            await using (var firstMoveDb = CreateContext())
            {
                var first = await CreateMovementService(firstMoveDb, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 3,
                    Reason = "Giao dịch gốc được hiệu chỉnh sau này"
                });
                originalId = first.TransactionId;
            }

            await using (var downstreamDb = CreateContext())
            {
                var bucket = await downstreamDb.InventoryStocks
                    .SingleAsync(x => x.ProductId == fixture.ProductId &&
                                      x.WarehouseId == fixture.WarehouseId &&
                                      x.LocationId == fixture.DestinationLocationId);
                await CreateMovementService(downstreamDb, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = bucket.Id,
                    DestinationLocationId = fixture.SourceLocationId,
                    Quantity = 2,
                    Reason = "Hàng đã được di chuyển sau giao dịch gốc"
                });
            }

            await using (var reversalDb = CreateContext())
            {
                var act = () => CreateReversalService(reversalDb, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = originalId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "Không được đảo vượt số dư thực tế"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_REVERSAL_INSUFFICIENT_STOCK");
            }

            await using var verify = CreateContext();
            (await verify.InventoryTransactions.CountAsync(x =>
                x.ProductId == fixture.ProductId &&
                x.TransactionType == TransactionType.Reversal)).Should().Be(0);
            (await verify.InventoryTransactions.CountAsync(x =>
                x.ProductId == fixture.ProductId &&
                x.TransactionType == TransactionType.Move)).Should().Be(2);
            (await verify.AuditLogs.CountAsync(x =>
                x.UserId == fixture.UserId && x.Action == "Inventory.Reversed")).Should().Be(0);
            var stocks = await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .ToListAsync();
            stocks.Sum(x => x.Quantity).Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.SourceLocationId).Quantity.Should().Be(9);
            stocks.Single(x => x.LocationId == fixture.DestinationLocationId).Quantity.Should().Be(1);

            var candidate = await CreateReversalService(verify, fixture.UserId)
                .GetCandidatesAsync(fixture.WarehouseId, 1, 20, originalId);
            candidate.Items.Should().ContainSingle().Which.IsReversed.Should().BeFalse();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task InventoryReversal_RejectsSecondReversalOfSameOriginalTransaction()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            InventoryMoveResultDto moved;
            await using (var moveDb = CreateContext())
            {
                moved = await CreateMovementService(moveDb, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 2,
                    Reason = "double reversal fixture"
                });
            }

            await using (var firstDb = CreateContext())
                await CreateReversalService(firstDb, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = moved.TransactionId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "first correction"
                });

            await using (var secondDb = CreateContext())
            {
                var act = () => CreateReversalService(secondDb, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = moved.TransactionId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "must be blocked"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_ALREADY_REVERSED");
            }

            await using var verify = CreateContext();
            (await verify.InventoryTransactions.CountAsync(x =>
                x.TransactionType == TransactionType.Reversal &&
                x.ReversalOfTransactionId == moved.TransactionId)).Should().Be(1);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task Traceability_ReturnsCurrentBucketsTimelineAndReversalLinks()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            InventoryMoveResultDto moved;
            await using (var moveDb = CreateContext())
            {
                moved = await CreateMovementService(moveDb, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 3,
                    Reason = "trace fixture"
                });
            }

            InventoryReversalResultDto reversal;
            await using (var reversalDb = CreateContext())
                reversal = await CreateReversalService(reversalDb, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = moved.TransactionId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "trace reversal"
                });

            await using var traceDb = CreateContext();
            var current = new CurrentUser(fixture.UserId);
            var result = await new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                traceDb,
                new WarehouseAuthorizationService(traceDb, current))
                .TraceAsync(productId: fixture.ProductId, limit: 50);

            result.CurrentBuckets.Sum(x => x.OnHandQuantity).Should().Be(10);
            var original = result.Events.Single(x => x.TransactionId == moved.TransactionId);
            original.TransactionType.Should().Be(nameof(TransactionType.Move));
            original.IsReversed.Should().BeTrue();
            var marker = result.Events.Single(x => x.TransactionId == reversal.ReversalTransactionId);
            marker.TransactionType.Should().Be(nameof(TransactionType.Reversal));
            marker.ReversalOfTransactionId.Should().Be(moved.TransactionId);
            marker.CorrectiveTransactionId.Should().Be(reversal.CorrectiveTransactionId);
            marker.ReversalTransactionId.Should().Be(reversal.ReversalTransactionId);
            original.ReversalTransactionId.Should().Be(reversal.ReversalTransactionId);
            original.CorrectiveTransactionId.Should().Be(reversal.CorrectiveTransactionId);
            result.Events.Single(x => x.TransactionId == reversal.CorrectiveTransactionId)
                .ReversalTransactionId.Should().Be(reversal.ReversalTransactionId);
            result.Events.Count(x => x.TransactionType == nameof(TransactionType.Move)).Should().Be(2);
        }
        finally { await CleanupAsync(fixture); }
    }


    [SqlServerFact]
    public async Task InventoryReversal_ConcurrentClaims_CreateOneChainAndOneCorrectiveEffect()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            InventoryMoveResultDto moved;
            await using (var moveDb = CreateContext())
            {
                moved = await CreateMovementService(moveDb, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 2,
                    Reason = "concurrent reversal fixture"
                });
            }

            async Task<(InventoryReversalResultDto? Result, Exception? Error)> AttemptAsync(string reason)
            {
                await using var db = CreateContext();
                try
                {
                    var result = await CreateReversalService(db, fixture.UserId).ReverseAsync(new()
                    {
                        OriginalTransactionId = moved.TransactionId,
                        ReasonCode = "OPERATION_CORRECTION",
                        Reason = reason
                    });
                    return (result, null);
                }
                catch (Exception ex)
                {
                    return (null, ex);
                }
            }

            var attempts = await Task.WhenAll(
                AttemptAsync("concurrent correction A"),
                AttemptAsync("concurrent correction B"));

            attempts.Count(x => x.Result is not null).Should().Be(1);
            var failure = attempts.Single(x => x.Error is not null).Error;
            failure.Should().BeOfType<BusinessRuleException>(
                $"both concurrent attempts must yield one success and a controlled conflict, actual error: {failure}");
            failure!.Data["ErrorCode"].Should().Be("INV_ALREADY_REVERSED");

            await using var verify = CreateContext();
            var markers = await verify.InventoryTransactions.AsNoTracking()
                .Where(x => x.TransactionType == TransactionType.Reversal &&
                            x.ReversalOfTransactionId == moved.TransactionId)
                .ToListAsync();
            markers.Should().ContainSingle();
            markers[0].CorrectiveTransactionId.Should().NotBeNull();

            var stocks = await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .ToListAsync();
            stocks.Sum(x => x.Quantity).Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.SourceLocationId).Quantity.Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.DestinationLocationId).Quantity.Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task Traceability_ReferenceSearch_ReturnsSameStructuredReversalChain()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            InventoryMoveResultDto moved;
            await using (var moveDb = CreateContext())
            {
                moved = await CreateMovementService(moveDb, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 3,
                    Reason = "reference trace fixture"
                });
            }

            InventoryReversalResultDto reversal;
            await using (var reversalDb = CreateContext())
                reversal = await CreateReversalService(reversalDb, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = moved.TransactionId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "reference trace reversal"
                });

            await using var readDb = CreateContext();
            var original = await readDb.InventoryTransactions.AsNoTracking()
                .SingleAsync(x => x.Id == moved.TransactionId);
            var corrective = await readDb.InventoryTransactions.AsNoTracking()
                .SingleAsync(x => x.Id == reversal.CorrectiveTransactionId);
            var marker = await readDb.InventoryTransactions.AsNoTracking()
                .SingleAsync(x => x.Id == reversal.ReversalTransactionId);

            async Task<InventoryTraceabilityResultDto> TraceAsync(string referenceType, int referenceId)
            {
                await using var traceDb = CreateContext();
                var current = new CurrentUser(fixture.UserId);
                return await new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                    traceDb,
                    new WarehouseAuthorizationService(traceDb, current))
                    .TraceAsync(
                        referenceType: referenceType,
                        referenceId: referenceId,
                        limit: 20);
            }

            var traces = new[]
            {
                await TraceAsync(original.ReferenceType!, original.ReferenceId!.Value),
                await TraceAsync(corrective.ReferenceType!, corrective.ReferenceId!.Value),
                await TraceAsync(marker.ReferenceType!, marker.ReferenceId!.Value)
            };
            var expectedIds = new[]
            {
                moved.TransactionId,
                reversal.CorrectiveTransactionId,
                reversal.ReversalTransactionId
            };

            foreach (var trace in traces)
            {
                // The anchor must include already-committed corrective/reversal
                // legs even if the direct reference matches only the original.
                trace.EventAnchorId!.Value.Should().BeGreaterThan(expectedIds.Max() - 1);
                trace.Events.Select(x => x.TransactionId).Should().Contain(expectedIds);
                trace.Events.Single(x => x.TransactionId == moved.TransactionId).IsReversed.Should().BeTrue();
                trace.Events.Single(x => x.TransactionId == reversal.ReversalTransactionId)
                    .CorrectiveTransactionId.Should().Be(reversal.CorrectiveTransactionId);
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task CorrectiveLeg_CannotBeReversedAgainOrOfferedAsCandidate()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int originalId;
            await using (var firstMove = CreateContext())
            {
                var move = await CreateMovementService(firstMove, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 3,
                    Reason = "Tạo giao dịch cần hiệu chỉnh"
                });
                originalId = move.TransactionId;
            }

            InventoryReversalResultDto reversed;
            await using (var correction = CreateContext())
                reversed = await CreateReversalService(correction, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = originalId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "Hiệu chỉnh chuyển vị trí"
                });

            await using (var inspect = CreateContext())
            {
                var candidates = await CreateReversalService(inspect, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20);
                candidates.Items.Select(x => x.Id).Should().NotContain(reversed.CorrectiveTransactionId);
                candidates.Items.Should().ContainSingle(x => x.Id == originalId)
                    .Which.IsReversed.Should().BeTrue();
            }

            await using (var denied = CreateContext())
            {
                var action = () => CreateReversalService(denied, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = reversed.CorrectiveTransactionId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "Không được đảo độc lập nhánh hiệu chỉnh"
                });
                var thrown = await action.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_REVERSAL_CORRECTIVE_NOT_ALLOWED");
            }

            await using var verified = CreateContext();
            (await verified.InventoryTransactions.CountAsync(x =>
                x.TransactionType == TransactionType.Reversal &&
                x.ProductId == fixture.ProductId &&
                x.WarehouseId == fixture.WarehouseId)).Should().Be(1);
            var stocks = await verified.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .ToListAsync();
            stocks.Sum(x => x.Quantity).Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.SourceLocationId)
                .Quantity.Should().Be(10);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReversalCandidates_UseSqlServerAuthoritativeMarkerAfterCommittedReversal()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int originalTransactionId;
            await using (var move = CreateContext())
            {
                var result = await CreateMovementService(move, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 2,
                    Reason = "Đối chiếu trạng thái đảo trên SQL Server"
                });
                originalTransactionId = result.TransactionId;
            }

            await using (var before = CreateContext())
            {
                var grantedWarehouses = await CreateReversalService(before, fixture.UserId)
                    .GetReversalWarehousesAsync();
                grantedWarehouses.Should().ContainSingle(x => x.Id == fixture.WarehouseId);
                var productCode = await before.Products.AsNoTracking()
                    .Where(x => x.Id == fixture.ProductId).Select(x => x.Code).SingleAsync();
                var byProduct = await CreateReversalService(before, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20, null, false, productCode[..3]);
                byProduct.Items.Should().ContainSingle(x => x.Id == originalTransactionId);
                var wrongProduct = await CreateReversalService(before, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20, null, false, "ZZ_NO_MATCH");
                wrongProduct.TotalRecords.Should().Be(0);
                var page = await CreateReversalService(before, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20);
                page.Items.Should().ContainSingle(x => x.Id == originalTransactionId)
                    .Which.IsReversed.Should().BeFalse();
                var pending = await CreateReversalService(before, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20, originalTransactionId, false);
                pending.TotalRecords.Should().Be(1);
                pending.Items.Single().IsReversed.Should().BeFalse();
                var notYetReversed = await CreateReversalService(before, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20, originalTransactionId, true);
                notYetReversed.TotalRecords.Should().Be(0);
            }

            await using (var reverse = CreateContext())
            {
                await CreateReversalService(reverse, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = originalTransactionId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "Đã xác minh số lượng và kho nguồn"
                });
            }

            await using (var after = CreateContext())
            {
                var page = await CreateReversalService(after, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20);
                page.Items.Should().ContainSingle(x => x.Id == originalTransactionId)
                    .Which.IsReversed.Should().BeTrue();
                var exact = await CreateReversalService(after, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20, originalTransactionId);
                exact.TotalRecords.Should().Be(1);
                exact.Items.Single().IsReversed.Should().BeTrue();
                var completed = await CreateReversalService(after, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20, originalTransactionId, true);
                completed.TotalRecords.Should().Be(1);
                completed.Items.Single().IsReversed.Should().BeTrue();
                var pending = await CreateReversalService(after, fixture.UserId)
                    .GetCandidatesAsync(fixture.WarehouseId, 1, 20, originalTransactionId, false);
                pending.TotalRecords.Should().Be(0);
                (await after.InventoryTransactions.CountAsync(x =>
                    x.ReversalOfTransactionId == originalTransactionId)).Should().Be(1);

                var stocks = await after.InventoryStocks.AsNoTracking()
                    .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                    .ToListAsync();
                stocks.Sum(x => x.Quantity).Should().Be(10);
                stocks.Single(x => x.LocationId == fixture.SourceLocationId)
                    .Quantity.Should().Be(10);
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task LegacyReferenceOnlyReversal_IsReportedAndCannotBeExecutedTwice()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int originalId;
            await using (var moveDb = CreateContext())
                originalId = (await CreateMovementService(moveDb, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 2,
                    Reason = "legacy reversal claim fixture"
                })).TransactionId;

            int markerId;
            await using (var seed = CreateContext())
            {
                var marker = new InventoryTransaction
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.DestinationLocationId,
                    LotId = fixture.LotId,
                    CreatedBy = fixture.UserId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Reversal,
                    Quantity = 2,
                    ReferenceType = "InventoryReversal",
                    ReferenceId = originalId,
                    ReversalOfTransactionId = null,
                    TransactionDate = DateTime.UtcNow
                };
                seed.InventoryTransactions.Add(marker);
                await seed.SaveChangesAsync();
                markerId = marker.Id;
            }

            await using (var queryDb = CreateContext())
            {
                var service = CreateReversalService(queryDb, fixture.UserId);
                var completed = await service.GetCandidatesAsync(
                    fixture.WarehouseId, 1, 20, originalId, true);
                completed.Items.Should().ContainSingle()
                    .Which.IsReversed.Should().BeTrue();
                (await service.GetCandidatesAsync(
                    fixture.WarehouseId, 1, 20, originalId, false))
                    .TotalRecords.Should().Be(0);

                var trace = await new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                    queryDb, new WarehouseAuthorizationService(queryDb, new CurrentUser(fixture.UserId)))
                    .TraceAsync(productId: fixture.ProductId, limit: 20);
                trace.Events.Single(x => x.TransactionId == originalId)
                    .ReversalTransactionId.Should().Be(markerId);
                trace.Events.Single(x => x.TransactionId == originalId)
                    .IsReversed.Should().BeTrue();
            }

            await using (var rejected = CreateContext())
            {
                var action = () => CreateReversalService(rejected, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = originalId,
                    ReasonCode = "OPERATION_CORRECTION",
                    Reason = "must reject legacy duplicate"
                });
                var error = await action.Should().ThrowAsync<BusinessRuleException>();
                error.Which.Data["ErrorCode"].Should().Be("INV_ALREADY_REVERSED");
            }

            await using var verify = CreateContext();
            (await verify.InventoryTransactions.CountAsync(x =>
                x.TransactionType == TransactionType.Reversal &&
                x.ReferenceType == "InventoryReversal" &&
                x.ReferenceId == originalId)).Should().Be(1);
            var stocks = await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .ToListAsync();
            stocks.Sum(x => x.Quantity).Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.DestinationLocationId)
                .Quantity.Should().Be(2);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task Traceability_ReferenceStockMatchIsAppliedBeforeFiveHundredBucketCap()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            const int count = 501;
            int targetLocationId;
            await using (var seed = CreateContext())
            {
                var locations = Enumerable.Range(0, count)
                    .Select(i => new WarehouseLocation
                    {
                        WarehouseId = fixture.WarehouseId,
                        Code = $"REFS{i:D4}{Guid.NewGuid():N}".Substring(0, 20),
                        Name = $"Vị trí tham chiếu {i:D4}",
                        LocationType = WarehouseLocationType.Legacy,
                        IsActive = true,
                        CreatedBy = fixture.UserId
                    }).ToList();
                seed.WarehouseLocations.AddRange(locations);
                await seed.SaveChangesAsync();

                targetLocationId = locations[^1].Id;
                var buckets = locations.Select((location, index) => new InventoryStock
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    LocationId = location.Id,
                    // 500 preceding unrelated location buckets share the product
                    // but not the traceable lot/serial identity.
                    LotId = index == count - 1 ? fixture.LotId : null,
                    Status = InventoryStatus.Available,
                    Quantity = 1
                }).ToList();
                seed.InventoryStocks.AddRange(buckets);
                seed.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    LocationId = targetLocationId,
                    LotId = fixture.LotId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Move,
                    Quantity = 1,
                    ReferenceType = "ReferenceBucketLimit",
                    ReferenceId = 319,
                    CreatedBy = fixture.UserId,
                    TransactionDate = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }

            await using var db = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                db, new WarehouseAuthorizationService(db, new CurrentUser(fixture.UserId)));
            var trace = await query.TraceAsync(
                referenceType: "ReferenceBucketLimit", referenceId: 319, limit: 20);

            trace.Events.Should().ContainSingle();
            // The target location sorts after >500 irrelevant product buckets.
            // Product-only Take(500) followed by in-memory lot filtering
            // silently drops it. Identity filtering inside SQL must retain it.
            trace.CurrentBuckets.Should().ContainSingle(x =>
                x.LocationId == targetLocationId && x.LotId == fixture.LotId);
            trace.CurrentBuckets.Should().OnlyContain(x =>
                x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId &&
                x.LotId == fixture.LotId);
            trace.BucketsTruncated.Should().BeFalse();

            // Product-only browsing includes all 501 seeded locations. It must
            // warn that more stock buckets exist beyond the 500-row response cap.
            var allBuckets = await query.TraceAsync(productId: fixture.ProductId, limit: 20);
            allBuckets.CurrentBuckets.Should().HaveCount(500);
            allBuckets.BucketsTruncated.Should().BeTrue();
            // A warehouse overview covers all current bucket identities and
            // is intentionally NOT restricted to recent ledger event IDs.
            var byWarehouse = await query.TraceAsync(
                warehouseId: fixture.WarehouseId, limit: 20);
            byWarehouse.CurrentBuckets.Should().HaveCount(500);
            byWarehouse.BucketsTruncated.Should().BeTrue();
            byWarehouse.CurrentBuckets.Should().OnlyContain(x =>
                x.WarehouseId == fixture.WarehouseId);
            var secondPage = await query.TraceAsync(
                warehouseId: fixture.WarehouseId, limit: 20, bucketOffset: 500);
            // The fixture has one stock bucket plus 501 seeded buckets:
            // page two contains exactly the two buckets after the first 500.
            secondPage.CurrentBuckets.Should().HaveCount(2);
            secondPage.BucketsTruncated.Should().BeFalse();
            secondPage.CurrentBuckets.Should().Contain(x => x.LocationId == targetLocationId);
            var firstPageIds = byWarehouse.CurrentBuckets.Select(x => x.InventoryStockId).ToHashSet();
            secondPage.CurrentBuckets.Should().OnlyContain(x =>
                x.WarehouseId == fixture.WarehouseId && !firstPageIds.Contains(x.InventoryStockId));

            // A nonexistent document must NOT broaden the stock query to all
            // current buckets merely because the warehouse is valid.
            var unknownReference = await query.TraceAsync(
                warehouseId: fixture.WarehouseId,
                referenceType: "ReferenceBucketLimit", referenceId: 999, limit: 20);
            unknownReference.Events.Should().BeEmpty();
            unknownReference.CurrentBuckets.Should().BeEmpty();
            unknownReference.BucketsTruncated.Should().BeFalse();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task Traceability_ReferenceIdentityUsesFullHistoryAndIntersectsAllFilters()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            const int referenceId = 713;
            await using (var seed = CreateContext())
            {
                seed.InventoryStocks.Add(new InventoryStock
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.DestinationLocationId,
                    Status = InventoryStatus.Available,
                    Quantity = 6m,
                    ReservedQuantity = 0m
                });

                var start = DateTime.UtcNow.AddHours(-2);
                for (var i = 0; i < 26; i++)
                    seed.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = fixture.ProductId,
                        WarehouseId = fixture.WarehouseId,
                        LocationId = i == 0 ? fixture.SourceLocationId : fixture.DestinationLocationId,
                        LotId = i == 0 ? fixture.LotId : null,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Move,
                        Quantity = 1m,
                        ReferenceType = "TraceIdentityWindow",
                        ReferenceId = referenceId,
                        CreatedBy = fixture.UserId,
                        TransactionDate = start.AddMinutes(i)
                    });
                await seed.SaveChangesAsync();
            }

            await using var db = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                db, new WarehouseAuthorizationService(db, new CurrentUser(fixture.UserId)));
            var recent = await query.TraceAsync(
                referenceType: "TraceIdentityWindow", referenceId: referenceId, limit: 20);

            recent.Events.Should().HaveCount(20);
            recent.EventsTruncated.Should().BeTrue();
            recent.Events.Should().NotContain(x => x.LotId == fixture.LotId);
            // The older lot-bearing document line must still identify its
            // current bucket even though it is outside the event window.
            recent.CurrentBuckets.Should().HaveCount(2);
            recent.CurrentBuckets.Should().ContainSingle(x =>
                x.LotId == fixture.LotId && x.LocationId == fixture.SourceLocationId);
            recent.CurrentBuckets.Should().ContainSingle(x =>
                x.LotId == null && x.LocationId == fixture.DestinationLocationId);

            var combined = await query.TraceAsync(
                warehouseId: fixture.WarehouseId, productId: fixture.ProductId,
                referenceType: "TraceIdentityWindow", referenceId: referenceId, limit: 20);
            combined.CurrentBuckets.Should().HaveCount(2);

            var unknown = await query.TraceAsync(
                warehouseId: fixture.WarehouseId, productId: fixture.ProductId,
                referenceType: "TraceIdentityWindow", referenceId: referenceId + 1, limit: 20);
            unknown.Events.Should().BeEmpty();
            unknown.CurrentBuckets.Should().BeEmpty();

            var lotNumber = await db.InventoryLots.Where(x => x.Id == fixture.LotId)
                .Select(x => x.LotNumber).SingleAsync();
            var lotOnly = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber,
                referenceType: "TraceIdentityWindow", referenceId: referenceId, limit: 20);
            lotOnly.CurrentBuckets.Should().ContainSingle(x => x.LotId == fixture.LotId);

            var wrongLotReference = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber,
                referenceType: "TraceIdentityWindow", referenceId: referenceId + 1, limit: 20);
            wrongLotReference.CurrentBuckets.Should().BeEmpty();
        }
        finally { await CleanupAsync(fixture); }
    }


    [SqlServerFact]
    public async Task Traceability_RelatedDocuments_UsesTrackedIdentityAuthorizedWarehousesAndLedgerAnchor()
    {
        var fixture = await CreateFixtureAsync();
        var otherWarehouseId = 0;
        try
        {
            var lotNumber = string.Empty;
            await using (var seed = CreateContext())
            {
                lotNumber = await seed.InventoryLots.Where(x => x.Id == fixture.LotId)
                    .Select(x => x.LotNumber).SingleAsync();
                var hiddenWarehouse = new Warehouse
                {
                    Code = "LH" + Guid.NewGuid().ToString("N")[..10],
                    Name = "Kho không có quyền xem"
                };
                seed.Warehouses.Add(hiddenWarehouse);
                await seed.SaveChangesAsync();
                otherWarehouseId = hiddenWarehouse.Id;

                var start = DateTime.UtcNow.AddDays(-4);
                void Add(int warehouseId, int? lotId, string reference, int referenceId, int minute)
                {
                    seed.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = fixture.ProductId, WarehouseId = warehouseId,
                        LocationId = warehouseId == fixture.WarehouseId ? fixture.SourceLocationId : null,
                        LotId = lotId, CreatedBy = fixture.UserId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Move, Quantity = 1,
                        ReferenceType = reference, ReferenceId = referenceId,
                        TransactionDate = start.AddMinutes(minute)
                    });
                }

                Add(fixture.WarehouseId, fixture.LotId, "GoodsReceipt", 21, 0);
                Add(fixture.WarehouseId, fixture.LotId, "GoodsReceipt", 21, 1);
                Add(fixture.WarehouseId, fixture.LotId, "InboundQC", 22, 2);
                Add(fixture.WarehouseId, fixture.LotId, "Shipment", 23, 3);
                Add(fixture.WarehouseId, null, "UntrackedDifferentLot", 24, 4);
                Add(otherWarehouseId, fixture.LotId, "HiddenShipment", 25, 5);
                await seed.SaveChangesAsync();
            }

            await using var read = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                read, new WarehouseAuthorizationService(read, new CurrentUser(fixture.UserId)));
            var first = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber, limit: 20);

            first.RelatedDocumentsTruncated.Should().BeFalse();
            first.RelatedDocuments.Should().HaveCount(3);
            first.RelatedDocuments.Should().OnlyContain(x =>
                x.WarehouseId == fixture.WarehouseId && x.WarehouseName != "");
            first.RelatedDocuments.Should().NotContain(x => x.ReferenceType == "HiddenShipment");
            first.RelatedDocuments.Should().NotContain(x => x.ReferenceType == "UntrackedDifferentLot");
            first.RelatedDocuments.Single(x => x.ReferenceType == "GoodsReceipt" && x.ReferenceId == 21)
                .EventCount.Should().Be(2);
            first.RelatedDocuments.Select(x => x.LastTransactionId).Should().BeInDescendingOrder();
            first.EventAnchorId.Should().BeGreaterThan(0);

            await using (var later = CreateContext())
            {
                later.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.SourceLocationId, LotId = fixture.LotId,
                    CreatedBy = fixture.UserId, InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Move, Quantity = 1,
                    ReferenceType = "Return", ReferenceId = 26,
                    TransactionDate = DateTime.UtcNow.AddYears(-1)
                });
                await later.SaveChangesAsync();
            }

            var sameAnchor = await query.TraceAsync(productId: fixture.ProductId,
                lotNumber: lotNumber, limit: 20, eventAnchorId: first.EventAnchorId);
            sameAnchor.RelatedDocuments.Select(x => x.ReferenceType)
                .Should().NotContain("Return");
            sameAnchor.RelatedDocuments.Select(x => x.LastTransactionId)
                .Should().Equal(first.RelatedDocuments.Select(x => x.LastTransactionId));

            var fresh = await query.TraceAsync(productId: fixture.ProductId,
                lotNumber: lotNumber, limit: 20);
            fresh.RelatedDocuments.Should().ContainSingle(x =>
                x.ReferenceType == "Return" && x.ReferenceId == 26);
            fresh.EventAnchorId.Should().BeGreaterThan(first.EventAnchorId!.Value);

            var documentFiltered = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber,
                referenceType: "GoodsReceipt", referenceId: 21, limit: 20);
            documentFiltered.RelatedDocuments.Should().BeEmpty();
            var missingLot = await query.TraceAsync(
                productId: fixture.ProductId, lotNumber: lotNumber + "-missing", limit: 20);
            missingLot.RelatedDocuments.Should().BeEmpty();
            // A tracked identity without an explicit product does not fan out
            // into an unbounded cross-product document query.
            var lotWithoutProduct = await query.TraceAsync(lotNumber: lotNumber, limit: 20);
            lotWithoutProduct.RelatedDocuments.Should().BeEmpty();
        }
        finally
        {
            if (otherWarehouseId > 0)
            {
                await using var cleanupOther = CreateContext();
                await cleanupOther.InventoryTransactions.Where(x =>
                    x.WarehouseId == otherWarehouseId).ExecuteDeleteAsync();
                await cleanupOther.Warehouses.Where(x => x.Id == otherWarehouseId)
                    .ExecuteDeleteAsync();
            }
            await CleanupAsync(fixture);
        }
    }

    [SqlServerFact]
    public async Task Traceability_RelatedDocuments_CapsAt100AndReportsTruncation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var lotNumber = string.Empty;
            await using (var seed = CreateContext())
            {
                lotNumber = await seed.InventoryLots.Where(x => x.Id == fixture.LotId)
                    .Select(x => x.LotNumber).SingleAsync();
                var start = DateTime.UtcNow.AddDays(-2);
                for (var i = 0; i < 103; i++)
                    seed.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId,
                        LocationId = fixture.SourceLocationId, LotId = fixture.LotId,
                        CreatedBy = fixture.UserId, InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Move, Quantity = 1,
                        ReferenceType = "TrackedDocument", ReferenceId = i + 1,
                        TransactionDate = start.AddMinutes(i)
                    });
                await seed.SaveChangesAsync();
            }
            await using var read = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                read, new WarehouseAuthorizationService(read, new CurrentUser(fixture.UserId)));
            var result = await query.TraceAsync(warehouseId: fixture.WarehouseId,
                productId: fixture.ProductId, lotNumber: lotNumber, limit: 20);
            result.RelatedDocuments.Should().HaveCount(100);
            result.RelatedDocumentsTruncated.Should().BeTrue();
            result.RelatedDocuments.Should().OnlyContain(x =>
                x.WarehouseId == fixture.WarehouseId &&
                x.ReferenceType == "TrackedDocument" && x.EventCount == 1);
            result.RelatedDocuments.Select(x => x.ReferenceId)
                .Should().Contain(103).And.NotContain(1);
        }
        finally { await CleanupAsync(fixture); }
    }


    [SqlServerFact]
    public async Task Reconciliation_WarehouseScope_RejectsUnassignedPairsAndWarehouseSelector()
    {
        var fixture = await CreateFixtureAsync();
        var hiddenId = 0;
        try
        {
            await using (var seed = CreateContext())
            {
                var hidden = new Warehouse
                {
                    Code = "RECON-" + Guid.NewGuid().ToString("N")[..10],
                    Name = "Kho ngoài phân quyền"
                };
                seed.Warehouses.Add(hidden);
                await seed.SaveChangesAsync();
                hiddenId = hidden.Id;
                seed.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = hiddenId,
                    CreatedBy = fixture.UserId,
                    TransactionType = TransactionType.Import,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = 7m,
                    TransactionDate = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }

            await using var read = CreateContext();
            var service = new ERP.Infrastructure.Queries.InventoryReconciliationQueryService(
                read, new WarehouseAuthorizationService(read, new CurrentUser(fixture.UserId)));
            var options = await service.GetAccessibleWarehousesAsync();
            options.Should().Contain(x => x.Id == fixture.WarehouseId);
            options.Should().NotContain(x => x.Id == hiddenId);

            var unfiltered = await service.GetReconciliationsAsync(
                null, fixture.ProductId, null);
            unfiltered.Items.Should().Contain(x => x.WarehouseId == fixture.WarehouseId);
            unfiltered.Items.Should().OnlyContain(x => x.WarehouseId != hiddenId);
            unfiltered.Items.Should().NotContain(x => x.WarehouseName == "Kho ngoài phân quyền");

            var explicitAllowed = await service.GetReconciliationsAsync(
                fixture.WarehouseId, fixture.ProductId, null);
            explicitAllowed.Items.Should().ContainSingle(x =>
                x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);

            var forbidden = () => service.GetReconciliationsAsync(hiddenId, fixture.ProductId, null);
            await forbidden.Should().ThrowAsync<NotFoundException>();

            // Nonexistent warehouses must also fail closed, including when
            // the caller has broad warehouse privileges.
            var unknown = () => service.GetReconciliationsAsync(int.MaxValue, fixture.ProductId, null);
            await unknown.Should().ThrowAsync<NotFoundException>();
        }
        finally
        {
            if (hiddenId > 0)
            {
                await using var clear = CreateContext();
                await clear.InventoryTransactions
                    .Where(x => x.WarehouseId == hiddenId).ExecuteDeleteAsync();
                await clear.Warehouses.Where(x => x.Id == hiddenId).ExecuteDeleteAsync();
            }
            await CleanupAsync(fixture);
        }
    }



    [SqlServerFact]
    public async Task ReconciliationInvestigation_StatusChange_TracksAllStatusesWithoutDoubleCounting()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using (var seed = CreateContext())
            {
                seed.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.SourceLocationId,
                    LotId = fixture.LotId,
                    CreatedBy = fixture.UserId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Import,
                    Quantity = 10m, TransactionDate = DateTime.UtcNow
                });
                await seed.SaveChangesAsync();
            }
            // Exercise the real production status mutation and immutable
            // StatusChange ledger, rather than synthesizing both projection
            // rows in the test.
            await using (var changeDb = CreateContext())
            {
                var user = new CurrentUser(fixture.UserId);
                var service = new InventoryStatusService(changeDb,
                    new WarehouseAuthorizationService(changeDb, user), user);
                await service.ChangeAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId, Quantity = 3m,
                    ToStatus = "QC_HOLD", Reason = "INV-11 kiểm tra dòng trạng thái"
                });
            }

            await using var read = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryReconciliationQueryService(
                read, new WarehouseAuthorizationService(read, new CurrentUser(fixture.UserId)));
            var evidence = await query.GetInvestigationAsync(fixture.WarehouseId, fixture.ProductId);
            evidence.StatusBreakdown.Should().HaveCount(8);
            evidence.UnclassifiedLedgerEventCount.Should().Be(0);
            evidence.AllStatusCurrentQuantity.Should().Be(10m);
            evidence.AllStatusReservedQuantity.Should().Be(0m);
            evidence.AllStatusExpectedQuantity.Should().Be(10m);
            evidence.AllStatusDifference.Should().Be(0m);
            var available = evidence.StatusBreakdown.Single(s =>
                s.Status == nameof(InventoryStatus.Available));
            available.CurrentQuantity.Should().Be(7m);
            available.DirectLedgerNetQuantity.Should().Be(10m);
            available.StatusChangeOutQuantity.Should().Be(3m);
            available.ExpectedQuantity.Should().Be(7m);
            available.Difference.Should().Be(0m);
            var qc = evidence.StatusBreakdown.Single(s =>
                s.Status == nameof(InventoryStatus.QcHold));
            qc.CurrentQuantity.Should().Be(3m);
            qc.BucketCount.Should().Be(1);
            qc.StatusChangeInQuantity.Should().Be(3m);
            qc.ExpectedQuantity.Should().Be(3m);
            qc.Difference.Should().Be(0m);
            evidence.EventCount.Should().Be(1);
            evidence.Events.Should().ContainSingle(x => x.TransactionType == "Import");
            var anchoredId = evidence.EventAnchorId;

            // Unknown historic type with no canonical sign cannot silently
            // count as zero and yield a false "matched" status report.
            await using (var legacy = CreateContext())
            {
                legacy.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.SourceLocationId, LotId = fixture.LotId,
                    CreatedBy = fixture.UserId, InventoryStatus = InventoryStatus.QcHold,
                    TransactionType = TransactionType.TransferAdjustment,
                    Quantity = 2m, TransactionDate = DateTime.UtcNow.AddYears(-5)
                });
                await legacy.SaveChangesAsync();
            }
            var oldAnchor = await query.GetInvestigationAsync(
                fixture.WarehouseId, fixture.ProductId, anchoredId);
            oldAnchor.UnclassifiedLedgerEventCount.Should().Be(0);
            oldAnchor.AllStatusExpectedQuantity.Should().Be(10m);
            oldAnchor.LedgerHasEventsAfterAnchor.Should().BeTrue();

            var updated = await query.GetInvestigationAsync(fixture.WarehouseId, fixture.ProductId);
            updated.UnclassifiedLedgerEventCount.Should().Be(1);
            updated.AllStatusExpectedQuantity.Should().BeNull();
            updated.AllStatusDifference.Should().BeNull();
            updated.StatusBreakdown.Should().OnlyContain(s =>
                s.ExpectedQuantity == null && s.Difference == null);
            updated.AllStatusCurrentQuantity.Should().Be(10m);
            updated.EventAnchorId.Should().BeGreaterThan(anchoredId);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReconciliationInvestigation_RealLedgerBucketEvidence_IsAuthorizedBoundedAndAnchored()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using (var seed = CreateContext())
            {
                seed.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = fixture.ProductId,
                        WarehouseId = fixture.WarehouseId,
                        LocationId = fixture.SourceLocationId,
                        LotId = fixture.LotId,
                        CreatedBy = fixture.UserId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Import,
                        Quantity = 8,
                        TransactionDate = DateTime.UtcNow
                    },
                    new InventoryTransaction
                    {
                        ProductId = fixture.ProductId,
                        WarehouseId = fixture.WarehouseId,
                        LocationId = fixture.SourceLocationId,
                        LotId = fixture.LotId,
                        CreatedBy = fixture.UserId,
                        InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.AdjustmentIncrease,
                        Quantity = 4,
                        TransactionDate = DateTime.UtcNow
                    });
                await seed.SaveChangesAsync();
            }

            await using var read = CreateContext();
            var service = new ERP.Infrastructure.Queries.InventoryReconciliationQueryService(
                read, new WarehouseAuthorizationService(read, new CurrentUser(fixture.UserId)));
            var first = await service.GetInvestigationAsync(fixture.WarehouseId, fixture.ProductId, limit: 1);
            first.IsReadOnly.Should().BeTrue();
            first.CurrentQuantity.Should().Be(10);
            first.ExpectedQuantity.Should().Be(12);
            first.Difference.Should().Be(-2);
            first.BucketCount.Should().Be(1);
            first.Buckets.Should().ContainSingle(x =>
                x.InventoryStockId == fixture.SourceStockId &&
                x.LocationId == fixture.SourceLocationId &&
                x.LotId == fixture.LotId && x.Quantity == 10);
            first.Buckets[0].LocationCode.Should().NotBeNullOrWhiteSpace();
            first.Buckets[0].LotNumber.Should().StartWith("LOT-");
            first.EventCount.Should().Be(2);
            first.Events.Should().ContainSingle();
            first.EventsTruncated.Should().BeTrue();
            first.NextEventBeforeId.Should().Be(first.Events[0].TransactionId);
            first.LedgerHasEventsAfterAnchor.Should().BeFalse();
            first.Events[0].SignedQuantity.Should().Be(4);
            first.EventAnchorId.Should().Be(first.Events[0].TransactionId);

            await using (var newEvent = CreateContext())
            {
                newEvent.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.SourceLocationId,
                    LotId = fixture.LotId,
                    CreatedBy = fixture.UserId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Export,
                    Quantity = 1,
                    TransactionDate = DateTime.UtcNow.AddYears(-5)
                });
                await newEvent.SaveChangesAsync();
            }
            var anchored = await service.GetInvestigationAsync(
                fixture.WarehouseId, fixture.ProductId, first.EventAnchorId, limit: 1);
            anchored.Events.Select(x => x.TransactionId)
                .Should().Equal(first.Events.Select(x => x.TransactionId));
            anchored.ExpectedQuantity.Should().Be(12);
            anchored.EventCount.Should().Be(2);
            anchored.LedgerHasEventsAfterAnchor.Should().BeTrue();
            var older = await service.GetInvestigationAsync(
                fixture.WarehouseId, fixture.ProductId,
                first.EventAnchorId, limit: 1, eventBeforeId: first.NextEventBeforeId);
            older.EventBeforeId.Should().Be(first.NextEventBeforeId);
            older.EventAnchorId.Should().Be(first.EventAnchorId);
            older.EventCount.Should().Be(2);
            older.ExpectedQuantity.Should().Be(12);
            older.Events.Should().ContainSingle();
            older.EventsTruncated.Should().BeFalse();
            older.NextEventBeforeId.Should().BeNull();
            older.Events[0].SignedQuantity.Should().Be(8);
            older.Events[0].TransactionId.Should().BeLessThan(first.NextEventBeforeId!.Value);
            var fresh = await service.GetInvestigationAsync(fixture.WarehouseId, fixture.ProductId);
            fresh.EventCount.Should().Be(3);
            fresh.ExpectedQuantity.Should().Be(11);
            fresh.Difference.Should().Be(-1);
            fresh.Events.First().TransactionType.Should().Be("Export");
            fresh.Events.First().SignedQuantity.Should().Be(-1);
            fresh.EventAnchorId.Should().BeGreaterThan(first.EventAnchorId);

            var notAllowed = () => service.GetInvestigationAsync(int.MaxValue, fixture.ProductId);
            await notAllowed.Should().ThrowAsync<NotFoundException>();
            var invalid = () => service.GetInvestigationAsync(fixture.WarehouseId, fixture.ProductId, limit: 101);
            await invalid.Should().ThrowAsync<BusinessRuleException>();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReconciliationInvestigation_SqlBucketKeyset_TraversesHundredAndRechecksGrant()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            // Distinct real SQL Server locations are essential: the canonical
            // stock bucket index rejects duplicate warehouse/product/status/
            // location/lot/serial keys, unlike the InMemory provider.
            await using (var seed = CreateContext())
            {
                var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                var locations = Enumerable.Range(1, 104)
                    .Select(n => new WarehouseLocation
                    {
                        WarehouseId = fixture.WarehouseId,
                        Code = $"BKP{suffix}{n:D3}",
                        Name = $"Reconciliation keyset bucket {n}",
                        LocationType = WarehouseLocationType.Legacy,
                        IsActive = true,
                        CreatedBy = fixture.UserId
                    }).ToArray();
                seed.WarehouseLocations.AddRange(locations);
                await seed.SaveChangesAsync();
                seed.InventoryStocks.AddRange(locations.Select(location => new InventoryStock
                {
                    WarehouseId = fixture.WarehouseId,
                    ProductId = fixture.ProductId,
                    LocationId = location.Id,
                    LotId = fixture.LotId,
                    Status = InventoryStatus.Available,
                    Quantity = 1m,
                    ReservedQuantity = 0m
                }));
                await seed.SaveChangesAsync();
            }

            await using var read = CreateContext();
            var service = new ERP.Infrastructure.Queries.InventoryReconciliationQueryService(
                read, new WarehouseAuthorizationService(read, new CurrentUser(fixture.UserId)));
            var first = await service.GetInvestigationAsync(fixture.WarehouseId, fixture.ProductId);
            first.BucketCount.Should().Be(105);
            first.Buckets.Should().HaveCount(100);
            first.BucketsTruncated.Should().BeTrue();
            first.NextBucketAfterId.Should().Be(first.Buckets.Last().InventoryStockId);
            first.Buckets.Select(x => x.InventoryStockId).Should().BeInAscendingOrder();
            first.Buckets.Should().OnlyContain(x => x.LotId == fixture.LotId &&
                x.LocationId.HasValue && !string.IsNullOrWhiteSpace(x.LocationCode));
            first.BucketHasRowsAfterAnchor.Should().BeFalse();

            int lateBucketId;
            await using (var insert = CreateContext())
            {
                var location = new WarehouseLocation
                {
                    WarehouseId = fixture.WarehouseId,
                    Code = "BKPLATE" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
                    Name = "Inserted after bucket anchor",
                    LocationType = WarehouseLocationType.Legacy,
                    IsActive = true,
                    CreatedBy = fixture.UserId
                };
                insert.WarehouseLocations.Add(location);
                await insert.SaveChangesAsync();
                var lateBucket = new InventoryStock
                {
                    WarehouseId = fixture.WarehouseId,
                    ProductId = fixture.ProductId,
                    LocationId = location.Id,
                    LotId = fixture.LotId,
                    Status = InventoryStatus.Available,
                    Quantity = 99m
                };
                insert.InventoryStocks.Add(lateBucket);
                await insert.SaveChangesAsync();
                lateBucketId = lateBucket.Id;
            }

            var second = await service.GetInvestigationAsync(
                fixture.WarehouseId, fixture.ProductId, first.EventAnchorId,
                bucketAnchorId: first.BucketAnchorId,
                bucketAfterId: first.NextBucketAfterId);
            second.BucketAnchorId.Should().Be(first.BucketAnchorId);
            second.BucketAfterId.Should().Be(first.NextBucketAfterId);
            second.BucketCount.Should().Be(105);
            second.Buckets.Should().HaveCount(5);
            second.BucketsTruncated.Should().BeFalse();
            second.NextBucketAfterId.Should().BeNull();
            second.BucketHasRowsAfterAnchor.Should().BeTrue();
            second.Buckets.Select(x => x.InventoryStockId).Should()
                .OnlyContain(id => id <= first.BucketAnchorId && id != lateBucketId);
            first.Buckets.Select(x => x.InventoryStockId)
                .Intersect(second.Buckets.Select(x => x.InventoryStockId))
                .Should().BeEmpty();

            // Changing pages must never alter the immutable Ledger anchor.
            second.EventAnchorId.Should().Be(first.EventAnchorId);
            second.Events.Select(x => x.TransactionId)
                .Should().Equal(first.Events.Select(x => x.TransactionId));

            var refreshed = await service.GetInvestigationAsync(fixture.WarehouseId, fixture.ProductId);
            refreshed.BucketCount.Should().Be(106);
            refreshed.BucketAnchorId.Should().Be(lateBucketId);
            refreshed.BucketHasRowsAfterAnchor.Should().BeFalse();

            await using (var revoke = CreateContext())
                await revoke.UserWarehouses.Where(x =>
                    x.UserId == fixture.UserId && x.WarehouseId == fixture.WarehouseId)
                    .ExecuteDeleteAsync();

            // The already-open investigation's next SQL page must reauthorize.
            var denied = () => service.GetInvestigationAsync(
                fixture.WarehouseId, fixture.ProductId, first.EventAnchorId,
                bucketAnchorId: first.BucketAnchorId,
                bucketAfterId: first.NextBucketAfterId);
            await denied.Should().ThrowAsync<NotFoundException>();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task TraceabilityWarehouseChoices_ContainOnlyAssignedWarehouses()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int otherWarehouseId;
            await using (var setup = CreateContext())
            {
                var other = new Warehouse
                {
                    // Warehouse.Code in the production schema is capped at 20 characters.
                    Code = "UA" + Guid.NewGuid().ToString("N")[..10],
                    Name = "Kho không được phân quyền"
                };
                setup.Warehouses.Add(other);
                await setup.SaveChangesAsync();
                otherWarehouseId = other.Id;
            }
            try
            {
                await using var queryDb = CreateContext();
                var service = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                    queryDb, new WarehouseAuthorizationService(queryDb, new CurrentUser(fixture.UserId)));
                var warehouses = await service.GetAccessibleWarehousesAsync();
                warehouses.Should().ContainSingle(x => x.Id == fixture.WarehouseId);
                warehouses.Should().NotContain(x => x.Id == otherWarehouseId);
                warehouses.Should().OnlyContain(x => x.Id == fixture.WarehouseId);

                // Revoke access while the page remains open: the next lookup
                // and query must not expose the formerly authorized warehouse.
                await using (var revokeDb = CreateContext())
                    await revokeDb.UserWarehouses.Where(x =>
                        x.UserId == fixture.UserId &&
                        x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();

                (await service.GetAccessibleWarehousesAsync()).Should().BeEmpty();
                var forbidden = () => service.TraceAsync(warehouseId: fixture.WarehouseId);
                await forbidden.Should().ThrowAsync<NotFoundException>();
            }
            finally
            {
                await using var cleanup = CreateContext();
                await cleanup.Warehouses.Where(x => x.Id == otherWarehouseId).ExecuteDeleteAsync();
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task Traceability_WarehouseOnlyIncludesUnchangedStockWithoutAnyLedgerEvents()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            // The current stock remains even without a recent matching
            // ledger history; deriving stock buckets from events would
            // incorrectly return an empty warehouse inventory snapshot.
            await using (var seed = CreateContext())
                await seed.InventoryTransactions.Where(x =>
                    x.ProductId == fixture.ProductId &&
                    x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();

            await using var queryDb = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                queryDb, new WarehouseAuthorizationService(queryDb, new CurrentUser(fixture.UserId)));

            var overview = await query.TraceAsync(warehouseId: fixture.WarehouseId, limit: 20);
            overview.Events.Should().BeEmpty();
            overview.EventsTruncated.Should().BeFalse();
            overview.CurrentBuckets.Should().NotBeEmpty();
            overview.CurrentBuckets.Should().OnlyContain(x =>
                x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            overview.CurrentBuckets.Sum(x => x.OnHandQuantity).Should().Be(10);
            overview.BucketsTruncated.Should().BeFalse();
            var beyondCurrentStocks = await query.TraceAsync(
                warehouseId: fixture.WarehouseId, bucketOffset: 500);
            beyondCurrentStocks.Events.Should().BeEmpty();
            beyondCurrentStocks.CurrentBuckets.Should().BeEmpty();
            beyondCurrentStocks.BucketsTruncated.Should().BeFalse();

            // The explicit single-warehouse scope must be enforced before
            // inventory or ledger results from other warehouses are returned.
            var denied = () => query.TraceAsync(warehouseId: int.MaxValue, limit: 20);
            await denied.Should().ThrowAsync<NotFoundException>();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task Traceability_UsesNewestSqlEventsAndIncludesOlderOriginalInReversalChain()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await using (var db = CreateContext())
            {
                var start = DateTime.UtcNow.AddDays(-3);
                for (var i = 0; i < 210; i++)
                    db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId,
                        LocationId = fixture.SourceLocationId, LotId = fixture.LotId,
                        CreatedBy = fixture.UserId, InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Move, Quantity = 1,
                        ReferenceType = "TraceHistory", ReferenceId = i + 1,
                        TransactionDate = start.AddMinutes(i)
                    });
                await db.SaveChangesAsync();
            }

            int originalId;
            await using (var db = CreateContext())
                originalId = (await CreateMovementService(db, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 2, Reason = "recent timeline test"
                })).TransactionId;

            InventoryReversalResultDto reversed;
            await using (var db = CreateContext())
                reversed = await CreateReversalService(db, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = originalId, ReasonCode = "OPERATION_CORRECTION",
                    Reason = "recent reversal test"
                });

            await using var read = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                read, new WarehouseAuthorizationService(read, new CurrentUser(fixture.UserId)));
            var expected = new[]
            {
                originalId, reversed.ReversalTransactionId, reversed.CorrectiveTransactionId
            };
            var recent = await query.TraceAsync(productId: fixture.ProductId, limit: 20);
            recent.Events.Should().HaveCount(20);
            recent.EventsTruncated.Should().BeTrue();
            recent.Events.Select(x => x.TransactionId).Should().Contain(expected);
            recent.Events.Select(x => x.TransactionDate).Should().BeInAscendingOrder();

            var chain = await query.TraceAsync(productId: fixture.ProductId, limit: 2);
            chain.Events.Select(x => x.TransactionId).Should().Contain(expected);
            chain.Events.Single(x => x.TransactionId == originalId)
                .ReversalTransactionId.Should().Be(reversed.ReversalTransactionId);
            chain.Events.Single(x => x.TransactionId == reversed.ReversalTransactionId)
                .CorrectiveTransactionId.Should().Be(reversed.CorrectiveTransactionId);
        }
        finally { await CleanupAsync(fixture); }
    }


    [SqlServerFact]
    public async Task Traceability_AnchorsLedgerPagesAcrossNewBackdatedAndRecentTransactions()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            const int referenceId = 876231;
            await using (var seed = CreateContext())
            {
                var start = DateTime.UtcNow.AddDays(-10);
                for (var i = 0; i < 45; i++)
                    seed.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId,
                        LocationId = fixture.SourceLocationId, LotId = fixture.LotId,
                        CreatedBy = fixture.UserId, InventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.Move, Quantity = 1,
                        ReferenceType = "TracePageAnchor", ReferenceId = referenceId,
                        TransactionDate = start.AddMinutes(i)
                    });
                await seed.SaveChangesAsync();
            }

            await using var db = CreateContext();
            var query = new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                db, new WarehouseAuthorizationService(db, new CurrentUser(fixture.UserId)));
            var first = await query.TraceAsync(warehouseId: fixture.WarehouseId,
                referenceType: "TracePageAnchor", referenceId: referenceId, limit: 20);
            first.EventAnchorId.Should().BeGreaterThan(0);
            first.Events.Should().HaveCount(20);
            first.EventsTruncated.Should().BeTrue();

            var older = await query.TraceAsync(warehouseId: fixture.WarehouseId,
                referenceType: "TracePageAnchor", referenceId: referenceId,
                limit: 20, eventOffset: 20, eventAnchorId: first.EventAnchorId);
            older.Events.Should().HaveCount(20);
            var originalOlderIds = older.Events.Select(x => x.TransactionId).ToArray();

            // Later commits must not shift the already-open 20-row ledger page,
            // even if they carry an old business timestamp.
            await using (var concurrentWriter = CreateContext())
            {
                concurrentWriter.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.SourceLocationId, LotId = fixture.LotId,
                    CreatedBy = fixture.UserId, InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Move, Quantity = 1,
                    ReferenceType = "TracePageAnchor", ReferenceId = referenceId,
                    TransactionDate = DateTime.UtcNow.AddDays(1)
                });
                concurrentWriter.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId,
                    LocationId = fixture.SourceLocationId, LotId = fixture.LotId,
                    CreatedBy = fixture.UserId, InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Move, Quantity = 1,
                    ReferenceType = "TracePageAnchor", ReferenceId = referenceId,
                    TransactionDate = DateTime.UtcNow.AddDays(-30)
                });
                await concurrentWriter.SaveChangesAsync();
            }

            var stableOlder = await query.TraceAsync(warehouseId: fixture.WarehouseId,
                referenceType: "TracePageAnchor", referenceId: referenceId,
                limit: 20, eventOffset: 20, eventAnchorId: first.EventAnchorId);
            stableOlder.EventAnchorId.Should().Be(first.EventAnchorId);
            stableOlder.Events.Select(x => x.TransactionId).Should().Equal(originalOlderIds);
            first.Events.Select(x => x.TransactionId)
                .Intersect(stableOlder.Events.Select(x => x.TransactionId)).Should().BeEmpty();
            var stableFirst = await query.TraceAsync(warehouseId: fixture.WarehouseId,
                referenceType: "TracePageAnchor", referenceId: referenceId,
                limit: 20, eventAnchorId: first.EventAnchorId);
            stableFirst.Events.Select(x => x.TransactionId)
                .Should().Equal(first.Events.Select(x => x.TransactionId));

            var fresh = await query.TraceAsync(warehouseId: fixture.WarehouseId,
                referenceType: "TracePageAnchor", referenceId: referenceId, limit: 20);
            fresh.EventAnchorId.Should().BeGreaterThan(first.EventAnchorId!.Value);
            fresh.Events.Select(x => x.TransactionId)
                .Should().NotBeEquivalentTo(first.Events.Select(x => x.TransactionId));
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReversalReasonCode_ValidatesTypeAndPersistsWithLedgerAndAudit()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int originalId;
            await using (var moving = CreateContext())
                originalId = (await CreateMovementService(moving, fixture.UserId).MoveAsync(new()
                {
                    InventoryStockId = fixture.SourceStockId,
                    DestinationLocationId = fixture.DestinationLocationId,
                    Quantity = 2,
                    Reason = "Reason code fixture"
                })).TransactionId;

            await using (var rejected = CreateContext())
            {
                var attempt = () => CreateReversalService(rejected, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = originalId,
                    ReasonCode = "STATUS_ERROR",
                    Reason = "Mã dành cho giao dịch trạng thái không hợp lệ ở đây"
                });
                await attempt.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>()
                    .WithMessage("*Mã lý do đảo*");
            }

            await using (var verify = CreateContext())
            {
                (await verify.InventoryTransactions.CountAsync(x =>
                    x.ReversalOfTransactionId == originalId)).Should().Be(0);
                (await verify.AuditLogs.CountAsync(x =>
                    x.EntityId == originalId && x.Action == "Inventory.Reversed")).Should().Be(0);
            }

            InventoryReversalResultDto result;
            await using (var accepted = CreateContext())
                result = await CreateReversalService(accepted, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = originalId,
                    ReasonCode = " LOCATION_ERROR ",
                    Reason = "Xác minh sai vị trí cần đảo"
                });

            await using var after = CreateContext();
            var marker = await after.InventoryTransactions.AsNoTracking()
                .SingleAsync(x => x.Id == result.ReversalTransactionId);
            marker.ReasonCode.Should().Be("LOCATION_ERROR");
            marker.Note.Should().Be("Xác minh sai vị trí cần đảo");
            result.ReasonCode.Should().Be("LOCATION_ERROR");
            var audit = await after.AuditLogs.AsNoTracking().SingleAsync(x =>
                x.EntityId == originalId && x.Action == "Inventory.Reversed");
            audit.NewValues.Should().Contain("ReasonCode: LOCATION_ERROR");
            var trace = await new ERP.Infrastructure.Queries.InventoryTraceabilityQueryService(
                after, new WarehouseAuthorizationService(after, new CurrentUser(fixture.UserId)))
                .TraceAsync(referenceType: "InventoryReversal", referenceId: originalId);
            trace.Events.Single(x => x.TransactionId == marker.Id)
                .ReasonCode.Should().Be("LOCATION_ERROR");
        }
        finally { await CleanupAsync(fixture); }
    }

    private static InventoryLockService CreateLockService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new InventoryLockService(db, new WarehouseAuthorizationService(db, current), current);
    }

    private static InventoryMovementService CreateMovementService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        var locks = new InventoryLockService(db, new WarehouseAuthorizationService(db, current), current);
        return new InventoryMovementService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            locks);
    }

    private static InventoryReversalService CreateReversalService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        var authorization = new WarehouseAuthorizationService(db, current);
        var locks = new InventoryLockService(db, authorization, current);
        var movement = new InventoryMovementService(db, authorization, current, locks);
        var status = new InventoryStatusService(db, authorization, current, locks);
        return new InventoryReversalService(db, authorization, current, movement, status);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var role = new Role { RoleName = $"LockMoveRole{suffix}" };
        var user = new User
        {
            Username = $"LockMoveUser{suffix}",
            PasswordHash = "not-used",
            FullName = "Inventory lock move user",
            Role = role
        };
        var unit = new Unit { Code = $"LMU{suffix}", Name = "Lock move unit", DecimalPlaces = 4 };
        var product = new Product { Code = $"LMP{suffix}", Name = "Lock move product", Unit = unit };
        var warehouse = new Warehouse { Code = $"LMW{suffix}", Name = "Lock move warehouse" };
        db.AddRange(user, product, warehouse);
        await db.SaveChangesAsync();

        var source = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"LMS{suffix}",
            Name = "Lock move source",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var destination = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"LMD{suffix}",
            Name = "Lock move destination",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        db.AddRange(source, destination);
        await db.SaveChangesAsync();

        var lot = new InventoryLot
        {
            ProductId = product.Id,
            LotNumber = $"LOT-{suffix}",
            ReceivedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        db.InventoryLots.Add(lot);
        await db.SaveChangesAsync();

        db.UserWarehouses.Add(new UserWarehouse
        {
            UserId = user.Id,
            WarehouseId = warehouse.Id,
            CreatedBy = user.Id
        });
        var stock = new InventoryStock
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            LocationId = source.Id,
            LotId = lot.Id,
            Status = InventoryStatus.Available,
            Quantity = 10,
            ReservedQuantity = 0
        };
        db.InventoryStocks.Add(stock);
        await db.SaveChangesAsync();

        return new Fixture(
            user.Id, role.Id, unit.Id, product.Id, warehouse.Id,
            source.Id, destination.Id, lot.Id, stock.Id);
    }

    private static async Task CleanupAsync(Fixture f)
    {
        await using var db = CreateContext();
        await db.AuditLogs.Where(x => x.UserId == f.UserId).ExecuteDeleteAsync();
        await db.InventoryTransactions.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLocationMovements.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryLocks.Where(x => x.WarehouseId == f.WarehouseId).ExecuteDeleteAsync();
        await db.StockAllocations.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.WarehouseId).ExecuteDeleteAsync();
        await db.StockReservations.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == f.ProductId && x.WarehouseId == f.WarehouseId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => x.UserId == f.UserId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(x => x.Id == f.LotId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == f.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == f.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == f.UnitId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == f.WarehouseId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == f.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == f.RoleId).ExecuteDeleteAsync();
    }

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(ConnectionString)
            .Options);

    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }

    private sealed record Fixture(
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int WarehouseId,
        int SourceLocationId,
        int DestinationLocationId,
        int LotId,
        int SourceStockId);
}
