using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace ERP.Application.Tests;

public class Wave5SectorBRemediationTests
{
    private static ErpKhoDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave5SectorB_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ErpKhoDbContext(options);
    }

    private static Mock<IWarehouseAuthorizationService> CreateOpenWarehouseAuth()
    {
        var mock = new Mock<IWarehouseAuthorizationService>();
        mock.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)new List<int> { 1, 2, 3, 4, 5, 10 });
        mock.Setup(x => x.EnsureWarehouseAccessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static Mock<ICurrentUser> CreateCurrentUser(int userId = 1, string role = "Manager", bool isGlobalAdmin = true)
    {
        var mock = new Mock<ICurrentUser>();
        mock.SetupGet(x => x.UserId).Returns(userId);
        mock.SetupGet(x => x.Role).Returns(role);
        mock.SetupGet(x => x.IsGlobalAdmin).Returns(isGlobalAdmin);
        return mock;
    }

    [Fact]
    public async Task W5_03_WarehouseDelete_RemovesSystemDefaultsAndEmptyStocks_AndBlocksOperationalHistory()
    {
        await using var db = CreateDbContext();
        var repo = new WarehouseRepository(db);
        var stockRepo = new InventoryStockRepository(db);
        var authMock = CreateOpenWarehouseAuth();
        var service = new WarehouseService(repo, authMock.Object, stockRepo);

        var created = await service.CreateWarehouseAsync(new CreateWarehouseDto
        {
            Code = "WH-DEL-01",
            Name = "Delete Test Warehouse"
        }, "admin");

        db.InventoryStocks.Add(new InventoryStock
        {
            WarehouseId = created.Id,
            ProductId = 10,
            Quantity = 0m,
            ReservedQuantity = 0m,
            Status = InventoryStatus.Available
        });
        db.UserWarehouses.Add(new UserWarehouse { UserId = 1, WarehouseId = created.Id });
        db.WarehouseCalendars.Add(new WarehouseCalendar { WarehouseId = created.Id, TimeZoneId = "UTC" });
        db.WarehouseShifts.Add(new WarehouseShift { WarehouseId = created.Id, Code = "S1", Name = "Shift 1" });
        await db.SaveChangesAsync();

        await service.DeleteWarehouseAsync(created.Id);

        (await db.Warehouses.AnyAsync(w => w.Id == created.Id)).Should().BeFalse();
        (await db.WarehouseLocations.AnyAsync(l => l.WarehouseId == created.Id)).Should().BeFalse();
        (await db.WarehouseShifts.AnyAsync(s => s.WarehouseId == created.Id)).Should().BeFalse();

        var usedWh = await service.CreateWarehouseAsync(new CreateWarehouseDto
        {
            Code = "WH-USED-01",
            Name = "Used Warehouse"
        }, "admin");
        db.Docks.Add(new Dock
        {
            WarehouseId = usedWh.Id,
            Code = "DOCK-01",
            Name = "Dock 01"
        });
        await db.SaveChangesAsync();

        var act = () => service.DeleteWarehouseAsync(usedWh.Id);
        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task W5_09_ProductDeleteAndImmutablePolicy_BlocksOperationalChildRecordsAndCleanDeletesUnused()
    {
        await using var db = CreateDbContext();
        var productRepo = new ProductRepository(db);
        var catalogMock = new Mock<IProductCatalogService>();
        var service = new ProductService(productRepo, catalogMock.Object);

        var unit1 = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0, IsActive = true };
        var unit2 = new Unit { Id = 2, Code = "BOX", Name = "Box", DecimalPlaces = 0, IsActive = true };
        db.Units.AddRange(unit1, unit2);

        var unusedProduct = new Product
        {
            Code = "PRD-UNUSED",
            Name = "Unused Product",
            UnitId = unit1.Id,
            TrackingType = ProductTrackingType.None,
            IsActive = true
        };
        var usedProduct = new Product
        {
            Code = "PRD-PO",
            Name = "PO Referenced Product",
            UnitId = unit1.Id,
            TrackingType = ProductTrackingType.None,
            IsActive = true
        };
        db.Products.AddRange(unusedProduct, usedProduct);
        await db.SaveChangesAsync();

        db.InventoryStocks.Add(new InventoryStock
        {
            WarehouseId = 1,
            ProductId = unusedProduct.Id,
            Quantity = 0m,
            ReservedQuantity = 0m,
            Status = InventoryStatus.Available
        });
        db.QcPolicies.Add(new QcPolicy { ProductId = unusedProduct.Id, Rule = "DEFAULT" });
        await db.SaveChangesAsync();

        await service.DeleteProductAsync(unusedProduct.Id);
        (await db.Products.AnyAsync(p => p.Id == unusedProduct.Id)).Should().BeFalse();

        db.PurchaseOrderLines.Add(new PurchaseOrderLine
        {
            PurchaseOrderId = 1,
            ProductId = usedProduct.Id,
            OrderedQuantity = 5m
        });
        await db.SaveChangesAsync();

        var deleteAct = () => service.DeleteProductAsync(usedProduct.Id);
        await deleteAct.Should().ThrowAsync<BusinessRuleException>();

        var updateTrackingAct = () => service.UpdateProductAsync(usedProduct.Id, new UpdateProductDto
        {
            Name = "PO Referenced Product",
            UnitId = unit1.Id,
            TrackingType = "Serial",
            UpdateTrackingPolicy = false
        }, "admin");
        await updateTrackingAct.Should().ThrowAsync<BusinessRuleException>();

        var updateUnitAct = () => service.UpdateProductAsync(usedProduct.Id, new UpdateProductDto
        {
            Name = "PO Referenced Product",
            UnitId = unit2.Id
        }, "admin");
        await updateUnitAct.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task W5_07_Stocktake_BlindMaskingInDraft_AndSerialLotPositiveAdjustmentGuard()
    {
        await using var db = CreateDbContext();
        var uow = new UnitOfWork(db);
        var stocktakeRepo = new StocktakeRepository(db);
        var stockRepo = new InventoryStockRepository(db);
        var txRepo = new InventoryTransactionRepository(db);
        var whRepo = new WarehouseRepository(db);
        var prodRepo = new ProductRepository(db);
        var auditRepo = new AuditLogRepository(db);
        var authMock = CreateOpenWarehouseAuth();
        var creatorUser = CreateCurrentUser(1);

        var wh = new Warehouse { Id = 1, Code = "WH-ST", Name = "Stocktake WH", IsActive = true };
        var unit = new Unit { Id = 1, Code = "EA", Name = "Each", DecimalPlaces = 0, IsActive = true };
        db.Users.AddRange(
            new User { Id = 1, Username = "maker", FullName = "Maker User", PasswordHash = "hash", IsActive = true },
            new User { Id = 2, Username = "checker", FullName = "Checker User", PasswordHash = "hash", IsActive = true });
        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        await db.SaveChangesAsync();

        var loc = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = wh.Id,
            Code = "A-01",
            Name = "Bin A-01",
            LocationType = WarehouseLocationType.Storage,
            IsPickable = true,
            IsReceivable = true,
            IsActive = true
        };
        var serialProd = new Product
        {
            Id = 100,
            Code = "SER-01",
            Name = "Serial Item",
            UnitId = unit.Id,
            TrackingType = ProductTrackingType.Serial,
            IsActive = true
        };
        db.WarehouseLocations.Add(loc);
        db.Products.Add(serialProd);
        await db.SaveChangesAsync();

        var serial1 = new InventorySerial { Id = 1, ProductId = serialProd.Id, SerialNumber = "SN-001" };
        db.InventorySerials.Add(serial1);
        db.InventoryStocks.Add(new InventoryStock
        {
            WarehouseId = wh.Id,
            LocationId = loc.Id,
            ProductId = serialProd.Id,
            SerialId = serial1.Id,
            Status = InventoryStatus.Available,
            Quantity = 1m
        });
        await db.SaveChangesAsync();

        var service = new StocktakeService(
            stocktakeRepo,
            stockRepo,
            txRepo,
            whRepo,
            uow,
            auditRepo,
            authMock.Object,
            creatorUser.Object,
            null,
            prodRepo);
        var queryService = new StocktakeQueryService(db, authMock.Object);

        var stocktakeId = await service.CreateStocktakeAsync(new CreateStocktakeDto
        {
            WarehouseId = wh.Id,
            IsBlindCount = true,
            Note = "Cycle count"
        }, 1);

        var initialDto = await queryService.GetStocktakeByIdAsync(stocktakeId);
        var detailId = initialDto!.Details.Single().Id;

        await service.UpdateStocktakeDetailAsync(stocktakeId, detailId, new UpdateStocktakeDetailDto
        {
            ActualQuantity = 3m,
            Note = "Counted 3"
        });

        var queriedDraft = await queryService.GetStocktakeByIdAsync(stocktakeId);
        queriedDraft!.Details.Single().SystemQuantity.Should().Be(0m);
        queriedDraft.Details.Single().DifferenceQuantity.Should().Be(0m);

        var mappedDraft = StocktakeService.MapToDto(await db.Stocktakes.Include(s => s.Details).SingleAsync(s => s.Id == stocktakeId));
        mappedDraft.Details.Single().SystemQuantity.Should().Be(0m);
        mappedDraft.Details.Single().DifferenceQuantity.Should().Be(0m);

        var checkerUser = CreateCurrentUser(2);
        var approveService = new StocktakeService(
            stocktakeRepo,
            stockRepo,
            txRepo,
            whRepo,
            uow,
            auditRepo,
            authMock.Object,
            checkerUser.Object,
            null,
            prodRepo);

        var approveAct = () => approveService.ApproveStocktakeAsync(stocktakeId, 2);
        await approveAct.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task W5_08_W5_16_W5_27_StockTransfer_DamagedReceipt_ReverseDraftCancelAndReversal()
    {
        await using var db = CreateDbContext();
        var stockRepoMock = new Mock<ERP.Domain.Interfaces.IInventoryStockRepository>();
        stockRepoMock
            .Setup(x => x.TryDecreaseStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int productId, int warehouseId, decimal qty, CancellationToken _) =>
            {
                var stock = db.InventoryStocks.FirstOrDefault(s =>
                    s.ProductId == productId && s.WarehouseId == warehouseId && s.Status == InventoryStatus.Available);
                if (stock == null || stock.Quantity < qty) return false;
                stock.Quantity -= qty;
                return true;
            });
        var authMock = CreateOpenWarehouseAuth();
        var makerUser = CreateCurrentUser(1, "Manager", true);
        var checkerUser = CreateCurrentUser(2, "Manager", true);

        var makerService = new StockTransferService(db, stockRepoMock.Object, authMock.Object, makerUser.Object);
        var checkerService = new StockTransferService(db, stockRepoMock.Object, authMock.Object, checkerUser.Object);

        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0, IsActive = true };
        var whSrc = new Warehouse { Id = 1, Code = "WH-SRC", Name = "Source WH", IsActive = true };
        var whDst = new Warehouse { Id = 2, Code = "WH-DST", Name = "Dest WH", IsActive = true };
        db.Units.Add(unit);
        db.Warehouses.AddRange(whSrc, whDst);
        db.InventoryStatusDefinitions.AddRange(
            new InventoryStatusDefinition { Id = InventoryStatus.Available, Code = "AVAILABLE", Name = "Available", IsAvailable = true, IsPickable = true, IsAllocatable = true, IsReservable = true, IsShippable = true },
            new InventoryStatusDefinition { Id = InventoryStatus.Damaged, Code = "DAMAGED", Name = "Damaged" });
        await db.SaveChangesAsync();

        var srcLegacy = new WarehouseLocation
        {
            Id = 1,
            WarehouseId = whSrc.Id,
            Code = "LEGACY",
            Name = "Legacy",
            LocationType = WarehouseLocationType.Legacy,
            IsPickable = true,
            IsReceivable = true,
            IsActive = true,
            IsSystemManaged = true
        };
        var dstLegacy = new WarehouseLocation
        {
            Id = 2,
            WarehouseId = whDst.Id,
            Code = "LEGACY",
            Name = "Legacy",
            LocationType = WarehouseLocationType.Legacy,
            IsPickable = true,
            IsReceivable = true,
            IsActive = true,
            IsSystemManaged = true
        };
        var dstDamaged = new WarehouseLocation
        {
            Id = 3,
            WarehouseId = whDst.Id,
            Code = "DAMAGED",
            Name = "Damaged",
            LocationType = WarehouseLocationType.Damaged,
            IsPickable = false,
            IsReceivable = true,
            IsActive = true
        };
        db.WarehouseLocations.AddRange(srcLegacy, dstLegacy, dstDamaged);

        var prod = new Product
        {
            Id = 10,
            Code = "PRD-TF",
            Name = "Transfer Product",
            UnitId = unit.Id,
            TrackingType = ProductTrackingType.None,
            IsActive = true
        };
        db.Products.Add(prod);
        db.InventoryStocks.Add(new InventoryStock
        {
            WarehouseId = whSrc.Id,
            LocationId = srcLegacy.Id,
            ProductId = prod.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            ReservedQuantity = 0m
        });
        await db.SaveChangesAsync();

        var draft = await makerService.CreateAsync(new CreateStockTransferDto
        {
            SourceWarehouseId = whSrc.Id,
            DestinationWarehouseId = whDst.Id,
            Details =
            [
                new StockTransferDetailInputDto
                {
                    ProductId = prod.Id,
                    Quantity = 10m
                }
            ]
        });

        await checkerService.ApproveAsync(draft.Id);
        await makerService.DispatchAsync(draft.Id);

        await makerService.ReceiveAsync(draft.Id, new ReceiveStockTransferDto
        {
            Details =
            [
                new ReceiveStockTransferLineDto
                {
                    ProductId = prod.Id,
                    ReceivedQuantity = 8m,
                    MissingQuantity = 0m,
                    DamagedQuantity = 2m
                }
            ]
        });

        var dstAvailable = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == whDst.Id && s.ProductId == prod.Id && s.Status == InventoryStatus.Available);
        dstAvailable.Quantity.Should().Be(8m);

        var dstDamagedStock = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == whDst.Id && s.ProductId == prod.Id && s.Status == InventoryStatus.Damaged);
        dstDamagedStock.Quantity.Should().Be(2m);
        dstDamagedStock.LocationId.Should().Be(dstDamaged.Id);

        var reverse1 = await makerService.CreateReverseDraftAsync(draft.Id, new CreateReverseStockTransferDto
        {
            ReasonCode = "TRANSFER_DISPATCH_ERROR",
            Reason = "Reverse good quantity"
        });
        reverse1.Details.Single().RequestedQuantity.Should().Be(8m);

        await makerService.CancelAsync(reverse1.Id);

        var reverse2 = await makerService.CreateReverseDraftAsync(draft.Id, new CreateReverseStockTransferDto
        {
            ReasonCode = "TRANSFER_DISPATCH_ERROR",
            Reason = "Retry reverse after cancel"
        });
        reverse2.Id.Should().NotBe(reverse1.Id);
        reverse2.Details.Single().RequestedQuantity.Should().Be(8m);
    }

    [Fact]
    public async Task W5_15_InventoryMovementAndDisposition_EnforcesLocationCompatibility_AndRelocatesReleasedStock()
    {
        await using var db = CreateDbContext();
        var authMock = CreateOpenWarehouseAuth();
        var userMock = CreateCurrentUser(1);
        var locksMock = new Mock<IInventoryLockEvaluator>();
        var movementService = new InventoryMovementService(db, authMock.Object, userMock.Object, locksMock.Object);
        var statusService = new InventoryStatusService(db, authMock.Object, userMock.Object, locksMock.Object);

        var wh = new Warehouse { Id = 1, Code = "WH-MV", Name = "Movement WH", IsActive = true };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0, IsActive = true };
        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.InventoryStatusDefinitions.AddRange(
            new InventoryStatusDefinition { Id = InventoryStatus.Available, Code = "AVAILABLE", Name = "Available", IsAvailable = true, IsPickable = true, IsAllocatable = true, IsReservable = true, IsShippable = true },
            new InventoryStatusDefinition { Id = InventoryStatus.Damaged, Code = "DAMAGED", Name = "Damaged" });
        await db.SaveChangesAsync();

        var storageLoc = new WarehouseLocation
        {
            Id = 1,
            WarehouseId = wh.Id,
            Code = "STOR-01",
            Name = "Storage 01",
            LocationType = WarehouseLocationType.Storage,
            IsPickable = true,
            IsReceivable = true,
            IsActive = true
        };
        var damagedLoc = new WarehouseLocation
        {
            Id = 2,
            WarehouseId = wh.Id,
            Code = "DMG-01",
            Name = "Damaged 01",
            LocationType = WarehouseLocationType.Damaged,
            IsPickable = false,
            IsReceivable = true,
            IsActive = true
        };
        db.WarehouseLocations.AddRange(storageLoc, damagedLoc);

        var prod = new Product
        {
            Id = 10,
            Code = "PRD-MV",
            Name = "Movement Product",
            UnitId = unit.Id,
            TrackingType = ProductTrackingType.None,
            IsActive = true
        };
        db.Products.Add(prod);

        var availStock = new InventoryStock
        {
            Id = 100,
            WarehouseId = wh.Id,
            LocationId = storageLoc.Id,
            ProductId = prod.Id,
            Status = InventoryStatus.Available,
            Quantity = 5m
        };
        var dmgStock = new InventoryStock
        {
            Id = 101,
            WarehouseId = wh.Id,
            LocationId = damagedLoc.Id,
            ProductId = prod.Id,
            Status = InventoryStatus.Damaged,
            Quantity = 3m
        };
        db.InventoryStocks.AddRange(availStock, dmgStock);
        await db.SaveChangesAsync();

        var moveAvailableToDamaged = () => movementService.MoveAsync(new CreateInventoryMoveDto
        {
            InventoryStockId = availStock.Id,
            DestinationLocationId = damagedLoc.Id,
            Quantity = 2m,
            Reason = "Test invalid move"
        });
        await moveAvailableToDamaged.Should().ThrowAsync<BusinessRuleException>();

        var moveDamagedToStorage = () => movementService.MoveAsync(new CreateInventoryMoveDto
        {
            InventoryStockId = dmgStock.Id,
            DestinationLocationId = storageLoc.Id,
            Quantity = 1m,
            Reason = "Test invalid move"
        });
        await moveDamagedToStorage.Should().ThrowAsync<BusinessRuleException>();

        await statusService.ExecuteDispositionAsync(new CreateInventoryDispositionDto
        {
            InventoryStockId = dmgStock.Id,
            Action = "ReleaseToAvailable",
            Quantity = 2m,
            Reason = "Repaired and released"
        });

        var storageStock = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == wh.Id && s.LocationId == storageLoc.Id && s.Status == InventoryStatus.Available);
        storageStock.Quantity.Should().Be(7m);

        var remainingDamaged = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == wh.Id && s.LocationId == damagedLoc.Id && s.Status == InventoryStatus.Damaged);
        remainingDamaged.Quantity.Should().Be(1m);
    }

    [Fact]
    public async Task W5_18_W5_19_InventoryQueries_EnforcesExplicitWarehouseAccess_ClampsPagination_AndReconcilesBlockedLocations()
    {
        await using var db = CreateDbContext();
        var uow = new UnitOfWork(db);
        var stockRepo = new InventoryStockRepository(db);
        var userMock = CreateCurrentUser(2);

        var authMock = new Mock<IWarehouseAuthorizationService>();
        authMock.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int>)new List<int> { 1 });
        authMock.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        authMock.Setup(x => x.EnsureWarehouseAccessAsync(2, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Forbidden WH 2"));

        var txQuery = new InventoryTransactionQueryService(db, authMock.Object);
        var invQuery = new InventoryQueryService(db, authMock.Object);

        var txForbidden = () => txQuery.GetHistoryAsync(null, null, null, 2, null, null, null, null, 1, 20);
        await txForbidden.Should().ThrowAsync<ForbiddenException>();

        var invForbidden = () => invQuery.GetCurrentStockAsync(2, null, null, null);
        await invForbidden.Should().ThrowAsync<ForbiddenException>();

        var page = await txQuery.GetHistoryAsync(null, null, null, 1, null, null, null, null, -5, 5000);
        page.PageIndex.Should().Be(1);
        page.PageSize.Should().Be(200);

        var wh = new Warehouse { Id = 1, Code = "WH-01", Name = "WH 01", IsActive = true };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces", DecimalPlaces = 0, IsActive = true };
        var blockedLoc = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BLK-01",
            Name = "Blocked Bin",
            LocationType = WarehouseLocationType.Storage,
            IsPickable = false,
            IsBlocked = true,
            IsActive = true
        };
        var prod = new Product { Id = 100, Code = "PRD-REC", Name = "Reconcile Product", UnitId = 1, IsActive = true };
        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.WarehouseLocations.Add(blockedLoc);
        db.Products.Add(prod);
        db.InventoryStocks.Add(new InventoryStock
        {
            WarehouseId = 1,
            LocationId = blockedLoc.Id,
            ProductId = prod.Id,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            ReservedQuantity = 4m
        });
        await db.SaveChangesAsync();

        var reservationService = new StockReservationService(
            db,
            stockRepo,
            uow,
            authMock.Object,
            userMock.Object,
            new StockReservationOptions());

        var reconcileIssues = await reservationService.ReconcileAsync();

        reconcileIssues.Should().ContainSingle(i =>
            i.WarehouseId == 1 &&
            i.ProductId == prod.Id &&
            i.Issue == "LedgerMismatch" &&
            i.StockReserved == 4m);
    }
}
