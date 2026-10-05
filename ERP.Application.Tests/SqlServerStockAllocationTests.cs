using ERP.Application.DTOs;
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
public sealed class SqlServerStockAllocationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task ReleasingUnallocatedReservation_DoesNotStealBucketCommittedToAnotherAllocation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int firstReservationId;
            int allocatedReservationId;
            await using (var reserve = CreateContext())
            {
                var service = CreateReservationService(reserve, fixture.UserId);
                firstReservationId = (await service.CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 10
                })).Id;
                allocatedReservationId = (await service.CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 10
                })).Id;
            }

            await using (var allocate = CreateContext())
            {
                var rows = await CreateAllocationService(allocate, fixture.UserId).AutoAllocateAsync(new()
                {
                    ReservationId = allocatedReservationId,
                    Quantity = 10
                });
                rows.Should().ContainSingle();
                rows.Single().LocationId.Should().Be(fixture.FirstLocationId);
            }

            await using (var release = CreateContext())
            {
                await CreateReservationService(release, fixture.UserId).ReleaseAsync(
                    firstReservationId,
                    new ReleaseStockReservationDto { Quantity = 10, Reason = "release unallocated reservation" });
            }

            await using (var verify = CreateContext())
            {
                var stocks = await verify.InventoryStocks.AsNoTracking()
                    .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                    .OrderBy(x => x.LocationId)
                    .ToListAsync();
                stocks.Single(x => x.LocationId == fixture.FirstLocationId).ReservedQuantity.Should().Be(10);
                stocks.Single(x => x.LocationId == fixture.SecondLocationId).ReservedQuantity.Should().Be(0);

                var reservation = await verify.StockReservations.SingleAsync(x => x.Id == allocatedReservationId);
                await CreateReservationService(verify, fixture.UserId).ConsumeAsync(reservation, fixture.UserId);
                await new UnitOfWork(verify).SaveChangesAsync();
            }

            await using var final = CreateContext();
            var finalStocks = await final.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .OrderBy(x => x.LocationId)
                .ToListAsync();
            finalStocks.Single(x => x.LocationId == fixture.FirstLocationId).Quantity.Should().Be(0);
            finalStocks.Single(x => x.LocationId == fixture.FirstLocationId).ReservedQuantity.Should().Be(0);
            finalStocks.Single(x => x.LocationId == fixture.SecondLocationId).Quantity.Should().Be(10);
            finalStocks.Single(x => x.LocationId == fixture.SecondLocationId).ReservedQuantity.Should().Be(0);
            (await final.StockReservations.AsNoTracking().SingleAsync(x => x.Id == allocatedReservationId)).Status
                .Should().Be(StockReservationStatus.Consumed);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task PartialReservationRelease_PreservesAllocationAndReleasesOnlyUnallocatedPool()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int reservationId;
            await using (var reserve = CreateContext())
            {
                reservationId = (await CreateReservationService(reserve, fixture.UserId).CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 20
                })).Id;
            }

            await using (var allocate = CreateContext())
                await CreateAllocationService(allocate, fixture.UserId).AutoAllocateAsync(new()
                {
                    ReservationId = reservationId,
                    Quantity = 10
                });

            await using (var release = CreateContext())
                await CreateReservationService(release, fixture.UserId).ReleaseAsync(
                    reservationId,
                    new ReleaseStockReservationDto { Quantity = 5, Reason = "partial release" });

            await using var verify = CreateContext();
            var saved = await verify.StockReservations.AsNoTracking().SingleAsync(x => x.Id == reservationId);
            saved.ReleasedQuantity.Should().Be(5);
            saved.AllocatedQuantity.Should().Be(10);
            saved.Status.Should().Be(StockReservationStatus.PartiallyAllocated);

            var allocation = await verify.StockAllocations.AsNoTracking().SingleAsync(x => x.ReservationId == reservationId);
            allocation.Status.Should().Be(StockAllocationStatus.Active);

            var stocks = await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .OrderBy(x => x.LocationId)
                .ToListAsync();
            stocks.Sum(x => x.ReservedQuantity).Should().Be(15);
            stocks.Single(x => x.LocationId == fixture.FirstLocationId).ReservedQuantity.Should().Be(10);
            stocks.Single(x => x.LocationId == fixture.SecondLocationId).ReservedQuantity.Should().Be(5);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task FullReservationRelease_ReleasesOwnAllocationAndAllReservedQuantityAtomically()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int reservationId;
            await using (var reserve = CreateContext())
                reservationId = (await CreateReservationService(reserve, fixture.UserId).CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 10
                })).Id;

            await using (var allocate = CreateContext())
                await CreateAllocationService(allocate, fixture.UserId).AutoAllocateAsync(new()
                {
                    ReservationId = reservationId,
                    Quantity = 10
                });

            await using (var release = CreateContext())
                await CreateReservationService(release, fixture.UserId).ReleaseAsync(
                    reservationId,
                    new ReleaseStockReservationDto { Reason = "full release" });

            await using var verify = CreateContext();
            var reservation = await verify.StockReservations.AsNoTracking().SingleAsync(x => x.Id == reservationId);
            reservation.Status.Should().Be(StockReservationStatus.Released);
            reservation.ReleasedQuantity.Should().Be(10);
            reservation.AllocatedQuantity.Should().Be(0);
            (await verify.StockAllocations.AsNoTracking().SingleAsync(x => x.ReservationId == reservationId)).Status
                .Should().Be(StockAllocationStatus.Released);
            (await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId && x.WarehouseId == fixture.WarehouseId)
                .SumAsync(x => x.ReservedQuantity)).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
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

    private static async Task<Fixture> CreateFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var role = new Role { RoleName = $"AllocRole{suffix}" };
        var user = new User { Username = $"AllocUser{suffix}", PasswordHash = "not-used", FullName = "Allocation user", Role = role };
        var unit = new Unit { Code = $"AU{suffix}", Name = "Allocation unit" };
        var product = new Product { Code = $"AP{suffix}", Name = "Allocation product", Unit = unit };
        var warehouse = new Warehouse { Code = $"AW{suffix}", Name = "Allocation warehouse" };
        db.AddRange(user, product, warehouse);
        await db.SaveChangesAsync();

        var first = new WarehouseLocation
        {
            WarehouseId = warehouse.Id, Code = $"A1{suffix}", Name = "Allocation location 1",
            LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, CreatedBy = user.Id
        };
        var second = new WarehouseLocation
        {
            WarehouseId = warehouse.Id, Code = $"A2{suffix}", Name = "Allocation location 2",
            LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true, CreatedBy = user.Id
        };
        db.AddRange(first, second);
        await db.SaveChangesAsync();

        db.UserWarehouses.Add(new UserWarehouse { UserId = user.Id, WarehouseId = warehouse.Id, CreatedBy = user.Id });
        db.InventoryStocks.AddRange(
            new InventoryStock { ProductId = product.Id, WarehouseId = warehouse.Id, LocationId = first.Id, Quantity = 10, Status = InventoryStatus.Available },
            new InventoryStock { ProductId = product.Id, WarehouseId = warehouse.Id, LocationId = second.Id, Quantity = 10, Status = InventoryStatus.Available });
        await db.SaveChangesAsync();

        return new Fixture(user.Id, role.Id, unit.Id, product.Id, warehouse.Id, first.Id, second.Id);
    }

    private static async Task CleanupAsync(Fixture fixture)
    {
        await using var db = CreateContext();
        await db.AuditLogs.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.StockAllocations.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.StockReservations.Where(x => x.CreatedBy == fixture.UserId).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fixture.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fixture.UnitId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Warehouses.Where(x => x.Id == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == fixture.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == fixture.RoleId).ExecuteDeleteAsync();
    }

    private static ErpKhoDbContext CreateContext() => new(
        new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

    private sealed record CurrentUser(int UserId, string Role = "Manager") : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }

    private sealed record Fixture(int UserId, int RoleId, int UnitId, int ProductId, int WarehouseId, int FirstLocationId, int SecondLocationId);
}
