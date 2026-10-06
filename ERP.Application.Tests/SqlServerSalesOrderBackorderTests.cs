using ERP.Application.DTOs;
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
public sealed class SqlServerSalesOrderBackorderTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task ReleasePartialAvailability_CreatesReservationAllocationPickingAndBackorder_WithoutChangingOnHand()
    {
        var fixture = await CreateFixtureAsync(stockQuantity: 6m);
        try
        {
            var order = await CreateOrderAsync(fixture, 10m);

            SalesOrderDto released;
            await using (var db = CreateContext())
            {
                released = await CreateDemandService(db, fixture.UserId).ReleaseSalesOrderAsync(order.Id, new()
                {
                    RowVersion = order.RowVersion
                });
            }

            released.Status.Should().Be(nameof(SalesOrderStatus.Released));
            released.OrderedQuantity.Should().Be(10);
            released.ReservedQuantity.Should().Be(6);
            released.AllocatedQuantity.Should().Be(6);
            released.BackorderQuantity.Should().Be(4);
            released.ShippedQuantity.Should().Be(0);
            released.CancelledQuantity.Should().Be(0);
            released.OpenQuantity.Should().Be(10);

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(6);
            stock.ReservedQuantity.Should().Be(6);

            var reservation = await verify.StockReservations.AsNoTracking().SingleAsync();
            reservation.SourceType.Should().Be("SalesOrder");
            reservation.SourceId.Should().Be(order.Id);
            reservation.Quantity.Should().Be(6);
            reservation.AllocatedQuantity.Should().Be(6);

            (await verify.StockAllocations.AsNoTracking().SumAsync(x => x.Quantity)).Should().Be(6);
            var task = await verify.PickingTasks.AsNoTracking().Include(x => x.Lines).SingleAsync();
            task.Status.Should().Be(PickingTaskStatus.Open);
            task.Lines.Sum(x => x.RequestedQuantity).Should().Be(6);

            var backorder = await verify.Backorders.AsNoTracking().SingleAsync();
            backorder.Quantity.Should().Be(4);
            backorder.RecoveredQuantity.Should().Be(0);
            backorder.Status.Should().Be(BackorderStatus.Open);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReallocateBackorder_BeforePickingStart_ExpandsOpenPickingTask()
    {
        var fixture = await CreateFixtureAsync(stockQuantity: 6m);
        try
        {
            var order = await CreateOrderAsync(fixture, 10m);
            await using (var release = CreateContext())
            {
                await CreateDemandService(release, fixture.UserId).ReleaseSalesOrderAsync(order.Id, new()
                {
                    RowVersion = order.RowVersion
                });
            }

            int backorderId;
            string backorderVersion;
            await using (var replenish = CreateContext())
            {
                await replenish.InventoryStocks
                    .Where(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.Quantity, 10m));

                var row = await replenish.Backorders.AsNoTracking().SingleAsync();
                backorderId = row.Id;
                backorderVersion = Convert.ToBase64String(row.RowVersion);
            }

            BackorderDto recovered;
            await using (var recover = CreateContext())
            {
                recovered = await CreateDemandService(recover, fixture.UserId).ReallocateBackorderAsync(
                    backorderId,
                    new BackorderReallocateDto
                    {
                        Quantity = 4,
                        RowVersion = backorderVersion
                    });
            }

            recovered.RecoveredQuantity.Should().Be(4);
            recovered.RemainingQuantity.Should().Be(0);
            recovered.Status.Should().Be(nameof(BackorderStatus.Allocated));

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(10);
            stock.ReservedQuantity.Should().Be(10);

            var reservation = await verify.StockReservations.AsNoTracking().SingleAsync();
            reservation.Quantity.Should().Be(10);
            reservation.AllocatedQuantity.Should().Be(10);
            reservation.Status.Should().Be(StockReservationStatus.Allocated);

            var task = await verify.PickingTasks.AsNoTracking().Include(x => x.Lines).SingleAsync();
            task.Status.Should().Be(PickingTaskStatus.Open);
            task.Lines.Should().HaveCount(2);
            task.Lines.Sum(x => x.RequestedQuantity).Should().Be(10);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task ReallocateBackorder_AfterPickingStarted_IsRejected()
    {
        var fixture = await CreateFixtureAsync(stockQuantity: 6m);
        try
        {
            var order = await CreateOrderAsync(fixture, 10m);
            await using (var release = CreateContext())
            {
                await CreateDemandService(release, fixture.UserId).ReleaseSalesOrderAsync(order.Id, new()
                {
                    RowVersion = order.RowVersion
                });
            }

            PickingTaskDto assigned;
            await using (var assign = CreateContext())
            {
                var picking = CreatePickingService(assign, fixture.UserId);
                var summary = (await picking.ListAsync(fixture.WarehouseId)).Single();
                var detail = await picking.GetAsync(summary.Id);
                assigned = await picking.AssignAsync(summary.Id, new PickingStateCommandDto
                {
                    AssignedUserId = fixture.UserId,
                    RowVersion = detail.RowVersion!
                });
            }

            await using (var start = CreateContext())
            {
                await CreatePickingService(start, fixture.UserId).StartAsync(assigned.Id, new PickingStateCommandDto
                {
                    RowVersion = assigned.RowVersion!
                });
            }

            int backorderId;
            string backorderVersion;
            await using (var replenish = CreateContext())
            {
                await replenish.InventoryStocks
                    .Where(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.Quantity, 10m));
                var row = await replenish.Backorders.AsNoTracking().SingleAsync();
                backorderId = row.Id;
                backorderVersion = Convert.ToBase64String(row.RowVersion);
            }

            await using (var recover = CreateContext())
            {
                var service = CreateDemandService(recover, fixture.UserId);
                var act = () => service.ReallocateBackorderAsync(
                    backorderId,
                    new BackorderReallocateDto { RowVersion = backorderVersion });
                var thrown = await act.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>();
                thrown.Which.Data["HttpStatusCode"].Should().Be(409);
                thrown.Which.Data["ErrorCode"].Should().Be("BACKORDER_EXECUTION_STARTED");
            }

            await using var verify = CreateContext();
            (await verify.Backorders.AsNoTracking().SingleAsync()).RecoveredQuantity.Should().Be(0);
            (await verify.StockReservations.AsNoTracking().SingleAsync()).Quantity.Should().Be(6);
            (await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId))
                .ReservedQuantity.Should().Be(6);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task CancelReleasedOrder_ReleasesUnconsumedCommitment_AndCancelsBackorderWithoutChangingOnHand()
    {
        var fixture = await CreateFixtureAsync(stockQuantity: 6m);
        try
        {
            var order = await CreateOrderAsync(fixture, 10m);
            SalesOrderDto released;
            await using (var release = CreateContext())
            {
                released = await CreateDemandService(release, fixture.UserId).ReleaseSalesOrderAsync(order.Id, new()
                {
                    RowVersion = order.RowVersion
                });
            }

            SalesOrderDto cancelled;
            await using (var cancel = CreateContext())
            {
                cancelled = await CreateDemandService(cancel, fixture.UserId).CancelSalesOrderAsync(order.Id, new()
                {
                    RowVersion = released.RowVersion,
                    Reason = "Customer cancelled remaining demand"
                });
            }

            cancelled.Status.Should().Be(nameof(SalesOrderStatus.Cancelled));
            cancelled.CancelledQuantity.Should().Be(10);
            cancelled.OpenQuantity.Should().Be(0);
            cancelled.ShippedQuantity.Should().Be(0);

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(6);
            stock.ReservedQuantity.Should().Be(0);

            (await verify.StockReservations.AsNoTracking().SingleAsync()).Status.Should().Be(StockReservationStatus.Cancelled);
            (await verify.StockAllocations.AsNoTracking().SingleAsync()).Status.Should().Be(StockAllocationStatus.Released);
            (await verify.PickingTasks.AsNoTracking().SingleAsync()).Status.Should().Be(PickingTaskStatus.Cancelled);

            var backorder = await verify.Backorders.AsNoTracking().SingleAsync();
            backorder.Status.Should().Be(BackorderStatus.Cancelled);
            backorder.CancelledQuantity.Should().Be(4);
        }
        finally { await CleanupAsync(fixture); }
    }

    [SqlServerFact]
    public async Task PartialShipment_PreservesBackorder_AndCancelOnlyCancelsRemainder()
    {
        var fixture = await CreateFixtureAsync(stockQuantity: 6m);
        try
        {
            var order = await CreateOrderAsync(fixture, 10m);

            SalesOrderDto released;
            await using (var release = CreateContext())
            {
                released = await CreateDemandService(release, fixture.UserId).ReleaseSalesOrderAsync(order.Id, new()
                {
                    RowVersion = order.RowVersion
                });
            }

            PickingTaskDto assigned;
            await using (var assign = CreateContext())
            {
                var picking = CreatePickingService(assign, fixture.UserId);
                var summary = (await picking.ListAsync(fixture.WarehouseId)).Single();
                var detail = await picking.GetAsync(summary.Id);
                assigned = await picking.AssignAsync(summary.Id, new PickingStateCommandDto
                {
                    AssignedUserId = fixture.UserId,
                    RowVersion = detail.RowVersion!
                });
            }

            PickingTaskDto started;
            await using (var start = CreateContext())
            {
                started = await CreatePickingService(start, fixture.UserId).StartAsync(assigned.Id, new PickingStateCommandDto
                {
                    RowVersion = assigned.RowVersion!
                });
            }

            PickingTaskDto picked;
            await using (var pick = CreateContext())
            {
                picked = await CreatePickingService(pick, fixture.UserId).PickAsync(started.Id, new PickScanDto
                {
                    TaskLineId = started.Lines.Single().Id,
                    LocationBarcode = fixture.LocationCode,
                    ProductBarcode = fixture.ProductCode,
                    Quantity = 6m,
                    RowVersion = started.RowVersion!
                });
            }

            await using (var completePick = CreateContext())
            {
                await CreatePickingService(completePick, fixture.UserId, withPacking: true).CompleteAsync(
                    picked.Id,
                    new PickingStateCommandDto { RowVersion = picked.RowVersion! });
            }

            PackingSessionDto packingSession;
            await using (var readPacking = CreateContext())
            {
                var packing = CreatePackingService(readPacking, fixture.UserId);
                var summary = (await packing.ListAsync(fixture.WarehouseId)).Single();
                packingSession = await packing.GetAsync(summary.Id);
            }

            PackingSessionDto withHu;
            await using (var createHu = CreateContext())
            {
                withHu = await CreatePackingService(createHu, fixture.UserId).CreateHandlingUnitAsync(
                    packingSession.Id,
                    new CreateHandlingUnitDto
                    {
                        Type = "Carton",
                        HuCode = $"SO-HU-{fixture.Suffix}",
                        Barcode = $"SO-HU-{fixture.Suffix}",
                        RowVersion = packingSession.RowVersion!
                    });
            }

            var hu = withHu.HandlingUnits.Single();
            PackingSessionDto packedSession;
            await using (var pack = CreateContext())
            {
                packedSession = await CreatePackingService(pack, fixture.UserId).PackAsync(
                    packingSession.Id,
                    new PackIntoHandlingUnitDto
                    {
                        HandlingUnitId = hu.Id,
                        PickingTaskLineId = withHu.Lines.Single().PickingTaskLineId,
                        ProductBarcode = fixture.ProductCode,
                        Quantity = 6m,
                        RowVersion = withHu.RowVersion!
                    });
            }

            PackingSessionDto huClosed;
            await using (var closeHu = CreateContext())
            {
                huClosed = await CreatePackingService(closeHu, fixture.UserId).CloseHandlingUnitAsync(
                    packingSession.Id,
                    hu.Id,
                    new PackingStateCommandDto { RowVersion = packedSession.RowVersion! });
            }

            await using (var completePacking = CreateContext())
            {
                await CreatePackingService(completePacking, fixture.UserId, withShipment: true).CompleteAsync(
                    packingSession.Id,
                    new PackingStateCommandDto { RowVersion = huClosed.RowVersion! });
            }

            ShipmentDto staged;
            int shipmentId;
            await using (var stage = CreateContext())
            {
                var shipmentService = CreateShipmentService(stage, fixture.UserId);
                var shipment = (await shipmentService.ListAsync(fixture.WarehouseId)).Single();
                shipmentId = shipment.Id;
                var detail = await shipmentService.GetAsync(shipmentId);
                staged = await shipmentService.StageAsync(shipmentId, new StageShipmentDto
                {
                    StagingLocationCode = fixture.StagingLocationCode,
                    RowVersion = detail.RowVersion!
                });
            }

            int appointmentId;
            await using (var dock = CreateContext())
            {
                appointmentId = await CreateOutboundAppointmentAsync(
                    dock,
                    fixture,
                    DockAppointmentStatus.InService);
            }

            ShipmentDto loading;
            await using (var startLoading = CreateContext())
            {
                loading = await CreateShipmentService(startLoading, fixture.UserId).StartLoadingAsync(
                    shipmentId,
                    new StartShipmentLoadingDto
                    {
                        DockAppointmentId = appointmentId,
                        RowVersion = staged.RowVersion!
                    });
            }

            ShipmentDto huLoaded;
            await using (var load = CreateContext())
            {
                huLoaded = await CreateShipmentService(load, fixture.UserId).LoadHandlingUnitAsync(
                    shipmentId,
                    new LoadShipmentHandlingUnitDto
                    {
                        HandlingUnitBarcode = hu.Barcode,
                        RowVersion = loading.RowVersion!
                    });
            }

            ShipmentDto loaded;
            await using (var completeLoading = CreateContext())
            {
                loaded = await CreateShipmentService(completeLoading, fixture.UserId).CompleteLoadingAsync(
                    shipmentId,
                    new CompleteShipmentLoadingDto
                    {
                        SealNumber = $"SO-SEAL-{fixture.Suffix}",
                        RowVersion = huLoaded.RowVersion!
                    });
            }

            await using (var dispatch = CreateContext())
            {
                await CreateShipmentService(dispatch, fixture.UserId).DispatchAsync(
                    shipmentId,
                    new ShipmentStateCommandDto { RowVersion = loaded.RowVersion! });
            }

            SalesOrderDto partiallyFulfilled;
            await using (var readOrder = CreateContext())
            {
                partiallyFulfilled = await CreateDemandService(readOrder, fixture.UserId).GetSalesOrderAsync(order.Id);
            }

            partiallyFulfilled.Status.Should().Be(nameof(SalesOrderStatus.PartiallyFulfilled));
            partiallyFulfilled.OrderedQuantity.Should().Be(10m);
            partiallyFulfilled.ShippedQuantity.Should().Be(6m);
            partiallyFulfilled.BackorderQuantity.Should().Be(4m);
            partiallyFulfilled.CancelledQuantity.Should().Be(0m);
            partiallyFulfilled.OpenQuantity.Should().Be(4m);
            (partiallyFulfilled.ShippedQuantity + partiallyFulfilled.OpenQuantity + partiallyFulfilled.CancelledQuantity)
                .Should().Be(partiallyFulfilled.OrderedQuantity);

            SalesOrderDto cancelled;
            await using (var cancel = CreateContext())
            {
                cancelled = await CreateDemandService(cancel, fixture.UserId).CancelSalesOrderAsync(order.Id, new()
                {
                    RowVersion = partiallyFulfilled.RowVersion,
                    Reason = "Customer cancelled only the unshipped remainder"
                });
            }

            cancelled.Status.Should().Be(nameof(SalesOrderStatus.Cancelled));
            cancelled.OrderedQuantity.Should().Be(10m);
            cancelled.ShippedQuantity.Should().Be(6m);
            cancelled.BackorderQuantity.Should().Be(0m);
            cancelled.CancelledQuantity.Should().Be(4m);
            cancelled.OpenQuantity.Should().Be(0m);
            (cancelled.ShippedQuantity + cancelled.OpenQuantity + cancelled.CancelledQuantity)
                .Should().Be(cancelled.OrderedQuantity);

            await using var verify = CreateContext();
            var stock = await verify.InventoryStocks.AsNoTracking()
                .SingleAsync(x => x.ProductId == fixture.ProductId && x.LocationId == fixture.LocationId);
            stock.Quantity.Should().Be(0m);
            stock.ReservedQuantity.Should().Be(0m);

            var shipLedger = await verify.InventoryTransactions.AsNoTracking()
                .Where(x => x.ReferenceType == "Shipment" &&
                            x.ReferenceId == shipmentId &&
                            x.TransactionType == TransactionType.Ship)
                .ToListAsync();
            shipLedger.Should().ContainSingle();
            shipLedger.Single().Quantity.Should().Be(6m);

            var backorder = await verify.Backorders.AsNoTracking().SingleAsync();
            backorder.Status.Should().Be(BackorderStatus.Cancelled);
            backorder.CancelledQuantity.Should().Be(4m);
        }
        finally { await CleanupAsync(fixture); }
    }

    private static async Task<SalesOrderDto> CreateOrderAsync(Fixture fixture, decimal quantity)
    {
        await using var db = CreateContext();
        return await CreateDemandService(db, fixture.UserId).CreateSalesOrderAsync(new CreateSalesOrderDto
        {
            ExternalOrderId = $"EXT-{fixture.Suffix}",
            CustomerId = fixture.CustomerId,
            WarehouseId = fixture.WarehouseId,
            Priority = 10,
            Lines =
            [
                new CreateSalesOrderLineDto
                {
                    ExternalLineId = "1",
                    ProductId = fixture.ProductId,
                    OrderedQuantity = quantity
                }
            ]
        });
    }

    private static SalesOrderBackorderService CreateDemandService(ErpKhoDbContext db, int userId)
    {
        var current = new CurrentUser(userId);
        var auth = new WarehouseAuthorizationService(db, current);
        var reservation = new StockReservationService(
            db,
            new InventoryStockRepository(db),
            new UnitOfWork(db),
            auth,
            current,
            new StockReservationOptions(),
            new PickingTaskIntegration(db));
        var allocation = new StockAllocationService(
            db,
            auth,
            current,
            new PickingTaskIntegration(db));
        return new SalesOrderBackorderService(
            db,
            new InventoryStockRepository(db),
            reservation,
            allocation,
            auth,
            current);
    }

    private static PickingService CreatePickingService(
        ErpKhoDbContext db,
        int userId,
        bool withPacking = false)
    {
        var current = new CurrentUser(userId);
        return new PickingService(
            db,
            new WarehouseAuthorizationService(db, current),
            current,
            withPacking ? new PackingSessionIntegration(db) : null);
    }

    private static PackingService CreatePackingService(
        ErpKhoDbContext db,
        int userId,
        bool withShipment = false)
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
            current,
            new StockReservationService(
                db,
                new InventoryStockRepository(db),
                new UnitOfWork(db),
                new WarehouseAuthorizationService(db, current),
                current,
                new StockReservationOptions()));
    }

    private static async Task<int> CreateOutboundAppointmentAsync(
        ErpKhoDbContext db,
        Fixture fixture,
        DockAppointmentStatus status)
    {
        var dock = new Dock
        {
            WarehouseId = fixture.WarehouseId,
            Code = $"SO-DOCK-{fixture.Suffix}",
            Name = "Sales order outbound dock",
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
            Code = $"SO-APT-{fixture.Suffix}",
            Direction = DockAppointmentDirection.Outbound,
            Status = status,
            PlannedStartUtc = now.AddMinutes(-30),
            PlannedEndUtc = now.AddHours(2),
            VehiclePlate = "51C-54321",
            TrailerPlate = "SO-TR-100",
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

    private static async Task<Fixture> CreateFixtureAsync(decimal stockQuantity)
    {
        await using var db = CreateContext();
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var role = new Role { RoleName = $"SoRole{suffix}" };
        var user = new User
        {
            Username = $"SoUser{suffix}",
            PasswordHash = "not-used",
            FullName = "Sales order user",
            Role = role
        };
        var customer = new BusinessPartner
        {
            Code = $"CUS{suffix}",
            Name = "Backorder customer",
            IsCustomer = true,
            IsActive = true
        };
        var unit = new Unit { Code = $"SOU{suffix}", Name = "Sales order unit", DecimalPlaces = 4 };
        var product = new Product { Code = $"SOP{suffix}", Name = "Sales order product", Unit = unit };
        var warehouse = new Warehouse { Code = $"SOW{suffix}", Name = "Sales order warehouse" };
        db.AddRange(user, customer, product, warehouse);
        await db.SaveChangesAsync();

        var location = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"SOL{suffix}",
            Name = "Sales order pick bin",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true,
            CreatedBy = user.Id
        };
        var stagingLocation = new WarehouseLocation
        {
            WarehouseId = warehouse.Id,
            Code = $"SOSTG{suffix}",
            Name = "Sales order outbound staging",
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
            Quantity = stockQuantity,
            Status = InventoryStatus.Available
        });
        await db.SaveChangesAsync();

        return new Fixture(
            suffix,
            user.Id,
            role.Id,
            customer.Id,
            unit.Id,
            product.Id,
            product.Code,
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
        await db.InventoryTransactions
            .Where(x => x.WarehouseId == fixture.WarehouseId && x.ProductId == fixture.ProductId)
            .ExecuteDeleteAsync();
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
        await db.StockReservations.Where(x => x.SourceType == "SalesOrder").ExecuteDeleteAsync();
        await db.Backorders.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.SalesOrderLines.Where(x => x.SalesOrder.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.SalesOrders.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.InventoryStocks.Where(x => x.ProductId == fixture.ProductId).ExecuteDeleteAsync();
        await db.UserWarehouses.Where(x => x.UserId == fixture.UserId).ExecuteDeleteAsync();
        await db.WarehouseLocations.Where(x => x.WarehouseId == fixture.WarehouseId).ExecuteDeleteAsync();
        await db.Products.Where(x => x.Id == fixture.ProductId).ExecuteDeleteAsync();
        await db.Units.Where(x => x.Id == fixture.UnitId).ExecuteDeleteAsync();
        await db.BusinessPartners.Where(x => x.Id == fixture.CustomerId).ExecuteDeleteAsync();
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
        int CustomerId,
        int UnitId,
        int ProductId,
        string ProductCode,
        int WarehouseId,
        int LocationId,
        string LocationCode,
        int StagingLocationId,
        string StagingLocationCode);
}
