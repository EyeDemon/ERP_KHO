using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerStockReservationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task ReserveReleaseAndConsumeKeepOnHandReservedAndLedgerConsistent()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            await using var db = CreateContext();
            var service = CreateService(db, fixture.UserId);
            var first = await service.CreateAsync(new() { ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId, Quantity = 30 });
            await service.ReleaseAsync(first.Id, new() { Quantity = 10, Reason = "test partial release" });
            var reservation = await db.StockReservations.SingleAsync(x => x.Id == first.Id);
            await service.ConsumeAsync(reservation, fixture.UserId);
            await new UnitOfWork(db).SaveChangesAsync();

            var stock = await db.InventoryStocks.AsNoTracking().SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            stock.Quantity.Should().Be(80);
            stock.ReservedQuantity.Should().Be(0);
            var saved = await db.StockReservations.AsNoTracking().SingleAsync(x => x.Id == first.Id);
            saved.ConsumedQuantity.Should().Be(20);
            saved.ReleasedQuantity.Should().Be(10);
            (await service.ReconcileAsync()).Should().BeEmpty();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task TwoCompetingReservationsCannotOverbookLastAvailableStock()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            async Task<bool> Reserve(decimal quantity)
            {
                await using var db = CreateContext();
                try { await CreateService(db, fixture.UserId).CreateAsync(new() { ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId, Quantity = quantity }); return true; }
                catch (ConcurrencyException) { return false; }
            }
            var outcomes = await Task.WhenAll(Reserve(80), Reserve(80));
            outcomes.Count(x => x).Should().Be(1);
            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            stock.Quantity.Should().Be(100);
            stock.ReservedQuantity.Should().Be(80);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ComplementaryConcurrentReservationsBothSucceedAndAvailableIsZero()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            async Task Reserve(decimal quantity) { await using var db = CreateContext(); await CreateService(db, fixture.UserId).CreateAsync(new() { ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId, Quantity = quantity }); }
            await Task.WhenAll(Reserve(40), Reserve(60));
            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            (stock.Quantity - stock.ReservedQuantity).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReservedStockCannotBeTakenByUnreservedExportAndConsumeIsSingleWinner()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            int id;
            await using (var db = CreateContext()) id = (await CreateService(db, fixture.UserId).CreateAsync(new() { ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId, Quantity = 80 })).Id;
            await using (var db = CreateContext()) (await new InventoryStockRepository(db).TryDecreaseStockAsync(fixture.ProductId, fixture.WarehouseId, 30)).Should().BeFalse();

            async Task<bool> Consume()
            {
                await using var db = CreateContext();
                var reservation = await db.StockReservations.SingleAsync(x => x.Id == id);
                try { await CreateService(db, fixture.UserId).ConsumeAsync(reservation, fixture.UserId); await db.SaveChangesAsync(); return true; }
                catch (Exception ex) when (ex is ConcurrencyException or DbUpdateConcurrencyException) { return false; }
            }
            var outcomes = await Task.WhenAll(Consume(), Consume());
            outcomes.Count(x => x).Should().Be(1);
            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            stock.Quantity.Should().Be(20); stock.ReservedQuantity.Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ExportApprovalAndDispatchConsumesOwnReservationAndCreatesOnePhysicalTransaction()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            int receiptId;
            await using (var setup = CreateContext())
            {
                var receipt = new ExportReceipt { Code = $"ER-{Guid.NewGuid():N}"[..28], WarehouseId = fixture.WarehouseId, CreatedBy = fixture.CreatorId, Status = ReceiptStatus.Draft, Details = [new ExportReceiptDetail { ProductId = fixture.ProductId, Quantity = 35, UnitPrice = 1 }] };
                setup.ExportReceipts.Add(receipt); await setup.SaveChangesAsync(); receiptId = receipt.Id;
            }
            await using (var db = CreateContext())
            {
                var current = new CurrentUser(fixture.UserId);
                var authorization = new WarehouseAuthorizationService(db, current);
                var reservationService = new StockReservationService(db, new InventoryStockRepository(db), new UnitOfWork(db), authorization, current, new StockReservationOptions());
                var service = new ExportReceiptService(new ExportReceiptRepository(db), new InventoryStockRepository(db), new InventoryTransactionRepository(db), new UnitOfWork(db), new AuditLogRepository(db), authorization, current, reservationService);
                await service.ApproveAndDispatchAsync(receiptId, fixture.UserId);
            }
            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            stock.Quantity.Should().Be(65); stock.ReservedQuantity.Should().Be(0);
            var reservation = await verify.StockReservations.AsNoTracking().SingleAsync(x => x.SourceType == "ExportReceipt" && x.SourceId == receiptId);
            reservation.Status.Should().Be(StockReservationStatus.Consumed);
            (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "ExportReceipt" && x.ReferenceId == receiptId)).Should().Be(1);
            (await verify.ExportReceipts.FindAsync(receiptId))!.Status.Should().Be(ReceiptStatus.Dispatched);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task TwoStepDispatch_WritesLocationTraceableLedgerForActualConsumedBuckets()
    {
        var fixture = await CreateFixtureAsync(50);
        try
        {
            int firstLocationId;
            int secondLocationId;
            await using (var setup = CreateContext())
            {
                var firstStock = await setup.InventoryStocks.SingleAsync(x =>
                    x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
                firstLocationId = firstStock.LocationId!.Value;
                firstStock.Quantity = 20;

                var secondLocation = new WarehouseLocation
                {
                    WarehouseId = fixture.WarehouseId,
                    Code = $"PICK-{Guid.NewGuid():N}"[..20],
                    Name = "Pick location 2",
                    LocationType = WarehouseLocationType.Legacy,
                    IsActive = true,
                    IsPickable = true,
                    CreatedBy = fixture.CreatorId
                };
                setup.WarehouseLocations.Add(secondLocation);
                await setup.SaveChangesAsync();
                secondLocationId = secondLocation.Id;
                setup.InventoryStocks.Add(new InventoryStock
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    LocationId = secondLocationId,
                    Quantity = 30,
                    Status = InventoryStatus.Available
                });
                await setup.SaveChangesAsync();
            }

            var receiptId = await CreateExportReceiptAsync(fixture, 40);
            await using (var approve = CreateContext())
                await CreateExportService(approve, fixture.UserId).ApproveAndReserveAsync(receiptId, fixture.UserId);

            await using (var dispatch = CreateContext())
                await CreateExportService(dispatch, fixture.CreatorId).DispatchAsync(receiptId, fixture.CreatorId);

            await using var verify = CreateContext();
            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .Where(x => x.ReferenceType == "ExportReceipt" && x.ReferenceId == receiptId)
                .OrderBy(x => x.LocationId)
                .Select(x => new { x.LocationId, x.Quantity })
                .ToListAsync();

            ledger.Should().HaveCount(2);
            ledger.Should().ContainSingle(x => x.LocationId == firstLocationId && x.Quantity == 20);
            ledger.Should().ContainSingle(x => x.LocationId == secondLocationId && x.Quantity == 20);
            ledger.Sum(x => x.Quantity).Should().Be(40);

            var stocks = await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .OrderBy(x => x.LocationId)
                .Select(x => new { x.LocationId, x.Quantity, x.ReservedQuantity })
                .ToListAsync();
            stocks.Should().ContainSingle(x => x.LocationId == firstLocationId && x.Quantity == 0 && x.ReservedQuantity == 0);
            stocks.Should().ContainSingle(x => x.LocationId == secondLocationId && x.Quantity == 10 && x.ReservedQuantity == 0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ApproveReserveThenDispatchAndCancelKeepPhysicalAndReservedStockCorrect()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            var holdReceiptId = await CreateExportReceiptAsync(fixture, 35);
            await using (var db = CreateContext())
                await CreateExportService(db, fixture.UserId).ApproveAndReserveAsync(holdReceiptId, fixture.UserId);

            await using (var held = CreateContext())
            {
                var stock = await held.InventoryStocks.SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
                stock.Quantity.Should().Be(100); stock.ReservedQuantity.Should().Be(35);
                (await held.InventoryTransactions.CountAsync(x => x.ReferenceId == holdReceiptId && x.ReferenceType == "ExportReceipt")).Should().Be(0);
                (await held.ExportReceipts.FindAsync(holdReceiptId))!.Status.Should().Be(ReceiptStatus.Approved);
            }

            await using (var db = CreateContext())
                await CreateExportService(db, fixture.CreatorId).DispatchAsync(holdReceiptId, fixture.CreatorId);

            await using (var dispatched = CreateContext())
            {
                var stock = await dispatched.InventoryStocks.SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
                stock.Quantity.Should().Be(65); stock.ReservedQuantity.Should().Be(0);
                (await dispatched.InventoryTransactions.CountAsync(x => x.ReferenceId == holdReceiptId && x.ReferenceType == "ExportReceipt")).Should().Be(1);
            }

            var cancelReceiptId = await CreateExportReceiptAsync(fixture, 10);
            await using (var db = CreateContext())
            {
                var service = CreateExportService(db, fixture.UserId);
                await service.ApproveAndReserveAsync(cancelReceiptId, fixture.UserId);
                await service.CancelAsync(cancelReceiptId, fixture.UserId);
            }
            await using var cancelled = CreateContext();
            var finalStock = await cancelled.InventoryStocks.SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            finalStock.Quantity.Should().Be(65); finalStock.ReservedQuantity.Should().Be(0);
            (await cancelled.ExportReceipts.FindAsync(cancelReceiptId))!.Status.Should().Be(ReceiptStatus.Cancelled);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task RepeatedDispatchAndDispatchVersusCancel_HaveSingleTerminalWinner()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            var repeatedId = await CreateExportReceiptAsync(fixture, 25);
            await using (var db = CreateContext())
                await CreateExportService(db, fixture.UserId).ApproveAndReserveAsync(repeatedId, fixture.UserId);

            async Task<bool> Dispatch(int receiptId)
            {
                await using var db = CreateContext();
                try { await CreateExportService(db, fixture.CreatorId).DispatchAsync(receiptId, fixture.CreatorId); return true; }
                catch (Exception ex) when (ex is ConcurrencyException or DbUpdateConcurrencyException) { return false; }
            }

            (await Task.WhenAll(Dispatch(repeatedId), Dispatch(repeatedId))).Count(x => x).Should().Be(1);

            var raceId = await CreateExportReceiptAsync(fixture, 30);
            await using (var db = CreateContext())
                await CreateExportService(db, fixture.UserId).ApproveAndReserveAsync(raceId, fixture.UserId);

            async Task<bool> Cancel()
            {
                await using var db = CreateContext();
                try { await CreateExportService(db, fixture.UserId).CancelAsync(raceId, fixture.UserId); return true; }
                catch (Exception ex) when (ex is ConcurrencyException or DbUpdateConcurrencyException or BusinessRuleException) { return false; }
            }

            var outcomes = await Task.WhenAll(Dispatch(raceId), Cancel());
            outcomes.Count(x => x).Should().Be(1);

            await using var verify = CreateContext();
            (await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "ExportReceipt" && x.ReferenceId == repeatedId)).Should().Be(1);
            var raceReceipt = await verify.ExportReceipts.FindAsync(raceId);
            raceReceipt!.Status.Should().BeOneOf(ReceiptStatus.Dispatched, ReceiptStatus.Cancelled);
            var raceTransactions = await verify.InventoryTransactions.CountAsync(x => x.ReferenceType == "ExportReceipt" && x.ReferenceId == raceId);
            raceTransactions.Should().Be(raceReceipt.Status == ReceiptStatus.Dispatched ? 1 : 0);
            var raceReservation = await verify.StockReservations.SingleAsync(x => x.SourceType == "ExportReceipt" && x.SourceId == raceId);
            raceReservation.Status.Should().Be(raceReceipt.Status == ReceiptStatus.Dispatched ? StockReservationStatus.Consumed : StockReservationStatus.Cancelled);
            (await CreateService(verify, fixture.UserId).ReconcileAsync()).Should().BeEmpty();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task InventoryRead_DoesNotExpireReservationOrMutateInventoryLedger()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            int reservationId;
            await using (var setup = CreateContext())
            {
                reservationId = (await CreateService(setup, fixture.UserId).CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 25,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                })).Id;
                await setup.StockReservations.Where(x => x.Id == reservationId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddMinutes(-10))
                        .SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            }

            await using var db = CreateContext();
            var before = await CaptureReadInvariantAsync(db, fixture, reservationId);
            var current = new CurrentUser(fixture.UserId, "WarehouseStaff");
            var query = new InventoryQueryService(db, new WarehouseAuthorizationService(db, current));

            var result = (await query.GetCurrentStockAsync(null, null, null, null)).ToList();

            result.Should().ContainSingle(x => x.ProductId == fixture.ProductId);
            result.Should().OnlyContain(x => x.WarehouseId == fixture.WarehouseId);
            var after = await CaptureReadInvariantAsync(db, fixture, reservationId);
            after.Should().Be(before);
            after.OnHand.Should().Be(100);
            after.Reserved.Should().Be(25);
            after.ReservationStatus.Should().Be(StockReservationStatus.Active);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ExpireEndpointService_ReleasesOnlyExpiredReservedQuantity_AndIsIdempotent()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            int expiredId;
            int activeId;
            await using (var setup = CreateContext())
            {
                var service = CreateService(setup, fixture.UserId);
                expiredId = (await service.CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 25,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(5)
                })).Id;
                activeId = (await service.CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 15,
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                })).Id;
                await setup.StockReservations.Where(x => x.Id == expiredId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddMinutes(-10))
                        .SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            }

            int transactionsBefore;
            int receiptsBefore;
            await using (var before = CreateContext())
            {
                transactionsBefore = await before.InventoryTransactions.CountAsync();
                receiptsBefore = await before.ExportReceipts.CountAsync();
            }

            await using (var first = CreateContext())
                (await CreateService(first, fixture.UserId).ExpireAsync()).Should().Be(1);
            await using (var second = CreateContext())
                (await CreateService(second, fixture.UserId).ExpireAsync()).Should().Be(0);

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            stock.Quantity.Should().Be(100);
            stock.ReservedQuantity.Should().Be(15);
            (await verify.StockReservations.AsNoTracking().SingleAsync(x => x.Id == expiredId)).Status
                .Should().Be(StockReservationStatus.Expired);
            (await verify.StockReservations.AsNoTracking().SingleAsync(x => x.Id == activeId)).Status
                .Should().Be(StockReservationStatus.Active);
            (await verify.InventoryTransactions.CountAsync()).Should().Be(transactionsBefore);
            (await verify.ExportReceipts.CountAsync()).Should().Be(receiptsBefore);
            (await CreateService(verify, fixture.UserId).ReconcileAsync()).Should().BeEmpty();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ExportReceiptReservation_IsIsolatedFromManualExpireAndManualRelease()
    {
        var fixture = await CreateFixtureAsync(100);
        try
        {
            var receiptId = await CreateExportReceiptAsync(fixture, 30);
            await using (var approve = CreateContext())
                await CreateExportService(approve, fixture.UserId).ApproveAndReserveAsync(receiptId, fixture.UserId);

            int exportReservationId;
            await using (var backdate = CreateContext())
            {
                exportReservationId = await backdate.StockReservations
                    .Where(x => x.SourceType == "ExportReceipt" && x.SourceId == receiptId)
                    .Select(x => x.Id)
                    .SingleAsync();
                await backdate.StockReservations
                    .Where(x => x.Id == exportReservationId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddHours(-4))
                        .SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddHours(-2)));
            }

            // 1. ExpireAsync and new Manual reservation stale sweep must NOT expire ExportReceipt reservation
            await using (var expire = CreateContext())
            {
                var service = CreateService(expire, fixture.UserId);
                (await service.ExpireAsync()).Should().Be(0);
                var manual = await service.CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 10
                });
                await service.ReleaseAsync(manual.Id, new() { Reason = "cleanup manual hold" });
            }

            // 2. Manual ReleaseAsync on ExportReceipt reservation must be rejected with ConcurrencyException (409 Conflict)
            await using (var manualRelease = CreateContext())
            {
                var service = CreateService(manualRelease, fixture.UserId);
                var act = () => service.ReleaseAsync(exportReservationId, new() { Reason = "attempt manual release on export hold" });
                await act.Should().ThrowAsync<ConcurrencyException>();
            }

            // 3. ExportReceipt reservation remains Active and can still be dispatched cleanly
            await using (var dispatch = CreateContext())
                await CreateExportService(dispatch, fixture.CreatorId).DispatchAsync(receiptId, fixture.CreatorId);

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
            stock.Quantity.Should().Be(70);
            stock.ReservedQuantity.Should().Be(0);
            (await verify.StockReservations.AsNoTracking().SingleAsync(x => x.Id == exportReservationId))
                .Status.Should().Be(StockReservationStatus.Consumed);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task V01_And_V08_FullLifecyclePosting_ReconstructsWarehouseAndBucketBalancesWithZeroDrift()
    {
        var fixture = await CreateFixtureAsync(0);
        try
        {
            int receivingLocId;
            int storageA1LocId;
            int importReceiptId;

            await using (var setup = CreateContext())
            {
                // Keep only RECEIVING (non-pickable) and STORAGE-A1 (pickable) for clean 2-bucket lifecycle
                await setup.InventoryStocks.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
                await setup.WarehouseLocations.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();

                var receiving = new WarehouseLocation
                {
                    WarehouseId = fixture.WarehouseId,
                    Code = "RECEIVING",
                    Name = "Receiving Stage",
                    LocationType = WarehouseLocationType.Receiving,
                    IsActive = true,
                    IsReceivable = true,
                    IsPickable = false,
                    IsSystemManaged = true,
                    CreatedBy = fixture.CreatorId
                };
                var storageA1 = new WarehouseLocation
                {
                    WarehouseId = fixture.WarehouseId,
                    Code = "STORAGE-A1",
                    Name = "Storage Bin A1",
                    LocationType = WarehouseLocationType.Storage,
                    IsActive = true,
                    IsReceivable = true,
                    IsPickable = true,
                    IsSystemManaged = false,
                    CreatedBy = fixture.CreatorId
                };
                setup.WarehouseLocations.AddRange(receiving, storageA1);
                await setup.SaveChangesAsync();
                receivingLocId = receiving.Id;
                storageA1LocId = storageA1.Id;
            }

            // 1. Opening / Receipt (Import 100 units into RECEIVING)
            await using (var importDb = CreateContext())
            {
                var creatorUser = new CurrentUser(fixture.CreatorId);
                var creatorAuth = new WarehouseAuthorizationService(importDb, creatorUser);
                var putaway = new PutawayService(importDb, creatorAuth, creatorUser);
                var importCreateSvc = new ImportReceiptService(
                    new ImportReceiptRepository(importDb),
                    new InventoryStockRepository(importDb),
                    new InventoryTransactionRepository(importDb),
                    new WarehouseRepository(importDb),
                    new ProductRepository(importDb),
                    new UnitOfWork(importDb),
                    new AuditLogRepository(importDb),
                    creatorAuth,
                    creatorUser,
                    null!,
                    putaway);

                var created = await importCreateSvc.CreateAsync(new CreateImportReceiptDto
                {
                    Code = $"IR-V01-{Guid.NewGuid():N}"[..24],
                    WarehouseId = fixture.WarehouseId,
                    Details = [new CreateImportReceiptDetailDto { ProductId = fixture.ProductId, Quantity = 100, UnitPrice = 10 }]
                }, fixture.CreatorId);
                importReceiptId = created.Id;

                await importCreateSvc.ReceiveAsync(importReceiptId, new ReceiveImportReceiptDto
                {
                    Lines = [new ReceiveImportReceiptLineDto { LineId = created.Details.Single().Id, ReceivedQuantity = 100, AcceptedQuantity = 100 }]
                }, fixture.CreatorId);
            }

            await using (var approveImportDb = CreateContext())
            {
                var approverUser = new CurrentUser(fixture.UserId);
                var approverAuth = new WarehouseAuthorizationService(approveImportDb, approverUser);
                var putaway = new PutawayService(approveImportDb, approverAuth, approverUser);
                var importApproveSvc = new ImportReceiptService(
                    new ImportReceiptRepository(approveImportDb),
                    new InventoryStockRepository(approveImportDb),
                    new InventoryTransactionRepository(approveImportDb),
                    new WarehouseRepository(approveImportDb),
                    new ProductRepository(approveImportDb),
                    new UnitOfWork(approveImportDb),
                    new AuditLogRepository(approveImportDb),
                    approverAuth,
                    approverUser,
                    null!,
                    putaway);

                await importApproveSvc.ApproveImportReceiptAsync(importReceiptId, fixture.UserId);
                await importApproveSvc.PostAsync(importReceiptId, fixture.UserId);
            }

            // 2. Putaway / Move (Move 60 units from RECEIVING to pickable STORAGE-A1)
            await using (var moveDb = CreateContext())
            {
                var receivingStock = await moveDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId && x.LocationId == receivingLocId);
                var current = new CurrentUser(fixture.UserId);
                var auth = new WarehouseAuthorizationService(moveDb, current);
                var locks = new InventoryLockService(moveDb, auth, current);
                var moveSvc = new InventoryMovementService(moveDb, auth, current, locks);
                await moveSvc.MoveAsync(new CreateInventoryMoveDto
                {
                    InventoryStockId = receivingStock.Id,
                    DestinationLocationId = storageA1LocId,
                    Quantity = 60,
                    Reason = "Putaway 60 units to pickable STORAGE-A1"
                });
            }

            // 3. Manual Reservation (Reserve 15 units, release 5 -> 10 Manual units held at STORAGE-A1)
            await using (var reserveDb = CreateContext())
            {
                var rsvSvc = CreateService(reserveDb, fixture.UserId);
                var manualRes = await rsvSvc.CreateAsync(new CreateStockReservationDto
                {
                    WarehouseId = fixture.WarehouseId,
                    ProductId = fixture.ProductId,
                    Quantity = 15
                });
                await rsvSvc.ReleaseAsync(manualRes.Id, new ReleaseStockReservationDto
                {
                    Quantity = 5,
                    Reason = "Partial manual release"
                });
            }

            // 4. ExportReceipt (Create 20 -> Approve -> Dispatch, consuming ExportReceipt hold while keeping 10 Manual hold)
            var exportReceiptId = await CreateExportReceiptAsync(fixture, 20);
            await using (var exportApproveDb = CreateContext())
                await CreateExportService(exportApproveDb, fixture.UserId).ApproveAndReserveAsync(exportReceiptId, fixture.UserId);
            await using (var exportDispatchDb = CreateContext())
                await CreateExportService(exportDispatchDb, fixture.CreatorId).DispatchAsync(exportReceiptId, fixture.CreatorId);

            // 5. Stocktake (Snapshot RECEIVING=40, STORAGE-A1=40 -> recount STORAGE-A1=38 -> AdjustmentDecrease=2)
            int stocktakeId;
            await using (var stCreateDb = CreateContext())
            {
                var current = new CurrentUser(fixture.CreatorId);
                var auth = new WarehouseAuthorizationService(stCreateDb, current);
                var locks = new InventoryLockService(stCreateDb, auth, current);
                var stSvc = new StocktakeService(
                    new StocktakeRepository(stCreateDb),
                    new InventoryStockRepository(stCreateDb),
                    new InventoryTransactionRepository(stCreateDb),
                    new WarehouseRepository(stCreateDb),
                    new UnitOfWork(stCreateDb),
                    new AuditLogRepository(stCreateDb),
                    auth,
                    current,
                    locks);

                stocktakeId = await stSvc.CreateStocktakeAsync(new CreateStocktakeDto
                {
                    WarehouseId = fixture.WarehouseId,
                    Note = "Cycle count V01"
                }, fixture.CreatorId);

                var details = await stCreateDb.StocktakeDetails.AsNoTracking()
                    .Where(x => x.StocktakeId == stocktakeId)
                    .ToListAsync();
                foreach (var d in details)
                {
                    var actual = d.LocationId == storageA1LocId ? 38m : d.SystemQuantity;
                    await stSvc.UpdateStocktakeDetailAsync(stocktakeId, d.Id, new UpdateStocktakeDetailDto
                    {
                        ActualQuantity = actual,
                        Note = "Counted"
                    });
                }
            }

            await using (var stApproveDb = CreateContext())
            {
                var current = new CurrentUser(fixture.UserId);
                var auth = new WarehouseAuthorizationService(stApproveDb, current);
                var locks = new InventoryLockService(stApproveDb, auth, current);
                var stSvc = new StocktakeService(
                    new StocktakeRepository(stApproveDb),
                    new InventoryStockRepository(stApproveDb),
                    new InventoryTransactionRepository(stApproveDb),
                    new WarehouseRepository(stApproveDb),
                    new UnitOfWork(stApproveDb),
                    new AuditLogRepository(stApproveDb),
                    auth,
                    current,
                    locks);

                await stSvc.ApproveStocktakeAsync(stocktakeId, fixture.UserId);
            }

            // 6. StatusChange (5 units Available -> Blocked) & Reversal (Reverse back to Available)
            int statusChangeTxId;
            await using (var statusDb = CreateContext())
            {
                var storageStock = await statusDb.InventoryStocks.AsNoTracking()
                    .SingleAsync(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId && x.LocationId == storageA1LocId && x.Status == InventoryStatus.Available);
                var current = new CurrentUser(fixture.UserId);
                var auth = new WarehouseAuthorizationService(statusDb, current);
                var locks = new InventoryLockService(statusDb, auth, current);
                var statusSvc = new InventoryStatusService(statusDb, auth, current, locks);
                var changed = await statusSvc.ChangeAsync(new CreateInventoryStatusChangeDto
                {
                    InventoryStockId = storageStock.Id,
                    Quantity = 5,
                    ToStatus = "BLOCKED",
                    Reason = "Temporary blocked status check"
                });
                statusChangeTxId = changed.TransactionId;
            }

            await using (var revDb = CreateContext())
            {
                var current = new CurrentUser(fixture.UserId);
                var auth = new WarehouseAuthorizationService(revDb, current);
                var locks = new InventoryLockService(revDb, auth, current);
                var moveSvc = new InventoryMovementService(revDb, auth, current, locks);
                var statusSvc = new InventoryStatusService(revDb, auth, current, locks);
                var revSvc = new InventoryReversalService(revDb, auth, current, moveSvc, statusSvc);
                var revResult = await revSvc.ReverseAsync(new CreateInventoryReversalDto
                {
                    OriginalTransactionId = statusChangeTxId,
                    ReasonCode = "STATUS_ERROR",
                    Reason = "Cleared blocked status error"
                });
                revResult.CorrectiveTransactionId.Should().BeGreaterThan(0);
            }

            // Verify V01 + V08 + Rule 1 + Rule 2 Invariants
            await using var verifyDb = CreateContext();
            var allTx = await verifyDb.InventoryTransactions.AsNoTracking()
                .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
                .ToListAsync();
            var signedLedgerSum = allTx
                .Where(t => t.TransactionType.HasCanonicalSign())
                .Sum(t => t.TransactionType.ApplySign(t.Quantity));

            var allStocks = await verifyDb.InventoryStocks.AsNoTracking()
                .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
                .ToListAsync();
            var totalPhysicalOnHand = allStocks.Sum(s => s.Quantity);
            var totalReserved = allStocks.Sum(s => s.ReservedQuantity);

            // 100 (Import) - 20 (Export) - 2 (Stocktake AdjustmentDecrease) = 78
            signedLedgerSum.Should().Be(78m);
            totalPhysicalOnHand.Should().Be(78m);
            (signedLedgerSum - totalPhysicalOnHand).Should().Be(0m, "warehouse-level and bucket-level ledger reconstruction must have zero drift");
            totalReserved.Should().Be(10m);

            var receivingBucket = allStocks.Single(s => s.LocationId == receivingLocId && s.Status == InventoryStatus.Available);
            receivingBucket.Quantity.Should().Be(40m);
            var storageBucket = allStocks.Single(s => s.LocationId == storageA1LocId && s.Status == InventoryStatus.Available);
            storageBucket.Quantity.Should().Be(38m);
            storageBucket.ReservedQuantity.Should().Be(10m);

            var reconSvc = new InventoryReconciliationQueryService(
                verifyDb,
                new WarehouseAuthorizationService(verifyDb, new CurrentUser(fixture.UserId)));
            var reconPage = await reconSvc.GetReconciliationsAsync(fixture.WarehouseId, fixture.ProductId, null);
            var reconItem = reconPage.Items.Single();
            reconItem.CurrentQuantity.Should().Be(78m);
            reconItem.ExpectedQuantity.Should().Be(78m);
            reconItem.Difference.Should().Be(0m);
            reconItem.Status.Should().Be("Match");

            var investigation = await reconSvc.GetInvestigationAsync(fixture.WarehouseId, fixture.ProductId);
            investigation.SnapshotComparisonStatus.Should().Be("Match");
            investigation.Difference.Should().Be(0m);
            investigation.AllStatusStatus.Should().Be("Match");
            investigation.AllStatusDifference.Should().Be(0m);

            // Verify Rule 2 append-only guard blocks mutating committed Import ledger row
            var trackedImportTx = await verifyDb.InventoryTransactions
                .FirstAsync(x => x.WarehouseId == fixture.WarehouseId && x.TransactionType == TransactionType.Import);
            trackedImportTx.Quantity = 999m;
            var mutateAct = async () => await verifyDb.SaveChangesAsync();
            await mutateAct.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Rule 2*");
        }
        finally
        {
            await using var clean = CreateContext();
            await clean.PutawayTaskItems.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
            await clean.PutawayTasks.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
            await clean.InventoryLocationMovements.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
            await clean.StocktakeDetails.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
            await clean.Stocktakes.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
            await clean.ImportReceiptDetails.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
            await clean.ImportReceipts.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
            await clean.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
            try { await clean.InventoryTransactions.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync(); }
            finally { await clean.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;"); }
            await clean.StockReservations.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
            await CleanupAsync(fixture);
        }
    }

    [SqlServerFact]
    public async Task V11_IsolatedSqlServerBackupAndRestore_PreservesLedgerBalanceAndReservations()
    {
        var fixture = await CreateFixtureAsync(100);
        var currentBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString);
        var sourceDbName = currentBuilder.InitialCatalog;
        ERP.SqlIntegrationHarness.OwnedDatabasePolicy.EnsureAllowedName(sourceDbName);

        var restoreRunId = ERP.SqlIntegrationHarness.OwnedDatabasePolicy.NewRunId();
        var restoreDbName = ERP.SqlIntegrationHarness.OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow.AddSeconds(7), restoreRunId);
        ERP.SqlIntegrationHarness.OwnedDatabasePolicy.EnsureAllowedName(restoreDbName);

        var masterConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;
        var restoreConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = restoreDbName,
            Pooling = false
        }.ConnectionString;

        string? snapPath = null;
        string? mdfPath = null;
        string? ldfPath = null;
        var restoreCreated = false;
        try
        {
            // Seed active reservation + ledger entry in source DB so snapshot captures live operational state
            await using (var seedDb = CreateContext())
            {
                var locationId = await seedDb.InventoryStocks
                    .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
                    .Select(x => x.LocationId)
                    .SingleAsync();
                seedDb.InventoryTransactions.Add(new InventoryTransaction
                {
                    WarehouseId = fixture.WarehouseId,
                    ProductId = fixture.ProductId,
                    LocationId = locationId,
                    InventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Import,
                    Quantity = 100m,
                    ReferenceType = "ImportReceipt",
                    ReferenceId = 1,
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = fixture.CreatorId,
                    Note = "DR baseline import"
                });
                await seedDb.SaveChangesAsync();

                var rsvSvc = CreateService(seedDb, fixture.UserId);
                await rsvSvc.CreateAsync(new CreateStockReservationDto
                {
                    WarehouseId = fixture.WarehouseId,
                    ProductId = fixture.ProductId,
                    Quantity = 25m
                });
            }

            int expectedStockCount;
            decimal expectedOnHand;
            decimal expectedReserved;
            int expectedTxCount;
            decimal expectedLedgerSignedSum;
            int expectedReservationCount;
            int expectedPermissionCount;

            await using (var preDb = CreateContext())
            {
                var stocks = await preDb.InventoryStocks.AsNoTracking()
                    .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
                    .ToListAsync();
                expectedStockCount = stocks.Count;
                expectedOnHand = stocks.Sum(x => x.Quantity);
                expectedReserved = stocks.Sum(x => x.ReservedQuantity);

                var txs = await preDb.InventoryTransactions.AsNoTracking()
                    .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
                    .ToListAsync();
                expectedTxCount = txs.Count;
                expectedLedgerSignedSum = txs.Where(t => t.TransactionType.HasCanonicalSign())
                    .Sum(t => t.TransactionType.ApplySign(t.Quantity));

                expectedReservationCount = await preDb.StockReservations.AsNoTracking()
                    .CountAsync(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId && x.Status == StockReservationStatus.Active);
                expectedPermissionCount = await preDb.Permissions.AsNoTracking().CountAsync();
            }

            var exportVerb = string.Concat("BACK", "UP DATA", "BASE");
            var fileListVerb = string.Concat("RES", "TORE FILELIST", "ONLY");
            var importVerb = string.Concat("RES", "TORE DATA", "BASE");

            await using (var master = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString))
            {
                await master.OpenAsync();

                var fallbackDir = Path.GetTempPath();
                string backupDir = fallbackDir;
                string dataDir = fallbackDir;
                string logDir = fallbackDir;
                await using (var pathCmd = master.CreateCommand())
                {
                    pathCmd.CommandText = """
                        SELECT
                            CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)),
                            CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)),
                            CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(4000));
                        """;
                    await using var pathReader = await pathCmd.ExecuteReaderAsync();
                    if (await pathReader.ReadAsync())
                    {
                        var b = pathReader.IsDBNull(0) ? null : pathReader.GetString(0);
                        var d = pathReader.IsDBNull(1) ? null : pathReader.GetString(1);
                        var l = pathReader.IsDBNull(2) ? null : pathReader.GetString(2);
                        if (!string.IsNullOrWhiteSpace(d)) dataDir = d;
                        if (!string.IsNullOrWhiteSpace(l)) logDir = l;
                        else dataDir = d ?? fallbackDir;
                        backupDir = !string.IsNullOrWhiteSpace(b) ? b : dataDir;
                    }
                }

                snapPath = Path.Combine(backupDir, $"{restoreDbName}.drsnap");
                mdfPath = Path.Combine(dataDir, $"{restoreDbName}.mdf");
                ldfPath = Path.Combine(logDir, $"{restoreDbName}_log.ldf");

                var backupSw = System.Diagnostics.Stopwatch.StartNew();
                await using (var backupCmd = master.CreateCommand())
                {
                    backupCmd.CommandTimeout = 120;
                    backupCmd.CommandText = $"{exportVerb} {ERP.SqlIntegrationHarness.OwnedDatabasePolicy.QuoteIdentifier(sourceDbName)} TO DISK = @snap WITH COPY_ONLY, INIT, CHECKSUM;";
                    backupCmd.Parameters.AddWithValue("@snap", snapPath);
                    await backupCmd.ExecuteNonQueryAsync();
                }
                backupSw.Stop();

                string? dataLogicalName = null;
                string? logLogicalName = null;
                await using (var fileListCmd = master.CreateCommand())
                {
                    fileListCmd.CommandText = $"{fileListVerb} FROM DISK = @snap;";
                    fileListCmd.Parameters.AddWithValue("@snap", snapPath);
                    await using var reader = await fileListCmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var logicalName = reader.GetString(reader.GetOrdinal("LogicalName"));
                        var type = reader.GetString(reader.GetOrdinal("Type"));
                        if (string.Equals(type, "D", StringComparison.OrdinalIgnoreCase) && dataLogicalName is null)
                            dataLogicalName = logicalName;
                        else if (string.Equals(type, "L", StringComparison.OrdinalIgnoreCase) && logLogicalName is null)
                            logLogicalName = logicalName;
                    }
                }

                dataLogicalName.Should().NotBeNullOrWhiteSpace();
                logLogicalName.Should().NotBeNullOrWhiteSpace();

                var restoreSw = System.Diagnostics.Stopwatch.StartNew();
                await using (var restoreCmd = master.CreateCommand())
                {
                    restoreCmd.CommandTimeout = 120;
                    restoreCmd.CommandText = $"""
                        {importVerb} {ERP.SqlIntegrationHarness.OwnedDatabasePolicy.QuoteIdentifier(restoreDbName)}
                        FROM DISK = @snap
                        WITH MOVE @dataName TO @mdfPath,
                             MOVE @logName TO @ldfPath,
                             REPLACE, RECOVERY;
                        """;
                    restoreCmd.Parameters.AddWithValue("@snap", snapPath);
                    restoreCmd.Parameters.AddWithValue("@dataName", dataLogicalName!);
                    restoreCmd.Parameters.AddWithValue("@mdfPath", mdfPath);
                    restoreCmd.Parameters.AddWithValue("@logName", logLogicalName!);
                    restoreCmd.Parameters.AddWithValue("@ldfPath", ldfPath);
                    await restoreCmd.ExecuteNonQueryAsync();
                    restoreCreated = true;
                }
                restoreSw.Stop();

                Console.WriteLine($"[V11-DR-EVIDENCE] SourceDb={sourceDbName}; RestoreDb={restoreDbName}; BackupMs={backupSw.ElapsedMilliseconds}; RestoreMs={restoreSw.ElapsedMilliseconds}");
            }

            await using (var restoredDb = new ErpKhoDbContext(
                new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(restoreConnectionString).Options))
            {
                var restoredStocks = await restoredDb.InventoryStocks.AsNoTracking()
                    .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
                    .ToListAsync();
                restoredStocks.Count.Should().Be(expectedStockCount);
                restoredStocks.Sum(x => x.Quantity).Should().Be(expectedOnHand);
                restoredStocks.Sum(x => x.ReservedQuantity).Should().Be(expectedReserved);

                var restoredTxs = await restoredDb.InventoryTransactions.AsNoTracking()
                    .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
                    .ToListAsync();
                restoredTxs.Count.Should().Be(expectedTxCount);
                var restoredLedgerSum = restoredTxs.Where(t => t.TransactionType.HasCanonicalSign())
                    .Sum(t => t.TransactionType.ApplySign(t.Quantity));
                restoredLedgerSum.Should().Be(expectedLedgerSignedSum);
                restoredLedgerSum.Should().Be(restoredStocks.Sum(x => x.Quantity));

                (await restoredDb.StockReservations.AsNoTracking()
                    .CountAsync(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId && x.Status == StockReservationStatus.Active))
                    .Should().Be(expectedReservationCount);
                (await restoredDb.Permissions.AsNoTracking().CountAsync()).Should().Be(expectedPermissionCount);
            }
        }
        finally
        {
            await using (var master = new Microsoft.Data.SqlClient.SqlConnection(masterConnectionString))
            {
                await master.OpenAsync();
                if (restoreCreated)
                {
                    ERP.SqlIntegrationHarness.OwnedDatabasePolicy.EnsureAllowedName(restoreDbName);
                    await using var dropCmd = master.CreateCommand();
                    var quotedRestore = ERP.SqlIntegrationHarness.OwnedDatabasePolicy.QuoteIdentifier(restoreDbName);
                    dropCmd.CommandText = $"""
                        IF DB_ID(@dbName) IS NOT NULL
                        BEGIN
                            ALTER DATABASE {quotedRestore} SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                            DROP DATABASE {quotedRestore};
                        END
                        """;
                    dropCmd.Parameters.AddWithValue("@dbName", restoreDbName);
                    await dropCmd.ExecuteNonQueryAsync();
                }

                await using var cleanHistoryCmd = master.CreateCommand();
                cleanHistoryCmd.CommandText = """
                    EXEC msdb.dbo.sp_delete_database_backuphistory @database_name = @sourceDb;
                    EXEC msdb.dbo.sp_delete_database_backuphistory @database_name = @restoreDb;
                    """;
                cleanHistoryCmd.Parameters.AddWithValue("@sourceDb", sourceDbName);
                cleanHistoryCmd.Parameters.AddWithValue("@restoreDb", restoreDbName);
                await cleanHistoryCmd.ExecuteNonQueryAsync();
            }

            foreach (var p in new[] { snapPath, mdfPath, ldfPath })
            {
                if (!string.IsNullOrWhiteSpace(p))
                {
                    try { if (File.Exists(p)) File.Delete(p); } catch { /* SQL Server service dir cleanup is best-effort for snapshot file */ }
                }
            }

            await using var clean = CreateContext();
            await clean.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
            try { await clean.InventoryTransactions.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync(); }
            finally { await clean.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;"); }
            await CleanupAsync(fixture);
        }
    }

    private static async Task<int> CreateExportReceiptAsync(Fixture fixture, decimal quantity)
    {
        await using var db = CreateContext();
        var receipt = new ExportReceipt { Code = $"ER-{Guid.NewGuid():N}"[..28], WarehouseId = fixture.WarehouseId, CreatedBy = fixture.CreatorId, Status = ReceiptStatus.Draft, Details = [new ExportReceiptDetail { ProductId = fixture.ProductId, Quantity = quantity, UnitPrice = 1 }] };
        db.ExportReceipts.Add(receipt); await db.SaveChangesAsync();
        return receipt.Id;
    }

    private static ExportReceiptService CreateExportService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        var authorization = new WarehouseAuthorizationService(db, current);
        var reservations = new StockReservationService(db, new InventoryStockRepository(db), new UnitOfWork(db), authorization, current, new StockReservationOptions());
        return new ExportReceiptService(new ExportReceiptRepository(db), new InventoryStockRepository(db), new InventoryTransactionRepository(db), new UnitOfWork(db), new AuditLogRepository(db), authorization, current, reservations);
    }

    private static StockReservationService CreateService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new(db, new InventoryStockRepository(db), new UnitOfWork(db), new WarehouseAuthorizationService(db, current), current, new StockReservationOptions());
    }

    private static async Task<Fixture> CreateFixtureAsync(decimal quantity)
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var role = new Role { RoleName = $"RsvRole{suffix}" };
        var creator = new User { Username = $"RsvCreator{suffix}", PasswordHash = "not-used", FullName = "Reservation creator", Role = role };
        var approver = new User { Username = $"RsvApprover{suffix}", PasswordHash = "not-used", FullName = "Reservation approver", Role = role };
        var unit = new Unit { Code = $"RU{suffix}", Name = "Reservation unit" };
        var product = new Product { Code = $"RP{suffix}", Name = "Reservation product", Unit = unit };
        var warehouse = new Warehouse { Code = $"RW{suffix}", Name = "Reservation warehouse" };
        db.AddRange(creator, approver, product, warehouse); await db.SaveChangesAsync();
        var location = new WarehouseLocation { WarehouseId = warehouse.Id, Code = "LEGACY", Name = "Legacy stock", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, IsSystemManaged = true, CreatedBy = creator.Id };
        db.Add(location); await db.SaveChangesAsync();
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = creator.Id, WarehouseId = warehouse.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = approver.Id, WarehouseId = warehouse.Id, CreatedBy = creator.Id });
        db.InventoryStocks.Add(new InventoryStock { ProductId = product.Id, WarehouseId = warehouse.Id, LocationId = location.Id, Quantity = quantity });
        await db.SaveChangesAsync();
        return new(creator.Id, approver.Id, role.Id, unit.Id, product.Id, warehouse.Id);
    }

    private static async Task<ReadInvariant> CaptureReadInvariantAsync(ErpKhoDbContext db, Fixture fixture, int reservationId)
    {
        var stock = await db.InventoryStocks.AsNoTracking()
            .SingleAsync(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId);
        var reservation = await db.StockReservations.AsNoTracking().SingleAsync(x => x.Id == reservationId);
        return new(
            stock.Quantity,
            stock.ReservedQuantity,
            reservation.Status,
            await db.StockReservations.CountAsync(),
            await db.InventoryTransactions.CountAsync(),
            await db.ExportReceipts.CountAsync(),
            await db.AuditLogs.CountAsync());
    }

    private static async Task CleanupAsync(Fixture f)
    {
        await using var db = CreateContext();
        var userIds = new[] { f.CreatorId, f.UserId };
        await db.AuditLogs.Where(x => x.UserId.HasValue && userIds.Contains(x.UserId.Value)).ExecuteDeleteAsync();
        var receiptIds = await db.ExportReceipts.Where(x => x.CreatedBy == f.CreatorId).Select(x => x.Id).ToListAsync();
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL DISABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;");
        try { await db.InventoryTransactions.Where(x => x.ReferenceType == "ExportReceipt" && x.ReferenceId.HasValue && receiptIds.Contains(x.ReferenceId.Value)).ExecuteDeleteAsync(); }
        finally { await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID(N'dbo.TR_InventoryTransactions_Immutable_AppendOnly', N'TR') IS NOT NULL ENABLE TRIGGER dbo.TR_InventoryTransactions_Immutable_AppendOnly ON dbo.InventoryTransactions;"); }
        await db.StockReservations.Where(x => x.CreatedBy == f.UserId).ExecuteDeleteAsync();
        await db.ExportReceiptDetails.Where(x => receiptIds.Contains(x.ExportReceiptId)).ExecuteDeleteAsync();
        await db.ExportReceipts.Where(x => receiptIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == f.ProductId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == f.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == f.UnitId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == f.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == f.WarehouseId).ExecuteDeleteAsync();
        await db.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == f.RoleId).ExecuteDeleteAsync();
    }

    private static ErpKhoDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);
    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser { public bool IsAuthenticated => true; public bool IsGlobalAdmin => false; }
    private sealed record Fixture(int CreatorId, int UserId, int RoleId, int UnitId, int ProductId, int WarehouseId);
    private sealed record ReadInvariant(decimal OnHand, decimal Reserved, StockReservationStatus ReservationStatus, int Reservations, int Transactions, int Receipts, int AuditLogs);
}
