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
            marker.Note.Should().Be("operator correction");
            marker.Quantity.Should().Be(4);
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
                    Reason = "first correction"
                });

            await using (var secondDb = CreateContext())
            {
                var act = () => CreateReversalService(secondDb, fixture.UserId).ReverseAsync(new()
                {
                    OriginalTransactionId = moved.TransactionId,
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
            failure.Should().BeOfType<BusinessRuleException>();
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
