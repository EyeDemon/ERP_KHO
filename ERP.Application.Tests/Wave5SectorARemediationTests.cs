using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ERP.Application.Tests;

public class Wave5SectorARemediationTests
{
    private static readonly byte[] Ver = [0, 0, 0, 0, 0, 0, 0, 1];
    private static string EncodedVer => Convert.ToBase64String(Ver);

    private static ErpKhoDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new ErpKhoDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    [Fact]
    public async Task W5_01_PutawayMoveAsync_FiltersSourceBucketsByReceiptDeclaredIdentities_AndAvoidsCrossReceiptLockedLot()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1", IsActive = true };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product
        {
            Id = 1,
            Code = "SKU-LOT",
            Name = "Lot Product",
            UnitId = 1,
            Unit = unit,
            TrackingType = ProductTrackingType.Lot,
            IsActive = true
        };
        var receivingLoc = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "RECEIVING",
            Name = "Receiving Staging",
            LocationType = WarehouseLocationType.Receiving,
            IsActive = true,
            IsReceivable = true,
            IsSystemManaged = true,
            RowVersion = Ver
        };
        var storageLoc = new WarehouseLocation
        {
            Id = 20,
            WarehouseId = 1,
            Code = "BIN-A1",
            Name = "Storage Bin A1",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            RowVersion = Ver
        };

        var lot1 = new InventoryLot { Id = 1, ProductId = 1, LotNumber = "LOT-RECEIPT-1" };
        var lot2 = new InventoryLot { Id = 2, ProductId = 1, LotNumber = "LOT-RECEIPT-2" };

        var receipt1 = new ImportReceipt
        {
            Id = 100,
            Code = "IR-100",
            WarehouseId = 1,
            Warehouse = warehouse,
            Status = ReceiptStatus.Posted,
            CreatedBy = 1,
            Details =
            [
                new ImportReceiptDetail
                {
                    Id = 1001,
                    ImportReceiptId = 100,
                    ProductId = 1,
                    Product = product,
                    AcceptedQuantity = 5,
                    BaseAcceptedQuantity = 5,
                    OperationUnitId = 1,
                    OperationUnitCodeSnapshot = "PCS",
                    BaseUnitId = 1,
                    BaseUnitCodeSnapshot = "PCS",
                    ConversionFactor = 1
                }
            ]
        };

        var receipt2 = new ImportReceipt
        {
            Id = 200,
            Code = "IR-200",
            WarehouseId = 1,
            Warehouse = warehouse,
            Status = ReceiptStatus.Posted,
            CreatedBy = 1,
            Details =
            [
                new ImportReceiptDetail
                {
                    Id = 2001,
                    ImportReceiptId = 200,
                    ProductId = 1,
                    Product = product,
                    AcceptedQuantity = 5,
                    BaseAcceptedQuantity = 5,
                    OperationUnitId = 1,
                    OperationUnitCodeSnapshot = "PCS",
                    BaseUnitId = 1,
                    BaseUnitCodeSnapshot = "PCS",
                    ConversionFactor = 1
                }
            ]
        };

        db.Warehouses.Add(warehouse);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.WarehouseLocations.AddRange(receivingLoc, storageLoc);
        db.InventoryLots.AddRange(lot1, lot2);
        db.ImportReceipts.AddRange(receipt1, receipt2);
        db.ImportReceiptInventoryIdentities.AddRange(
            new ImportReceiptInventoryIdentity
            {
                Id = 1,
                ImportReceiptDetailId = 1001,
                ProductId = 1,
                TargetStatus = InventoryStatus.Available,
                BaseQuantity = 5,
                LotNumber = "LOT-RECEIPT-1"
            },
            new ImportReceiptInventoryIdentity
            {
                Id = 2,
                ImportReceiptDetailId = 2001,
                ProductId = 1,
                TargetStatus = InventoryStatus.Available,
                BaseQuantity = 5,
                LotNumber = "LOT-RECEIPT-2"
            });

        // Both lots are sitting at RECEIVING, and LOT-RECEIPT-1 (smaller LotId) is locked!
        var stockLot1 = new InventoryStock
        {
            Id = 1,
            WarehouseId = 1,
            LocationId = 10,
            ProductId = 1,
            LotId = 1,
            Lot = lot1,
            Status = InventoryStatus.Available,
            Quantity = 5,
            ReservedQuantity = 0
        };
        var stockLot2 = new InventoryStock
        {
            Id = 2,
            WarehouseId = 1,
            LocationId = 10,
            ProductId = 1,
            LotId = 2,
            Lot = lot2,
            Status = InventoryStatus.Available,
            Quantity = 5,
            ReservedQuantity = 0
        };
        db.InventoryStocks.AddRange(stockLot1, stockLot2);
        db.InventoryLocks.Add(new InventoryLock
        {
            Id = 1,
            WarehouseId = 1,
            LocationId = 10,
            ProductId = 1,
            LotId = 1,
            Status = InventoryLockStatus.Active,
            LockType = InventoryLockType.InvestigationHold,
            Reason = "Hold Receipt 1 Lot",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        var task2 = new PutawayTask
        {
            Id = 2,
            ReceiptId = 200,
            Receipt = receipt2,
            WarehouseId = 1,
            Warehouse = warehouse,
            Status = PutawayTaskStatus.InProgress,
            AssignedUserId = 1,
            RowVersion = Ver,
            Items =
            [
                new PutawayTaskItem
                {
                    Id = 20,
                    PutawayTaskId = 2,
                    ReceiptLineId = 2001,
                    ReceiptLine = receipt2.Details.Single(),
                    ProductId = 1,
                    InventoryStatus = InventoryStatus.Available,
                    SourceLocationId = 10,
                    SourceLocation = receivingLoc,
                    OperationUnitId = 1,
                    OperationUnitCodeSnapshot = "PCS",
                    BaseUnitId = 1,
                    BaseUnitCodeSnapshot = "PCS",
                    ConversionFactorSnapshot = 1,
                    BaseUnitDecimalPlaces = 0,
                    RequiredOperationQuantity = 5,
                    RequiredBaseQuantity = 5,
                    MovedBaseQuantity = 0,
                    RowVersion = Ver
                }
            ]
        };
        db.PutawayTasks.Add(task2);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);
        currentUser.SetupGet(x => x.Role).Returns("WarehouseStaff");
        var lockEval = new Mock<IInventoryLockEvaluator>();

        var service = new PutawayService(db, auth.Object, currentUser.Object, lockEval.Object);
        var result = await service.MoveAsync(2, new MovePutawayItemDto
        {
            ItemId = 20,
            DestinationLocationId = 20,
            Quantity = 5,
            UnitCode = "PCS",
            RowVersion = EncodedVer
        });

        result.Status.Should().Be(nameof(PutawayTaskStatus.Completed));
        stockLot1.Quantity.Should().Be(5m, "Receipt 1's lot at RECEIVING must not be touched by Receipt 2's putaway task");
        stockLot2.Quantity.Should().Be(0m, "Receipt 2's declared lot must be moved out of RECEIVING");

        var destBucket = await db.InventoryStocks.SingleAsync(x => x.LocationId == 20 && x.ProductId == 1);
        destBucket.LotId.Should().Be(2);
        destBucket.Quantity.Should().Be(5m);

        var moveRecord = await db.InventoryLocationMovements.SingleAsync(x => x.PutawayTaskId == 2);
        moveRecord.LotId.Should().Be(2);
        moveRecord.ReceiptLineId.Should().Be(2001);
    }

    [Fact]
    public async Task W5_02_ResolveAlternativeLocation_NeverStealsOtherOrderUncommittedReserved_AndAlwaysReleasesSourceReservedQuantity()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1" };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product { Id = 1, Code = "SKU-1", Name = "Product 1", UnitId = 1, Unit = unit };

        var binA = new WarehouseLocation { Id = 1, WarehouseId = 1, Code = "BIN-A", Name = "Bin A", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };
        var binB = new WarehouseLocation { Id = 2, WarehouseId = 1, Code = "BIN-B", Name = "Bin B", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };
        var binC = new WarehouseLocation { Id = 3, WarehouseId = 1, Code = "BIN-C", Name = "Bin C", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };

        // Bin A: Order 1 has 10 reserved & allocated
        var stockBinA = new InventoryStock { Id = 1, WarehouseId = 1, LocationId = 1, Location = binA, ProductId = 1, Status = InventoryStatus.Available, Quantity = 10, ReservedQuantity = 10 };
        // Bin B: Order 2 has 6 reserved (uncommitted: Committed = 0, ReservedQuantity = 6, Quantity = 6)
        var stockBinB = new InventoryStock { Id = 2, WarehouseId = 1, LocationId = 2, Location = binB, ProductId = 1, Status = InventoryStatus.Available, Quantity = 6, ReservedQuantity = 6 };
        // Bin C: 6 units of truly unreserved available stock
        var stockBinC = new InventoryStock { Id = 3, WarehouseId = 1, LocationId = 3, Location = binC, ProductId = 1, Status = InventoryStatus.Available, Quantity = 6, ReservedQuantity = 0 };

        var resOrder1 = new StockReservation { Id = 1, WarehouseId = 1, ProductId = 1, Quantity = 10, AllocatedQuantity = 10, Status = StockReservationStatus.Allocated, SourceType = "SalesOrder", SourceId = 10, SourceCode = "SO-10" };
        var resOrder2 = new StockReservation { Id = 2, WarehouseId = 1, ProductId = 1, Quantity = 6, AllocatedQuantity = 0, Status = StockReservationStatus.Active, SourceType = "SalesOrder", SourceId = 20, SourceCode = "SO-20" };

        var allocOrder1 = new StockAllocation
        {
            Id = 1,
            AllocationCode = "ALC-ORD1",
            ReservationId = 1,
            Reservation = resOrder1,
            WarehouseId = 1,
            LocationId = 1,
            Location = binA,
            ProductId = 1,
            InventoryStatus = InventoryStatus.Available,
            Quantity = 10,
            Status = StockAllocationStatus.Picking
        };

        var task = new PickingTask
        {
            Id = 1,
            TaskCode = "PICK-ORD1",
            WarehouseId = 1,
            Warehouse = warehouse,
            SourceType = "SalesOrder",
            SourceId = 10,
            SourceCode = "SO-10",
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
                    Allocation = allocOrder1,
                    ProductId = 1,
                    Product = product,
                    SourceLocationId = 1,
                    SourceLocation = binA,
                    RequestedQuantity = 10,
                    PickedQuantity = 4,
                    Status = PickingTaskLineStatus.ShortPick,
                    ShortPicks =
                    [
                        new ShortPickException
                        {
                            Id = 100,
                            PickingTaskLineId = 1,
                            ExpectedQuantity = 10,
                            PickedQuantity = 4,
                            ShortageQuantity = 6,
                            Reason = "MISSING_AT_BIN_A",
                            Status = ShortPickExceptionStatus.Open
                        }
                    ]
                }
            ]
        };

        db.Warehouses.Add(warehouse);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.WarehouseLocations.AddRange(binA, binB, binC);
        db.InventoryStocks.AddRange(stockBinA, stockBinB, stockBinC);
        db.StockReservations.AddRange(resOrder1, resOrder2);
        db.StockAllocations.Add(allocOrder1);
        db.PickingTasks.Add(task);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);

        var service = new PickingService(db, auth.Object, currentUser.Object);
        var updated = await service.ResolveShortPickAsync(1, 100, new ResolveShortPickDto
        {
            Resolution = nameof(ShortPickResolutionType.AlternativeLocation),
            RowVersion = EncodedVer
        });

        // Bin B (Order 2's uncommitted reservation) must NOT be hijacked
        stockBinB.ReservedQuantity.Should().Be(6m);
        db.StockAllocations.Local.Where(a => a.LocationId == 2).Should().BeEmpty();

        // Bin C (unreserved stock) must absorb the 6-unit reservation shift
        stockBinC.ReservedQuantity.Should().Be(6m);

        // Bin A (short-picked source bin) must have its ReservedQuantity reduced from 10 to 4 (0 orphaned reserve!)
        stockBinA.ReservedQuantity.Should().Be(4m);

        var newLine = updated.Lines.Single(x => x.Status == nameof(PickingTaskLineStatus.InProgress));
        newLine.SourceLocationId.Should().Be(3);
        newLine.RequestedQuantity.Should().Be(6m);
    }

    [Fact]
    public async Task W5_04_ReallocateBackorder_AfterShortPickResolution_SucceedsAndAppendsPickingLine_AndSupervisorOverridePreservesInvariants()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1", IsActive = true };
        var customer = new BusinessPartner { Id = 1, Code = "CUST1", Name = "Customer 1", IsCustomer = true, IsActive = true };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product { Id = 1, Code = "SKU-1", Name = "Product 1", UnitId = 1, Unit = unit, IsActive = true };
        var binA = new WarehouseLocation { Id = 1, WarehouseId = 1, Code = "BIN-A", Name = "Bin A", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true };

        var stockBinA = new InventoryStock
        {
            Id = 1,
            WarehouseId = 1,
            LocationId = 1,
            Location = binA,
            ProductId = 1,
            Status = InventoryStatus.Available,
            Quantity = 10,
            ReservedQuantity = 10
        };

        var soLine = new SalesOrderLine
        {
            Id = 1,
            SalesOrderId = 1,
            ExternalLineId = "L1",
            ProductId = 1,
            Product = product,
            OrderedQuantity = 10,
            CancelledQuantity = 0,
            UomCode = "PCS"
        };
        var so = new SalesOrder
        {
            Id = 1,
            OrderCode = "SO-1",
            ExternalOrderId = "EXT-SO-1",
            CustomerId = 1,
            Customer = customer,
            WarehouseId = 1,
            Warehouse = warehouse,
            Status = SalesOrderStatus.Released,
            RowVersion = Ver,
            Lines = [soLine]
        };

        var reservation = new StockReservation
        {
            Id = 1,
            ReservationCode = "RES-1",
            WarehouseId = 1,
            Warehouse = warehouse,
            ProductId = 1,
            Product = product,
            Quantity = 10,
            AllocatedQuantity = 10,
            Status = StockReservationStatus.Allocated,
            SourceType = "SalesOrder",
            SourceId = 1,
            SourceCode = "EXT-SO-1",
            ExpiresAt = DateTime.UtcNow.AddHours(4)
        };
        var alloc = new StockAllocation
        {
            Id = 1,
            AllocationCode = "ALC-1",
            ReservationId = 1,
            Reservation = reservation,
            WarehouseId = 1,
            LocationId = 1,
            Location = binA,
            ProductId = 1,
            InventoryStatus = InventoryStatus.Available,
            Quantity = 10,
            Status = StockAllocationStatus.Picking
        };
        var task = new PickingTask
        {
            Id = 1,
            TaskCode = "PICK-1",
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
                    SourceLocation = binA,
                    RequestedQuantity = 10,
                    PickedQuantity = 6,
                    Status = PickingTaskLineStatus.ShortPick,
                    ShortPicks =
                    [
                        new ShortPickException
                        {
                            Id = 10,
                            PickingTaskLineId = 1,
                            ExpectedQuantity = 10,
                            PickedQuantity = 6,
                            ShortageQuantity = 4,
                            Reason = "SHORT_4",
                            Status = ShortPickExceptionStatus.Open
                        }
                    ]
                }
            ]
        };

        db.Warehouses.Add(warehouse);
        db.BusinessPartners.Add(customer);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.WarehouseLocations.Add(binA);
        db.InventoryStocks.Add(stockBinA);
        db.SalesOrders.Add(so);
        db.StockReservations.Add(reservation);
        db.StockAllocations.Add(alloc);
        db.PickingTasks.Add(task);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        auth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);

        var pickingService = new PickingService(db, auth.Object, currentUser.Object);

        // SupervisorOverride on short pick must NOT cancel SalesOrderLine.CancelledQuantity; it moves shortage to Backorder
        await pickingService.OverrideShortPickAsync(1, 10, new OverrideShortPickDto
        {
            Reason = "Supervisor approved partial pick and deferred remainder",
            RowVersion = EncodedVer
        });

        soLine.CancelledQuantity.Should().Be(0m);
        soLine.Backorder.Should().NotBeNull();
        soLine.Backorder!.Quantity.Should().Be(4m);
        soLine.Backorder.RowVersion = Ver;
        task.Status.Should().Be(PickingTaskStatus.Resolved);

        // Replenish stock at Bin A so 4 units are available for backorder reallocation
        stockBinA.Quantity = 10m; // ReservedQuantity is currently 6, so 4 unreserved available
        await db.SaveChangesAsync();

        var stockRepo = new Mock<IInventoryStockRepository>();
        stockRepo.Setup(x => x.GetAvailableQuantityAsync(1, 1, It.IsAny<CancellationToken>())).ReturnsAsync(4m);

        var resService = new Mock<IStockReservationService>();
        resService
            .Setup(x => x.IncreaseSourceReservationAsync("SalesOrder", 1, "EXT-SO-1", 1, 1, 4m, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                stockBinA.ReservedQuantity += 4m;
                reservation.Quantity += 4m;
                reservation.Status = StockReservationStatus.PartiallyAllocated;
                return reservation;
            });

        var allocService = new Mock<IStockAllocationService>();
        allocService
            .Setup(x => x.AutoAllocateAsync(It.IsAny<CreateStockAllocationDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var newAlloc = new StockAllocation
                {
                    Id = 2,
                    AllocationCode = "ALC-2",
                    ReservationId = 1,
                    WarehouseId = 1,
                    LocationId = 1,
                    ProductId = 1,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = 4m,
                    Status = StockAllocationStatus.Active
                };
                db.StockAllocations.Add(newAlloc);
                reservation.AllocatedQuantity += 4m;
                reservation.Status = StockReservationStatus.Allocated;
                db.SaveChanges();
                return [new StockAllocationDto { Id = 2, ReservationId = 1, WarehouseId = 1, LocationId = 1, ProductId = 1, Quantity = 4m }];
            });

        var backorderService = new SalesOrderBackorderService(
            db,
            stockRepo.Object,
            resService.Object,
            allocService.Object,
            auth.Object,
            currentUser.Object);

        var recovered = await backorderService.ReallocateBackorderAsync(soLine.Backorder.Id, new BackorderReallocateDto
        {
            Quantity = 4m,
            RowVersion = EncodedVer
        });

        recovered.RecoveredQuantity.Should().Be(4m);
        recovered.RemainingQuantity.Should().Be(0m);
        recovered.Status.Should().Be(nameof(BackorderStatus.Allocated));

        // PickingTask was reopened to InProgress with a new PickingTaskLine for the recovered allocation
        task.Status.Should().Be(PickingTaskStatus.InProgress);
        task.Lines.Should().HaveCount(2);
        task.Lines.Should().Contain(l => l.AllocationId == 2 && l.RequestedQuantity == 4m && l.Status == PickingTaskLineStatus.InProgress);
    }

    [Fact]
    public async Task W5_05_And_W5_28_ImportReceipt_ApproveValidatesPoTolerance_CancelAllowsNonPosted_AndReceiveLogsActualStatus()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1", IsActive = true };
        var supplier = new BusinessPartner { Id = 1, Code = "SUP1", Name = "Supplier 1", IsSupplier = true, IsActive = true };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0 };
        var product = new Product { Id = 1, Code = "SKU1", Name = "Product 1", UnitId = 1, Unit = unit, IsActive = true };
        var po = new PurchaseOrder
        {
            Id = 10,
            Code = "PO-10",
            ExternalPoId = "EXT-PO-10",
            WarehouseId = 1,
            SupplierId = 1,
            Status = PurchaseOrderStatus.Open,
            Lines =
            [
                new PurchaseOrderLine
                {
                    Id = 100,
                    PurchaseOrderId = 10,
                    ExternalLineId = "PO-L1",
                    ProductId = 1,
                    OrderedQuantity = 10m,
                    BaseOrderedQuantity = 10m,
                    ConversionFactorSnapshot = 1m,
                    AllowedOverReceiptPct = 10m // Max allowed = 11
                }
            ]
        };
        var asn = new Asn
        {
            Id = 50,
            Code = "ASN-50",
            WarehouseId = 1,
            SupplierId = 1,
            PurchaseOrderId = 10,
            Status = AsnStatus.Receiving
        };

        var overReceipt = new ImportReceipt
        {
            Id = 1,
            Code = "IR-OVER",
            WarehouseId = 1,
            SupplierId = 1,
            Supplier = supplier,
            PurchaseOrderId = 10,
            AsnId = 50,
            Asn = asn,
            Status = ReceiptStatus.Received,
            CreatedBy = 1,
            Details =
            [
                new ImportReceiptDetail
                {
                    Id = 11,
                    ImportReceiptId = 1,
                    PurchaseOrderLineId = 100,
                    ProductId = 1,
                    Quantity = 15m,
                    ExpectedQuantity = 15m,
                    ReceivedQuantity = 15m,
                    AcceptedQuantity = 15m,
                    BaseReceivedQuantity = 15m,
                    BaseAcceptedQuantity = 15m,
                    ConversionFactor = 1m
                }
            ]
        };

        var qcReceipt = new ImportReceipt
        {
            Id = 2,
            Code = "IR-QC",
            WarehouseId = 1,
            SupplierId = 1,
            Status = ReceiptStatus.Draft,
            CreatedBy = 1,
            Details =
            [
                new ImportReceiptDetail
                {
                    Id = 22,
                    ImportReceiptId = 2,
                    ProductId = 1,
                    Quantity = 5m,
                    ExpectedQuantity = 5m,
                    RequiresQc = true,
                    OperationUnitDecimalPlaces = 0,
                    BaseUnitDecimalPlaces = 0,
                    ConversionFactor = 1m
                }
            ]
        };

        db.Warehouses.Add(warehouse);
        db.BusinessPartners.Add(supplier);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.PurchaseOrders.Add(po);
        db.Asns.Add(asn);
        db.ImportReceipts.AddRange(overReceipt, qcReceipt);
        await db.SaveChangesAsync();

        var importRepo = new Mock<IImportReceiptRepository>();
        importRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(overReceipt);
        importRepo.Setup(x => x.GetByIdWithDetailsAsync(2)).ReturnsAsync(qcReceipt);

        var auditLogs = new List<AuditLog>();
        var auditRepo = new Mock<IAuditLogRepository>();
        auditRepo.Setup(x => x.AddAsync(It.IsAny<AuditLog>()))
            .Callback<AuditLog>(auditLogs.Add)
            .Returns(Task.CompletedTask);

        var uow = new Mock<IUnitOfWork>();
        var inboundPlanning = new ReceiptInboundPlanningIntegration(db);

        var service = new ImportReceiptService(
            importRepo.Object,
            Mock.Of<IInventoryStockRepository>(),
            Mock.Of<IInventoryTransactionRepository>(),
            Mock.Of<IWarehouseRepository>(),
            Mock.Of<IProductRepository>(),
            uow.Object,
            auditRepo.Object,
            null!,
            null!,
            null!,
            null!,
            inboundPlanning);

        // 1. W5-05: ApproveImportReceiptAsync must reject over-tolerance PO receipt BEFORE transitioning to ReadyToPost
        var approveAct = () => service.ApproveImportReceiptAsync(1, approvedByUserId: 2);
        var approveErr = await approveAct.Should().ThrowAsync<BusinessRuleException>();
        approveErr.Which.Data["HttpStatusCode"].Should().Be(409);
        overReceipt.Status.Should().Be(ReceiptStatus.Received, "Receipt must not enter terminal ReadyToPost when PO tolerance fails");

        // 2. W5-05: CancelAsync must allow cancelling a Received (or ReadyToPost) receipt and restore linked ASN to Arrived
        await service.CancelAsync(1, cancelledByUserId: 2);
        overReceipt.Status.Should().Be(ReceiptStatus.Cancelled);
        asn.Status.Should().Be(AsnStatus.Arrived);
        auditLogs.Should().Contain(a => a.Action == "ImportReceipt.Cancelled" && a.EntityId == 1 && a.OldValues == "Status: Received");

        // 3. W5-28: ReceiveAsync on a RequiresQc receipt must record NewValues = "Status: QcPending"
        await service.ReceiveAsync(2, new ReceiveImportReceiptDto
        {
            Lines =
            [
                new ReceiveImportReceiptLineDto
                {
                    LineId = 22,
                    ReceivedQuantity = 5m,
                    AcceptedQuantity = 0m,
                    DamagedQuantity = 0m,
                    RejectedQuantity = 0m
                }
            ]
        }, receivedByUserId: 1);

        qcReceipt.Status.Should().Be(ReceiptStatus.QcPending);
        auditLogs.Should().Contain(a => a.Action == "ImportReceipt.Received" && a.EntityId == 2 && a.NewValues == "Status: QcPending");
    }

    [Fact]
    public async Task W5_26_And_W5_14a_DockYardAndWarehouseCalendar_AllowNonOverlappingDockAfterServiceCompleted_AndHonorCalendarExceptions()
    {
        await using var db = CreateInMemoryContext();
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Warehouse 1", IsActive = true };
        var calendar = new WarehouseCalendar
        {
            WarehouseId = 1,
            TimeZoneId = "UTC",
            RowVersion = Ver,
            Days = Enumerable.Range(0, 7).Select(d => new WarehouseCalendarDay
            {
                DayOfWeek = d,
                IsOpen = d != 0, // Sunday (0) is closed by default, Mon-Sat open 08:00-18:00
                OpensAtLocal = d == 0 ? null : TimeSpan.FromHours(8),
                ClosesAtLocal = d == 0 ? null : TimeSpan.FromHours(18)
            }).ToList()
        };
        var dock = new Dock
        {
            Id = 1,
            WarehouseId = 1,
            Code = "DOCK-01",
            Name = "Dock 01",
            SupportsInbound = true,
            SupportsOutbound = true,
            IsActive = true,
            RowVersion = Ver
        };

        // Prior appointment completed dock service (08:00 - 09:00), waiting at yard/gate before checkout
        var completedAppt = new DockAppointment
        {
            Id = 1,
            WarehouseId = 1,
            Warehouse = warehouse,
            Code = "APT-DONE",
            Direction = DockAppointmentDirection.Inbound,
            Status = DockAppointmentStatus.Completed,
            DockId = 1,
            Dock = dock,
            PlannedStartUtc = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc),
            PlannedEndUtc = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc),
            CheckedInAtUtc = new DateTime(2026, 10, 19, 7, 50, 0, DateTimeKind.Utc),
            ServiceCompletedAtUtc = new DateTime(2026, 10, 19, 8, 55, 0, DateTimeKind.Utc),
            CheckedOutAtUtc = null,
            RowVersion = Ver
        };

        // Next appointment checked in for non-overlapping window (10:00 - 11:00)
        var nextAppt = new DockAppointment
        {
            Id = 2,
            WarehouseId = 1,
            Warehouse = warehouse,
            Code = "APT-NEXT",
            Direction = DockAppointmentDirection.Inbound,
            Status = DockAppointmentStatus.CheckedIn,
            PlannedStartUtc = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc),
            PlannedEndUtc = new DateTime(2026, 10, 19, 11, 0, 0, DateTimeKind.Utc),
            CheckedInAtUtc = new DateTime(2026, 10, 19, 9, 50, 0, DateTimeKind.Utc),
            RowVersion = Ver
        };

        db.Warehouses.Add(warehouse);
        db.WarehouseCalendars.Add(calendar);
        db.Docks.Add(dock);
        db.DockAppointments.AddRange(completedAppt, nextAppt);
        await db.SaveChangesAsync();

        var auth = new Mock<IWarehouseAuthorizationService>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);

        var fixedSundayProvider = new Mock<TimeProvider>();
        // 2026-10-18 is a Sunday (normally closed in calendar)
        var sundayUtc = new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
        fixedSundayProvider.Setup(x => x.GetUtcNow()).Returns(sundayUtc);

        var dockService = new DockYardService(db, auth.Object, currentUser.Object, fixedSundayProvider.Object);

        // 1. W5-26: Assigning dock to nextAppt succeeds even though completedAppt has CheckedOutAtUtc == null
        var assigned = await dockService.AssignDockAsync(2, new DockAppointmentAssignDockDto
        {
            DockId = 1,
            RowVersion = EncodedVer
        });
        assigned.DockId.Should().Be(1);
        assigned.Status.Should().Be(DockAppointmentStatus.DockAssigned);

        // 2. W5-14a: On Sunday 2026-10-18, calendar is closed until EMERGENCY_OPEN_OVERRIDE is added
        var calendarService = new WarehouseCalendarService(db, auth.Object, currentUser.Object, fixedSundayProvider.Object);
        (await calendarService.GetAsync(1)).IsOpenNow.Should().BeFalse();
        await FluentActions.Awaiting(() => calendarService.ValidateOperatingWindowAsync(1, sundayUtc.UtcDateTime))
            .Should().ThrowAsync<BusinessRuleException>();

        db.AuditLogs.Add(new AuditLog
        {
            Id = 500,
            EntityName = "WarehouseCalendarException",
            EntityId = 1,
            WarehouseId = 1,
            Action = "EMERGENCY_OPEN_OVERRIDE",
            OldValues = "2026-10-18",
            NewValues = "2026-10-18",
            Reason = "Peak season Sunday opening",
            Result = "Success",
            Timestamp = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        (await calendarService.GetAsync(1)).IsOpenNow.Should().BeTrue();
        (await calendarService.ValidateOperatingWindowAsync(1, sundayUtc.UtcDateTime)).Should().BeTrue();

        // Adding a HOLIDAY_CLOSURE for Monday 2026-10-19 closes Monday
        db.AuditLogs.Add(new AuditLog
        {
            Id = 501,
            EntityName = "WarehouseCalendarException",
            EntityId = 1,
            WarehouseId = 1,
            Action = "HOLIDAY_CLOSURE",
            OldValues = "2026-10-19",
            NewValues = "2026-10-19",
            Reason = "Site holiday closure",
            Result = "Success",
            Timestamp = DateTime.UtcNow.AddMinutes(1)
        });
        await db.SaveChangesAsync();

        var mondayUtc = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc);
        await FluentActions.Awaiting(() => calendarService.ValidateOperatingWindowAsync(1, mondayUtc))
            .Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*ngoại lệ lịch vận hành*");
    }
}
