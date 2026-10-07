using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerInventoryStatusLotSerialTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task StatusChange_PreservesPhysicalOnHand_AndWritesFromToLedger()
    {
        var fixture = await CreateFixtureAsync(ProductTrackingType.None);
        try
        {
            await using (var db = CreateContext())
            {
                var service = CreateStatusService(db, fixture.UserId);
                var result = await service.ChangeAsync(new()
                {
                    InventoryStockId = fixture.StockId,
                    Quantity = 4,
                    ToStatus = "QUARANTINE",
                    Reason = "quality investigation"
                });
                result.FromStatus.Should().Be(nameof(InventoryStatus.Available));
                result.ToStatus.Should().Be(nameof(InventoryStatus.Quarantine));
                result.Quantity.Should().Be(4);
            }

            await using var verify = CreateContext();
            var stocks = await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .OrderBy(x => x.Status)
                .ToListAsync();
            stocks.Sum(x => x.Quantity).Should().Be(10);
            stocks.Single(x => x.Status == InventoryStatus.Available).Quantity.Should().Be(6);
            stocks.Single(x => x.Status == InventoryStatus.Quarantine).Quantity.Should().Be(4);

            var ledger = await verify.InventoryTransactions.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.TransactionType == TransactionType.StatusChange);
            ledger.Quantity.Should().Be(4);
            ledger.FromInventoryStatus.Should().Be(InventoryStatus.Available);
            ledger.ToInventoryStatus.Should().Be(InventoryStatus.Quarantine);
            ledger.InventoryStatus.Should().Be(InventoryStatus.Quarantine);
            ledger.ReferenceType.Should().Be("InventoryStatusChange");
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task StatusChange_CannotMoveReservedQuantity_AndUsesCanonicalError()
    {
        var fixture = await CreateFixtureAsync(ProductTrackingType.None);
        try
        {
            await using (var reserve = CreateContext())
            {
                await CreateReservationService(reserve, fixture.UserId).CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 8
                });
            }

            await using (var change = CreateContext())
            {
                var service = CreateStatusService(change, fixture.UserId);
                var act = () => service.ChangeAsync(new()
                {
                    InventoryStockId = fixture.StockId,
                    Quantity = 3,
                    ToStatus = "BLOCKED",
                    Reason = "must not move committed stock"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["HttpStatusCode"].Should().Be(409);
                thrown.Which.Data["ErrorCode"].Should().Be("INV_STATUS_CHANGE_NOT_ALLOWED");
            }

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking().SingleAsync(x => x.Id == fixture.StockId);
            stock.Status.Should().Be(InventoryStatus.Available);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(8);
            (await verify.InventoryTransactions.AsNoTracking()
                .CountAsync(x => x.ProductId == fixture.ProductId && x.TransactionType == TransactionType.StatusChange))
                .Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task GenericStatusChange_CannotReleaseQuarantineExpiredOrRecallBlockedToAvailable()
    {
        foreach (var sourceStatus in new[] { InventoryStatus.Quarantine, InventoryStatus.Expired, InventoryStatus.RecallBlocked })
        {
            var fixture = await CreateFixtureAsync(ProductTrackingType.None, sourceStatus);
            try
            {
                await using var db = CreateContext();
                var service = CreateStatusService(db, fixture.UserId);
                var act = () => service.ChangeAsync(new()
                {
                    InventoryStockId = fixture.StockId,
                    Quantity = 1,
                    ToStatus = "AVAILABLE",
                    Reason = "generic release must be blocked"
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("INV_STATUS_CHANGE_NOT_ALLOWED");
            }
            finally { await CleanupAsync(fixture); }
        }
    }

    [SqlServerFact]
    public async Task ReservationAndAllocation_UseFefo_AndExcludeExpiredLot()
    {
        var fixture = await CreateFixtureAsync(ProductTrackingType.Lot, createDefaultStock:false);
        try
        {
            int earlyLotId;
            int lateLotId;
            int expiredLotId;
            await using (var setup = CreateContext())
            {
                var now = DateTime.UtcNow;
                var early = new InventoryLot
                {
                    ProductId = fixture.ProductId,
                    LotNumber = $"EARLY-{fixture.Suffix}",
                    ManufactureDate = now.Date.AddDays(-30),
                    ExpiryDate = now.Date.AddDays(30),
                    ReceivedAt = now.AddDays(-5),
                    CreatedAt = now
                };
                var late = new InventoryLot
                {
                    ProductId = fixture.ProductId,
                    LotNumber = $"LATE-{fixture.Suffix}",
                    ManufactureDate = now.Date.AddDays(-20),
                    ExpiryDate = now.Date.AddDays(90),
                    ReceivedAt = now.AddDays(-10),
                    CreatedAt = now
                };
                var expired = new InventoryLot
                {
                    ProductId = fixture.ProductId,
                    LotNumber = $"OLD-{fixture.Suffix}",
                    ManufactureDate = now.Date.AddDays(-120),
                    ExpiryDate = now.Date.AddDays(-1),
                    ReceivedAt = now.AddDays(-100),
                    CreatedAt = now
                };
                setup.InventoryLots.AddRange(early, late, expired);
                await setup.SaveChangesAsync();
                earlyLotId = early.Id; lateLotId = late.Id; expiredLotId = expired.Id;
                setup.InventoryStocks.AddRange(
                    new InventoryStock { ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId, LocationId = fixture.LocationId, Status = InventoryStatus.Available, LotId = late.Id, Quantity = 5 },
                    new InventoryStock { ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId, LocationId = fixture.LocationId, Status = InventoryStatus.Available, LotId = expired.Id, Quantity = 5 },
                    new InventoryStock { ProductId = fixture.ProductId, WarehouseId = fixture.WarehouseId, LocationId = fixture.LocationId, Status = InventoryStatus.Available, LotId = early.Id, Quantity = 5 });
                await setup.SaveChangesAsync();
            }

            int reservationId;
            await using (var reserve = CreateContext())
            {
                reservationId = (await CreateReservationService(reserve, fixture.UserId).CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 5
                })).Id;
            }

            await using (var verifyReservation = CreateContext())
            {
                var stocks = await verifyReservation.InventoryStocks.AsNoTracking()
                    .Where(x => x.ProductId == fixture.ProductId)
                    .ToListAsync();
                stocks.Single(x => x.LotId == earlyLotId).ReservedQuantity.Should().Be(5);
                stocks.Single(x => x.LotId == lateLotId).ReservedQuantity.Should().Be(0);
                stocks.Single(x => x.LotId == expiredLotId).ReservedQuantity.Should().Be(0);
            }

            await using (var allocate = CreateContext())
            {
                var rows = await CreateAllocationService(allocate, fixture.UserId).AutoAllocateAsync(new()
                {
                    ReservationId = reservationId,
                    Quantity = 5
                });
                rows.Should().ContainSingle();
                rows.Single().LotId.Should().Be(earlyLotId);
                rows.Single().ExpiryDate.Should().BeCloseTo(DateTime.UtcNow.Date.AddDays(30), TimeSpan.FromDays(1));
                rows.Single().SelectionReason.Should().Contain("FEFO");
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task SerialReceiptIdentity_RequiresOneBaseUnit_AndRejectsDuplicateSerial()
    {
        var fixture = await CreateFixtureAsync(ProductTrackingType.Serial, createDefaultStock:false);
        try
        {
            int receiptId;
            int lineId;
            await using (var setup = CreateContext())
            {
                var receipt = new ImportReceipt
                {
                    Code = $"IR-{fixture.Suffix}",
                    WarehouseId = fixture.WarehouseId,
                    Status = ReceiptStatus.Received,
                    CreatedBy = fixture.UserId
                };
                var line = new ImportReceiptDetail
                {
                    ProductId = fixture.ProductId,
                    Quantity = 2,
                    ExpectedQuantity = 2,
                    ReceivedQuantity = 2,
                    AcceptedQuantity = 2,
                    BaseExpectedQuantity = 2,
                    BaseReceivedQuantity = 2,
                    BaseAcceptedQuantity = 2,
                    FinalReceivedQuantity = 2,
                    BaseFinalReceivedQuantity = 2,
                    OperationUnitId = fixture.UnitId,
                    OperationUnitCodeSnapshot = "EA",
                    BaseUnitId = fixture.UnitId,
                    BaseUnitCodeSnapshot = "EA",
                    ConversionFactor = 1,
                    ConversionVersion = 1
                };
                receipt.Details.Add(line);
                setup.ImportReceipts.Add(receipt);
                await setup.SaveChangesAsync();
                receiptId = receipt.Id; lineId = line.Id;
            }

            await using (var invalidQuantity = CreateContext())
            {
                var service = CreateIdentityService(invalidQuantity, fixture.UserId);
                var act = () => service.SetAsync(receiptId, new()
                {
                    Lines =
                    [
                        new SetImportReceiptInventoryIdentityLineDto
                        {
                            LineId = lineId,
                            TargetStatus = "AVAILABLE",
                            BaseQuantity = 2,
                            SerialNumber = $"SER-QTY-{fixture.Suffix}"
                        }
                    ]
                }, fixture.UserId);
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("SERIAL_QUANTITY_INVALID");
            }

            await using (var duplicate = CreateContext())
            {
                var service = CreateIdentityService(duplicate, fixture.UserId);
                var serial = $"SER-DUP-{fixture.Suffix}";
                var act = () => service.SetAsync(receiptId, new()
                {
                    Lines =
                    [
                        new SetImportReceiptInventoryIdentityLineDto { LineId = lineId, TargetStatus = "AVAILABLE", BaseQuantity = 1, SerialNumber = serial },
                        new SetImportReceiptInventoryIdentityLineDto { LineId = lineId, TargetStatus = "AVAILABLE", BaseQuantity = 1, SerialNumber = serial }
                    ]
                }, fixture.UserId);
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("SERIAL_DUPLICATE");
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    private static InventoryStatusService CreateStatusService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new InventoryStatusService(db, new WarehouseAuthorizationService(db, current), current);
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

    private static StockAllocationService CreateAllocationService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new StockAllocationService(db, new WarehouseAuthorizationService(db, current), current);
    }

    private static ReceiptInventoryIdentityService CreateIdentityService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new ReceiptInventoryIdentityService(db, new WarehouseAuthorizationService(db, current));
    }

    private static async Task<Fixture> CreateFixtureAsync(
        ProductTrackingType trackingType,
        InventoryStatus sourceStatus = InventoryStatus.Available,
        bool createDefaultStock = true)
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var role = new Role { RoleName = $"InvRole{suffix}" };
        var user = new User { Username = $"InvUser{suffix}", PasswordHash = "not-used", FullName = "Inventory user", Role = role };
        var unit = new Unit { Code = $"IU{suffix}", Name = "Inventory unit", DecimalPlaces = 4 };
        var product = new Product
        {
            Code = $"IP{suffix}",
            Name = "Inventory product",
            Unit = unit,
            TrackingType = trackingType,
            ExpiryControl = trackingType == ProductTrackingType.Lot
        };
        var warehouse = new Warehouse { Code = $"IW{suffix}", Name = "Inventory warehouse" };
        db.AddRange(user, product, warehouse);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"IL{suffix}",
            Name = "Inventory location",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        db.WarehouseLocations.Add(location);
        await db.SaveChangesAsync();
        db.UserWarehouses.Add(new UserWarehouse { UserId = user.Id, WarehouseId = warehouse.Id, CreatedBy = user.Id });

        var stockId = 0;
        if (createDefaultStock)
        {
            var stock = new InventoryStock
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                LocationId = location.Id,
                Status = sourceStatus,
                Quantity = 10
            };
            db.InventoryStocks.Add(stock);
            await db.SaveChangesAsync();
            stockId = stock.Id;
        }
        else
        {
            await db.SaveChangesAsync();
        }

        return new Fixture(suffix, user.Id, role.Id, unit.Id, product.Id, warehouse.Id, location.Id, stockId);
    }

    private static async Task CleanupAsync(Fixture fixture)
    {
        await using var db = CreateContext();
        await db.AuditLogs.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.InventoryTransactions.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.StockAllocations.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.StockReservations.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.ImportReceiptInventoryIdentities.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        var receiptIds = await db.ImportReceiptDetails
            .Where(x => x.ProductId == fixture.ProductId)
            .Select(x => x.ImportReceiptId)
            .Distinct()
            .ToListAsync();
        await db.ImportReceiptDetails.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        if (receiptIds.Count > 0) await db.ImportReceipts.Where(x => receiptIds.Contains(x.Id)).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.InventorySerials.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.InventoryLots.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fixture.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fixture.UnitId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fixture.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fixture.RoleId).ExecuteDeleteAsync();
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
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        int WarehouseId,
        int LocationId,
        int StockId);
}
