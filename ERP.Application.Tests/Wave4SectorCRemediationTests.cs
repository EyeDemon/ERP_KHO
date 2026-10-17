using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ERP.Application.Tests;

public sealed class Wave4SectorCRemediationTests
{
    private static readonly byte[] Ver = [0, 0, 0, 0, 0, 0, 0, 1];
    private static string EncodedVer => Convert.ToBase64String(Ver);

    private static ErpKhoDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave4SectorC_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ErpKhoDbContext(options);
    }

    [Fact]
    public async Task W4_SecB_02_CompleteAsn_ChecksWarehouseAccessBeforeExposingUnpostedReceiptConflict()
    {
        await using var db = CreateInMemoryContext();
        db.Asns.Add(new Asn
        {
            Id = 10,
            Code = "ASN-SEC-01",
            WarehouseId = 99,
            SupplierId = 1,
            Status = AsnStatus.Receiving,
            RowVersion = Ver
        });
        db.ImportReceipts.Add(new ImportReceipt
        {
            Id = 20,
            Code = "IR-SEC-01",
            WarehouseId = 99,
            AsnId = 10,
            Status = ReceiptStatus.Draft
        });
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        auth.Setup(x => x.EnsureWarehouseAccessAsync(99, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Không tìm thấy ASN hoặc bạn không có quyền truy cập."));
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(1);

        var service = new InboundPlanningService(db, auth.Object, user.Object);
        var act = () => service.CompleteAsnAsync(10, new InboundStateCommandDto { RowVersion = EncodedVer });
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task W4_SecC_01_ZeroPickComplete_DoesNotAutoCreateEmptyPackingSession()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1" };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product { Id = 1, Code = "SKU-1", Name = "Product 1", UnitId = 1, Unit = unit };
        var loc = new WarehouseLocation
        {
            Id = 1,
            WarehouseId = 1,
            Code = "BIN-01",
            Name = "Bin 01",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true
        };
        var res = new StockReservation
        {
            Id = 1,
            WarehouseId = 1,
            ProductId = 1,
            Quantity = 5,
            ReleasedQuantity = 5,
            AllocatedQuantity = 0,
            Status = StockReservationStatus.Released,
            SourceType = "SalesOrder",
            SourceId = 1,
            SourceCode = "SO-1"
        };
        var alloc = new StockAllocation
        {
            Id = 1,
            ReservationId = 1,
            Reservation = res,
            WarehouseId = 1,
            LocationId = 1,
            Location = loc,
            ProductId = 1,
            Quantity = 5,
            Status = StockAllocationStatus.Released
        };
        var task = new PickingTask
        {
            Id = 1,
            TaskCode = "PICK-01",
            WarehouseId = 1,
            Warehouse = warehouse,
            SourceType = "SalesOrder",
            SourceId = 1,
            SourceCode = "SO-1",
            Status = PickingTaskStatus.Resolved,
            AssignedUserId = 1,
            RowVersion = Ver,
            Lines =
            [
                new PickingTaskLine
                {
                    Id = 1,
                    PickingTaskId = 1,
                    AllocationId = 1,
                    Allocation = alloc,
                    ProductId = 1,
                    Product = product,
                    SourceLocationId = 1,
                    SourceLocation = loc,
                    RequestedQuantity = 5,
                    PickedQuantity = 0,
                    Status = PickingTaskLineStatus.Resolved
                }
            ]
        };
        db.Warehouses.Add(warehouse);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.WarehouseLocations.Add(loc);
        db.StockReservations.Add(res);
        db.StockAllocations.Add(alloc);
        db.PickingTasks.Add(task);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(1);
        var packingIntegration = new Mock<IPackingSessionIntegration>();

        var service = new PickingService(db, auth.Object, user.Object, packingIntegration.Object);
        var result = await service.CompleteAsync(1, new PickingStateCommandDto { RowVersion = EncodedVer });

        result.Status.Should().Be(nameof(PickingTaskStatus.Completed));
        packingIntegration.Verify(
            x => x.EnsureForPickingTaskAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task W4_SecC_01_ShipmentDispatch_AllowsResolvedShortPickWithRemainingPickedQuantity()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1" };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product { Id = 1, Code = "SKU-1", Name = "Product 1", UnitId = 1, Unit = unit };
        var loc = new WarehouseLocation
        {
            Id = 1,
            WarehouseId = 1,
            Code = "BIN-01",
            Name = "Bin 01",
            LocationType = WarehouseLocationType.Legacy,
            IsActive = true,
            IsPickable = true
        };
        var res = new StockReservation
        {
            Id = 1,
            WarehouseId = 1,
            ProductId = 1,
            Quantity = 10,
            ReleasedQuantity = 4,
            AllocatedQuantity = 6,
            Status = StockReservationStatus.Allocated,
            SourceType = "SalesOrder",
            SourceId = 1,
            SourceCode = "SO-1"
        };
        var allocPicked = new StockAllocation
        {
            Id = 1,
            ReservationId = 1,
            Reservation = res,
            WarehouseId = 1,
            LocationId = 1,
            Location = loc,
            ProductId = 1,
            InventoryStatus = InventoryStatus.Available,
            Quantity = 6,
            Status = StockAllocationStatus.Picked
        };
        var task = new PickingTask
        {
            Id = 1,
            TaskCode = "PICK-01",
            WarehouseId = 1,
            Warehouse = warehouse,
            SourceType = "SalesOrder",
            SourceId = 1,
            SourceCode = "SO-1",
            Status = PickingTaskStatus.Completed,
            RowVersion = Ver,
            Lines =
            [
                new PickingTaskLine
                {
                    Id = 1,
                    PickingTaskId = 1,
                    AllocationId = 1,
                    Allocation = allocPicked,
                    ProductId = 1,
                    Product = product,
                    SourceLocationId = 1,
                    SourceLocation = loc,
                    RequestedQuantity = 10,
                    PickedQuantity = 6,
                    Status = PickingTaskLineStatus.Resolved,
                    ShortPicks =
                    [
                        new ShortPickException
                        {
                            Id = 1,
                            PickingTaskLineId = 1,
                            ExpectedQuantity = 10,
                            PickedQuantity = 6,
                            ShortageQuantity = 4,
                            Reason = "DAMAGED",
                            Status = ShortPickExceptionStatus.Resolved,
                            ResolutionType = ShortPickResolutionType.Backorder
                        }
                    ]
                }
            ]
        };
        var hu = new HandlingUnit
        {
            Id = 1,
            PackingSessionId = 1,
            WarehouseId = 1,
            HuCode = "HU-01",
            Barcode = "HU-01",
            Type = HandlingUnitType.Carton,
            Status = HandlingUnitStatus.Loaded,
            RowVersion = Ver,
            Contents =
            [
                new HandlingUnitContent
                {
                    Id = 1,
                    HandlingUnitId = 1,
                    PickingTaskLineId = 1,
                    ProductId = 1,
                    Product = product,
                    Quantity = 6
                }
            ]
        };
        var session = new PackingSession
        {
            Id = 1,
            SessionCode = "PACK-01",
            PickingTaskId = 1,
            PickingTask = task,
            WarehouseId = 1,
            Warehouse = warehouse,
            Status = PackingSessionStatus.Packed,
            RowVersion = Ver,
            HandlingUnits = [hu]
        };
        var shipment = new Shipment
        {
            Id = 1,
            ShipmentCode = "SHP-01",
            PackingSessionId = 1,
            PackingSession = session,
            WarehouseId = 1,
            Warehouse = warehouse,
            SourceType = "SalesOrder",
            SourceId = 1,
            SourceCode = "SO-1",
            Status = ShipmentStatus.Loaded,
            RowVersion = Ver,
            HandlingUnits =
            [
                new ShipmentHandlingUnit
                {
                    Id = 1,
                    ShipmentId = 1,
                    HandlingUnitId = 1,
                    HandlingUnit = hu,
                    Sequence = 1,
                    LoadedAt = DateTime.UtcNow
                }
            ]
        };
        db.Warehouses.Add(warehouse);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.WarehouseLocations.Add(loc);
        db.StockReservations.Add(res);
        db.StockAllocations.Add(allocPicked);
        db.PickingTasks.Add(task);
        db.PackingSessions.Add(session);
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(1);
        var stockReservationService = new Mock<IStockReservationService>();
        stockReservationService
            .Setup(x => x.ConsumeAsync(It.IsAny<StockReservation>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InventoryStockConsumption(1, 6m, InventoryStatus.Available, null, null)]);

        var service = new ShipmentService(db, auth.Object, user.Object, stockReservationService.Object);
        var dispatched = await service.DispatchAsync(1, new ShipmentStateCommandDto { RowVersion = EncodedVer });

        dispatched.Status.Should().Be(nameof(ShipmentStatus.Dispatched));
        var tx = await db.InventoryTransactions.SingleAsync();
        tx.TransactionType.Should().Be(TransactionType.Ship);
        tx.Quantity.Should().Be(6m);
    }

    [Fact]
    public async Task W4_SecC_02_ShortPickReallocate_UsesFefoAndUnreservedAvailableStockAndRejectsExpiredLot()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1" };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product
        {
            Id = 1,
            Code = "SKU-LOT",
            Name = "Lot Product",
            UnitId = 1,
            Unit = unit,
            TrackingType = ProductTrackingType.Lot,
            ExpiryControl = true
        };
        var binSource = new WarehouseLocation { Id = 1, WarehouseId = 1, Code = "BIN-SRC", Name = "Source", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true };
        var binExpired = new WarehouseLocation { Id = 2, WarehouseId = 1, Code = "BIN-EXP", Name = "Expired", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true };
        var binLate = new WarehouseLocation { Id = 3, WarehouseId = 1, Code = "BIN-LATE", Name = "Late", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true };
        var binEarly = new WarehouseLocation { Id = 4, WarehouseId = 1, Code = "BIN-EARLY", Name = "Early", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true };

        var lotSrc = new InventoryLot { Id = 1, ProductId = 1, LotNumber = "LOT-SRC", ExpiryDate = DateTime.UtcNow.Date.AddDays(10), ReceivedAt = DateTime.UtcNow.AddDays(-5) };
        var lotExp = new InventoryLot { Id = 2, ProductId = 1, LotNumber = "LOT-EXP", ExpiryDate = DateTime.UtcNow.Date.AddDays(-1), ReceivedAt = DateTime.UtcNow.AddDays(-10) };
        var lotLate = new InventoryLot { Id = 3, ProductId = 1, LotNumber = "LOT-LATE", ExpiryDate = DateTime.UtcNow.Date.AddDays(30), ReceivedAt = DateTime.UtcNow.AddDays(-2) };
        var lotEarly = new InventoryLot { Id = 4, ProductId = 1, LotNumber = "LOT-EARLY", ExpiryDate = DateTime.UtcNow.Date.AddDays(5), ReceivedAt = DateTime.UtcNow.AddDays(-3) };

        var statusDef = new InventoryStatusDefinition { Code = "AVAILABLE", Name = "Available", IsPickable = true, IsAllocatable = true };
        var stockSrc = new InventoryStock { Id = 1, WarehouseId = 1, LocationId = 1, Location = binSource, ProductId = 1, LotId = 1, Lot = lotSrc, Status = InventoryStatus.Available, StatusDefinition = statusDef, Quantity = 5, ReservedQuantity = 5 };
        var stockExp = new InventoryStock { Id = 2, WarehouseId = 1, LocationId = 2, Location = binExpired, ProductId = 1, LotId = 2, Lot = lotExp, Status = InventoryStatus.Available, StatusDefinition = statusDef, Quantity = 5, ReservedQuantity = 0 };
        var stockLate = new InventoryStock { Id = 3, WarehouseId = 1, LocationId = 3, Location = binLate, ProductId = 1, LotId = 3, Lot = lotLate, Status = InventoryStatus.Available, StatusDefinition = statusDef, Quantity = 5, ReservedQuantity = 0 };
        var stockEarly = new InventoryStock { Id = 4, WarehouseId = 1, LocationId = 4, Location = binEarly, ProductId = 1, LotId = 4, Lot = lotEarly, Status = InventoryStatus.Available, StatusDefinition = statusDef, Quantity = 5, ReservedQuantity = 0 };

        var res = new StockReservation { Id = 1, WarehouseId = 1, ProductId = 1, Quantity = 5, AllocatedQuantity = 5, Status = StockReservationStatus.Allocated, SourceType = "SalesOrder", SourceId = 1, SourceCode = "SO-1" };
        var alloc = new StockAllocation { Id = 1, ReservationId = 1, Reservation = res, WarehouseId = 1, LocationId = 1, Location = binSource, ProductId = 1, LotId = 1, Lot = lotSrc, InventoryStatus = InventoryStatus.Available, Quantity = 5, Status = StockAllocationStatus.Picking };
        var task = new PickingTask
        {
            Id = 1,
            TaskCode = "PICK-FEFO",
            WarehouseId = 1,
            Warehouse = warehouse,
            SourceType = "SalesOrder",
            SourceId = 1,
            SourceCode = "SO-1",
            Status = PickingTaskStatus.ShortPick,
            AssignedUserId = 1,
            RowVersion = Ver,
            Lines =
            [
                new PickingTaskLine
                {
                    Id = 1,
                    PickingTaskId = 1,
                    Sequence = 1,
                    AllocationId = 1,
                    Allocation = alloc,
                    ProductId = 1,
                    Product = product,
                    SourceLocationId = 1,
                    SourceLocation = binSource,
                    RequestedQuantity = 5,
                    PickedQuantity = 0,
                    Status = PickingTaskLineStatus.ShortPick,
                    ShortPicks =
                    [
                        new ShortPickException
                        {
                            Id = 10,
                            PickingTaskLineId = 1,
                            ExpectedQuantity = 5,
                            PickedQuantity = 0,
                            ShortageQuantity = 5,
                            Reason = "MISSING",
                            Status = ShortPickExceptionStatus.Open
                        }
                    ]
                }
            ]
        };

        db.Warehouses.Add(warehouse);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.WarehouseLocations.AddRange(binSource, binExpired, binLate, binEarly);
        db.InventoryLots.AddRange(lotSrc, lotExp, lotLate, lotEarly);
        db.InventoryStocks.AddRange(stockSrc, stockExp, stockLate, stockEarly);
        db.StockReservations.Add(res);
        db.StockAllocations.Add(alloc);
        db.PickingTasks.Add(task);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(1);

        var service = new PickingService(db, auth.Object, user.Object);
        var updated = await service.ResolveShortPickAsync(1, 10, new ResolveShortPickDto
        {
            Resolution = nameof(ShortPickResolutionType.AlternativeLocation),
            RowVersion = EncodedVer
        });

        updated.Lines.Should().HaveCount(2);
        var replacementLine = updated.Lines.Single(x => x.Status == nameof(PickingTaskLineStatus.InProgress));
        replacementLine.SourceLocationId.Should().Be(4);
        replacementLine.LotNumber.Should().Be("LOT-EARLY");
        stockSrc.ReservedQuantity.Should().Be(0m);
        stockEarly.ReservedQuantity.Should().Be(5m);
    }

    [Fact]
    public async Task W4_SecC_03_ShortPickBackorder_ReleasesCorrectLotBucketAndCreatesSalesOrderBackorder()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1" };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product { Id = 1, Code = "SKU-1", Name = "Product 1", UnitId = 1, Unit = unit, TrackingType = ProductTrackingType.Lot };
        var bin = new WarehouseLocation { Id = 1, WarehouseId = 1, Code = "BIN-01", Name = "Bin 01", LocationType = WarehouseLocationType.Legacy, IsActive = true, IsPickable = true };
        var lot1 = new InventoryLot { Id = 1, ProductId = 1, LotNumber = "LOT-1" };
        var lot2 = new InventoryLot { Id = 2, ProductId = 1, LotNumber = "LOT-2" };

        var stockLot1 = new InventoryStock { Id = 1, WarehouseId = 1, LocationId = 1, Location = bin, ProductId = 1, LotId = 1, Lot = lot1, Status = InventoryStatus.Available, Quantity = 10, ReservedQuantity = 10 };
        var stockLot2 = new InventoryStock { Id = 2, WarehouseId = 1, LocationId = 1, Location = bin, ProductId = 1, LotId = 2, Lot = lot2, Status = InventoryStatus.Available, Quantity = 10, ReservedQuantity = 10 };

        var soLine = new SalesOrderLine { Id = 1, SalesOrderId = 50, ProductId = 1, OrderedQuantity = 10 };
        var so = new SalesOrder { Id = 50, OrderCode = "SO-50", WarehouseId = 1, CustomerId = 1, Status = SalesOrderStatus.Draft, RowVersion = Ver, Lines = [soLine] };
        var res = new StockReservation { Id = 1, WarehouseId = 1, ProductId = 1, Quantity = 10, AllocatedQuantity = 10, Status = StockReservationStatus.Allocated, SourceType = "SalesOrder", SourceId = 50, SourceCode = "SO-50" };
        var alloc = new StockAllocation { Id = 1, ReservationId = 1, Reservation = res, WarehouseId = 1, LocationId = 1, Location = bin, ProductId = 1, LotId = 2, Lot = lot2, InventoryStatus = InventoryStatus.Available, Quantity = 10, Status = StockAllocationStatus.Picking };
        var task = new PickingTask
        {
            Id = 1,
            TaskCode = "PICK-LOT2",
            WarehouseId = 1,
            Warehouse = warehouse,
            SourceType = "SalesOrder",
            SourceId = 50,
            SourceCode = "SO-50",
            Status = PickingTaskStatus.ShortPick,
            AssignedUserId = 1,
            RowVersion = Ver,
            Lines =
            [
                new PickingTaskLine
                {
                    Id = 1,
                    PickingTaskId = 1,
                    Sequence = 1,
                    AllocationId = 1,
                    Allocation = alloc,
                    ProductId = 1,
                    Product = product,
                    SourceLocationId = 1,
                    SourceLocation = bin,
                    RequestedQuantity = 10,
                    PickedQuantity = 6,
                    Status = PickingTaskLineStatus.ShortPick,
                    ShortPicks =
                    [
                        new ShortPickException
                        {
                            Id = 20,
                            PickingTaskLineId = 1,
                            ExpectedQuantity = 10,
                            PickedQuantity = 6,
                            ShortageQuantity = 4,
                            Reason = "DAMAGED",
                            Status = ShortPickExceptionStatus.Open
                        }
                    ]
                }
            ]
        };

        db.Warehouses.Add(warehouse);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.WarehouseLocations.Add(bin);
        db.InventoryLots.AddRange(lot1, lot2);
        db.InventoryStocks.AddRange(stockLot1, stockLot2);
        db.SalesOrders.Add(so);
        db.StockReservations.Add(res);
        db.StockAllocations.Add(alloc);
        db.PickingTasks.Add(task);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(1);

        var service = new PickingService(db, auth.Object, user.Object);
        await service.ResolveShortPickAsync(1, 20, new ResolveShortPickDto
        {
            Resolution = nameof(ShortPickResolutionType.Backorder),
            RowVersion = EncodedVer
        });

        stockLot1.ReservedQuantity.Should().Be(10m);
        stockLot2.ReservedQuantity.Should().Be(6m);
        soLine.Backorder.Should().NotBeNull();
        soLine.Backorder!.Quantity.Should().Be(4m);
    }

    [Fact]
    public async Task W4_SecC_04_Packing_EnforcesTrackedLotAndHierarchyInvariants()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1" };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product
        {
            Id = 1,
            Code = "SKU-LOT",
            Name = "Lot Product",
            UnitId = 1,
            Unit = unit,
            TrackingType = ProductTrackingType.Lot
        };
        var lot = new InventoryLot { Id = 1, ProductId = 1, LotNumber = "LOT-REQ-01", ExpiryDate = DateTime.UtcNow.Date.AddDays(10) };
        var alloc = new StockAllocation { Id = 1, WarehouseId = 1, LocationId = 1, ProductId = 1, LotId = 1, Lot = lot, Quantity = 5, Status = StockAllocationStatus.Picked };
        var task = new PickingTask
        {
            Id = 1,
            TaskCode = "PICK-01",
            WarehouseId = 1,
            Warehouse = warehouse,
            SourceType = "SalesOrder",
            SourceId = 1,
            SourceCode = "SO-1",
            Status = PickingTaskStatus.Completed,
            RowVersion = Ver,
            Lines =
            [
                new PickingTaskLine
                {
                    Id = 1,
                    PickingTaskId = 1,
                    AllocationId = 1,
                    Allocation = alloc,
                    ProductId = 1,
                    Product = product,
                    RequestedQuantity = 5,
                    PickedQuantity = 5,
                    Status = PickingTaskLineStatus.Picked
                }
            ]
        };
        var childHu = new HandlingUnit { Id = 1, PackingSessionId = 1, WarehouseId = 1, HuCode = "CHILD-1", Barcode = "CHILD-1", Type = HandlingUnitType.Carton, Status = HandlingUnitStatus.Open, RowVersion = Ver };
        var parentHu = new HandlingUnit { Id = 2, PackingSessionId = 1, WarehouseId = 1, HuCode = "PARENT-1", Barcode = "PARENT-1", Type = HandlingUnitType.Pallet, Status = HandlingUnitStatus.Open, RowVersion = Ver };
        var session = new PackingSession
        {
            Id = 1,
            SessionCode = "PACK-01",
            PickingTaskId = 1,
            PickingTask = task,
            WarehouseId = 1,
            Warehouse = warehouse,
            Status = PackingSessionStatus.Open,
            RowVersion = Ver,
            HandlingUnits = [childHu, parentHu]
        };
        db.Warehouses.Add(warehouse);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.InventoryLots.Add(lot);
        db.StockAllocations.Add(alloc);
        db.PickingTasks.Add(task);
        db.PackingSessions.Add(session);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(1);
        var integration = new Mock<IPackingSessionIntegration>();

        var service = new PackingService(db, auth.Object, user.Object, integration.Object);

        // Missing LotNumber on lot-tracked product is rejected
        var missingLotAct = () => service.PackAsync(1, new PackIntoHandlingUnitDto
        {
            HandlingUnitId = 1,
            PickingTaskLineId = 1,
            ProductBarcode = "SKU-LOT",
            Quantity = 5,
            LotNumber = null,
            RowVersion = EncodedVer
        });
        (await missingLotAct.Should().ThrowAsync<BusinessRuleException>())
            .Which.Data["ErrorCode"].Should().Be("HU_CONTENT_MISMATCH");

        // Valid LotNumber with zero-width char succeeds
        await service.PackAsync(1, new PackIntoHandlingUnitDto
        {
            HandlingUnitId = 1,
            PickingTaskLineId = 1,
            ProductBarcode = "\u200bSKU-LOT\ufeff",
            Quantity = 5,
            LotNumber = "\u200bLOT-REQ-01",
            RowVersion = EncodedVer
        });

        // Nest open/in-use child into parent, then attempt to close parent while child is still InUse -> rejected
        await service.NestHandlingUnitAsync(1, 1, new NestHandlingUnitDto
        {
            ParentHandlingUnitId = 2,
            RowVersion = EncodedVer
        });
        var closeParentWithOpenChild = () => service.CloseHandlingUnitAsync(1, 2, new PackingStateCommandDto { RowVersion = EncodedVer });
        (await closeParentWithOpenChild.Should().ThrowAsync<BusinessRuleException>())
            .Which.Data["ErrorCode"].Should().Be("HU_CLOSED");

        // Close child first, then close parent, then attempt to unnest child from closed parent -> rejected
        await service.CloseHandlingUnitAsync(1, 1, new PackingStateCommandDto { RowVersion = EncodedVer });
        await service.CloseHandlingUnitAsync(1, 2, new PackingStateCommandDto { RowVersion = EncodedVer });
        var unnestFromClosedParent = () => service.UnnestHandlingUnitAsync(1, 1, new PackingStateCommandDto { RowVersion = EncodedVer });
        (await unnestFromClosedParent.Should().ThrowAsync<BusinessRuleException>())
            .Which.Data["ErrorCode"].Should().Be("HU_CLOSED");
    }

    [Fact]
    public async Task W4_SecC_05_ReceiptInventoryIdentity_BlocksDuplicateTrackedSerialInLocalContext()
    {
        await using var db = CreateInMemoryContext();
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product { Id = 1, Code = "SKU-SER", Name = "Serial Product", UnitId = 1, Unit = unit, TrackingType = ProductTrackingType.Serial };
        var receipt = new ImportReceipt { Id = 1, Code = "IR-01", WarehouseId = 1, Status = ReceiptStatus.ReadyToPost };
        var detail = new ImportReceiptDetail { Id = 1, ImportReceiptId = 1, ProductId = 1, Product = product, BaseAcceptedQuantity = 1 };
        db.Units.Add(unit);
        db.Products.Add(product);
        db.ImportReceipts.Add(receipt);
        db.ImportReceiptDetails.Add(detail);
        db.ImportReceiptInventoryIdentities.Add(new ImportReceiptInventoryIdentity
        {
            Id = 1,
            ImportReceiptDetailId = 1,
            ProductId = 1,
            TargetStatus = InventoryStatus.Available,
            BaseQuantity = 1,
            SerialNumber = "SER-001"
        });
        await db.SaveChangesAsync();

        // Simulate a serial added earlier in the same DbContext unit of work before SaveChanges
        db.InventorySerials.Add(new InventorySerial
        {
            ProductId = 1,
            SerialNumber = "SER-001",
            CreatedAt = DateTime.UtcNow
        });

        var auth = new Mock<IWarehouseAuthorizationService>();
        var service = new ReceiptInventoryIdentityService(db, auth.Object);
        var act = () => service.TryPostTrackedBucketAsync(receipt, detail, InventoryStatus.Available, 1m, 1, 10);
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Data["ErrorCode"].Should().Be("SERIAL_ALREADY_ON_HAND");
    }

    [Fact]
    public async Task W4_SecC_06_ReceiptInboundPlanningIntegration_ExcludesRejectedQtyAndHonorsUnderReceiptTolerance()
    {
        await using var db = CreateInMemoryContext();
        var poLine = new PurchaseOrderLine
        {
            Id = 1,
            PurchaseOrderId = 1,
            ExternalLineId = "L1",
            ProductId = 1,
            OrderedQuantity = 100,
            BaseOrderedQuantity = 100,
            AllowedOverReceiptPct = 0,
            AllowedUnderReceiptPct = 5
        };
        var po = new PurchaseOrder
        {
            Id = 1,
            Code = "PO-01",
            WarehouseId = 1,
            SupplierId = 1,
            Status = PurchaseOrderStatus.Open,
            RowVersion = Ver,
            Lines = [poLine]
        };
        var receipt = new ImportReceipt
        {
            Id = 1,
            Code = "IR-01",
            WarehouseId = 1,
            PurchaseOrderId = 1,
            Status = ReceiptStatus.ReadyToPost,
            Details =
            [
                new ImportReceiptDetail
                {
                    Id = 1,
                    ImportReceiptId = 1,
                    PurchaseOrderLineId = 1,
                    ProductId = 1,
                    BaseReceivedQuantity = 100,
                    BaseAcceptedQuantity = 95,
                    BaseDamagedQuantity = 0,
                    BaseRejectedQuantity = 5,
                    BasePostedQuantity = 100
                }
            ]
        };
        db.PurchaseOrders.Add(po);
        db.ImportReceipts.Add(receipt);
        await db.SaveChangesAsync();

        var service = new ReceiptInboundPlanningIntegration(db);
        await service.ValidatePostAsync(receipt);
        await service.ApplyPostedStateAsync(receipt, 1);

        po.Status.Should().Be(PurchaseOrderStatus.Received);
    }

    [Fact]
    public async Task W4_SecC_07_BarcodeNormalization_StripsControlAndZeroWidthWhilePreservingLeadingZeros()
    {
        var repo = new Mock<IProductCatalogRepository>();
        var products = new Mock<IProductRepository>();
        products.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Product { Id = 1, Code = "SKU-1", Name = "Product 1" });
        repo.Setup(x => x.AddBarcodeAsync(It.IsAny<ProductBarcode>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProductBarcode b, CancellationToken _) => b);

        var service = new ProductCatalogService(repo.Object, products.Object);
        var created = await service.AddBarcodeAsync(1, new CreateProductBarcodeDto
        {
            Value = "\u200b\ufeff0012345678905\r\n"
        });

        created.Value.Should().Be("0012345678905");
    }

    [Fact]
    public async Task W4_SecC_08_WarehouseCalendarAndDockYard_SupportsOvernightShiftAfterMidnightAndNullShiftWhenClosed()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1" };
        var calendar = new WarehouseCalendar
        {
            WarehouseId = 1,
            TimeZoneId = "UTC",
            RowVersion = Ver,
            Days =
            [
                new WarehouseCalendarDay
                {
                    Id = 1,
                    DayOfWeek = (int)DayOfWeek.Monday,
                    IsOpen = true,
                    OpensAtLocal = new TimeSpan(22, 0, 0),
                    ClosesAtLocal = new TimeSpan(6, 0, 0)
                },
                new WarehouseCalendarDay
                {
                    Id = 2,
                    DayOfWeek = (int)DayOfWeek.Tuesday,
                    IsOpen = false
                },
                new WarehouseCalendarDay
                {
                    Id = 3,
                    DayOfWeek = (int)DayOfWeek.Wednesday,
                    IsOpen = false
                }
            ],
            Shifts =
            [
                new WarehouseShift
                {
                    Id = 1,
                    WarehouseId = 1,
                    Code = "NIGHT",
                    Name = "Night Shift",
                    StartTimeLocal = new TimeSpan(22, 0, 0),
                    EndTimeLocal = new TimeSpan(6, 0, 0),
                    IsActive = true,
                    RowVersion = Ver
                }
            ]
        };
        // 2026-10-20 is a Tuesday; 01:00 to 02:00 UTC falls in Monday's overnight window (22:00 Mon -> 06:00 Tue)
        var appointment = new DockAppointment
        {
            Id = 1,
            Code = "APT-OVERNIGHT",
            WarehouseId = 1,
            Warehouse = warehouse,
            Direction = DockAppointmentDirection.Inbound,
            Status = DockAppointmentStatus.Draft,
            PlannedStartUtc = new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc),
            PlannedEndUtc = new DateTime(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc),
            CarrierName = "Carrier",
            RowVersion = Ver
        };
        db.Warehouses.Add(warehouse);
        db.WarehouseCalendars.Add(calendar);
        db.DockAppointments.Add(appointment);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var user = new Mock<ICurrentUser>();
        user.SetupGet(x => x.UserId).Returns(1);

        // 2026-10-21 23:00 UTC is Wednesday (closed day), even though 23:00 is inside NIGHT shift hours
        var wednesdayNightProvider = new FixedTimeProvider(new DateTimeOffset(2026, 10, 21, 23, 0, 0, TimeSpan.Zero));
        var calendarService = new WarehouseCalendarService(db, auth.Object, user.Object, wednesdayNightProvider);
        var calendarDto = await calendarService.GetAsync(1);
        calendarDto.IsOpenNow.Should().BeFalse();
        calendarDto.CurrentShiftCode.Should().BeNull();

        var dockYardService = new DockYardService(db, auth.Object, user.Object, wednesdayNightProvider);
        var confirmed = await dockYardService.ConfirmAsync(1, new DockAppointmentCommandDto { RowVersion = EncodedVer });
        confirmed.Status.Should().Be(DockAppointmentStatus.Confirmed);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
