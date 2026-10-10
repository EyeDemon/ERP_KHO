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
public sealed class SqlServerPickingTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task FullAllocation_CreatesOpenPickingTask_WithoutChangingOnHand()
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
                    Quantity = 10
                })).Id;
            }

            await using (var allocate = CreateContext())
            {
                var rows = await CreateAllocationService(allocate, fixture.UserId, withPicking: true).AutoAllocateAsync(new()
                {
                    ReservationId = reservationId,
                    Quantity = 10
                });
                rows.Should().ContainSingle();
            }

            await using var verify = CreateContext();
            var task = await verify.PickingTasks.AsNoTracking()
                .Include(x => x.Lines)
                .SingleAsync();
            task.Status.Should().Be(PickingTaskStatus.Open);
            task.Lines.Should().ContainSingle();
            task.Lines.Single().RequestedQuantity.Should().Be(10);
            task.Lines.Single().PickedQuantity.Should().Be(0);

            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.FirstLocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
            (await verify.StockAllocations.AsNoTracking().SingleAsync()).Status.Should().Be(StockAllocationStatus.Active);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task WrongLocationScan_ReturnsCanonicalConflictCode_AndDoesNotMutateStock()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var task = await PrepareStartedTaskAsync(fixture);
            await using var db = CreateContext();
            var service = CreatePickingService(db, fixture.UserId);

            var act = () => service.PickAsync(task.Id, new PickScanDto
            {
                TaskLineId = task.Lines.Single().Id,
                LocationBarcode = "WRONG-LOCATION",
                ProductBarcode = fixture.ProductCode,
                Quantity = 1,
                RowVersion = task.RowVersion!
            });

            var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
            thrown.Which.Data["HttpStatusCode"].Should().Be(409);
            thrown.Which.Data["ErrorCode"].Should().Be("PICK_WRONG_LOCATION");

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.FirstLocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
            (await verify.PickingTaskLines.AsNoTracking().SingleAsync()).PickedQuantity.Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task CompletedPicking_MarksAllocationPicked_ButKeepsInventoryUnchanged()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var task = await PrepareStartedTaskAsync(fixture);
            PickingTaskDto picked;
            await using (var pick = CreateContext())
            {
                picked = await CreatePickingService(pick, fixture.UserId).PickAsync(task.Id, new PickScanDto
                {
                    TaskLineId = task.Lines.Single().Id,
                    LocationBarcode = fixture.FirstLocationCode,
                    ProductBarcode = fixture.ProductCode,
                    Quantity = 10,
                    RowVersion = task.RowVersion!
                });
            }

            await using (var complete = CreateContext())
            {
                var completed = await CreatePickingService(complete, fixture.UserId).CompleteAsync(task.Id, new PickingStateCommandDto
                {
                    RowVersion = picked.RowVersion!
                });
                completed.Status.Should().Be(nameof(PickingTaskStatus.Completed));
            }

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.FirstLocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
            (await verify.StockAllocations.AsNoTracking().SingleAsync()).Status.Should().Be(StockAllocationStatus.Picked);
            (await verify.StockReservations.AsNoTracking().SingleAsync()).Status.Should().Be(StockReservationStatus.Allocated);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task AllocationLinkedToPicking_CannotBeReleasedOrReallocatedDirectly()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            int reservationId;
            int allocationId;
            await using (var reserve = CreateContext())
            {
                reservationId = (await CreateReservationService(reserve, fixture.UserId).CreateAsync(new()
                {
                    ProductId = fixture.ProductId,
                    WarehouseId = fixture.WarehouseId,
                    Quantity = 10
                })).Id;
            }

            await using (var allocate = CreateContext())
            {
                allocationId = (await CreateAllocationService(allocate, fixture.UserId, withPicking: true).AutoAllocateAsync(new()
                {
                    ReservationId = reservationId,
                    Quantity = 10
                })).Single().Id;
            }

            await using (var release = CreateContext())
            {
                var service = CreateAllocationService(release, fixture.UserId, withPicking: true);
                await FluentActions.Awaiting(() => service.ReleaseAsync(
                        allocationId,
                        new ReleaseStockAllocationDto { Reason = "must be blocked" }))
                    .Should().ThrowAsync<ConcurrencyException>();
            }

            await using (var reallocate = CreateContext())
            {
                var service = CreateAllocationService(reallocate, fixture.UserId, withPicking: true);
                await FluentActions.Awaiting(() => service.ReallocateAsync(
                        allocationId,
                        new ReallocateStockAllocationDto { LocationId = fixture.SecondLocationId, Reason = "must be blocked" }))
                    .Should().ThrowAsync<ConcurrencyException>();
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task OpenPickingTask_ManualReservationRelease_CancelsTaskAndReleasesReservedStock()
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
                    Quantity = 10
                })).Id;
            }

            await using (var allocate = CreateContext())
            {
                await CreateAllocationService(allocate, fixture.UserId, withPicking: true).AutoAllocateAsync(new()
                {
                    ReservationId = reservationId,
                    Quantity = 10
                });
            }

            await using (var release = CreateContext())
            {
                await CreateReservationService(release, fixture.UserId, withPicking: true).ReleaseAsync(
                    reservationId,
                    new ReleaseStockReservationDto { Reason = "manual reservation cancelled before picking starts" });
            }

            await using var verify = CreateContext();
            (await verify.PickingTasks.AsNoTracking().SingleAsync()).Status.Should().Be(PickingTaskStatus.Cancelled);
            (await verify.StockAllocations.AsNoTracking().SingleAsync()).Status.Should().Be(StockAllocationStatus.Released);
            (await verify.StockReservations.AsNoTracking().SingleAsync()).Status.Should().Be(StockReservationStatus.Released);
            (await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId)
                .SumAsync(x => x.ReservedQuantity)).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task InProgressPicking_BlocksReservationRelease_AndPreservesCommitment()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var task = await PrepareStartedTaskAsync(fixture);

            int reservationId;
            await using (var lookup = CreateContext())
            {
                reservationId = await lookup.PickingTaskLines.AsNoTracking()
                    .Where(x => x.PickingTaskId == task.Id)
                    .Select(x => x.Allocation.ReservationId)
                    .SingleAsync();
            }

            await using (var release = CreateContext())
            {
                var service = CreateReservationService(release, fixture.UserId, withPicking: true);
                await FluentActions.Awaiting(() => service.ReleaseAsync(
                        reservationId,
                        new ReleaseStockReservationDto { Reason = "must be blocked after movement starts" }))
                    .Should().ThrowAsync<ConcurrencyException>();
            }

            await using var verify = CreateContext();
            (await verify.PickingTasks.AsNoTracking().SingleAsync()).Status.Should().Be(PickingTaskStatus.InProgress);
            (await verify.StockAllocations.AsNoTracking().SingleAsync()).Status.Should().Be(StockAllocationStatus.Picking);
            (await verify.InventoryStocks.AsNoTracking()
                .Where(x => x.ProductId == fixture.ProductId)
                .SumAsync(x => x.ReservedQuantity)).Should().Be(10);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task CompletedPicking_ProtectsReservationPastOriginalExpiry_UntilDispatchConsumption()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var task = await PrepareStartedTaskAsync(fixture);
            PickingTaskDto picked;
            await using (var pick = CreateContext())
            {
                picked = await CreatePickingService(pick, fixture.UserId).PickAsync(task.Id, new PickScanDto
                {
                    TaskLineId = task.Lines.Single().Id,
                    LocationBarcode = fixture.FirstLocationCode,
                    ProductBarcode = fixture.ProductCode,
                    Quantity = 10,
                    RowVersion = task.RowVersion!
                });
            }

            await using (var complete = CreateContext())
            {
                await CreatePickingService(complete, fixture.UserId).CompleteAsync(task.Id, new PickingStateCommandDto
                {
                    RowVersion = picked.RowVersion!
                });
            }

            int reservationId;
            await using (var expire = CreateContext())
            {
                reservationId = await expire.StockReservations.AsNoTracking().Select(x => x.Id).SingleAsync();
                var oldCreated = DateTime.UtcNow.AddHours(-2);
                var oldExpiry = DateTime.UtcNow.AddHours(-1);
                await expire.StockReservations
                    .Where(x => x.Id == reservationId)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.CreatedAt, oldCreated)
                        .SetProperty(x => x.ExpiresAt, oldExpiry));

                var expiredCount = await CreateReservationService(expire, fixture.UserId, withPicking: true).ExpireAsync();
                expiredCount.Should().Be(0);
            }

            await using (var dispatch = CreateContext())
            {
                var reservation = await dispatch.StockReservations.SingleAsync(x => x.Id == reservationId);
                var reservationService = CreateReservationService(dispatch, fixture.UserId, withPicking: true);
                var consumed = await reservationService.ConsumeAsync(reservation, fixture.UserId);
                consumed.Sum(x => x.Quantity).Should().Be(10);
                await new UnitOfWork(dispatch).SaveChangesAsync();
            }

            await using var verify = CreateContext();
            (await verify.StockReservations.AsNoTracking().SingleAsync()).Status.Should().Be(StockReservationStatus.Consumed);
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.FirstLocationId);
            stock.Quantity.Should().Be(0);
            stock.ReservedQuantity.Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    private static async Task<PickingTaskDto> PrepareStartedTaskAsync(Fixture fixture)
    {
        int reservationId;
        await using (var reserve = CreateContext())
        {
            reservationId = (await CreateReservationService(reserve, fixture.UserId).CreateAsync(new()
            {
                ProductId = fixture.ProductId,
                WarehouseId = fixture.WarehouseId,
                Quantity = 10
            })).Id;
        }

        await using (var allocate = CreateContext())
        {
            await CreateAllocationService(allocate, fixture.UserId, withPicking: true).AutoAllocateAsync(new()
            {
                ReservationId = reservationId,
                Quantity = 10
            });
        }

        PickingTaskDto assigned;
        await using (var assign = CreateContext())
        {
            var service = CreatePickingService(assign, fixture.UserId);
            var created = (await service.ListAsync(fixture.WarehouseId)).Single();
            var detail = await service.GetAsync(created.Id);
            assigned = await service.AssignAsync(created.Id, new PickingStateCommandDto
            {
                AssignedUserId = fixture.UserId,
                RowVersion = detail.RowVersion!
            });
        }

        await using var start = CreateContext();
        return await CreatePickingService(start, fixture.UserId).StartAsync(assigned.Id, new PickingStateCommandDto
        {
            RowVersion = assigned.RowVersion!
        });
    }

    private static StockReservationService CreateReservationService(
        ErpKhoDbContext db,
        int userId,
        bool withPicking = false)
    {
        var current = new CurrentUser(userId);
        return new StockReservationService(
            db,
            new InventoryStockRepository(db),
            new UnitOfWork(db),
            new WarehouseAuthorizationService(db, current),
            current,
            new StockReservationOptions(),
            withPicking ? new PickingTaskIntegration(db) : null);
    }

    private static StockAllocationService CreateAllocationService(
        ErpKhoDbContext db,
        int userId,
        bool withPicking)
    {
        var current = new CurrentUser(userId);
        return new StockAllocationService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            withPicking ? new PickingTaskIntegration(db) : null);
    }

    private static PickingService CreatePickingService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new PickingService(db, new WarehouseAuthorizationService(db, current), current);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var role = new Role { RoleName = $"PickRole{suffix}" };
        var user = new User
        {
            Username = $"PickUser{suffix}",
            PasswordHash = "not-used",
            FullName = "Picking user",
            Role = role
        };
        var unit = new Unit { Code = $"PU{suffix}", Name = "Picking unit", DecimalPlaces = 4 };
        var product = new Product { Code = $"PP{suffix}", Name = "Picking product", Unit = unit };
        var warehouse = new Warehouse { Code = $"PW{suffix}", Name = "Picking warehouse" };
        db.AddRange(user, product, warehouse);
        await db.SaveChangesAsync();

        var first = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"P1{suffix}",
            Name = "Picking location 1",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var second = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"P2{suffix}",
            Name = "Picking location 2",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        db.AddRange(first, second);
        await db.SaveChangesAsync();

        db.UserWarehouses.Add(new UserWarehouse
        {
            UserId = user.Id,
            WarehouseId = warehouse.Id,
            CreatedBy = user.Id
        });
        db.InventoryStocks.AddRange(
            new InventoryStock
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                LocationId = first.Id,
                Quantity = 10,
                Status = InventoryStatus.Available
            },
            new InventoryStock
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                LocationId = second.Id,
                Quantity = 10,
                Status = InventoryStatus.Available
            });
        await db.SaveChangesAsync();

        return new Fixture(
            user.Id,
            role.Id,
            unit.Id,
            product.Id,
            product.Code,
            warehouse.Id,
            first.Id,
            first.Code,
            second.Id);
    }

    private static async Task CleanupAsync(Fixture fixture)
    {
        await using var db = CreateContext();
        await db.AuditLogs.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.ShortPickExceptions
            .Where(x => x.PickingTaskLine.PickingTask.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.PickingTaskLines
            .Where(x => x.PickingTask.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.PickingTasks.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
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
        string ProductCode,
        int WarehouseId,
        int FirstLocationId,
        string FirstLocationCode,
        int SecondLocationId);
}
