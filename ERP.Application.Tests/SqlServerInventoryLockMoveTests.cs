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
