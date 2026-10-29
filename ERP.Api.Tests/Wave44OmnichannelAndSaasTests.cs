using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public class Wave44OmnichannelAndSaasTests
{
    [Fact]
    public async Task Wave44_TwoRowOpposingLedger_ForReplenishmentCrossdockAndRefurbish_MaintainsZeroWarehouseNetChange()
    {
        var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
        db.Products.Add(new ERP.Domain.Entities.Product { Id = 101, Code = "SKU-W44-01", Name = "Sản phẩm Wave 44", UnitId = 1, IsActive = true });
        db.WarehouseLocations.AddRange(
            new ERP.Domain.Entities.WarehouseLocation { Id = 10, WarehouseId = 1, Code = "RES-01", Name = "Kệ dự trữ", LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage, IsActive = true, IsPickable = true },
            new ERP.Domain.Entities.WarehouseLocation { Id = 11, WarehouseId = 1, Code = "PICK-01", Name = "Kệ nhặt hàng", LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage, IsActive = true, IsPickable = true },
            new ERP.Domain.Entities.WarehouseLocation { Id = 12, WarehouseId = 1, Code = "RCV-01", Name = "Cửa nhận hàng", LocationType = ERP.Domain.Enums.WarehouseLocationType.Receiving, IsActive = true, IsPickable = false },
            new ERP.Domain.Entities.WarehouseLocation { Id = 13, WarehouseId = 1, Code = "STG-01", Name = "Khu tập kết xuất", LocationType = ERP.Domain.Enums.WarehouseLocationType.Staging, IsActive = true, IsPickable = true },
            new ERP.Domain.Entities.WarehouseLocation { Id = 14, WarehouseId = 1, Code = "QRN-01", Name = "Khu biệt trữ", LocationType = ERP.Domain.Enums.WarehouseLocationType.Receiving, IsActive = true, IsPickable = false });
        db.InventoryStocks.AddRange(
            new ERP.Domain.Entities.InventoryStock { Id = 1001, WarehouseId = 1, ProductId = 101, LocationId = 10, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 50m, ReservedQuantity = 0m },
            new ERP.Domain.Entities.InventoryStock { Id = 1002, WarehouseId = 1, ProductId = 101, LocationId = 11, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 5m, ReservedQuantity = 0m },
            new ERP.Domain.Entities.InventoryStock { Id = 1003, WarehouseId = 1, ProductId = 101, LocationId = 12, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 20m, ReservedQuantity = 0m },
            new ERP.Domain.Entities.InventoryStock { Id = 1004, WarehouseId = 1, ProductId = 101, LocationId = 14, Status = ERP.Domain.Enums.InventoryStatus.Quarantine, Quantity = 8m, ReservedQuantity = 0m });
        await db.SaveChangesAsync();

        var controlOps = new InventoryControlOperationsService(db);
        var controlController = new InventoryControlController(
            new Mock<IInventoryLockService>().Object,
            new Mock<IInventoryMovementService>().Object,
            controlOps);
        var repRes = await controlController.ExecuteReplenishment(
            new InventoryControlController.ExecuteReplenishmentRequest(
                WarehouseId: 1,
                ProductId: 101,
                SourceLocationId: 10,
                TargetLocationId: 11,
                Quantity: 10m),
            controlOps,
            null,
            default);
        repRes.Should().BeOfType<OkObjectResult>();

        var repMoves = await db.InventoryTransactions
            .Where(t => t.ReferenceType == "Replenishment" && t.TransactionType == ERP.Domain.Enums.TransactionType.Move)
            .ToListAsync();
        repMoves.Should().HaveCount(2);
        repMoves.Sum(t => t.Quantity).Should().Be(0m);
        repMoves.Single(t => t.LocationId == 10).Quantity.Should().Be(-10m);
        repMoves.Single(t => t.LocationId == 11).Quantity.Should().Be(10m);

        var dockController = new DockYardController(new Mock<IDockYardService>().Object, new WmsGovernanceOperationsService(db));
        var xdRes = await dockController.DispatchCrossdock(
            new DispatchCrossdockRequest(
                WarehouseId: 1,
                ProductId: 101,
                SourceLocationId: 12,
                TargetLocationId: 13,
                Quantity: 6m,
                ExecuteStockMutation: true),
            default);
        xdRes.Should().BeOfType<OkObjectResult>();

        var xdMoves = await db.InventoryTransactions
            .Where(t => t.ReferenceType == "CrossDock" && t.TransactionType == ERP.Domain.Enums.TransactionType.Move)
            .ToListAsync();
        xdMoves.Should().HaveCount(2);
        xdMoves.Sum(t => t.Quantity).Should().Be(0m);
        xdMoves.Single(t => t.LocationId == 12).Quantity.Should().Be(-6m);
        xdMoves.Single(t => t.LocationId == 13).Quantity.Should().Be(6m);

        var importController = new ImportReceiptsController(new Mock<IImportReceiptService>().Object, null, new ImportReceiptOperationsService(db));
        var refRes = await importController.ExecuteRtvRefurbish(
            new ImportReceiptsController.ExecuteRtvRefurbishRequest(
                WarehouseId: 1,
                ActionType: "REFURBISH",
                ProductId: 101,
                LocationId: 14,
                Quantity: 3m,
                ExecuteStockMutation: true),
            default);
        refRes.Should().BeOfType<OkObjectResult>();

        var statusMoves = await db.InventoryTransactions
            .Where(t => t.ReferenceType == "InventoryStatusChange" && t.TransactionType == ERP.Domain.Enums.TransactionType.StatusChange)
            .ToListAsync();
        statusMoves.Should().HaveCount(2);
        statusMoves.Sum(t => t.Quantity).Should().Be(0m);
        statusMoves.Single(t => t.InventoryStatus == ERP.Domain.Enums.InventoryStatus.Quarantine).Quantity.Should().Be(-3m);
        statusMoves.Single(t => t.InventoryStatus == ERP.Domain.Enums.InventoryStatus.Available).Quantity.Should().Be(3m);
    }

    [Fact]
    public async Task Wave44_CanonicalGrainHash_ComputesDeterministicSha256Over9PartGrain_AndValuationEndpointReturnsMacSummary()
    {
        var hash1 = ERP.Domain.Entities.InventoryStock.ComputeCanonicalGrainHash(
            tenantId: 1,
            warehouseId: 1,
            locationId: 10,
            productId: 101,
            lotId: null,
            serialId: null,
            handlingUnitId: null,
            status: ERP.Domain.Enums.InventoryStatus.Available,
            ownerId: null);
        var hash2 = ERP.Domain.Entities.InventoryStock.ComputeCanonicalGrainHash(
            tenantId: 1,
            warehouseId: 1,
            locationId: 10,
            productId: 101,
            lotId: 0,
            serialId: 0,
            handlingUnitId: 0,
            status: ERP.Domain.Enums.InventoryStatus.Available,
            ownerId: 0);
        var hashDifferentOwner = ERP.Domain.Entities.InventoryStock.ComputeCanonicalGrainHash(
            tenantId: 1,
            warehouseId: 1,
            locationId: 10,
            productId: 101,
            lotId: null,
            serialId: null,
            handlingUnitId: null,
            status: ERP.Domain.Enums.InventoryStatus.Available,
            ownerId: 99);

        hash1.Should().HaveCount(32);
        hash1.Should().Equal(hash2);
        hash1.Should().NotEqual(hashDifferentOwner);

        var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
        db.Products.Add(new ERP.Domain.Entities.Product
        {
            Id = 101,
            Code = "SKU-W44-MAC",
            Name = "Sản phẩm tính giá MAC",
            UnitId = 1,
            MovingAverageCost = 125000m,
            IsActive = true
        });
        db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
        {
            Id = 2001,
            WarehouseId = 1,
            ProductId = 101,
            LocationId = 10,
            Status = ERP.Domain.Enums.InventoryStatus.Available,
            Quantity = 10m,
            ReservedQuantity = 2m,
            MovingAverageCost = 125000m
        });
        await db.SaveChangesAsync();

        var recController = new InventoryReconciliationController(
            new Mock<IInventoryReconciliationQueryService>().Object,
            new WmsGovernanceOperationsService(db),
            null!,
            new Mock<IStockReservationService>().Object);
        recController.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Accountant") },
                        "TestAuth"))
            }
        };

        var valRes = await recController.GetValuation(warehouseId: 1, productId: 101, cancellationToken: default);
        var ok = valRes.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = System.Text.Json.JsonSerializer.SerializeToDocument(ok.Value);
        doc.RootElement.GetProperty("valuationMethod").GetString().Should().Be("MOVING_AVERAGE_COST");
        doc.RootElement.GetProperty("totalOnHandQuantity").GetDecimal().Should().Be(10m);
        doc.RootElement.GetProperty("totalInventoryValue").GetDecimal().Should().Be(1250000m);
    }

    [Fact]
    public async Task Wave44_SalesOrderCancelPutback_ShipmentRtsReturn_AndSmallStocktakeVarianceAutoApprove_ExecuteEndToEnd()
    {
        var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
        db.Products.Add(new ERP.Domain.Entities.Product { Id = 101, Code = "SKU-W44-FLOW", Name = "Sản phẩm luồng Wave 44", UnitId = 1, IsActive = true });
        db.WarehouseLocations.AddRange(
            new ERP.Domain.Entities.WarehouseLocation { Id = 10, WarehouseId = 1, Code = "PICK-01", Name = "Kệ nhặt", LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage, IsActive = true, IsPickable = true },
            new ERP.Domain.Entities.WarehouseLocation { Id = 14, WarehouseId = 1, Code = "QRN-01", Name = "Khu biệt trữ", LocationType = ERP.Domain.Enums.WarehouseLocationType.Receiving, IsActive = true, IsPickable = false });
        db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
        {
            Id = 3001,
            WarehouseId = 1,
            ProductId = 101,
            LocationId = 10,
            Status = ERP.Domain.Enums.InventoryStatus.Available,
            Quantity = 20m,
            ReservedQuantity = 5m,
            AllocatedQuantity = 5m
        });
        db.SalesOrders.Add(new ERP.Domain.Entities.SalesOrder
        {
            Id = 401,
            OrderCode = "SO-W44-401",
            WarehouseId = 1,
            CustomerId = 1,
            Status = ERP.Domain.Enums.SalesOrderStatus.CancelPendingPutback,
            Lines =
            [
                new ERP.Domain.Entities.SalesOrderLine
                {
                    Id = 4011,
                    SalesOrderId = 401,
                    ExternalLineId = "LINE-1",
                    ProductId = 101,
                    OrderedQuantity = 5m
                }
            ]
        });
        db.StockReservations.Add(new ERP.Domain.Entities.StockReservation
        {
            Id = 500,
            ReservationCode = "RSV-W44-500",
            WarehouseId = 1,
            ProductId = 101,
            Quantity = 5m,
            SourceType = "SalesOrder",
            SourceId = 401,
            Status = ERP.Domain.Enums.StockReservationStatus.Active
        });
        db.StockAllocations.Add(new ERP.Domain.Entities.StockAllocation
        {
            Id = 501,
            AllocationCode = "ALC-W44-501",
            ReservationId = 500,
            WarehouseId = 1,
            ProductId = 101,
            LocationId = 10,
            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
            Quantity = 5m,
            Status = ERP.Domain.Enums.StockAllocationStatus.Picked
        });
        db.Shipments.Add(new ERP.Domain.Entities.Shipment
        {
            Id = 601,
            ShipmentCode = "SHP-W44-601",
            WarehouseId = 1,
            Status = ERP.Domain.Enums.ShipmentStatus.ReturnToWarehouse
        });
        await db.SaveChangesAsync();

        var soServiceMock = new Mock<ISalesOrderBackorderService>();
        soServiceMock.Setup(s => s.GetSalesOrderAsync(401, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ERP.Application.DTOs.SalesOrderDto { Id = 401, OrderCode = "SO-W44-401", Status = "Cancelled" });
        var soController = new SalesOrdersController(soServiceMock.Object, new SalesOrderOperationsService(db));
        var cancelPutbackRes = await soController.ConfirmCancelPutback(401, new ConfirmCancelPutbackRequest(Note: "Đã cất trả kệ PICK-01"), default);
        cancelPutbackRes.Should().BeOfType<OkObjectResult>();

        var updatedStock = await db.InventoryStocks.SingleAsync(s => s.Id == 3001);
        updatedStock.AllocatedQuantity.Should().Be(0m);
        updatedStock.ReservedQuantity.Should().Be(0m);

        var shpController = new ShipmentsController(new Mock<IShipmentService>().Object, new ShipmentOperationsService(db));
        var rtsRes = await shpController.CompleteRtsReturn(
            601,
            new CompleteShipmentRtsReturnRequest(QuarantineLocationId: 14, ProductId: 101, Quantity: 4m),
            default);
        rtsRes.Should().BeOfType<OkObjectResult>();

        var qrnStock = await db.InventoryStocks.SingleAsync(s => s.WarehouseId == 1 && s.ProductId == 101 && s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine);
        qrnStock.Quantity.Should().Be(4m);

        var stController = new StocktakesController(
            new Mock<IStocktakeService>().Object,
            new Mock<IStocktakeQueryService>().Object,
            new StocktakeOperationsService(db));
        var smallAdjRes = await stController.CreateAdjustmentProposal(
            new StocktakesController.CreateAdjustmentProposalRequest(
                WarehouseId: 1,
                ProductId: 101,
                LocationId: 10,
                DeltaQuantity: -1m,
                ReasonCode: "COUNT_VARIANCE",
                Note: "Lệch nhỏ tự động duyệt"),
            default);
        var smallAdjOk = smallAdjRes.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = System.Text.Json.JsonSerializer.SerializeToDocument(smallAdjOk.Value);
        doc.RootElement.GetProperty("status").GetString().Should().Be("APPROVED");
        doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã phê duyệt & Ghi sổ");
        doc.RootElement.GetProperty("resultingQuantity").GetDecimal().Should().Be(19m);
    }

    [Fact]
    public async Task Wave45_MarketplaceShopSync_SafetyStockBuffer_LowStockCutOff_And_BuyerCancellation3StateDecisionMatrix()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave45_Omnichannel_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Tổng TMĐT", IsActive = true });
        db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation { Id = 10, WarehouseId = 1, Code = "PICK-A01", Name = "Kệ Nhặt A01", LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage, IsActive = true, IsPickable = true, IsReceivable = true });
        db.Products.AddRange(
            new ERP.Domain.Entities.Product { Id = 101, Code = "SKU-MAIN-101", Name = "Serum Cấp Ẩm B5", UnitId = 1, IsActive = true, MovingAverageCost = 80000m },
            new ERP.Domain.Entities.Product { Id = 102, Code = "SKU-LOW-102", Name = "Kem Chống Nắng Phiên Bản Giới Hạn", UnitId = 1, IsActive = true, MovingAverageCost = 95000m },
            new ERP.Domain.Entities.Product { Id = 103, Code = "SKU-GIFT-103", Name = "Bông Tẩy Trang Tặng Kèm", UnitId = 1, IsActive = true, MovingAverageCost = 0m }
        );
        db.InventoryStocks.AddRange(
            new ERP.Domain.Entities.InventoryStock { Id = 1, WarehouseId = 1, ProductId = 101, LocationId = 10, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 100m, ReservedQuantity = 20m, AllocatedQuantity = 0m, LockedQuantity = 0m },
            new ERP.Domain.Entities.InventoryStock { Id = 2, WarehouseId = 1, ProductId = 102, LocationId = 10, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 5m, ReservedQuantity = 0m, AllocatedQuantity = 0m, LockedQuantity = 0m }
        );
        await db.SaveChangesAsync();

        var soServiceMock = new Mock<ISalesOrderBackorderService>();
        var soController = new SalesOrdersController(soServiceMock.Object, new SalesOrderOperationsService(db));

        var connectPrimary = await soController.ConnectMarketplaceShop(new SalesOrdersController.ConnectMarketplaceShopRequest(
            ChannelCode: "SHOPEE",
            ShopCode: "SHOPEE-FLAGSHIP",
            DefaultWarehouseId: 1,
            SafetyStockBufferPct: 15m,
            LowStockThreshold: 5m,
            IsPrimaryShop: true,
            InternalProductId: 101,
            ChannelSkuCode: "SP-101"), default);
        connectPrimary.Should().BeOfType<OkObjectResult>();

        var connectSecondary = await soController.ConnectMarketplaceShop(new SalesOrdersController.ConnectMarketplaceShopRequest(
            ChannelCode: "TIKTOK",
            ShopCode: "TIKTOK-SECONDARY",
            DefaultWarehouseId: 1,
            ReserveRatioPct: 50m,
            LowStockThreshold: 5m,
            IsPrimaryShop: false,
            InternalProductId: 102,
            ChannelSkuCode: "TT-102"), default);
        connectSecondary.Should().BeOfType<OkObjectResult>();

        var syncRes = await soController.SyncMarketplaceInventory(new SalesOrdersController.SyncMarketplaceInventoryRequest(WarehouseId: 1), default);
        var syncOk = syncRes.Should().BeOfType<OkObjectResult>().Subject;
        using var syncDoc = System.Text.Json.JsonSerializer.SerializeToDocument(syncOk.Value);
        var syncItems = syncDoc.RootElement.GetProperty("items").EnumerateArray().ToList();
        syncItems.Should().Contain(x => x.GetProperty("channelSkuCode").GetString() == "SP-101" && x.GetProperty("pushedQuantity").GetDecimal() == 68m);
        syncItems.Should().Contain(x => x.GetProperty("channelSkuCode").GetString() == "TT-102" && x.GetProperty("pushedQuantity").GetDecimal() == 0m && x.GetProperty("lowStockCutOffApplied").GetBoolean());

        (await db.IntegrationOutboxMessages.CountAsync(m => m.EventType == "Inventory.AvailableChanged")).Should().BeGreaterThanOrEqualTo(2);

        db.SalesOrders.AddRange(
            new ERP.Domain.Entities.SalesOrder { Id = 501, OrderCode = "SO-UNPICKED-501", WarehouseId = 1, Status = ERP.Domain.Enums.SalesOrderStatus.Released, CreatedBy = 1 },
            new ERP.Domain.Entities.SalesOrder { Id = 502, OrderCode = "SO-PICKED-502", WarehouseId = 1, Status = ERP.Domain.Enums.SalesOrderStatus.Released, CreatedBy = 1, Lines = [new ERP.Domain.Entities.SalesOrderLine { Id = 5021, ProductId = 101, OrderedQuantity = 3m }] },
            new ERP.Domain.Entities.SalesOrder { Id = 503, OrderCode = "SO-DISPATCHED-503", WarehouseId = 1, Status = ERP.Domain.Enums.SalesOrderStatus.Fulfilled, CreatedBy = 1 }
        );
        var resPicked = new ERP.Domain.Entities.StockReservation
        {
            Id = 901,
            ReservationCode = "RSV-502",
            WarehouseId = 1,
            ProductId = 101,
            SourceType = "SalesOrder",
            SourceId = 502,
            SourceCode = "SO-PICKED-502",
            Quantity = 3m,
            Status = ERP.Domain.Enums.StockReservationStatus.Active,
            CreatedBy = 1
        };
        db.StockReservations.Add(resPicked);
        db.StockAllocations.Add(new ERP.Domain.Entities.StockAllocation
        {
            Id = 902,
            ReservationId = 901,
            Reservation = resPicked,
            WarehouseId = 1,
            ProductId = 101,
            LocationId = 10,
            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
            Quantity = 3m,
            Status = ERP.Domain.Enums.StockAllocationStatus.Picked,
            AllocatedBy = 1
        });
        await db.SaveChangesAsync();

        var cancelUnpicked = await soController.WebhookBuyerCancellation("SHOPEE", new SalesOrdersController.BuyerCancellationWebhookRequest(SalesOrderId: 501, BuyerReason: "Đổi địa chỉ nhận hàng"), default);
        using var docUnpicked = System.Text.Json.JsonSerializer.SerializeToDocument(cancelUnpicked.Should().BeOfType<OkObjectResult>().Subject.Value);
        docUnpicked.RootElement.GetProperty("decision").GetString().Should().Be("ACCEPT");
        docUnpicked.RootElement.GetProperty("orderStatus").GetString().Should().Be("Cancelled");

        var cancelPicked = await soController.WebhookBuyerCancellation("SHOPEE", new SalesOrdersController.BuyerCancellationWebhookRequest(SalesOrderId: 502, BuyerReason: "Khách hủy khi đã nhặt hàng"), default);
        using var docPicked = System.Text.Json.JsonSerializer.SerializeToDocument(cancelPicked.Should().BeOfType<OkObjectResult>().Subject.Value);
        docPicked.RootElement.GetProperty("decision").GetString().Should().Be("ACCEPT_PENDING_PUTBACK");
        docPicked.RootElement.GetProperty("orderStatus").GetString().Should().Be("CancelPendingPutback");
        (await db.PutawayTasks.AnyAsync(t => t.TaskType == "Putback")).Should().BeTrue();

        var cancelDispatched = await soController.WebhookBuyerCancellation("SHOPEE", new SalesOrdersController.BuyerCancellationWebhookRequest(SalesOrderId: 503, BuyerReason: "Khách bấm hủy muộn"), default);
        using var docDispatched = System.Text.Json.JsonSerializer.SerializeToDocument(cancelDispatched.Should().BeOfType<OkObjectResult>().Subject.Value);
        docDispatched.RootElement.GetProperty("decision").GetString().Should().Be("REJECT");
        docDispatched.RootElement.GetProperty("reasonCode").GetString().Should().Be("ALREADY_DISPATCHED_TO_CARRIER");

        var giftZeroRes = await soController.EvaluatePromotionGiftRules(new SalesOrdersController.EvaluateGiftRulesRequest(WarehouseId: 1, GiftProductId: 103, GiftQuantity: 1m), default);
        using var docGift = System.Text.Json.JsonSerializer.SerializeToDocument(giftZeroRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        docGift.RootElement.GetProperty("giftSkippedDueToZeroStock").GetBoolean().Should().BeTrue();
        docGift.RootElement.GetProperty("mainOrderBlocked").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Wave45_BatchPrintAwb_ReprintReasonGuard_HandoverManifestHardGates_AddressNormalization_And_SlaPipeline()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave45_Outbound_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        var now = DateTime.UtcNow;
        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Xuất Vận Đơn", IsActive = true });
        db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation { Id = 11, WarehouseId = 1, Code = "A-01-02", Name = "Kệ A-01-02", LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage, IsActive = true, IsPickable = true, IsReceivable = true });
        var prod = new ERP.Domain.Entities.Product { Id = 201, Code = "SKU-AWB-201", Name = "Sữa Rửa Mặt Dịu Nhẹ", UnitId = 1, IsActive = true };
        db.Products.Add(prod);

        db.ExportReceipts.AddRange(
            new ERP.Domain.Entities.ExportReceipt
            {
                Id = 701,
                Code = "EXP-AWB-701",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ReceiptStatus.Approved,
                TrackingNumber = "GHN-701-TRACK",
                CarrierCode = "GHN",
                MarketplaceOrderId = "SP-ORD-701",
                MarketplaceShipByUtc = now.AddMinutes(45),
                CreatedBy = 1,
                Details = [new ERP.Domain.Entities.ExportReceiptDetail { Id = 7011, ProductId = 201, Product = prod, Quantity = 2m, UnitPrice = 150000m }]
            },
            new ERP.Domain.Entities.ExportReceipt
            {
                Id = 702,
                Code = "EXP-CANCEL-PUTBACK-702",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ReceiptStatus.CancelPendingPutback,
                TrackingNumber = "GHN-702-BLOCKED",
                CarrierCode = "GHN",
                MarketplaceOrderId = "SP-ORD-702",
                CreatedBy = 1
            }
        );
        await db.SaveChangesAsync();

        var expServiceMock = new Mock<IExportReceiptService>();
        expServiceMock.Setup(s => s.GetAllAsync()).ReturnsAsync(
        [
            new ERP.Application.DTOs.ExportReceiptDto { Id = 701, Code = "EXP-AWB-701", Status = "Approved", MarketplaceOrderId = "SP-ORD-701", MarketplaceShipByUtc = now.AddMinutes(45) }
        ]);
        var expController = new ExportReceiptsController(expServiceMock.Object, new ExportReceiptOperationsService(db));

        var pipeRes = await expController.GetPipelineSummary();
        using var pipeDoc = System.Text.Json.JsonSerializer.SerializeToDocument(pipeRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        pipeDoc.RootElement.GetProperty("slaAtRiskUnder2HoursCount").GetInt32().Should().Be(1);

        var print1 = await expController.BatchPrintAwb(new ExportReceiptsController.BatchPrintAwbRequest(WarehouseId: 1, ReceiptIds: [701], EnforceReprintReason: true), default);
        using var print1Doc = System.Text.Json.JsonSerializer.SerializeToDocument(print1.Should().BeOfType<OkObjectResult>().Subject.Value);
        print1Doc.RootElement.GetProperty("paperSize").GetString().Should().Be("100x150mm");
        var label0 = print1Doc.RootElement.GetProperty("labels")[0];
        label0.GetProperty("pickingSummaryFooter").GetString().Should().Contain("[Kệ: A-01-02 | SKU: SKU-AWB-201 x 2]");
        label0.GetProperty("awbPrintCount").GetInt32().Should().Be(1);

        var print2Blocked = await expController.BatchPrintAwb(new ExportReceiptsController.BatchPrintAwbRequest(WarehouseId: 1, ReceiptIds: [701], EnforceReprintReason: true, ReprintReason: ""), default);
        var conflictReprint = print2Blocked.Should().BeOfType<ConflictObjectResult>().Subject;
        using var conflictDoc = System.Text.Json.JsonSerializer.SerializeToDocument(conflictReprint.Value);
        conflictDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("REPRINT_REASON_REQUIRED");

        var scanBlocked = await expController.ScanHandoverAwb(null, new ExportReceiptsController.ScanHandoverAwbRequest(WarehouseId: 1, TrackingNumber: "GHN-702-BLOCKED"), default);
        using var scanBlockedDoc = System.Text.Json.JsonSerializer.SerializeToDocument(scanBlocked.Should().BeOfType<ConflictObjectResult>().Subject.Value);
        scanBlockedDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("ORDER_CANCELLED_PUTBACK_REQUIRED");

        var scanOk = await expController.ScanHandoverAwb(null, new ExportReceiptsController.ScanHandoverAwbRequest(WarehouseId: 1, TrackingNumber: "GHN-701-TRACK", CarrierCode: "GHN"), default);
        using var scanOkDoc = System.Text.Json.JsonSerializer.SerializeToDocument(scanOk.Should().BeOfType<OkObjectResult>().Subject.Value);
        var manifestId = scanOkDoc.RootElement.GetProperty("manifestId").GetInt32();
        scanOkDoc.RootElement.GetProperty("totalPackages").GetInt32().Should().Be(1);

        var scanDup = await expController.ScanHandoverAwb(manifestId, new ExportReceiptsController.ScanHandoverAwbRequest(ManifestId: manifestId, TrackingNumber: "GHN-701-TRACK"), default);
        using var scanDupDoc = System.Text.Json.JsonSerializer.SerializeToDocument(scanDup.Should().BeOfType<ConflictObjectResult>().Subject.Value);
        scanDupDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("DUPLICATE_AWB_SCAN");

        var sealRes = await expController.SealHandoverManifest(manifestId, new ExportReceiptsController.SealHandoverManifestRequest(DriverName: "Nguyễn Văn Giao", VehiclePlate: "51D-999.88"), default);
        using var sealDoc = System.Text.Json.JsonSerializer.SerializeToDocument(sealRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        sealDoc.RootElement.GetProperty("Status").GetString().Should().Be("SEALED");

        var addrRes = await expController.GetCarrierAddressMappings("TP. Hồ Chí Minh", "Thành phố Thủ Đức", "Phường An Khánh");
        addrRes.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task Wave45_OpeningBalanceImport_SaaSTenantProvision_SetupWizard_And_3TierQuotaVietQrBilling()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave45_SaaS_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính SaaS", IsActive = true });
        db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation { Id = 12, WarehouseId = 1, Code = "PICK-01", Name = "Kệ Nhặt 01", LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage, IsActive = true, IsPickable = true, IsReceivable = true });
        db.Products.Add(new ERP.Domain.Entities.Product { Id = 301, Code = "SKU-OPN-301", Name = "Kem Dưỡng Ẩm Đầu Kỳ", UnitId = 1, IsActive = true, MovingAverageCost = 0m });
        await db.SaveChangesAsync();

        var expController = new ExportReceiptsController(new Mock<IExportReceiptService>().Object, new ExportReceiptOperationsService(db));
        var openRes = await expController.ImportOpeningBalance(
            new ExportReceiptsController.ImportOpeningBalanceRequest(
                WarehouseId: 1,
                ProductId: 301,
                LocationId: 12,
                Quantity: 40m,
                UnitCost: 115000m),
            default);
        using var openDoc = System.Text.Json.JsonSerializer.SerializeToDocument(openRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        openDoc.RootElement.GetProperty("importedCount").GetInt32().Should().Be(1);
        openDoc.RootElement.GetProperty("referenceType").GetString().Should().Be("OPENING_BALANCE");

        var stock = await db.InventoryStocks.SingleAsync(s => s.ProductId == 301);
        stock.Quantity.Should().Be(40m);
        stock.MovingAverageCost.Should().Be(115000m);
        (await db.InventoryTransactions.AnyAsync(t => t.ProductId == 301 && t.ReferenceType == "OPENING_BALANCE" && t.UnitCost == 115000m)).Should().BeTrue();

        var permController = new PermissionsController(new PermissionOperationsService(db));
        var provRes = await permController.ProvisionSaaSTenant(new PermissionsController.ProvisionTenantRequest(CompanyName: "Mỹ Phẩm Thảo Mộc", PlanCode: "STARTER"), default);
        provRes.Should().BeOfType<OkObjectResult>();

        var wizRes = await permController.GetSaaSSetupWizard(default);
        using var wizDoc = System.Text.Json.JsonSerializer.SerializeToDocument(wizRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        wizDoc.RootElement.GetProperty("totalSteps").GetInt32().Should().Be(5);

        var seedRes = await permController.SeedSaaSDemoData(new PermissionsController.SeedDemoDataRequest(IndustryTemplate: "COSMETICS_FEFO"));
        seedRes.Should().BeOfType<OkObjectResult>();

        var softBurst = await permController.EnforceSubscriptionQuota(new PermissionsController.EnforceSubscriptionQuotaRequest(CurrentMonthOrderCount: 1100, MonthlyOrderQuota: 1000), default);
        using var softDoc = System.Text.Json.JsonSerializer.SerializeToDocument(softBurst.Should().BeOfType<OkObjectResult>().Subject.Value);
        softDoc.RootElement.GetProperty("quotaTierCode").GetString().Should().Be("SOFT_BURST_100_115");

        var actHardCap = async () => await permController.EnforceSubscriptionQuota(new PermissionsController.EnforceSubscriptionQuotaRequest(CurrentMonthOrderCount: 1200, MonthlyOrderQuota: 1000), default);
        var ex = await actHardCap.Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>();
        ex.Which.Data["ErrorCode"].Should().Be("SUBSCRIPTION_QUOTA_EXCEEDED");

        var invRes = await permController.CreateVietQrInvoice(new PermissionsController.CreateVietQrInvoiceRequest(PlanCode: "PRO", AmountVnd: 1490000m), default);
        using var invDoc = System.Text.Json.JsonSerializer.SerializeToDocument(invRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var invCode = invDoc.RootElement.GetProperty("invoiceCode").GetString()!;

        var webhookRes = await permController.HandleVietQrBillingWebhook(new PermissionsController.VietQrWebhookRequest(InvoiceCode: invCode, PlanCode: "PRO", PaidAmountVnd: 1490000m), default);
        using var webhookDoc = System.Text.Json.JsonSerializer.SerializeToDocument(webhookRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        webhookDoc.RootElement.GetProperty("upgradedPlanCode").GetString().Should().Be("PRO");
        webhookDoc.RootElement.GetProperty("newMonthlyOrderQuota").GetInt32().Should().Be(10000);
    }

    [Fact]
    public async Task Wave45_CodReconciliation_OwnerGrossProfit_MisaExcel_PackingVideoEvidence_And_PiiAnonymization()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave45_FinanceAndSec_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Đối Soát", IsActive = true });
        var prod = new ERP.Domain.Entities.Product { Id = 401, Code = "SKU-FIN-401", Name = "Tinh Chất Phục Hồi", UnitId = 1, IsActive = true, MovingAverageCost = 90000m };
        db.Products.Add(prod);
        db.ExportReceipts.Add(new ERP.Domain.Entities.ExportReceipt
        {
            Id = 801,
            Code = "EXP-FIN-801",
            WarehouseId = 1,
            Status = ERP.Domain.Enums.ReceiptStatus.Dispatched,
            CustomerNameSnapshot = "Nguyễn Thị Khách Mua Hàng",
            CreatedAt = DateTime.UtcNow.AddDays(-120),
            CreatedBy = 1,
            Details = [new ERP.Domain.Entities.ExportReceiptDetail { Id = 8011, ProductId = 401, Product = prod, Quantity = 5m, UnitPrice = 160000m }]
        });
        await db.SaveChangesAsync();

        var reportsController = new ReportsController(new Mock<IReportService>().Object, new ReportOperationsService(db));

        var codVerify = await reportsController.VerifyCodAndSettlementReconciliation(
            new ReportsController.VerifyCodSettlementRequest(
                WarehouseId: 1,
                CarrierCode: "GHN",
                Items:
                [
                    new ReportsController.CodSettlementItemRequest(
                        TrackingNumber: "GHN-COD-801",
                        CarrierCode: "GHN",
                        ExportReceiptId: 801,
                        ExpectedCodAmount: 800000m,
                        ActualCodCollected: 800000m,
                        EstimatedShippingFee: 25000m,
                        ActualShippingFee: 38000m,
                        PlatformCommissionFee: 56000m,
                        ReturnFee: 0m)
                ]),
            default);
        using var codDoc = System.Text.Json.JsonSerializer.SerializeToDocument(codVerify.Should().BeOfType<OkObjectResult>().Subject.Value);
        codDoc.RootElement.GetProperty("discrepancyCount").GetInt32().Should().Be(1);
        codDoc.RootElement.GetProperty("items")[0].GetProperty("feeOverchargeVariance").GetDecimal().Should().Be(13000m);

        var gpRes = await reportsController.GetOwnerGrossProfit(1, default);
        using var gpDoc = System.Text.Json.JsonSerializer.SerializeToDocument(gpRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        gpDoc.RootElement.GetProperty("grossRevenue").GetDecimal().Should().Be(800000m);
        gpDoc.RootElement.GetProperty("movingAverageCogs").GetDecimal().Should().Be(450000m);
        gpDoc.RootElement.GetProperty("netPocketProfit").GetDecimal().Should().BeGreaterThan(0m);

        var expController = new ExportReceiptsController(new Mock<IExportReceiptService>().Object, new ExportReceiptOperationsService(db));
        var misaRes = await expController.GetMisaExcelExport(1, "json", default);
        using var misaDoc = System.Text.Json.JsonSerializer.SerializeToDocument(misaRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        misaDoc.RootElement.GetProperty("defaultDebitCogsAccount").GetString().Should().Be("632");
        misaDoc.RootElement.GetProperty("defaultCreditInventoryAccount").GetString().Should().Be("1561");

        var packController = new PackingSessionsController(new Mock<IPackingService>().Object, new PackingSessionOperationsService(db));
        var vidRec = await packController.RecordPackingVideoEvidence(
            new PackingSessionsController.RecordPackingVideoEvidenceRequest(
                ExportReceiptId: 801,
                TrackingNumber: "GHN-COD-801",
                CameraStationId: "CAM-PACK-02"),
            default);
        vidRec.Should().BeOfType<OkObjectResult>();

        var vidGet = await packController.GetPackingVideoEvidence("GHN-COD-801", default);
        using var vidDoc = System.Text.Json.JsonSerializer.SerializeToDocument(vidGet.Should().BeOfType<OkObjectResult>().Subject.Value);
        vidDoc.RootElement.GetProperty("trackingNumber").GetString().Should().Be("GHN-COD-801");
        vidDoc.RootElement.GetProperty("disputeReady").GetBoolean().Should().BeTrue();

        var secController = new AccountSecurityController(
            new Mock<IAccountAdminService>().Object,
            new Mock<IUserSessionService>().Object,
            new WmsGovernanceOperationsService(db));
        var impRes = await secController.GrantSupportImpersonation(
            new AccountSecurityController.GrantSupportImpersonationRequest(SupportTicketCode: "SUP-2026-99", ExpiresInMinutes: 30, ReadOnlyScope: true),
            default);
        impRes.Should().BeOfType<OkObjectResult>();

        var piiRes = await secController.AnonymizeExpiredBuyerPii(
            new AccountSecurityController.AnonymizeBuyerPiiRequest(RetentionDays: 90),
            default);
        using var piiDoc = System.Text.Json.JsonSerializer.SerializeToDocument(piiRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        piiDoc.RootElement.GetProperty("anonymizedCount").GetInt32().Should().Be(1);

        var anonymizedReceipt = await db.ExportReceipts.SingleAsync(r => r.Id == 801);
        anonymizedReceipt.CustomerNameSnapshot.Should().StartWith("ANONYMIZED_PII_");
    }

    [Fact]
    public async Task Wave46_Permissions_SaaSSubscriptionBillingAliases_CreateVietQrInvoice_WebhookVietQr_And_SetupWizard_Return200WithDbBackedData()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave46_Permissions_SaaS_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Tổng W46", IsActive = true });
        db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "PICK-01",
            Name = "Kệ Nhặt 01",
            LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = true
        });
        db.Products.Add(new ERP.Domain.Entities.Product
        {
            Id = 101,
            Code = "SKU-W46-01",
            Name = "Sản phẩm SaaS W46",
            UnitId = 1,
            MovingAverageCost = 90000m,
            IsActive = true
        });
        db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
        {
            Id = 1,
            WarehouseId = 1,
            ProductId = 101,
            LocationId = 10,
            Status = ERP.Domain.Enums.InventoryStatus.Available,
            Quantity = 50m,
            MovingAverageCost = 90000m
        });
        db.MarketplaceShops.Add(new ERP.Domain.Entities.MarketplaceShop
        {
            Id = 1,
            ChannelCode = "SHOPEE",
            ShopCode = "SHOPEE-W46",
            ShopName = "Gian hàng Shopee W46",
            DefaultWarehouseId = 1,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var permController = new PermissionsController(new PermissionOperationsService(db));

        var wizRes = await permController.GetSaaSSetupWizard(default);
        using var wizDoc = System.Text.Json.JsonSerializer.SerializeToDocument(wizRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        wizDoc.RootElement.GetProperty("totalSteps").GetInt32().Should().Be(5);
        wizDoc.RootElement.GetProperty("completedSteps").GetInt32().Should().Be(5);
        wizDoc.RootElement.GetProperty("completionPct").GetInt32().Should().Be(100);
        wizDoc.RootElement.GetProperty("statusLabel").GetString().Should().Contain("Tiến độ hướng dẫn thiết lập nhanh 5 bước");

        var subRes = await permController.GetSaaSSubscriptionBilling(null, null, default);
        using var subDoc = System.Text.Json.JsonSerializer.SerializeToDocument(subRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        subDoc.RootElement.GetProperty("planCode").GetString().Should().Be("STARTER");
        subDoc.RootElement.GetProperty("subscriptionStatus").GetString().Should().Be("ACTIVE");
        subDoc.RootElement.GetProperty("quotaTierCode").GetString().Should().Be("NORMAL");
        (await db.TenantSubscriptions.CountAsync()).Should().Be(1);

        var invRes = await permController.CreateVietQrInvoice(
            new PermissionsController.CreateVietQrInvoiceRequest(PlanCode: "PRO", AmountVnd: 1490000m),
            default);
        using var invDoc = System.Text.Json.JsonSerializer.SerializeToDocument(invRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var invCode = invDoc.RootElement.GetProperty("invoiceCode").GetString()!;
        invCode.Should().StartWith("INV-VQR-");
        invDoc.RootElement.GetProperty("status").GetString().Should().Be("UNPAID");

        var whRes = await permController.HandleVietQrBillingWebhook(
            new PermissionsController.VietQrWebhookRequest(InvoiceCode: null, PlanCode: "PRO", PaidAmountVnd: 1490000m),
            default);
        using var whDoc = System.Text.Json.JsonSerializer.SerializeToDocument(whRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        whDoc.RootElement.GetProperty("invoiceCode").GetString().Should().Be(invCode);
        whDoc.RootElement.GetProperty("upgradedPlanCode").GetString().Should().Be("PRO");
        whDoc.RootElement.GetProperty("subscriptionStatus").GetString().Should().Be("ACTIVE");
        whDoc.RootElement.GetProperty("newMonthlyOrderQuota").GetInt32().Should().Be(10000);
        whDoc.RootElement.GetProperty("statusLabel").GetString().Should().Contain("Đã xác nhận thanh toán VietQR");

        var dbInvoice = await db.SaasBillingInvoices.SingleAsync();
        dbInvoice.Status.Should().Be("PAID");
    }

    [Fact]
    public async Task Wave46_ExportReceipts_MisaExcelAliases_And_BatchPrintAwb_DefaultDbReceiptsVsExplicitReprintGuard()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave46_Export_Misa_Awb_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Tổng W46", IsActive = true });
        db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "A-01-01",
            Name = "Kệ Nhặt A-01-01",
            LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = true
        });
        var prod = new ERP.Domain.Entities.Product
        {
            Id = 201,
            Code = "SKU-MISA-201",
            Name = "Kem Chống Nắng MISA",
            UnitId = 1,
            MovingAverageCost = 85000m,
            IsActive = true
        };
        db.Products.Add(prod);
        db.ExportReceipts.Add(new ERP.Domain.Entities.ExportReceipt
        {
            Id = 701,
            Code = "PXK-W46-701",
            WarehouseId = 1,
            Status = ERP.Domain.Enums.ReceiptStatus.Approved,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ERP.Domain.Entities.ExportReceiptDetail
                {
                    Id = 7011,
                    ProductId = 201,
                    Product = prod,
                    Quantity = 4m,
                    UnitPrice = 150000m
                }
            ]
        });
        await db.SaveChangesAsync();

        var expController = new ExportReceiptsController(new Mock<IExportReceiptService>().Object, new ExportReceiptOperationsService(db));

        var firstPrint = await expController.BatchPrintAwb(new ExportReceiptsController.BatchPrintAwbRequest(WarehouseId: 1), default);
        using var firstDoc = System.Text.Json.JsonSerializer.SerializeToDocument(firstPrint.Should().BeOfType<OkObjectResult>().Subject.Value);
        firstDoc.RootElement.GetProperty("paperSize").GetString().Should().Be("100x150mm");
        firstDoc.RootElement.GetProperty("printedCount").GetInt32().Should().Be(1);
        firstDoc.RootElement.GetProperty("labels")[0].GetProperty("pickingSummaryFooter").GetString()
            .Should().Contain("[Kệ: A-01-01 | SKU: SKU-MISA-201 x 4]");

        var quickPrintAgain = await expController.BatchPrintAwb(new ExportReceiptsController.BatchPrintAwbRequest(WarehouseId: 1, EnforceReprintReason: true), default);
        quickPrintAgain.Should().BeOfType<OkObjectResult>();

        var blockedReprint = await expController.BatchPrintAwb(
            new ExportReceiptsController.BatchPrintAwbRequest(WarehouseId: 1, ReceiptIds: [701], EnforceReprintReason: true, ReprintReason: ""),
            default);
        using var blockedDoc = System.Text.Json.JsonSerializer.SerializeToDocument(blockedReprint.Should().BeOfType<ConflictObjectResult>().Subject.Value);
        blockedDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("REPRINT_REASON_REQUIRED");

        var misaRes = await expController.GetMisaExcelExport(1, "json", default);
        using var misaDoc = System.Text.Json.JsonSerializer.SerializeToDocument(misaRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        misaDoc.RootElement.GetProperty("templateStandard").GetString().Should().Be("MISA_AMIS_ACT_200");
        misaDoc.RootElement.GetProperty("defaultDebitCogsAccount").GetString().Should().Be("632");
        misaDoc.RootElement.GetProperty("defaultCreditInventoryAccount").GetString().Should().Be("1561");
        misaDoc.RootElement.GetProperty("rowCount").GetInt32().Should().Be(1);
        misaDoc.RootElement.GetProperty("rows")[0].GetProperty("thanhTienGiaVon").GetDecimal().Should().Be(340000m);
        misaDoc.RootElement.GetProperty("rows")[0].GetProperty("doanhThu").GetDecimal().Should().Be(600000m);

        var misaPostRes = await expController.ExportMisaEinvoicePost(
            new ExportReceiptsController.ExportMisaEinvoiceRequest(WarehouseId: 1, Standard: "MISA_ND70_2025"),
            default);
        misaPostRes.Should().BeOfType<OkObjectResult>();

        var confirmHandoverRes = await expController.ConfirmCarrierHandover(
            new ExportReceiptsController.ConfirmCarrierHandoverRequest(
                WarehouseId: 1,
                CarrierCode: "GHN",
                TrackingNumbers: ["GHN84920188", "VTP99201122"]),
            default);
        using var confirmDoc = System.Text.Json.JsonSerializer.SerializeToDocument(confirmHandoverRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        confirmDoc.RootElement.GetProperty("Status").GetString().Should().Be("SEALED");
        confirmDoc.RootElement.GetProperty("TotalPackages").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Wave46_SalesOrders_SyncMarketplaceInventoryWithoutShopId_And_Reports_CodVerifyAliases_OwnerPnlAliases_Return200WithDbBackedData()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave46_Sync_Cod_Pnl_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Tổng W46", IsActive = true });
        db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "PICK-01",
            Name = "Kệ Nhặt 01",
            LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = true
        });
        var prod = new ERP.Domain.Entities.Product
        {
            Id = 301,
            Code = "SKU-SYNC-301",
            Name = "Tinh Chất Nghệ W46",
            UnitId = 1,
            MovingAverageCost = 70000m,
            IsActive = true
        };
        db.Products.Add(prod);
        db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
        {
            Id = 1,
            WarehouseId = 1,
            ProductId = 301,
            LocationId = 10,
            Status = ERP.Domain.Enums.InventoryStatus.Available,
            Quantity = 50m,
            ReservedQuantity = 10m,
            MovingAverageCost = 70000m
        });
        db.MarketplaceShops.Add(new ERP.Domain.Entities.MarketplaceShop
        {
            Id = 1,
            ChannelCode = "SHOPEE",
            ShopCode = "SHOPEE-OFFICIAL-01",
            ShopName = "Gian hàng Shopee Official",
            DefaultWarehouseId = 1,
            ReserveRatioPct = 90m,
            SafetyBufferQty = 0m,
            LowStockThreshold = 5m,
            IsPrimaryShop = true,
            IsActive = true
        });
        db.ExportReceipts.Add(new ERP.Domain.Entities.ExportReceipt
        {
            Id = 901,
            Code = "PXK-PNL-901",
            WarehouseId = 1,
            Status = ERP.Domain.Enums.ReceiptStatus.Dispatched,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ERP.Domain.Entities.ExportReceiptDetail
                {
                    Id = 9011,
                    ProductId = 301,
                    Product = prod,
                    Quantity = 10m,
                    UnitPrice = 150000m
                }
            ]
        });
        await db.SaveChangesAsync();

        var soController = new SalesOrdersController(new Mock<ISalesOrderBackorderService>().Object, new SalesOrderOperationsService(db));
        var syncRes = await soController.SyncMarketplaceInventory(
            new SalesOrdersController.SyncMarketplaceInventoryRequest(WarehouseId: 1, ShopId: null),
            default);
        using var syncDoc = System.Text.Json.JsonSerializer.SerializeToDocument(syncRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        syncDoc.RootElement.GetProperty("shopCount").GetInt32().Should().Be(1);
        syncDoc.RootElement.GetProperty("syncedCount").GetInt32().Should().Be(1);
        syncDoc.RootElement.GetProperty("items")[0].GetProperty("atpBaseQty").GetDecimal().Should().Be(40m);
        syncDoc.RootElement.GetProperty("items")[0].GetProperty("pushedQuantity").GetDecimal().Should().Be(36m);

        var syncByShopRes = await soController.SyncMarketplaceInventoryByShop(1, null, default);
        syncByShopRes.Should().BeOfType<OkObjectResult>();

        var reportsController = new ReportsController(new Mock<IReportService>().Object, new ReportOperationsService(db));
        var codVerifyRes = await reportsController.VerifyCodAndSettlementReconciliation(
            new ReportsController.VerifyCodSettlementRequest(
                WarehouseId: 1,
                CarrierCode: "GHN",
                Items:
                [
                    new ReportsController.CodSettlementItemRequest(
                        TrackingNumber: "GHN-W46-901",
                        CarrierCode: "GHN",
                        ExportReceiptId: 901,
                        ExpectedCodAmount: 1500000m,
                        ActualCodCollected: 1500000m,
                        EstimatedShippingFee: 25000m,
                        ActualShippingFee: 35000m,
                        PlatformCommissionFee: 105000m,
                        ReturnFee: 0m)
                ]),
            default);
        using var codVerifyDoc = System.Text.Json.JsonSerializer.SerializeToDocument(codVerifyRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        codVerifyDoc.RootElement.GetProperty("discrepancyCount").GetInt32().Should().Be(1);

        var codGetRes = await reportsController.GetCodAndSettlementReconciliation(1, "GHN", default);
        using var codGetDoc = System.Text.Json.JsonSerializer.SerializeToDocument(codGetRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        codGetDoc.RootElement.GetProperty("totalExpectedCod").GetDecimal().Should().Be(1500000m);
        codGetDoc.RootElement.GetProperty("totalFeeOverchargeVariance").GetDecimal().Should().Be(10000m);
        codGetDoc.RootElement.GetProperty("totalNetPayout").GetDecimal().Should().Be(1360000m);

        var pnlRes = await reportsController.GetOwnerGrossProfit(1, default);
        using var pnlDoc = System.Text.Json.JsonSerializer.SerializeToDocument(pnlRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        pnlDoc.RootElement.GetProperty("grossRevenue").GetDecimal().Should().Be(1500000m);
        pnlDoc.RootElement.GetProperty("movingAverageCogs").GetDecimal().Should().Be(700000m);
        pnlDoc.RootElement.GetProperty("platformCommissionFee").GetDecimal().Should().Be(105000m);
        pnlDoc.RootElement.GetProperty("shippingFeeDifferentials").GetDecimal().Should().Be(10000m);
        pnlDoc.RootElement.GetProperty("packagingCost").GetDecimal().Should().Be(25000m);
        pnlDoc.RootElement.GetProperty("netPocketProfit").GetDecimal().Should().Be(660000m);
    }
}
