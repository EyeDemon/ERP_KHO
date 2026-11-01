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
            controlOps,
            Mock.Of<IWarehouseAuthorizationService>());
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

        var dockController = new DockYardController(
            new Mock<IDockYardService>().Object,
            new WmsGovernanceOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>());
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

        var importController = new ImportReceiptsController(
            new Mock<IImportReceiptService>().Object,
            Mock.Of<IReceivingDiscrepancyService>(),
            new ImportReceiptOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>());
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
            Mock.Of<IWarehouseAuthorizationService>(),
            new Mock<IStockReservationService>().Object,
            Mock.Of<ICurrentUser>());
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
        var soController = new SalesOrdersController(
            soServiceMock.Object,
            new SalesOrderOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>());
        var cancelPutbackRes = await soController.ConfirmCancelPutback(401, new ConfirmCancelPutbackRequest(Note: "Đã cất trả kệ PICK-01"), default);
        cancelPutbackRes.Should().BeOfType<OkObjectResult>();

        var updatedStock = await db.InventoryStocks.SingleAsync(s => s.Id == 3001);
        updatedStock.AllocatedQuantity.Should().Be(0m);
        updatedStock.ReservedQuantity.Should().Be(0m);

        var shpController = new ShipmentsController(
            new Mock<IShipmentService>().Object,
            new ShipmentOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>());
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
            new StocktakeOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>());
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
        var soController = new SalesOrdersController(
            soServiceMock.Object,
            new SalesOrderOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>());

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
        var expController = new ExportReceiptsController(
            expServiceMock.Object,
            new ExportReceiptOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>(),
            new HandoverManifestService(db, expServiceMock.Object));

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

        var expController = new ExportReceiptsController(
            new Mock<IExportReceiptService>().Object,
            new ExportReceiptOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>(),
            new HandoverManifestService(db, Mock.Of<IExportReceiptService>()));
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

        var permController = new PermissionsController(
            new PermissionOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>());
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

        const string w44Secret = "TEST_W44_VIETQR_SECRET";
        Environment.SetEnvironmentVariable("SAAS_VIETQR_WEBHOOK_SECRET", w44Secret);
        var w44HttpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        w44HttpCtx.Request.Headers["X-VietQR-Signature"] = SaasBillingService.ComputeVietQrSignature(w44Secret, invCode, "PRO", 1490000m);
        permController.ControllerContext = new ControllerContext { HttpContext = w44HttpCtx };

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

        var reportsController = new ReportsController(
            new Mock<IReportService>().Object,
            new ReportOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>());

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

        var expController = new ExportReceiptsController(
            new Mock<IExportReceiptService>().Object,
            new ExportReceiptOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>(),
            new HandoverManifestService(db, Mock.Of<IExportReceiptService>()));
        var misaRes = await expController.GetMisaExcelExport(1, "json", default);
        using var misaDoc = System.Text.Json.JsonSerializer.SerializeToDocument(misaRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        misaDoc.RootElement.GetProperty("defaultDebitCogsAccount").GetString().Should().Be("632");
        misaDoc.RootElement.GetProperty("defaultCreditInventoryAccount").GetString().Should().Be("1561");

        var packController = new PackingSessionsController(
            new Mock<IPackingService>().Object,
            new PackingSessionOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>());
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
            new WmsGovernanceOperationsService(db),
            Mock.Of<IPasswordHasherService>());
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

        var permController = new PermissionsController(
            new PermissionOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>());

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
        (await db.TenantSubscriptions.CountAsync()).Should().Be(0);

        var invRes = await permController.CreateVietQrInvoice(
            new PermissionsController.CreateVietQrInvoiceRequest(PlanCode: "PRO", AmountVnd: 1490000m),
            default);
        using var invDoc = System.Text.Json.JsonSerializer.SerializeToDocument(invRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var invCode = invDoc.RootElement.GetProperty("invoiceCode").GetString()!;
        invCode.Should().StartWith("INV-VQR-");
        invDoc.RootElement.GetProperty("status").GetString().Should().Be("UNPAID");

        const string w46Secret = "TEST_W46_VIETQR_SECRET";
        Environment.SetEnvironmentVariable("SAAS_VIETQR_WEBHOOK_SECRET", w46Secret);
        var w46HttpCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        w46HttpCtx.Request.Headers["X-VietQR-Signature"] = SaasBillingService.ComputeVietQrSignature(w46Secret, null, "PRO", 1490000m);
        permController.ControllerContext = new ControllerContext { HttpContext = w46HttpCtx };

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

        var expController = new ExportReceiptsController(
            new Mock<IExportReceiptService>().Object,
            new ExportReceiptOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>(),
            new HandoverManifestService(db, Mock.Of<IExportReceiptService>()));

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

        var soController = new SalesOrdersController(
            new Mock<ISalesOrderBackorderService>().Object,
            new SalesOrderOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>());
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

        var reportsController = new ReportsController(
            new Mock<IReportService>().Object,
            new ReportOperationsService(db),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>());
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

    [Fact]
    public async Task Wave62_1_DoD1_VietQrWebhook_RejectsMissingInvalidAndBackdoorSignatures_And_ReplaysIdempotentlyWithoutDoubleExtendingPeriod()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave62_1_DoD1_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        const string testSecret = "W62_1_HMAC_SECRET_KEY_2026";
        Environment.SetEnvironmentVariable("SAAS_VIETQR_WEBHOOK_SECRET", testSecret);

        var saasService = new SaasBillingService(db);
        var saasCtrl = new SaasBillingController(saasService, Mock.Of<ICurrentUser>());

        var invRes = await saasCtrl.GenerateVietQrInvoice(
            new SaasCreateVietQrInvoiceRequest(PlanCode: "PRO", AmountVnd: 1490000m),
            default);
        using var invDoc = System.Text.Json.JsonSerializer.SerializeToDocument(invRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var invCode = invDoc.RootElement.GetProperty("invoiceCode").GetString()!;

        // 1. Missing X-VietQR-Signature header -> 401 Unauthorized
        saasCtrl.ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() };
        var missingHeaderRes = await saasCtrl.HandleVietQrBillingWebhook(
            new SaasVietQrWebhookRequest(InvoiceCode: invCode, PlanCode: "PRO", PaidAmountVnd: 1490000m),
            default);
        missingHeaderRes.Should().BeOfType<UnauthorizedObjectResult>();

        // 2. Wrong signature -> 401 Unauthorized
        var wrongSigCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        wrongSigCtx.Request.Headers["X-VietQR-Signature"] = "deadbeef0000111122223333444455556666777788889999aaaabbbbccccdddd";
        saasCtrl.ControllerContext = new ControllerContext { HttpContext = wrongSigCtx };
        var wrongSigRes = await saasCtrl.HandleVietQrBillingWebhook(
            new SaasVietQrWebhookRequest(InvoiceCode: invCode, PlanCode: "PRO", PaidAmountVnd: 1490000m),
            default);
        wrongSigRes.Should().BeOfType<UnauthorizedObjectResult>();

        // 3. Backdoor string "VALID_VIETQR_HMAC" -> 401 Unauthorized (backdoor eliminated)
        var backdoorCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        backdoorCtx.Request.Headers["X-VietQR-Signature"] = "VALID_VIETQR_HMAC";
        saasCtrl.ControllerContext = new ControllerContext { HttpContext = backdoorCtx };
        var backdoorRes = await saasCtrl.HandleVietQrBillingWebhook(
            new SaasVietQrWebhookRequest(InvoiceCode: invCode, PlanCode: "PRO", PaidAmountVnd: 1490000m),
            default);
        backdoorRes.Should().BeOfType<UnauthorizedObjectResult>();

        // 4. Valid HMAC signature -> 200 OK & upgrades subscription
        var validSig = SaasBillingService.ComputeVietQrSignature(testSecret, invCode, "PRO", 1490000m);
        var validCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        validCtx.Request.Headers["X-VietQR-Signature"] = validSig;
        saasCtrl.ControllerContext = new ControllerContext { HttpContext = validCtx };

        var firstOk = await saasCtrl.HandleVietQrBillingWebhook(
            new SaasVietQrWebhookRequest(InvoiceCode: invCode, PlanCode: "PRO", PaidAmountVnd: 1490000m),
            default);
        firstOk.Should().BeOfType<OkObjectResult>();

        var subAfterFirst = await db.TenantSubscriptions.SingleAsync();
        var periodEndAfterFirst = subAfterFirst.CurrentPeriodEndUtc;
        var invoiceCountAfterFirst = await db.SaasBillingInvoices.CountAsync();
        invoiceCountAfterFirst.Should().Be(1);

        // 5. Idempotent replay (2nd call with same InvoiceCode & valid HMAC) -> 200 OK, does NOT double-extend CurrentPeriodEndUtc or create duplicate invoice
        var secondOk = await saasCtrl.HandleVietQrBillingWebhook(
            new SaasVietQrWebhookRequest(InvoiceCode: invCode, PlanCode: "PRO", PaidAmountVnd: 1490000m),
            default);
        using var secondDoc = System.Text.Json.JsonSerializer.SerializeToDocument(secondOk.Should().BeOfType<OkObjectResult>().Subject.Value);
        secondDoc.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeTrue();

        var subAfterSecond = await db.TenantSubscriptions.SingleAsync();
        subAfterSecond.CurrentPeriodEndUtc.Should().Be(periodEndAfterFirst);
        (await db.SaasBillingInvoices.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Wave62_1_DoD2_ThreePlBilling_IsolatesByDepositorOwnerId_And_PreventsSameDayMultiPartnerInvoiceCodeCollision()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave62_1_DoD2_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH-3PL", Name = "Kho 3PL Đa Chủ Hàng", IsActive = true });
        db.BusinessPartners.AddRange(
            new ERP.Domain.Entities.BusinessPartner { Id = 6201, Code = "DEP-A", Name = "Chủ Hàng Depositor A", IsSupplier = true, IsCustomer = true, IsActive = true },
            new ERP.Domain.Entities.BusinessPartner { Id = 6202, Code = "DEP-B", Name = "Chủ Hàng Depositor B", IsSupplier = true, IsCustomer = true, IsActive = true });

        // Stock & Transactions in the same warehouse for Owner A (6201) and Owner B (6202)
        db.InventoryStocks.AddRange(
            new ERP.Domain.Entities.InventoryStock { Id = 1, WarehouseId = 1, ProductId = 1, LocationId = 10, OwnerId = 6201, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 100m },
            new ERP.Domain.Entities.InventoryStock { Id = 2, WarehouseId = 1, ProductId = 1, LocationId = 11, OwnerId = 6202, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 500m });
        db.InventoryTransactions.AddRange(
            new ERP.Domain.Entities.InventoryTransaction { Id = 10, WarehouseId = 1, ProductId = 1, OwnerId = 6201, TransactionType = ERP.Domain.Enums.TransactionType.Import, InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 20m, TransactionDate = DateTime.UtcNow, CreatedBy = 1 },
            new ERP.Domain.Entities.InventoryTransaction { Id = 11, WarehouseId = 1, ProductId = 1, OwnerId = 6202, TransactionType = ERP.Domain.Enums.TransactionType.Import, InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 300m, TransactionDate = DateTime.UtcNow, CreatedBy = 1 });
        await db.SaveChangesAsync();

        var controller = new BusinessPartnersController(
            new Mock<IBusinessPartnerService>().Object,
            new WmsGovernanceOperationsService(db),
            Mock.Of<ICurrentUser>());

        var resA = await controller.Generate3PlStatement(
            new BusinessPartnersController.Generate3PlStatementRequest(PartnerId: 6201, WarehouseId: 1, BillingPeriod: "2026-10", CreateReceivableInvoice: true),
            default);
        using var docA = System.Text.Json.JsonSerializer.SerializeToDocument(resA.Should().BeOfType<OkObjectResult>().Subject.Value);
        docA.RootElement.GetProperty("totalOnHandUnits").GetDecimal().Should().Be(100m); // Strictly Owner A's 100 units, NOT 600
        docA.RootElement.GetProperty("inboundUnits").GetDecimal().Should().Be(20m); // Strictly Owner A's 20 units, NOT 320
        docA.RootElement.GetProperty("receivableInvoiceCode").GetString().Should().Be($"AR-3PL-6201-{DateTime.UtcNow:yyyyMMdd}");

        var resB = await controller.Generate3PlStatement(
            new BusinessPartnersController.Generate3PlStatementRequest(PartnerId: 6202, WarehouseId: 1, BillingPeriod: "2026-10", CreateReceivableInvoice: true),
            default);
        using var docB = System.Text.Json.JsonSerializer.SerializeToDocument(resB.Should().BeOfType<OkObjectResult>().Subject.Value);
        docB.RootElement.GetProperty("totalOnHandUnits").GetDecimal().Should().Be(500m);
        docB.RootElement.GetProperty("inboundUnits").GetDecimal().Should().Be(300m);
        docB.RootElement.GetProperty("receivableInvoiceCreated").GetBoolean().Should().BeTrue();
        docB.RootElement.GetProperty("receivableInvoiceCode").GetString().Should().Be($"AR-3PL-6202-{DateTime.UtcNow:yyyyMMdd}");

        (await db.PartnerDebtInvoices.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Wave62_1_DoD3_SubscriptionQuota_PreventsDoubleCountingSalesOrderAndExportReceipt_And_EnforcesMaxWarehouses()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave62_1_DoD3_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho 01", IsActive = true });
        var sub = new ERP.Domain.Entities.TenantSubscription
        {
            Id = 1,
            TenantId = 1,
            PlanCode = "STARTER",
            Status = "ACTIVE",
            MonthlyOrderQuota = 1000,
            CurrentMonthOrderCount = 10,
            MaxWarehouses = 1
        };
        db.TenantSubscriptions.Add(sub);
        db.SalesOrders.Add(new ERP.Domain.Entities.SalesOrder
        {
            Id = 5001,
            OrderCode = "SO-5001",
            ExternalOrderId = "EXT-5001",
            CustomerId = 1,
            WarehouseId = 1,
            Status = ERP.Domain.Enums.SalesOrderStatus.Released,
            CreatedBy = 1
        });
        // Simulate SalesOrder creation already incrementing CurrentMonthOrderCount from 10 -> 11
        sub.CurrentMonthOrderCount += 1;
        await db.SaveChangesAsync();

        var expOps = new ExportReceiptOperationsService(db);
        // Linked ExportReceipt with SalesOrderId = 5001 must NOT increment CurrentMonthOrderCount again
        await expOps.IncrementSubscriptionOrderCountAsync(default, salesOrderId: 5001, marketplaceOrderId: null);
        (await db.TenantSubscriptions.SingleAsync()).CurrentMonthOrderCount.Should().Be(11);

        // Standalone ExportReceipt (no SalesOrderId / MarketplaceOrderId) DOES increment from 11 -> 12
        await expOps.IncrementSubscriptionOrderCountAsync(default, salesOrderId: null, marketplaceOrderId: null);
        (await db.TenantSubscriptions.SingleAsync()).CurrentMonthOrderCount.Should().Be(12);

        // Enforce MaxWarehouses = 1 in WarehouseService.CreateWarehouseAsync
        var whRepo = new ERP.Infrastructure.Repositories.WarehouseRepository(db);
        var whService = new ERP.Application.Services.WarehouseService(whRepo, new Mock<IWarehouseAuthorizationService>().Object);
        var actCreateSecondWh = async () => await whService.CreateWarehouseAsync(new ERP.Application.DTOs.CreateWarehouseDto
        {
            Code = "WH02",
            Name = "Kho Vượt Trần Gói Cước"
        }, "admin");
        var quotaEx = await actCreateSecondWh.Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>();
        quotaEx.Which.Data["ErrorCode"].Should().Be("SUBSCRIPTION_QUOTA_EXCEEDED");

        // Enforce MaxSkus in ProductService.CreateProductAsync when repository reports quota exceeded
        var prodRepoMock = new Mock<ERP.Domain.Interfaces.IProductRepository>();
        prodRepoMock.Setup(x => x.IsMaxSkusExceededAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var prodService = new ERP.Application.Services.ProductService(prodRepoMock.Object, new Mock<IProductCatalogService>().Object);
        var actCreateExceedSku = async () => await prodService.CreateProductAsync(new ERP.Application.DTOs.CreateProductDto
        {
            Code = "SKU-OVER-QUOTA",
            Name = "SKU Vượt Trần Gói Cước",
            UnitId = 1,
            CategoryId = 1
        }, "admin");
        var skuQuotaEx = await actCreateExceedSku.Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>();
        skuQuotaEx.Which.Data["ErrorCode"].Should().Be("SUBSCRIPTION_MAX_SKUS_EXCEEDED");
    }

    [Fact]
    public async Task Wave62_1_DoD4_ProvisionedTenantOwner_AuthenticatesViaAuthService_And_SupportImpersonationSwitchesTenantId()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave62_1_DoD4_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        var hasher = new ERP.Infrastructure.Services.PasswordHasherService();
        var permOps = new PermissionOperationsService(db, passwordHasher: hasher);

        var provisionRes = await permOps.ProvisionSaaSTenantAsync(
            new ProvisionTenantInput(
                CompanyName: "Công ty W62",
                TaxCode: "0109998888",
                OwnerEmail: "owner@w62.vn",
                IndustryTemplate: "ECOMMERCE_3ZONES",
                PlanCode: "STARTER",
                AllocateNewTenant: true,
                OwnerUsername: "owner_w62",
                OwnerPassword: "OwnerSecret!2026"),
            default);
        using var provDoc = System.Text.Json.JsonSerializer.SerializeToDocument(provisionRes.Payload);
        var provisionedTenantId = provDoc.RootElement.GetProperty("tenantId").GetInt32();
        provisionedTenantId.Should().BeGreaterThan(1);

        var ownerUser = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.TenantId == provisionedTenantId && u.Username == "owner_w62");
        ownerUser.PasswordHash.Should().NotBe("PROVISIONED_TENANT_OWNER");
        hasher.VerifyPassword(ownerUser.PasswordHash, "OwnerSecret!2026").Should().BeTrue();

        // Also verify full AuthService.LoginAsync succeeds for the provisioned Tenant Owner
        var userRepo = new ERP.Infrastructure.Repositories.UserRepository(db);
        var tokenServiceMock = new Mock<ITokenService>();
        tokenServiceMock.Setup(x => x.GenerateToken(It.IsAny<ERP.Domain.Entities.User>())).Returns("jwt-provisioned-owner");
        var authService = new ERP.Application.Services.AuthService(
            userRepo,
            hasher,
            tokenServiceMock.Object,
            new ERP.Application.Common.AuthSecurityOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 },
            null,
            null,
            null);
        var loginResp = await authService.LoginAsync(
            new ERP.Application.DTOs.LoginRequestDto
            {
                Username = "owner_w62",
                Password = "OwnerSecret!2026",
                TenantId = provisionedTenantId
            },
            new ERP.Application.DTOs.SessionContextDto { IpAddress = "127.0.0.1", UserAgent = "Wave62.1" });
        loginResp.Token.Should().Be("jwt-provisioned-owner");
        loginResp.TenantId.Should().Be(provisionedTenantId);

        // Verify SupportImpersonationMiddleware + HttpTenantContext switches TenantId to session.TenantId
        db.SupportImpersonationSessions.Add(new ERP.Domain.Entities.SupportImpersonationSession
        {
            Id = 9001,
            TenantId = provisionedTenantId,
            SessionCode = "IMP-W62-TOKEN",
            TokenHash = "IMP-W62-TOKEN",
            SupportTicketCode = "SUP-W62-01",
            GrantedByUserId = ownerUser.Id,
            SupportAgentUserId = 1,
            ReadOnlyScope = true,
            Status = "ACTIVE",
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30)
        });
        await db.SaveChangesAsync();

        var sp = new Mock<IServiceProvider>();
        sp.Setup(x => x.GetService(typeof(ERP.Infrastructure.Persistence.ErpKhoDbContext))).Returns(db);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Headers["X-Support-Impersonation-Token"] = "IMP-W62-TOKEN";
        httpContext.RequestServices = sp.Object;
        var accessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext };
        var tenantContext = new ERP.Api.Authorization.HttpTenantContext(accessor);

        var middleware = new ERP.Api.Middleware.SupportImpersonationMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(httpContext);

        tenantContext.TenantId.Should().Be(provisionedTenantId);
        db.CurrentTenantId.Should().Be(provisionedTenantId);

        // Also verify concurrent ProvisionSaaSTenantAsync calls on separate DbContext instances allocate distinct TenantIds
        await using var dbA = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        await using var dbB = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        var permOpsA = new PermissionOperationsService(dbA, passwordHasher: hasher);
        var permOpsB = new PermissionOperationsService(dbB, passwordHasher: hasher);
        var concurrentResults = await Task.WhenAll(
            permOpsA.ProvisionSaaSTenantAsync(new ProvisionTenantInput(CompanyName: "Concurrent A", AllocateNewTenant: true, OwnerUsername: "owner_conc_a", OwnerPassword: "OwnerSecret!2026"), default),
            permOpsB.ProvisionSaaSTenantAsync(new ProvisionTenantInput(CompanyName: "Concurrent B", AllocateNewTenant: true, OwnerUsername: "owner_conc_b", OwnerPassword: "OwnerSecret!2026"), default));
        using var docA = System.Text.Json.JsonSerializer.SerializeToDocument(concurrentResults[0].Payload);
        using var docB = System.Text.Json.JsonSerializer.SerializeToDocument(concurrentResults[1].Payload);
        var tenantIdA = docA.RootElement.GetProperty("tenantId").GetInt32();
        var tenantIdB = docB.RootElement.GetProperty("tenantId").GetInt32();
        tenantIdA.Should().NotBe(tenantIdB);
    }

    [Fact]
    public async Task Wave63_4_SaasPlanCatalogAndProvisioning_AcrossAllTiers_EnforcesConsistentQuotasAndPricing()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave63_4_SaasCatalog_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

        var hasher = new ERP.Infrastructure.Services.PasswordHasherService();
        var permOps = new PermissionOperationsService(db, passwordHasher: hasher);
        var saasBilling = new SaasBillingService(db);

        // 1. Provision ENTERPRISE tenant (AllocateNewTenant = true) -> verifies exact ENTERPRISE quotas (100000 / 20 / 100)
        var entRes = await permOps.ProvisionSaaSTenantAsync(
            new ProvisionTenantInput(
                CompanyName: "Tập Đoàn Enterprise W63.4",
                TaxCode: "0108889999",
                OwnerEmail: "ent@w634.vn",
                IndustryTemplate: "ECOMMERCE_3ZONES",
                PlanCode: "ENTERPRISE",
                AllocateNewTenant: true,
                OwnerUsername: "ent_owner_w634",
                OwnerPassword: "OwnerSecret!2026"),
            default);
        using var entDoc = System.Text.Json.JsonSerializer.SerializeToDocument(entRes.Payload);
        var entTenantId = entDoc.RootElement.GetProperty("tenantId").GetInt32();
        entDoc.RootElement.GetProperty("planCode").GetString().Should().Be("ENTERPRISE");
        entDoc.RootElement.GetProperty("monthlyOrderQuota").GetInt32().Should().Be(100000);
        entDoc.RootElement.GetProperty("maxWarehouses").GetInt32().Should().Be(20);
        entDoc.RootElement.GetProperty("maxUsers").GetInt32().Should().Be(100);

        var entSub = await db.TenantSubscriptions
            .IgnoreQueryFilters()
            .SingleAsync(s => s.TenantId == entTenantId);
        entSub.PlanCode.Should().Be("ENTERPRISE");
        entSub.MonthlyOrderQuota.Should().Be(100000);
        entSub.MaxWarehouses.Should().Be(20);
        entSub.MaxUsers.Should().Be(100);

        // 2. Provision FREE_TRIAL tenant (AllocateNewTenant = true) -> verifies exact FREE_TRIAL quotas (300 / 1 / 3)
        var trialRes = await permOps.ProvisionSaaSTenantAsync(
            new ProvisionTenantInput(
                CompanyName: "Cửa Hàng Dùng Thử W63.4",
                TaxCode: "0107776666",
                OwnerEmail: "trial@w634.vn",
                IndustryTemplate: "ECOMMERCE_3ZONES",
                PlanCode: "FREE_TRIAL",
                AllocateNewTenant: true,
                OwnerUsername: "trial_owner_w634",
                OwnerPassword: "OwnerSecret!2026"),
            default);
        using var trialDoc = System.Text.Json.JsonSerializer.SerializeToDocument(trialRes.Payload);
        var trialTenantId = trialDoc.RootElement.GetProperty("tenantId").GetInt32();
        trialDoc.RootElement.GetProperty("planCode").GetString().Should().Be("FREE_TRIAL");
        trialDoc.RootElement.GetProperty("monthlyOrderQuota").GetInt32().Should().Be(300);
        trialDoc.RootElement.GetProperty("maxWarehouses").GetInt32().Should().Be(1);
        trialDoc.RootElement.GetProperty("maxUsers").GetInt32().Should().Be(3);

        var trialSub = await db.TenantSubscriptions
            .IgnoreQueryFilters()
            .SingleAsync(s => s.TenantId == trialTenantId);
        trialSub.PlanCode.Should().Be("FREE_TRIAL");
        trialSub.MonthlyOrderQuota.Should().Be(300);
        trialSub.MaxWarehouses.Should().Be(1);
        trialSub.MaxUsers.Should().Be(3);

        // 3. Re-provisioning an existing tenant (sub != null) to GROWTH updates quotas in-place (10000 / 3 / 20)
        db.UseTenant(trialTenantId);
        var upgradeRes = await permOps.ProvisionSaaSTenantAsync(
            new ProvisionTenantInput(
                CompanyName: "Cửa Hàng Nâng Cấp Growth W63.4",
                PlanCode: "GROWTH",
                AllocateNewTenant: false),
            default);
        using var upgradeDoc = System.Text.Json.JsonSerializer.SerializeToDocument(upgradeRes.Payload);
        upgradeDoc.RootElement.GetProperty("planCode").GetString().Should().Be("GROWTH");
        upgradeDoc.RootElement.GetProperty("monthlyOrderQuota").GetInt32().Should().Be(10000);
        upgradeDoc.RootElement.GetProperty("maxWarehouses").GetInt32().Should().Be(3);
        upgradeDoc.RootElement.GetProperty("maxUsers").GetInt32().Should().Be(20);

        var upgradedSub = await db.TenantSubscriptions
            .IgnoreQueryFilters()
            .SingleAsync(s => s.TenantId == trialTenantId);
        upgradedSub.PlanCode.Should().Be("GROWTH");
        upgradedSub.MonthlyOrderQuota.Should().Be(10000);
        upgradedSub.MaxWarehouses.Should().Be(3);
        upgradedSub.MaxUsers.Should().Be(20);

        // 4. Verify SaasPlans catalog parity between SaasBillingService and PermissionOperationsService (DB table)
        var plansPayload = await saasBilling.GetPlansAsync(default);
        using var plansDoc = System.Text.Json.JsonSerializer.SerializeToDocument(plansPayload);
        var planItems = plansDoc.RootElement.GetProperty("plans").EnumerateArray().ToList();
        planItems.Should().HaveCountGreaterThanOrEqualTo(4);

        var dbPlans = await db.SaasPlans.AsNoTracking().ToDictionaryAsync(p => p.PlanCode, StringComparer.OrdinalIgnoreCase);
        var expectedSpecs = new (string Code, decimal Price, int Orders, int Warehouses, int Users, int Skus)[]
        {
            ("FREE_TRIAL", 0m, 300, 1, 3, 500),
            ("STARTER", 490000m, 1500, 1, 5, 2000),
            ("GROWTH", 1490000m, 10000, 3, 20, 20000),
            ("PRO", 1490000m, 10000, 3, 20, 20000),
            ("ENTERPRISE", 3490000m, 100000, 20, 100, 100000)
        };

        foreach (var exp in expectedSpecs)
        {
            dbPlans.Should().ContainKey(exp.Code);
            var dbPlan = dbPlans[exp.Code];
            dbPlan.MonthlyPriceVnd.Should().Be(exp.Price);
            dbPlan.MonthlyOrderQuota.Should().Be(exp.Orders);
            dbPlan.MaxWarehouses.Should().Be(exp.Warehouses);
            dbPlan.MaxUsers.Should().Be(exp.Users);

            var resolved = SaasBillingService.ResolvePlanSpec(exp.Code);
            resolved.MonthlyPriceVnd.Should().Be(exp.Price);
            resolved.MonthlyOrderQuota.Should().Be(exp.Orders);
            resolved.MaxWarehouses.Should().Be(exp.Warehouses);
            resolved.MaxUsers.Should().Be(exp.Users);
            resolved.MaxSkus.Should().Be(exp.Skus);

            ERP.Application.Services.ProductService.ResolveMaxSkusForPlan(exp.Code).Should().Be(exp.Skus);
        }
    }

    [Fact]
    public async Task Wave63_5_VietQrWebhook_RestoresOriginalTenantContextAndReleasesGateOnException()
    {
        const string webhookSecret = "W63_5_WEBHOOK_HMAC_SECRET_2026";
        Environment.SetEnvironmentVariable("SAAS_VIETQR_WEBHOOK_SECRET", webhookSecret);

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave63_5_WebhookLock_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        // 1. Verify VietQrWebhookGate is released in finally even when DbContext throws ObjectDisposedException
        var brokenDb = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        await brokenDb.DisposeAsync();

        var brokenSaasService = new SaasBillingService(brokenDb);
        var brokenPermService = new PermissionOperationsService(brokenDb);
        var brokenSig = SaasBillingService.ComputeVietQrSignature(webhookSecret, "INV-BROKEN-635", "ENTERPRISE", 3490000m);

        var actBrokenSaas = async () => await brokenSaasService.HandleVietQrWebhookAsync(
            new SaasVietQrWebhookRequest(InvoiceCode: "INV-BROKEN-635", PlanCode: "ENTERPRISE", PaidAmountVnd: 3490000m),
            actorUserId: 1,
            cancellationToken: default,
            providedSignature: brokenSig);
        await actBrokenSaas.Should().ThrowAsync<ObjectDisposedException>();
        SaasBillingService.VietQrWebhookGate.CurrentCount.Should().Be(1);

        var actBrokenPerm = async () => await brokenPermService.HandleVietQrBillingWebhookAsync(
            new VietQrWebhookInput(InvoiceCode: "INV-BROKEN-635", PlanCode: "ENTERPRISE", PaidAmountVnd: 3490000m),
            providedSignature: brokenSig,
            actorUserId: 1,
            ct: default);
        await actBrokenPerm.Should().ThrowAsync<ObjectDisposedException>();
        SaasBillingService.VietQrWebhookGate.CurrentCount.Should().Be(1);

        // 2. Verify originalTenantId is restored to 1 after processing cross-tenant webhook for Tenant 2 (and on idempotent replay)
        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        db.UseTenant(2);
        db.SaasBillingInvoices.Add(new ERP.Domain.Entities.SaasBillingInvoice
        {
            TenantId = 2,
            InvoiceCode = "INV-W635-SAAS-T2",
            PlanCode = "ENTERPRISE",
            AmountVnd = 3490000m,
            PaymentProvider = "VIETQR",
            Status = "UNPAID",
            QrPayload = "QR-W635-SAAS-T2",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.UseTenant(3);
        db.SaasBillingInvoices.Add(new ERP.Domain.Entities.SaasBillingInvoice
        {
            TenantId = 3,
            InvoiceCode = "INV-W635-PERM-T3",
            PlanCode = "ENTERPRISE",
            AmountVnd = 3490000m,
            PaymentProvider = "VIETQR",
            Status = "UNPAID",
            QrPayload = "QR-W635-PERM-T3",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var saasService = new SaasBillingService(db);
        var permService = new PermissionOperationsService(db);

        // 2a. SaasBillingService.HandleVietQrWebhookAsync for Tenant 2 while db is on Tenant 1
        db.UseTenant(1);
        db.CurrentTenantId.Should().Be(1);
        var sigT2 = SaasBillingService.ComputeVietQrSignature(webhookSecret, "INV-W635-SAAS-T2", "ENTERPRISE", 3490000m);

        var firstSaasRes = await saasService.HandleVietQrWebhookAsync(
            new SaasVietQrWebhookRequest(InvoiceCode: "INV-W635-SAAS-T2", PlanCode: "ENTERPRISE", PaidAmountVnd: 3490000m),
            actorUserId: 1,
            cancellationToken: default,
            providedSignature: sigT2);
        firstSaasRes.InvalidReason.Should().BeFalse();
        db.CurrentTenantId.Should().Be(1);

        var invT2 = await db.SaasBillingInvoices.IgnoreQueryFilters().SingleAsync(i => i.InvoiceCode == "INV-W635-SAAS-T2");
        invT2.Status.Should().Be("PAID");
        var subT2 = await db.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == 2);
        subT2.PlanCode.Should().Be("ENTERPRISE");
        subT2.MonthlyOrderQuota.Should().Be(100000);
        subT2.MaxWarehouses.Should().Be(20);
        subT2.MaxUsers.Should().Be(100);

        var replaySaasRes = await saasService.HandleVietQrWebhookAsync(
            new SaasVietQrWebhookRequest(InvoiceCode: "INV-W635-SAAS-T2", PlanCode: "ENTERPRISE", PaidAmountVnd: 3490000m),
            actorUserId: 1,
            cancellationToken: default,
            providedSignature: sigT2);
        using var replaySaasDoc = System.Text.Json.JsonSerializer.SerializeToDocument(replaySaasRes.Payload);
        replaySaasDoc.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeTrue();
        db.CurrentTenantId.Should().Be(1);

        // 2b. PermissionOperationsService.HandleVietQrBillingWebhookAsync for Tenant 3 while db is on Tenant 1
        db.UseTenant(1);
        var sigT3 = SaasBillingService.ComputeVietQrSignature(webhookSecret, "INV-W635-PERM-T3", "ENTERPRISE", 3490000m);

        var firstPermRes = await permService.HandleVietQrBillingWebhookAsync(
            new VietQrWebhookInput(InvoiceCode: "INV-W635-PERM-T3", PlanCode: "ENTERPRISE", PaidAmountVnd: 3490000m),
            providedSignature: sigT3,
            actorUserId: 1,
            ct: default);
        using var firstPermDoc = System.Text.Json.JsonSerializer.SerializeToDocument(firstPermRes.Payload);
        firstPermDoc.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeFalse();
        db.CurrentTenantId.Should().Be(1);

        var invT3 = await db.SaasBillingInvoices.IgnoreQueryFilters().SingleAsync(i => i.InvoiceCode == "INV-W635-PERM-T3");
        invT3.Status.Should().Be("PAID");
        var subT3 = await db.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == 3);
        subT3.PlanCode.Should().Be("ENTERPRISE");
        subT3.MonthlyOrderQuota.Should().Be(100000);
        subT3.MaxWarehouses.Should().Be(20);
        subT3.MaxUsers.Should().Be(100);

        var replayPermRes = await permService.HandleVietQrBillingWebhookAsync(
            new VietQrWebhookInput(InvoiceCode: "INV-W635-PERM-T3", PlanCode: "ENTERPRISE", PaidAmountVnd: 3490000m),
            providedSignature: sigT3,
            actorUserId: 1,
            ct: default);
        using var replayPermDoc = System.Text.Json.JsonSerializer.SerializeToDocument(replayPermRes.Payload);
        replayPermDoc.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeTrue();
        db.CurrentTenantId.Should().Be(1);
    }

    [Fact]
    public async Task Wave63_7_CreateVietQrInvoice_UsesConfiguredCorporateBankAcrossServices()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave63_7_VietQrTpBank_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        var saasService = new SaasBillingService(db);
        var permService = new PermissionOperationsService(db);

        // 1. Verify SaasBillingService.CreateVietQrInvoiceAsync
        var saasRes = await saasService.CreateVietQrInvoiceAsync(
            new SaasCreateVietQrInvoiceRequest(PlanCode: "GROWTH", CustomAmountVnd: null, AmountVnd: 1490000m),
            actorUserId: 1,
            cancellationToken: default);

        using var saasDoc = System.Text.Json.JsonSerializer.SerializeToDocument(saasRes);
        var saasRoot = saasDoc.RootElement;
        var saasInvoiceCode = saasRoot.GetProperty("invoiceCode").GetString();
        var saasPayload = saasRoot.GetProperty("vietQrPayload").GetString();

        saasRoot.GetProperty("bankCode").GetString().Should().Be("TPB");
        saasRoot.GetProperty("bankName").GetString().Should().Be("TPBank");
        saasRoot.GetProperty("bankBin").GetString().Should().Be("970423");
        saasRoot.GetProperty("accountNo").GetString().Should().Be("000010002026");
        saasPayload.Should().Contain("0006970423");
        saasPayload.Should().Contain("000010002026");
        saasPayload.Should().NotContain("98418102005");
        saasPayload.Should().NotContain("1903888999999");

        var dynamicCustomPayload = SaasBillingService.BuildVietQrPayload(1490000m, "INV-VQR-TEST", "970423", "1234567890123");
        dynamicCustomPayload.Should().Contain("38570010A00000072701270006970423011312345678901230208QRIBFTTA");

        var savedSaasInvoice = await db.SaasBillingInvoices
            .IgnoreQueryFilters()
            .SingleAsync(i => i.InvoiceCode == saasInvoiceCode);
        savedSaasInvoice.QrPayload.Should().Contain("000010002026");
        savedSaasInvoice.QrPayload.Should().Contain("0006970423");
        savedSaasInvoice.QrPayload.Should().NotContain("98418102005");

        // 2. Verify PermissionOperationsService.CreateVietQrInvoiceAsync
        var permOutcome = await permService.CreateVietQrInvoiceAsync(
            new CreateVietQrInvoiceInput(PlanCode: "ENTERPRISE", AmountVnd: 3490000m),
            ct: default);

        using var permDoc = System.Text.Json.JsonSerializer.SerializeToDocument(permOutcome.Payload);
        var permRoot = permDoc.RootElement;
        var permInvoiceCode = permRoot.GetProperty("invoiceCode").GetString();
        var permPayload = permRoot.GetProperty("vietQrPayload").GetString();

        permRoot.GetProperty("bankCode").GetString().Should().Be("TPB");
        permRoot.GetProperty("bankName").GetString().Should().Be("TPBank");
        permRoot.GetProperty("bankBin").GetString().Should().Be("970423");
        permRoot.GetProperty("accountNo").GetString().Should().Be("000010002026");
        permPayload.Should().Contain("0006970423");
        permPayload.Should().Contain("000010002026");
        permPayload.Should().NotContain("98418102005");

        var savedPermInvoice = await db.SaasBillingInvoices
            .IgnoreQueryFilters()
            .SingleAsync(i => i.InvoiceCode == permInvoiceCode);
        savedPermInvoice.QrPayload.Should().Contain("000010002026");
        savedPermInvoice.QrPayload.Should().Contain("0006970423");
        savedPermInvoice.QrPayload.Should().NotContain("98418102005");
    }

    [Fact]
    public async Task Wave64_0_VietQrIdempotentInvoice_AlphanumericTransferContent_And_DynamicTotpSecret()
    {
        const string webhookSecret = "W64_0_WEBHOOK_HMAC_SECRET_2026";
        Environment.SetEnvironmentVariable("SAAS_VIETQR_WEBHOOK_SECRET", webhookSecret);

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
            .UseInMemoryDatabase($"Wave64_0_IdempotentAndTotp_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
        db.UseTenant(1);
        db.Users.Add(new ERP.Domain.Entities.User
        {
            Id = 1,
            TenantId = 1,
            Username = "qa_admin",
            FullName = "QA Admin",
            PasswordHash = "HASH",
            RoleId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var saasService = new SaasBillingService(db);
        var permService = new PermissionOperationsService(db);

        // 1. Idempotent pending invoice reuse in SaasBillingService + alphanumeric transferContent without '-'
        var firstInvoice = await saasService.CreateVietQrInvoiceAsync(
            new SaasCreateVietQrInvoiceRequest(PlanCode: "GROWTH", CustomAmountVnd: null, AmountVnd: 1490000m),
            actorUserId: 1,
            cancellationToken: default);
        using var firstDoc = System.Text.Json.JsonSerializer.SerializeToDocument(firstInvoice);
        var firstCode = firstDoc.RootElement.GetProperty("invoiceCode").GetString()!;
        var firstTransferContent = firstDoc.RootElement.GetProperty("transferContent").GetString()!;
        var firstQrPayload = firstDoc.RootElement.GetProperty("vietQrPayload").GetString()!;

        firstDoc.RootElement.GetProperty("reusedPendingInvoice").GetBoolean().Should().BeFalse();
        firstDoc.RootElement.GetProperty("accountName").GetString().Should().Be("ERP KHO WMS SAAS");
        firstTransferContent.Should().NotContain("-");
        firstTransferContent.Should().NotContain(" ");
        firstTransferContent.Should().StartWith("ERPKHO");
        var cleanRef = SaasBillingService.NormalizeVietQrReference(firstCode);
        cleanRef.Should().NotContain("-");
        firstTransferContent.Should().Be($"ERPKHO{cleanRef}");
        firstQrPayload.Should().Contain(cleanRef);

        var secondInvoice = await saasService.CreateVietQrInvoiceAsync(
            new SaasCreateVietQrInvoiceRequest(PlanCode: "GROWTH", CustomAmountVnd: null, AmountVnd: 1490000m),
            actorUserId: 1,
            cancellationToken: default);
        using var secondDoc = System.Text.Json.JsonSerializer.SerializeToDocument(secondInvoice);
        secondDoc.RootElement.GetProperty("invoiceCode").GetString().Should().Be(firstCode);
        secondDoc.RootElement.GetProperty("reusedPendingInvoice").GetBoolean().Should().BeTrue();
        secondDoc.RootElement.GetProperty("transferContent").GetString().Should().Be(firstTransferContent);

        var growthInvoicesCount = await db.SaasBillingInvoices
            .CountAsync(i => i.TenantId == 1 && i.PlanCode == "GROWTH" && i.Status == "UNPAID");
        growthInvoicesCount.Should().Be(1);

        // 2. Webhook matches normalized alphanumeric reference (without '-') and activates subscription
        var sigNoHyphen = SaasBillingService.ComputeVietQrSignature(webhookSecret, cleanRef, "GROWTH", 1490000m);
        var webhookRes = await saasService.HandleVietQrWebhookAsync(
            new SaasVietQrWebhookRequest(InvoiceCode: cleanRef, PlanCode: "GROWTH", PaidAmountVnd: 1490000m),
            actorUserId: 1,
            cancellationToken: default,
            providedSignature: sigNoHyphen);
        webhookRes.InvalidReason.Should().BeFalse();

        var paidInvoice = await db.SaasBillingInvoices.SingleAsync(i => i.InvoiceCode == firstCode);
        paidInvoice.Status.Should().Be("PAID");

        // 3. Dynamic cryptographic TOTP secret persisted to DB (20-byte random Base32, never static sample)
        var secController = new ERP.Api.Controllers.AccountSecurityController(
            new Mock<IAccountAdminService>().Object,
            new Mock<IUserSessionService>().Object,
            new WmsGovernanceOperationsService(db),
            Mock.Of<IPasswordHasherService>());
        var setupRes = await secController.SetupTotp(1, default);
        var setupOk = setupRes.Should().BeOfType<OkObjectResult>().Subject;
        using var enrollDoc = System.Text.Json.JsonSerializer.SerializeToDocument(setupOk.Value);
        enrollDoc.RootElement.TryGetProperty("secret", out _).Should().BeFalse();
        enrollDoc.RootElement.TryGetProperty("base32Secret", out _).Should().BeFalse();
        enrollDoc.RootElement.TryGetProperty("secretKeyBase32", out _).Should().BeFalse();
        enrollDoc.RootElement.GetProperty("recoveryCodes").GetArrayLength().Should().Be(0);
        enrollDoc.RootElement.TryGetProperty("currentTotpCode", out _).Should().BeFalse();

        var revealRes = await secController.RevealTotpSecret(1);
        var revealOk = revealRes.Should().BeOfType<OkObjectResult>().Subject;
        using var revealDoc = System.Text.Json.JsonSerializer.SerializeToDocument(revealOk.Value);
        var enrolledSecret = revealDoc.RootElement.GetProperty("secretKeyBase32").GetString()!;
        enrolledSecret.Length.Should().Be(32);

        var currentOtp = ERP.Api.Controllers.AccountSecurityController.ComputeTotpCode(
            ERP.Api.Controllers.AccountSecurityController.DecodeBase32(enrolledSecret),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var wrongOtp = currentOtp == "000000" ? "999999" : "000000";

        var invalidVerifyRes = await secController.VerifyTotp(
            1,
            new ERP.Api.Controllers.AccountSecurityController.VerifyTotpRequest(Code: wrongOtp),
            default);
        var badReq = invalidVerifyRes.Should().BeOfType<BadRequestObjectResult>().Subject;
        using var invalidDoc = System.Text.Json.JsonSerializer.SerializeToDocument(badReq.Value);
        invalidDoc.RootElement.GetProperty("verified").GetBoolean().Should().BeFalse();
        invalidDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("INVALID_TOTP_CODE");

        var verifyRes = await secController.VerifyTotp(
            1,
            new ERP.Api.Controllers.AccountSecurityController.VerifyTotpRequest(Code: currentOtp),
            default);
        var verifyOk = verifyRes.Should().BeOfType<OkObjectResult>().Subject;
        using var verifyDoc = System.Text.Json.JsonSerializer.SerializeToDocument(verifyOk.Value);
        verifyDoc.RootElement.GetProperty("verified").GetBoolean().Should().BeTrue();
        verifyDoc.RootElement.GetProperty("status").GetString().Should().Be("PENDING_RECOVERY_ACK");
        verifyDoc.RootElement.GetProperty("recoveryCodes").GetArrayLength().Should().Be(8);
        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == 1)).TotpEnrollmentStatus.Should().Be("PENDING_RECOVERY_ACK");

        var confirmRes = await secController.ConfirmTotpActivation(1, default);
        var confirmOk = confirmRes.Should().BeOfType<OkObjectResult>().Subject;
        using var confirmDoc = System.Text.Json.JsonSerializer.SerializeToDocument(confirmOk.Value);
        confirmDoc.RootElement.GetProperty("status").GetString().Should().Be("ACTIVE");
        (await db.Users.AsNoTracking().SingleAsync(u => u.Id == 1)).TotpEnrollmentStatus.Should().Be("ACTIVE");

        var regenRes = await secController.RegenerateRecoveryCodes(
            1,
            new ERP.Api.Controllers.AccountSecurityController.VerifyTotpRequest(Code: currentOtp),
            default);
        var regenOk = regenRes.Should().BeOfType<OkObjectResult>().Subject;
        using var regenDoc = System.Text.Json.JsonSerializer.SerializeToDocument(regenOk.Value);
        regenDoc.RootElement.GetProperty("recoveryCodes").GetArrayLength().Should().Be(8);

        // 4. Downgrade guard in SaasBillingService & PermissionOperationsService when usage exceeds target plan ceiling
        db.Warehouses.AddRange(
            new ERP.Domain.Entities.Warehouse { Id = 11, TenantId = 1, Code = "WH-01", Name = "Kho 1", IsActive = true },
            new ERP.Domain.Entities.Warehouse { Id = 12, TenantId = 1, Code = "WH-02", Name = "Kho 2", IsActive = true },
            new ERP.Domain.Entities.Warehouse { Id = 13, TenantId = 1, Code = "WH-03", Name = "Kho 3", IsActive = true });
        await db.SaveChangesAsync();

        var downgradeAct = () => saasService.CreateVietQrInvoiceAsync(
            new SaasCreateVietQrInvoiceRequest(PlanCode: "STARTER", CustomAmountVnd: null, AmountVnd: 490000m),
            actorUserId: 1,
            cancellationToken: default);
        await downgradeAct.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>()
            .Where(ex => Equals(ex.Data["ErrorCode"], "PLAN_DOWNGRADE_USAGE_EXCEEDED"));

        var permDowngradeAct = () => permService.CreateVietQrInvoiceAsync(
            new CreateVietQrInvoiceInput(PlanCode: "STARTER", AmountVnd: 490000m),
            ct: default);
        await permDowngradeAct.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>()
            .Where(ex => Equals(ex.Data["ErrorCode"], "PLAN_DOWNGRADE_USAGE_EXCEEDED"));
    }
}
