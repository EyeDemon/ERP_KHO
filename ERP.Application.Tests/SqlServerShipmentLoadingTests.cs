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
public sealed class SqlServerShipmentLoadingTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task PackedNestedHu_AutoCreatesReadyShipment_WithRootOnly_WithoutInventoryMutation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);

            await using var verify = CreateContext();
            var shipment = await verify.Shipments.AsNoTracking().SingleAsync();
            shipment.Status.Should().Be(ShipmentStatus.Ready);
            var link = await verify.ShipmentHandlingUnits.AsNoTracking().SingleAsync();
            link.HandlingUnitId.Should().Be(prepared.ParentHuId);
            (await verify.HandlingUnits.AsNoTracking().CountAsync()).Should().Be(2);
            (await verify.HandlingUnits.AsNoTracking().AllAsync(x => x.Status == HandlingUnitStatus.Closed)).Should().BeTrue();

            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
            (await verify.InventoryTransactions.CountAsync(x => x.WarehouseId == fixture.WarehouseId)).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task StageLoadComplete_TransitionsWholeHuHierarchy_WithoutOnHandMutation()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            ShipmentDto staged;
            await using (var stage = CreateContext())
            {
                var service = CreateShipmentService(stage, fixture.UserId);
                var current = await service.GetAsync(prepared.ShipmentId);
                staged = await service.StageAsync(prepared.ShipmentId, new StageShipmentDto
                {
                    StagingLocationCode = fixture.StagingLocationCode,
                    RowVersion = current.RowVersion!
                });
                staged.Status.Should().Be(nameof(ShipmentStatus.Staging));
                staged.StagingLocationId.Should().Be(fixture.StagingLocationId);
                staged.StagingLocationCode.Should().Be(fixture.StagingLocationCode);
            }

            int appointmentId;
            await using (var dock = CreateContext())
            {
                appointmentId = await CreateOutboundAppointmentAsync(dock, fixture, DockAppointmentStatus.InService);
            }

            ShipmentDto loading;
            await using (var start = CreateContext())
            {
                loading = await CreateShipmentService(start, fixture.UserId).StartLoadingAsync(
                    prepared.ShipmentId,
                    new StartShipmentLoadingDto
                    {
                        DockAppointmentId = appointmentId,
                        RowVersion = staged.RowVersion!
                    });
                loading.Status.Should().Be(nameof(ShipmentStatus.Loading));
                loading.VehiclePlate.Should().Be("51C-12345");
            }

            ShipmentDto huLoaded;
            await using (var load = CreateContext())
            {
                huLoaded = await CreateShipmentService(load, fixture.UserId).LoadHandlingUnitAsync(
                    prepared.ShipmentId,
                    new LoadShipmentHandlingUnitDto
                    {
                        HandlingUnitBarcode = prepared.ParentHuBarcode,
                        RowVersion = loading.RowVersion!
                    });
                huLoaded.LoadedHandlingUnitCount.Should().Be(1);
            }

            await using (var verifyHierarchy = CreateContext())
            {
                var statuses = await verifyHierarchy.HandlingUnits.AsNoTracking()
                    .Where(x => x.WarehouseId == fixture.WarehouseId)
                    .Select(x => x.Status)
                    .ToListAsync();
                statuses.Should().OnlyContain(x => x == HandlingUnitStatus.Loaded);
            }

            ShipmentDto loaded;
            await using (var complete = CreateContext())
            {
                loaded = await CreateShipmentService(complete, fixture.UserId).CompleteLoadingAsync(
                    prepared.ShipmentId,
                    new CompleteShipmentLoadingDto
                    {
                        SealNumber = "seal-001",
                        RowVersion = huLoaded.RowVersion!
                    });
                loaded.Status.Should().Be(nameof(ShipmentStatus.Loaded));
                loaded.SealNumber.Should().Be("SEAL-001");
            }

            await using (var readiness = CreateContext())
            {
                await CreatePackingService(readiness, fixture.UserId).EnsureSourceReadyAsync(
                    "ExportReceipt",
                    fixture.ExportReceiptId);
                await CreateShipmentService(readiness, fixture.UserId).EnsureSourceReadyAsync(
                    "ExportReceipt",
                    fixture.ExportReceiptId);
            }

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);
            (await verify.InventoryTransactions.CountAsync()).Should().Be(0);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task DispatchReadiness_RejectsCanonicalShipmentBeforeLoaded()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            ShipmentDto staged;
            await using (var stage = CreateContext())
            {
                var shipmentService = CreateShipmentService(stage, fixture.UserId);
                var current = await shipmentService.GetAsync(prepared.ShipmentId);
                staged = await shipmentService.StageAsync(prepared.ShipmentId, new StageShipmentDto
                {
                    StagingLocationCode = fixture.StagingLocationCode,
                    RowVersion = current.RowVersion!
                });
            }

            await using var db = CreateContext();
            await CreatePackingService(db, fixture.UserId).EnsureSourceReadyAsync(
                "ExportReceipt",
                fixture.ExportReceiptId);

            var service = CreateShipmentService(db, fixture.UserId);
            await FluentActions.Awaiting(() => service.EnsureSourceReadyAsync(
                    "ExportReceipt",
                    fixture.ExportReceiptId))
                .Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>()
                .WithMessage("*chưa ở trạng thái LOADED*");
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task StartLoading_RequiresOutboundInServiceAppointment()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            ShipmentDto staged;
            await using (var stage = CreateContext())
            {
                var service = CreateShipmentService(stage, fixture.UserId);
                var current = await service.GetAsync(prepared.ShipmentId);
                staged = await service.StageAsync(prepared.ShipmentId, new StageShipmentDto
                {
                    StagingLocationCode = fixture.StagingLocationCode,
                    RowVersion = current.RowVersion!
                });
            }

            int appointmentId;
            await using (var dock = CreateContext())
            {
                appointmentId = await CreateOutboundAppointmentAsync(dock, fixture, DockAppointmentStatus.DockAssigned);
            }

            await using (var start = CreateContext())
            {
                var service = CreateShipmentService(start, fixture.UserId);
                var act = () => service.StartLoadingAsync(prepared.ShipmentId, new StartShipmentLoadingDto
                {
                    DockAppointmentId = appointmentId,
                    RowVersion = staged.RowVersion!
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("SHIPMENT_DOCK_APPOINTMENT_INVALID");
            }

            await using var verify = CreateContext();
            (await verify.Shipments.AsNoTracking().SingleAsync()).Status.Should().Be(ShipmentStatus.Staging);
            (await verify.HandlingUnits.AsNoTracking().AllAsync(x => x.Status == HandlingUnitStatus.Staged)).Should().BeTrue();
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task LoadingChildHuScan_IsRejectedBecauseShipmentOwnsRootHu()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var prepared = await PreparePackedNestedShipmentAsync(fixture);
            ShipmentDto staged;
            await using (var stage = CreateContext())
            {
                var service = CreateShipmentService(stage, fixture.UserId);
                var current = await service.GetAsync(prepared.ShipmentId);
                staged = await service.StageAsync(prepared.ShipmentId, new StageShipmentDto { StagingLocationCode = fixture.StagingLocationCode, RowVersion = current.RowVersion! });
            }

            int appointmentId;
            await using (var dock = CreateContext())
                appointmentId = await CreateOutboundAppointmentAsync(dock, fixture, DockAppointmentStatus.InService);

            ShipmentDto loading;
            await using (var start = CreateContext())
            {
                loading = await CreateShipmentService(start, fixture.UserId).StartLoadingAsync(
                    prepared.ShipmentId,
                    new StartShipmentLoadingDto { DockAppointmentId = appointmentId, RowVersion = staged.RowVersion! });
            }

            await using (var load = CreateContext())
            {
                var service = CreateShipmentService(load, fixture.UserId);
                var act = () => service.LoadHandlingUnitAsync(prepared.ShipmentId, new LoadShipmentHandlingUnitDto
                {
                    HandlingUnitBarcode = prepared.ChildHuBarcode,
                    RowVersion = loading.RowVersion!
                });
                var thrown = await act.Should().ThrowAsync<BusinessRuleException>();
                thrown.Which.Data["ErrorCode"].Should().Be("SHIPMENT_HU_MISMATCH");
            }
        }
        finally { await CleanupAsync(fixture); }
    }

    private static async Task<Prepared> PreparePackedNestedShipmentAsync(Fixture fixture)
    {
        var session = await PrepareCompletedPickingAsync(fixture);

        PackingSessionDto childCreated;
        await using (var create = CreateContext())
        {
            childCreated = await CreatePackingService(create, fixture.UserId).CreateHandlingUnitAsync(session.Id, new()
            {
                Type = "Carton",
                HuCode = $"CHILD-{fixture.Suffix}",
                Barcode = $"CHILD-{fixture.Suffix}",
                RowVersion = session.RowVersion!
            });
        }

        PackingSessionDto parentCreated;
        await using (var create = CreateContext())
        {
            parentCreated = await CreatePackingService(create, fixture.UserId).CreateHandlingUnitAsync(session.Id, new()
            {
                Type = "Pallet",
                HuCode = $"PARENT-{fixture.Suffix}",
                Barcode = $"PARENT-{fixture.Suffix}",
                RowVersion = childCreated.RowVersion!
            });
        }

        var child = parentCreated.HandlingUnits.Single(x => x.HuCode == $"CHILD-{fixture.Suffix}");
        var parent = parentCreated.HandlingUnits.Single(x => x.HuCode == $"PARENT-{fixture.Suffix}");

        PackingSessionDto packed;
        await using (var pack = CreateContext())
        {
            packed = await CreatePackingService(pack, fixture.UserId).PackAsync(session.Id, new()
            {
                HandlingUnitId = child.Id,
                PickingTaskLineId = parentCreated.Lines.Single().PickingTaskLineId,
                ProductBarcode = fixture.ProductCode,
                Quantity = 10,
                RowVersion = parentCreated.RowVersion!
            });
        }

        PackingSessionDto childClosed;
        await using (var close = CreateContext())
        {
            childClosed = await CreatePackingService(close, fixture.UserId).CloseHandlingUnitAsync(
                session.Id,
                child.Id,
                new PackingStateCommandDto { RowVersion = packed.RowVersion! });
        }

        PackingSessionDto nested;
        await using (var nest = CreateContext())
        {
            nested = await CreatePackingService(nest, fixture.UserId).NestHandlingUnitAsync(
                session.Id,
                child.Id,
                new NestHandlingUnitDto { ParentHandlingUnitId = parent.Id, RowVersion = childClosed.RowVersion! });
        }

        PackingSessionDto parentClosed;
        await using (var close = CreateContext())
        {
            parentClosed = await CreatePackingService(close, fixture.UserId).CloseHandlingUnitAsync(
                session.Id,
                parent.Id,
                new PackingStateCommandDto { RowVersion = nested.RowVersion! });
        }

        await using (var complete = CreateContext())
        {
            await CreatePackingService(complete, fixture.UserId, withShipment: true).CompleteAsync(
                session.Id,
                new PackingStateCommandDto { RowVersion = parentClosed.RowVersion! });
        }

        await using var read = CreateContext();
        var shipment = (await CreateShipmentService(read, fixture.UserId).ListAsync(fixture.WarehouseId)).Single();
        return new Prepared(
            shipment.Id,
            child.Id,
            child.Barcode,
            parent.Id,
            parent.Barcode);
    }

    private static async Task<PackingSessionDto> PrepareCompletedPickingAsync(Fixture fixture)
    {
        int reservationId;
        await using (var reserve = CreateContext())
        {
            reservationId = (await CreateReservationService(reserve, fixture.UserId).ReserveForExportAsync(
                exportReceiptId: fixture.ExportReceiptId,
                exportCode: fixture.ExportReceiptCode,
                warehouseId: fixture.WarehouseId,
                productId: fixture.ProductId,
                quantity: 10,
                userId: fixture.UserId)).Id;
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

    private static async Task<int> CreateOutboundAppointmentAsync(
        ErpKhoDbContext db,
        Fixture fixture,
        DockAppointmentStatus status)
    {
        var dock = new Dock
        {
            WarehouseId = fixture.WarehouseId,
            Code = $"DOCK-{fixture.Suffix}",
            Name = "Outbound dock",
            SupportsInbound = false,
            SupportsOutbound = true,
            IsActive = true
        };
        db.Docks.Add(dock);
        await db.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var appointment = new DockAppointment
        {
            WarehouseId = fixture.WarehouseId,
            Code = $"APT-{fixture.Suffix}",
            Direction = DockAppointmentDirection.Outbound,
            Status = status,
            PlannedStartUtc = now.AddMinutes(-30),
            PlannedEndUtc = now.AddHours(2),
            VehiclePlate = "51C-12345",
            TrailerPlate = "TR-100",
            DockId = dock.Id,
            DockAssignedAtUtc = now.AddMinutes(-20),
            ServiceStartedAtUtc = status == DockAppointmentStatus.InService ? now.AddMinutes(-10) : null,
            CreatedBy = fixture.UserId,
            CreatedAtUtc = now.AddHours(-1),
            UpdatedBy = fixture.UserId,
            UpdatedAtUtc = now
        };
        db.DockAppointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
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

    private static PackingService CreatePackingService(ErpKhoDbContext db, int userId, bool withShipment = false)
    {
        var current = new CurrentUser(userId);
        return new PackingService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            new PackingSessionIntegration(db),
            withShipment ? new ShipmentIntegration(db) : null);
    }

    private static ShipmentService CreateShipmentService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        return new ShipmentService(
            db,
            new WarehouseAuthorizationService(db, current),
            current);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var role = new Role { RoleName = $"ShipRole{suffix}" };
        var user = new User
        {
            Username = $"ShipUser{suffix}",
            PasswordHash = "not-used",
            FullName = "Shipment loading user",
            Role = role
        };
        var unit = new Unit { Code = $"SHU{suffix}", Name = "Shipment unit", DecimalPlaces = 4 };
        var product = new Product { Code = $"SHP{suffix}", Name = "Shipment product", Unit = unit };
        var warehouse = new Warehouse { Code = $"SHW{suffix}", Name = "Shipment warehouse" };
        db.AddRange(user, product, warehouse);
        await db.SaveChangesAsync();

        var exportReceipt = new ExportReceipt
        {
            Code = $"EX-SHIP-{suffix}",
            WarehouseId = warehouse.Id,
            Status = ReceiptStatus.Approved,
            CreatedBy = user.Id,
            ApprovedBy = user.Id,
            DispatchMode = ExportDispatchMode.RequireSeparateDispatch,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ApprovedAt = DateTime.UtcNow.AddMinutes(-5),
            Details =
            [
                new ExportReceiptDetail
                {
                    ProductId = product.Id,
                    Quantity = 10,
                    UnitPrice = 0,
                    BaseUomIdSnapshot = unit.Id,
                    BaseUomCodeSnapshot = unit.Code,
                    BaseUomNameSnapshot = unit.Name,
                    BaseUomDecimalPlacesSnapshot = unit.DecimalPlaces
                }
            ]
        };
        db.ExportReceipts.Add(exportReceipt);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"SHL{suffix}",
            Name = "Shipment source",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var stagingLocation = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"STG{suffix}",
            Name = "Outbound staging lane",
            LocationType = WarehouseLocationType.Staging,
            IsActive = true,
            IsPickable = false,
            IsReceivable = false,
            CreatedBy = user.Id
        };
        db.AddRange(location, stagingLocation);
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
            suffix,
            user.Id,
            role.Id,
            unit.Id,
            product.Id,
            product.Code,
            exportReceipt.Id,
            exportReceipt.Code,
            warehouse.Id,
            location.Id,
            location.Code,
            stagingLocation.Id,
            stagingLocation.Code);
    }

    private static async Task CleanupAsync(Fixture fixture)
    {
        await using var db = CreateContext();
        await db.AuditLogs.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.ShipmentHandlingUnits
            .Where(x => x.Shipment.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.Shipments.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.DockAppointmentEvents
            .Where(x => x.Appointment.WarehouseId == fixture.WarehouseId)
            .ExecuteDeleteAsync();
        await db.DockAppointments.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Docks.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
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
        string Suffix,
        int UserId,
        int RoleId,
        int UnitId,
        int ProductId,
        string ProductCode,
        int ExportReceiptId,
        string ExportReceiptCode,
        int WarehouseId,
        int LocationId,
        string LocationCode,
        int StagingLocationId,
        string StagingLocationCode);

    private sealed record Prepared(
        int ShipmentId,
        int ChildHuId,
        string ChildHuBarcode,
        int ParentHuId,
        string ParentHuBarcode);
}
