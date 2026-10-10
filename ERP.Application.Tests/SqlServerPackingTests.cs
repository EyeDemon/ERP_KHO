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
public sealed class SqlServerPackingTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task CompletedPicking_AutoCreatesOpenPackingSession_WithoutInventoryMutation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            await PrepareCompletedPickingAsync(fixture);

            await using var verify = CreateContext();
            var session = await verify.PackingSessions.AsNoTracking().SingleAsync();
            session.Status.Should().Be(PackingSessionStatus.Open);
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task PackQuantityAbovePicked_ReturnsCanonicalConflict_AndDoesNotMutateInventory()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var session = await PrepareCompletedPickingAsync(fixture);
            PackingSessionDto withHu;
            await using (var createHu = CreateContext())
            {
                withHu = await CreatePackingService(createHu, fixture.UserId).CreateHandlingUnitAsync(session.Id, new()
                {
                    Type = "Carton",
                    HuCode = "HU-OVERPACK",
                    Barcode = "HU-OVERPACK",
                    RowVersion = session.RowVersion!
                });
            }

            var hu = withHu.HandlingUnits.Single();
            await using (var pack = CreateContext())
            {
                var service = CreatePackingService(pack, fixture.UserId);
                var act = () => service.PackAsync(session.Id, new PackIntoHandlingUnitDto
                {
                    HandlingUnitId = hu.Id,
                    PickingTaskLineId = withHu.Lines.Single().PickingTaskLineId,
                    ProductBarcode = fixture.ProductCode,
                    Quantity = 11,
                    RowVersion = withHu.RowVersion!
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["HttpStatusCode"].Should().Be(409);
                thrown.Which.Data["ErrorCode"].Should().Be("PACK_QUANTITY_EXCEEDS_PICKED");
            }

            await using var verify = CreateContext();
            (await verify.HandlingUnitContents.CountAsync()).Should().Be(0);
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task PackCloseCompleteAndCloseSession_ConservesQuantity_AndDoesNotChangeOnHand()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var session = await PrepareCompletedPickingAsync(fixture);
            PackingSessionDto withHu;
            await using (var createHu = CreateContext())
            {
                withHu = await CreatePackingService(createHu, fixture.UserId).CreateHandlingUnitAsync(session.Id, new()
                {
                    Type = "Carton",
                    HuCode = "HU-CARTON-01",
                    Barcode = "HU-CARTON-01",
                    RowVersion = session.RowVersion!
                });
            }

            var hu = withHu.HandlingUnits.Single();
            PackingSessionDto packed;
            await using (var pack = CreateContext())
            {
                packed = await CreatePackingService(pack, fixture.UserId).PackAsync(session.Id, new()
                {
                    HandlingUnitId = hu.Id,
                    PickingTaskLineId = withHu.Lines.Single().PickingTaskLineId,
                    ProductBarcode = fixture.ProductCode,
                    Quantity = 10,
                    RowVersion = withHu.RowVersion!
                });
            }

            PackingSessionDto huClosed;
            await using (var closeHu = CreateContext())
            {
                huClosed = await CreatePackingService(closeHu, fixture.UserId).CloseHandlingUnitAsync(
                    session.Id,
                    hu.Id,
                    new PackingStateCommandDto { RowVersion = packed.RowVersion! });
            }

            PackingSessionDto completed;
            await using (var complete = CreateContext())
            {
                completed = await CreatePackingService(complete, fixture.UserId).CompleteAsync(
                    session.Id,
                    new PackingStateCommandDto { RowVersion = huClosed.RowVersion! });
                completed.Status.Should().Be(nameof(PackingSessionStatus.Packed));
                completed.PackedQuantity.Should().Be(10);
                completed.RemainingQuantity.Should().Be(0);
            }

            await using (var close = CreateContext())
            {
                var closed = await CreatePackingService(close, fixture.UserId).CloseAsync(
                    session.Id,
                    new PackingStateCommandDto { RowVersion = completed.RowVersion! });
                closed.Status.Should().Be(nameof(PackingSessionStatus.Closed));
            }

            await using var verify = CreateContext();
            (await verify.HandlingUnitContents.AsNoTracking().SumAsync(x => x.Quantity)).Should().Be(10);
            (await verify.HandlingUnits.AsNoTracking().SingleAsync()).Status.Should().Be(HandlingUnitStatus.Closed);
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task NestedHandlingUnit_CircularHierarchyIsRejected()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var session = await PrepareCompletedPickingAsync(fixture);
            PackingSessionDto first;
            await using (var create = CreateContext())
            {
                first = await CreatePackingService(create, fixture.UserId).CreateHandlingUnitAsync(session.Id, new()
                {
                    Type = "Carton",
                    HuCode = "HU-CHILD",
                    Barcode = "HU-CHILD",
                    RowVersion = session.RowVersion!
                });
            }

            PackingSessionDto second;
            await using (var create = CreateContext())
            {
                second = await CreatePackingService(create, fixture.UserId).CreateHandlingUnitAsync(session.Id, new()
                {
                    Type = "Pallet",
                    HuCode = "HU-PARENT",
                    Barcode = "HU-PARENT",
                    RowVersion = first.RowVersion!
                });
            }

            var childId = second.HandlingUnits.Single(x => x.HuCode == "HU-CHILD").Id;
            var parentId = second.HandlingUnits.Single(x => x.HuCode == "HU-PARENT").Id;
            PackingSessionDto nested;
            await using (var nest = CreateContext())
            {
                nested = await CreatePackingService(nest, fixture.UserId).NestHandlingUnitAsync(
                    session.Id,
                    childId,
                    new NestHandlingUnitDto
                    {
                        ParentHandlingUnitId = parentId,
                        RowVersion = second.RowVersion!
                    });
            }

            await using (var circular = CreateContext())
            {
                var service = CreatePackingService(circular, fixture.UserId);
                var act = () => service.NestHandlingUnitAsync(
                    session.Id,
                    parentId,
                    new NestHandlingUnitDto
                    {
                        ParentHandlingUnitId = childId,
                        RowVersion = nested.RowVersion!
                    });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("HU_CIRCULAR_NESTING");
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    private static async Task<PackingSessionDto> PrepareCompletedPickingAsync(Fixture fixture)
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
            await CreateAllocationService(allocate, fixture.UserId).AutoAllocateAsync(new()
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
            assigned = await service.AssignAsync(created.Id, new()
            {
                AssignedUserId = fixture.UserId,
                RowVersion = detail.RowVersion!
            });
        }

        PickingTaskDto started;
        await using (var start = CreateContext())
        {
            started = await CreatePickingService(start, fixture.UserId).StartAsync(assigned.Id, new()
            {
                RowVersion = assigned.RowVersion!
            });
        }

        PickingTaskDto picked;
        await using (var pick = CreateContext())
        {
            picked = await CreatePickingService(pick, fixture.UserId).PickAsync(started.Id, new()
            {
                TaskLineId = started.Lines.Single().Id,
                LocationBarcode = fixture.LocationCode,
                ProductBarcode = fixture.ProductCode,
                Quantity = 10,
                RowVersion = started.RowVersion!
            });
        }

        await using (var complete = CreateContext())
        {
            await CreatePickingService(complete, fixture.UserId, withPacking: true).CompleteAsync(picked.Id, new()
            {
                RowVersion = picked.RowVersion!
            });
        }

        await using var read = CreateContext();
        var packing = CreatePackingService(read, fixture.UserId);
        var createdSession = (await packing.ListAsync(fixture.WarehouseId)).Single();
        return await packing.GetAsync(createdSession.Id);
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
        return new StockAllocationService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            new PickingTaskIntegration(db));
    }

    private static PickingService CreatePickingService(ErpKhoDbContext db, int userId, bool withPacking = false)
    {
        var current = new CurrentUser(userId);
        return new PickingService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            withPacking ? new PackingSessionIntegration(db) : null);
    }

    private static PackingService CreatePackingService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new PackingService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            new PackingSessionIntegration(db));
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var role = new Role { RoleName = $"PackRole{suffix}" };
        var user = new User
        {
            Username = $"PackUser{suffix}",
            PasswordHash = "not-used",
            FullName = "Packing user",
            Role = role
        };
        var unit = new Unit { Code = $"PKU{suffix}", Name = "Packing unit", DecimalPlaces = 4 };
        var product = new Product { Code = $"PKP{suffix}", Name = "Packing product", Unit = unit };
        var warehouse = new Warehouse { Code = $"PKW{suffix}", Name = "Packing warehouse" };
        db.AddRange(user, product, warehouse);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"PKL{suffix}",
            Name = "Packing source",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        db.Add(location);
        await db.SaveChangesAsync();

        db.UserWarehouses.Add(new UserWarehouse
        {
            UserId = user.Id,
            WarehouseId = warehouse.Id,
            CreatedBy = user.Id
        });
        db.InventoryStocks.Add(new InventoryStock
        {
            ProductId = product.Id,
            WarehouseId = warehouse.Id,
            LocationId = location.Id,
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
            location.Id,
            location.Code);
    }

    private static async Task CleanupAsync(Fixture fixture)
    {
        await using var db = CreateContext();
        await db.AuditLogs.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.HandlingUnitContents
            .Where(x => x.HandlingUnit.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.HandlingUnits.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.PackingSessions.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
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
        int LocationId,
        string LocationCode);
}
