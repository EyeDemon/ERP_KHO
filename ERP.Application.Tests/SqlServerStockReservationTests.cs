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
                await CreateExportService(db, fixture.UserId).DispatchAsync(holdReceiptId, fixture.UserId);

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
                try { await CreateExportService(db, fixture.UserId).DispatchAsync(receiptId, fixture.UserId); return true; }
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
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = creator.Id, WarehouseId = warehouse.Id, CreatedBy = creator.Id },
            new UserWarehouse { UserId = approver.Id, WarehouseId = warehouse.Id, CreatedBy = creator.Id });
        db.InventoryStocks.Add(new InventoryStock { ProductId = product.Id, WarehouseId = warehouse.Id, Quantity = quantity });
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
        await db.InventoryTransactions.Where(x => x.ReferenceType == "ExportReceipt" && x.ReferenceId.HasValue && receiptIds.Contains(x.ReferenceId.Value)).ExecuteDeleteAsync();
        await db.StockReservations.Where(x => x.CreatedBy == f.UserId).ExecuteDeleteAsync();
        await db.ExportReceiptDetails.Where(x => receiptIds.Contains(x.ExportReceiptId)).ExecuteDeleteAsync();
        await db.ExportReceipts.Where(x => receiptIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == f.ProductId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => userIds.Contains(x.UserId)).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == f.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == f.UnitId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == f.WarehouseId).ExecuteDeleteAsync();
        await db.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == f.RoleId).ExecuteDeleteAsync();
    }

    private static ErpKhoDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);
    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser { public bool IsAuthenticated => true; public bool IsGlobalAdmin => false; }
    private sealed record Fixture(int CreatorId, int UserId, int RoleId, int UnitId, int ProductId, int WarehouseId);
    private sealed record ReadInvariant(decimal OnHand, decimal Reserved, StockReservationStatus ReservationStatus, int Reservations, int Transactions, int Receipts, int AuditLogs);
}
