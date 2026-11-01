using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public class Wave04ReplenishmentKitAndCodEvidenceTests
{
    private static DbContextOptions<ErpKhoDbContext> CreateInMemoryOptions(string dbName) =>
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static ControllerContext CreateControllerContextWithPermissions(int userId, string role, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("Id", userId.ToString()),
            new(ClaimTypes.Role, role)
        };
        foreach (var perm in permissions)
        {
            claims.Add(new Claim("permissions", perm));
            claims.Add(new Claim("permission", perm));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        return new ControllerContext { HttpContext = httpContext };
    }

    private static Mock<ICurrentUser> CreateMockCurrentUser(int userId, string role, bool isGlobalAdmin, params string[] permissions)
    {
        var permSet = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
        var mock = new Mock<ICurrentUser>();
        mock.SetupGet(x => x.UserId).Returns(userId);
        mock.SetupGet(x => x.TenantId).Returns(1);
        mock.SetupGet(x => x.IsAuthenticated).Returns(true);
        mock.SetupGet(x => x.IsGlobalAdmin).Returns(isGlobalAdmin);
        mock.SetupGet(x => x.Role).Returns(role);
        mock.Setup(x => x.HasPermission(It.IsAny<string>()))
            .Returns((string code) => isGlobalAdmin || permSet.Contains(code));
        return mock;
    }

    [Fact]
    public async Task TC_W4_01_PickFaceMinMaxReplenishment_PrioritizesFefoSourceLots_ValidatesMinMaxRules_AndMovesStockWithGrainHash()
    {
        var dbName = $"{nameof(TC_W4_01_PickFaceMinMaxReplenishment_PrioritizesFefoSourceLots_ValidatesMinMaxRules_AndMovesStockWithGrainHash)}_{Guid.NewGuid():N}";
        var options = CreateInMemoryOptions(dbName);
        await using var db = new ErpKhoDbContext(options);

        var now = DateTime.UtcNow;
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-REPL-01", Name = "Kho Bổ Sung Quầy Nhặt", IsActive = true });
        db.Units.Add(new Unit { Id = 1, Code = "PCS", Name = "Cái", IsActive = true });
        db.Products.Add(new Product
        {
            Id = 10,
            Code = "SKU-FEFO-10",
            Name = "Sữa Tươi Tiệt Trùng FEFO",
            UnitId = 1,
            TrackingType = ProductTrackingType.Lot,
            ExpiryControl = true,
            IsActive = true
        });

        var bulkLoc = new WarehouseLocation
        {
            Id = 101,
            WarehouseId = 1,
            Code = "BULK-A01",
            Name = "Kệ Cao Tầng Bulk A01",
            LocationType = WarehouseLocationType.Storage,
            StorageClass = "RESERVE",
            IsPickable = true,
            IsReceivable = true,
            IsActive = true
        };
        var pickLoc = new WarehouseLocation
        {
            Id = 102,
            WarehouseId = 1,
            Code = "PICK-P01",
            Name = "Ô Nhặt Lẻ Pick P01",
            LocationType = WarehouseLocationType.Staging,
            StorageClass = "PICK",
            IsPickable = true,
            IsReceivable = true,
            IsActive = true
        };
        db.WarehouseLocations.AddRange(bulkLoc, pickLoc);

        var lotEarly = new InventoryLot
        {
            Id = 501,
            ProductId = 10,
            LotNumber = "LOT-EARLY-15D",
            ManufactureDate = now.Date.AddDays(-60),
            ExpiryDate = now.Date.AddDays(15)
        };
        var lotLate = new InventoryLot
        {
            Id = 502,
            ProductId = 10,
            LotNumber = "LOT-LATE-90D",
            ManufactureDate = now.Date.AddDays(-10),
            ExpiryDate = now.Date.AddDays(90)
        };
        db.InventoryLots.AddRange(lotEarly, lotLate);

        var stockPick = new InventoryStock
        {
            Id = 1001,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 10,
            LocationId = 102,
            LotId = 501,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            ReservedQuantity = 0m
        };
        stockPick.RefreshGrainHash();

        var stockBulkLate = new InventoryStock
        {
            Id = 1002,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 10,
            LocationId = 101,
            LotId = 502,
            Status = InventoryStatus.Available,
            Quantity = 50m,
            ReservedQuantity = 0m
        };
        stockBulkLate.RefreshGrainHash();

        var stockBulkEarly = new InventoryStock
        {
            Id = 1003,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 10,
            LocationId = 101,
            LotId = 501,
            Status = InventoryStatus.Available,
            Quantity = 50m,
            ReservedQuantity = 0m
        };
        stockBulkEarly.RefreshGrainHash();

        db.InventoryStocks.AddRange(stockPick, stockBulkLate, stockBulkEarly);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controlOps = new InventoryControlOperationsService(db, whAuth.Object);
        var ctrl = new InventoryControlController(
            Mock.Of<IInventoryLockService>(),
            Mock.Of<IInventoryMovementService>(),
            controlOps,
            whAuth.Object)
        {
            ControllerContext = CreateControllerContextWithPermissions(
                1,
                AppRoles.Admin,
                AppPermissions.InventoryRead,
                AppPermissions.InventoryMovementCreate,
                AppPermissions.InventoryLockManage)
        };

        // 1. Negative validation: MaxBaseQty <= MinBaseQty must return 422 UnprocessableEntity
        var invalidRuleRes = await ctrl.UpsertLocationReplenishmentRule(
            102,
            new UpsertLocationReplenishmentRuleInput(WarehouseId: 1, LocationId: 102, MinBaseQty: 80m, MaxBaseQty: 50m, ReplenishFromLocationId: 101),
            controlOps,
            whAuth.Object,
            default);
        invalidRuleRes.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(422);

        // 2. Valid Min/Max rule configuration: Min = 20, Max = 100, ReplenishFromLocationId = 101 (BULK-A01)
        var validRuleRes = await ctrl.UpsertLocationReplenishmentRule(
            102,
            new UpsertLocationReplenishmentRuleInput(WarehouseId: 1, LocationId: 102, MinBaseQty: 20m, MaxBaseQty: 100m, ReplenishFromLocationId: 101),
            controlOps,
            whAuth.Object,
            default);
        validRuleRes.Should().BeOfType<OkObjectResult>();

        var updatedPickLoc = await db.WarehouseLocations.AsNoTracking().SingleAsync(l => l.Id == 102);
        updatedPickLoc.MinBaseQty.Should().Be(20m);
        updatedPickLoc.MaxBaseQty.Should().Be(100m);
        updatedPickLoc.ReplenishFromLocationId.Should().Be(101);

        // 3. Query replenishment suggestions -> Pick Bin has 10 <= Min(20), needs 90 to reach Max(100), prioritized by FEFO (LOT-EARLY-15D first)
        var suggestRes = await ctrl.ReplenishmentSuggestions(controlOps, whAuth.Object, warehouseId: 1, default);
        using var suggestDoc = JsonSerializer.SerializeToDocument(suggestRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var suggestions = suggestDoc.RootElement.EnumerateArray().ToList();
        suggestions.Should().NotBeEmpty();

        var pickSuggestion = suggestions.First(x => x.GetProperty("toLocationId").GetInt32() == 102);
        pickSuggestion.GetProperty("minBaseQty").GetDecimal().Should().Be(20m);
        pickSuggestion.GetProperty("maxBaseQty").GetDecimal().Should().Be(100m);
        pickSuggestion.GetProperty("currentPickFaceQty").GetDecimal().Should().Be(10m);
        pickSuggestion.GetProperty("suggestedQuantity").GetDecimal().Should().Be(90m);
        pickSuggestion.GetProperty("sourceLotNumber").GetString().Should().Be("LOT-EARLY-15D");
        pickSuggestion.GetProperty("fefoRank").GetInt32().Should().Be(1);
        pickSuggestion.GetProperty("triggerReason").GetString().Should().Be("PICK_FACE_BELOW_MIN_FEFO");

        // 4. Execute Min/Max replenishment for 90 PCS -> consumes all 50 PCS from LOT-EARLY-15D first, then 40 PCS from LOT-LATE-90D
        var execRes = await ctrl.ExecuteReplenishment(
            new InventoryControlController.ExecuteReplenishmentRequest(
                WarehouseId: 1,
                ProductId: 10,
                FromLocationId: 101,
                ToLocationId: 102,
                Quantity: 90m,
                ExecuteStockMutation: true),
            controlOps,
            whAuth.Object,
            default);
        using var execDoc = JsonSerializer.SerializeToDocument(execRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        execDoc.RootElement.GetProperty("movedQuantity").GetDecimal().Should().Be(90m);
        execDoc.RootElement.GetProperty("sourceLotNumber").GetString().Should().Be("LOT-EARLY-15D");

        // Verify Bulk early lot is completely depleted (0 PCS), Bulk late lot has 10 PCS remaining, and Pick Bin total is 100 PCS (MaxBaseQty)
        var bulkEarlyAfter = await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.Id == 1003);
        var bulkLateAfter = await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.Id == 1002);
        bulkEarlyAfter.Quantity.Should().Be(0m);
        bulkLateAfter.Quantity.Should().Be(10m);

        var pickStocksAfter = await db.InventoryStocks.AsNoTracking().Where(s => s.LocationId == 102 && s.ProductId == 10).ToListAsync();
        pickStocksAfter.Sum(s => s.Quantity).Should().Be(100m);
        foreach (var ps in pickStocksAfter)
        {
            ps.GrainHash.Should().Equal(InventoryStock.ComputeCanonicalGrainHash(
                ps.TenantId, ps.WarehouseId, ps.LocationId, ps.ProductId, ps.LotId, ps.SerialId, ps.HandlingUnitId, ps.Status, ps.OwnerId));
        }

        // Verify paired InventoryTransactions (Move out of Bulk + Move into Pick, algebraic sum = 0)
        var moveTx = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.WarehouseId == 1 && t.ProductId == 10 && t.TransactionType == TransactionType.Move)
            .ToListAsync();
        moveTx.Should().HaveCount(4); // 2 lots * 2 legs (-50/+50 and -40/+40)
        moveTx.Sum(t => t.Quantity).Should().Be(0m);
    }

    [Fact]
    public async Task TC_W4_02_KitWorkOrderAssemble_InheritsEarliestFefoExpiry_RollsUpComponentMac_MasksCostByRbac_AndReversesOnCancel()
    {
        var dbName = $"{nameof(TC_W4_02_KitWorkOrderAssemble_InheritsEarliestFefoExpiry_RollsUpComponentMac_MasksCostByRbac_AndReversesOnCancel)}_{Guid.NewGuid():N}";
        var options = CreateInMemoryOptions(dbName);
        await using var db = new ErpKhoDbContext(options);

        var now = DateTime.UtcNow;
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-KIT-01", Name = "Kho Đóng Gói Kit/VAS", IsActive = true });
        db.Units.Add(new Unit { Id = 1, Code = "BO", Name = "Bộ", IsActive = true });
        db.WarehouseLocations.Add(new WarehouseLocation
        {
            Id = 201,
            WarehouseId = 1,
            Code = "VAS-STAGE-01",
            Name = "Bàn Đóng Gói Kit VAS 01",
            LocationType = WarehouseLocationType.Storage,
            IsPickable = true,
            IsReceivable = true,
            IsActive = true
        });

        var kitProduct = new Product
        {
            Id = 20,
            Code = "KIT-TET-2026",
            Name = "Giỏ Quà Tết Sum Vầy 2026",
            UnitId = 1,
            MovingAverageCost = 0m,
            IsActive = true
        };
        var compA = new Product
        {
            Id = 21,
            Code = "COMP-TEA-21",
            Name = "Trà Ô Long Thượng Hạng",
            UnitId = 1,
            MovingAverageCost = 10_000m,
            IsActive = true
        };
        var compB = new Product
        {
            Id = 22,
            Code = "COMP-COOKIE-22",
            Name = "Bánh Quy Bơ Đan Mạch",
            UnitId = 1,
            MovingAverageCost = 25_000m,
            IsActive = true
        };
        db.Products.AddRange(kitProduct, compA, compB);

        // Component BOM: 1 Kit = 2 * COMP-TEA-21 (2 * 10,000 = 20,000) + 1 * COMP-COOKIE-22 (1 * 25,000 = 25,000) => Rolled-up Unit Cost = 45,000 VND
        db.ProductBundleComponents.AddRange(
            new ProductBundleComponent { Id = 1, ParentProductId = 20, ComponentProductId = 21, ComponentQuantity = 2m, IsActive = true },
            new ProductBundleComponent { Id = 2, ParentProductId = 20, ComponentProductId = 22, ComponentQuantity = 1m, IsActive = true });

        var earliestExpiry = now.Date.AddDays(20);
        var laterExpiry = now.Date.AddDays(75);
        var lotCompA = new InventoryLot { Id = 601, ProductId = 21, LotNumber = "LOT-TEA-20D", ExpiryDate = earliestExpiry };
        var lotCompB = new InventoryLot { Id = 602, ProductId = 22, LotNumber = "LOT-COOKIE-75D", ExpiryDate = laterExpiry };
        db.InventoryLots.AddRange(lotCompA, lotCompB);

        var stockCompA = new InventoryStock
        {
            Id = 2001,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 21,
            LocationId = 201,
            LotId = 601,
            Status = InventoryStatus.Available,
            Quantity = 20m
        };
        stockCompA.RefreshGrainHash();

        var stockCompB = new InventoryStock
        {
            Id = 2002,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 22,
            LocationId = 201,
            LotId = 602,
            Status = InventoryStatus.Available,
            Quantity = 10m
        };
        stockCompB.RefreshGrainHash();

        db.InventoryStocks.AddRange(stockCompA, stockCompB);
        await db.SaveChangesAsync();

        // User WITH inventory.cost.read
        var costReaderUser = CreateMockCurrentUser(
            userId: 1,
            role: AppRoles.Admin,
            isGlobalAdmin: true,
            AppPermissions.ProductRead,
            AppPermissions.ProductUpdate,
            AppPermissions.InventoryRead,
            AppPermissions.InventoryMovementCreate,
            AppPermissions.InventoryCostRead);

        var productOpsWithCost = new ProductOperationsService(db, costReaderUser.Object);
        var ctrlWithCost = new ProductsController(
            Mock.Of<IProductService>(),
            Mock.Of<IProductCatalogService>(),
            productOpsWithCost,
            costReaderUser.Object)
        {
            ControllerContext = CreateControllerContextWithPermissions(
                1,
                AppRoles.Admin,
                AppPermissions.ProductRead,
                AppPermissions.ProductUpdate,
                AppPermissions.InventoryRead,
                AppPermissions.InventoryMovementCreate,
                AppPermissions.InventoryCostRead)
        };

        // 1. Create & Auto-Complete KitWorkOrder for 5 Kits
        var createRes = await ctrlWithCost.CreateKitWorkOrder(
            new CreateKitWorkOrderInput(
                WarehouseId: 1,
                KitProductId: 20,
                TargetLocationId: 201,
                WorkOrderType: "Assemble",
                PlannedKitQty: 5m,
                Note: "Đóng gói 5 bộ quà Tết",
                AutoComplete: true),
            default);
        using var createDoc = JsonSerializer.SerializeToDocument(createRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var workOrderId = createDoc.RootElement.GetProperty("id").GetInt32();
        createDoc.RootElement.GetProperty("status").GetString().Should().Be("Completed");
        createDoc.RootElement.GetProperty("completedKitQty").GetDecimal().Should().Be(5m);
        createDoc.RootElement.GetProperty("baseUomCode").GetString().Should().Be("BO");
        createDoc.RootElement.GetProperty("conversionRateSnapshot").GetDecimal().Should().Be(1m);
        createDoc.RootElement.GetProperty("earliestComponentExpiryDate").GetDateTime().Date.Should().Be(earliestExpiry.Date);
        createDoc.RootElement.GetProperty("rolledUpUnitCost").GetDecimal().Should().Be(45_000m);

        // Verify component stocks decreased (20 - 10 = 10 for CompA; 10 - 5 = 5 for CompB) and Kit stock increased to 5 with inherited expiry lot
        (await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.Id == 2001)).Quantity.Should().Be(10m);
        (await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.Id == 2002)).Quantity.Should().Be(5m);

        var kitStock = await db.InventoryStocks.AsNoTracking()
            .Include(s => s.Lot)
            .SingleAsync(s => s.WarehouseId == 1 && s.ProductId == 20 && s.LocationId == 201);
        kitStock.Quantity.Should().Be(5m);
        kitStock.Lot.Should().NotBeNull();
        kitStock.Lot!.ExpiryDate.Should().Be(earliestExpiry.Date);

        // 2. RBAC Cost Masking: User WITHOUT inventory.cost.read must see rolledUpUnitCost == null and costHiddenByRbac == true
        var noCostUser = CreateMockCurrentUser(
            userId: 2,
            role: AppRoles.WarehouseStaff,
            isGlobalAdmin: false,
            AppPermissions.ProductRead,
            AppPermissions.InventoryRead);

        var productOpsNoCost = new ProductOperationsService(db, noCostUser.Object);
        var ctrlNoCost = new ProductsController(
            Mock.Of<IProductService>(),
            Mock.Of<IProductCatalogService>(),
            productOpsNoCost,
            noCostUser.Object)
        {
            ControllerContext = CreateControllerContextWithPermissions(
                2,
                AppRoles.WarehouseStaff,
                AppPermissions.ProductRead,
                AppPermissions.InventoryRead)
        };

        var listMaskedRes = await ctrlNoCost.GetKitWorkOrders(warehouseId: 1, kitProductId: 20, status: null, default);
        using var listMaskedDoc = JsonSerializer.SerializeToDocument(listMaskedRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        listMaskedDoc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeTrue();
        var maskedItem = listMaskedDoc.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == workOrderId);
        maskedItem.GetProperty("rolledUpUnitCost").ValueKind.Should().Be(JsonValueKind.Null);
        foreach (var compLine in maskedItem.GetProperty("componentLines").EnumerateArray())
        {
            compLine.GetProperty("componentUnitCost").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // 3. Cancel completed KitWorkOrder -> reverses component consumption and parent Kit stock (Δ = 0)
        var cancelRes = await ctrlWithCost.CancelKitWorkOrder(
            workOrderId,
            new CancelKitWorkOrderInput("Khách hủy đơn đặt bộ quà"),
            default);
        using var cancelDoc = JsonSerializer.SerializeToDocument(cancelRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        cancelDoc.RootElement.GetProperty("status").GetString().Should().Be("Cancelled");
        cancelDoc.RootElement.GetProperty("reversedTransactionsCount").GetInt32().Should().BeGreaterThan(0);

        (await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.Id == 2001)).Quantity.Should().Be(20m);
        (await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.Id == 2002)).Quantity.Should().Be(10m);
        (await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.WarehouseId == 1 && s.ProductId == 20)).Quantity.Should().Be(0m);

        var allKitTx = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.WarehouseId == 1)
            .ToListAsync();
        allKitTx.Where(t => t.ProductId == 20).Sum(t => t.Quantity).Should().Be(0m);
        allKitTx.Where(t => t.ProductId == 21).Sum(t => t.Quantity).Should().Be(0m);
        allKitTx.Where(t => t.ProductId == 22).Sum(t => t.Quantity).Should().Be(0m);
    }

    [Fact]
    public async Task TC_W4_03_CodAndShippingReconciliation_LinksInternalPackingScaleWeight_AndCctvVideoEvidenceDossier()
    {
        var dbName = $"{nameof(TC_W4_03_CodAndShippingReconciliation_LinksInternalPackingScaleWeight_AndCctvVideoEvidenceDossier)}_{Guid.NewGuid():N}";
        var options = CreateInMemoryOptions(dbName);
        await using var db = new ErpKhoDbContext(options);

        var now = DateTime.UtcNow;
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-COD-01", Name = "Kho Đối Soát COD", IsActive = true });
        db.Units.Add(new Unit { Id = 1, Code = "PCS", Name = "Cái", IsActive = true });
        db.Products.Add(new Product
        {
            Id = 31,
            Code = "SKU-COD-31",
            Name = "Tai Nghe Bluetooth Chống Ồn",
            UnitId = 1,
            UnitWeightKg = 1.25m,
            UnitVolumeM3 = 0.004m,
            IsActive = true
        });

        var exportReceipt = new ExportReceipt
        {
            Id = 301,
            Code = "XK-COD-301",
            WarehouseId = 1,
            Status = ReceiptStatus.Dispatched,
            CarrierCode = "GHN",
            TrackingNumber = "GHN-DISPUTE-301",
            CreatedBy = 1,
            CreatedAt = now.AddHours(-5),
            Details = new List<ExportReceiptDetail>
            {
                new()
                {
                    Id = 3011,
                    ExportReceiptId = 301,
                    ProductId = 31,
                    Quantity = 1m,
                    UnitPrice = 500_000m
                }
            }
        };
        db.ExportReceipts.Add(exportReceipt);

        var handlingUnit = new HandlingUnit
        {
            Id = 451,
            TenantId = 1,
            HuCode = "HU-PACK-301",
            Barcode = "GHN-DISPUTE-301",
            Type = HandlingUnitType.Carton,
            WarehouseId = 1,
            Status = HandlingUnitStatus.Shipped,
            PackingSessionId = 401,
            GrossWeightKg = 1.25m,
            NetWeightKg = 1.10m,
            VolumeM3 = 0.006m
        };
        db.HandlingUnits.Add(handlingUnit);

        var videoEvidence = new PackingVideoEvidence
        {
            Id = 701,
            TenantId = 1,
            PackingSessionId = 401,
            ExportReceiptId = 301,
            TrackingNumber = "GHN-DISPUTE-301",
            OrderCode = "XK-COD-301",
            CameraStationId = "CAM-PACK-02",
            StartedAtUtc = now.AddHours(-4),
            EndedAtUtc = now.AddHours(-4).AddSeconds(95),
            DurationSeconds = 95,
            VideoHashSha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            StorageUrl = "/api/v1/packing-sessions/401/video-evidence/stream.mp4",
            RecordedByUserId = 1,
            RecordedAtUtc = now.AddHours(-4).AddSeconds(95)
        };
        db.PackingVideoEvidences.Add(videoEvidence);

        // Also seed an In-Transit StockTransfer to verify GAP-024 (IN_TRANSIT_TRANSFER_AGING in GetOperationalExceptionsAsync)
        db.StockTransfers.Add(new StockTransfer
        {
            Id = 801,
            Code = "TF-AGING-801",
            SourceWarehouseId = 1,
            DestinationWarehouseId = 1,
            Status = StockTransferStatus.InTransit,
            DispatchedAt = now.AddHours(-52),
            CreatedBy = 1,
            CreatedAt = now.AddHours(-55)
        });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = CreateMockCurrentUser(
            userId: 1,
            role: AppRoles.Admin,
            isGlobalAdmin: true,
            AppPermissions.InventoryRead,
            AppPermissions.ExportReceiptUpdate,
            AppPermissions.InventoryMovementCreate);

        var reportOps = new ReportOperationsService(db, whAuth.Object, currentUser.Object);
        var reportsCtrl = new ReportsController(
            Mock.Of<IReportService>(),
            reportOps,
            whAuth.Object,
            currentUser.Object)
        {
            ControllerContext = CreateControllerContextWithPermissions(
                1,
                AppRoles.Admin,
                AppPermissions.InventoryRead,
                AppPermissions.ExportReceiptUpdate,
                AppPermissions.InventoryMovementCreate)
        };

        // 1. Verify COD & Shipping Fee settlement where Carrier overcharges shipping fee (55,000 vs Estimated 25,000 -> FeeOverchargeVariance = +30,000)
        var verifyReq = new ReportsController.VerifyCodSettlementRequest(
            WarehouseId: 1,
            CarrierCode: "GHN",
            ReconcileMode: "AUTO_FLAG_DISCREPANCY",
            Items:
            [
                new ReportsController.CodSettlementItemRequest(
                    ExportReceiptId: 301,
                    TrackingNumber: "GHN-DISPUTE-301",
                    ExpectedCodAmount: 500_000m,
                    ActualCodCollected: 500_000m,
                    EstimatedShippingFee: 25_000m,
                    ActualShippingFee: 55_000m)
            ]);

        var verifyRes = await reportsCtrl.VerifyCodAndSettlementReconciliation(verifyReq, file: null, default);
        using var verifyDoc = JsonSerializer.SerializeToDocument(verifyRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        verifyDoc.RootElement.GetProperty("discrepancyCount").GetInt32().Should().Be(1);

        var verifiedRow = verifyDoc.RootElement.GetProperty("items").EnumerateArray().Single();
        verifiedRow.GetProperty("reconciliationStatus").GetString().Should().Be("DISCREPANCY");
        verifiedRow.GetProperty("feeOverchargeVariance").GetDecimal().Should().Be(30_000m);
        verifiedRow.GetProperty("internalScaleWeightKg").GetDecimal().Should().Be(1.25m);
        verifiedRow.GetProperty("weightDiscrepancyKg").GetDecimal().Should().BeGreaterThan(0m);
        verifiedRow.GetProperty("hasVideoEvidence").GetBoolean().Should().BeTrue();
        verifiedRow.GetProperty("packingSessionId").GetInt32().Should().Be(401);
        verifiedRow.GetProperty("videoEvidenceId").GetInt32().Should().Be(701);
        verifiedRow.GetProperty("cameraCode").GetString().Should().Be("CAM-PACK-02");
        verifiedRow.GetProperty("videoFileUrl").GetString().Should().Be("/api/v1/packing-sessions/401/video-evidence/stream.mp4");
        verifiedRow.GetProperty("fileSha256Hash").GetString().Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        verifiedRow.GetProperty("disputeDossierReady").GetBoolean().Should().BeTrue();

        // 2. GET /api/v1/reports/cod-reconciliation also returns the linked scale weight & CCTV video evidence dossier
        var getCodRes = await reportsCtrl.GetCodAndSettlementReconciliation(warehouseId: 1, carrierCode: "GHN", cancellationToken: default);
        using var getCodDoc = JsonSerializer.SerializeToDocument(getCodRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var codItem = getCodDoc.RootElement.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("ExportReceiptId").GetInt32() == 301);
        codItem.GetProperty("internalScaleWeightKg").GetDecimal().Should().Be(1.25m);
        codItem.GetProperty("hasVideoEvidence").GetBoolean().Should().BeTrue();
        codItem.GetProperty("disputeDossierReady").GetBoolean().Should().BeTrue();

        // 3. Verify GAP-024: IN_TRANSIT_TRANSFER_AGING appears in Operational Exceptions
        var opExRes = await reportOps.GetOperationalExceptionsAsync(whAuth.Object, 1, true, default);
        using var opExDoc = JsonSerializer.SerializeToDocument(opExRes.Payload);
        opExDoc.RootElement.EnumerateArray()
            .Should().Contain(x => x.GetProperty("category").GetString() == "IN_TRANSIT_TRANSFER_AGING" &&
                                   x.GetProperty("code").GetString() == "TF-AGING-801");
    }

    [Fact]
    public async Task TC_W4_04_ConcurrentReplenishmentExecution_SerializesViaLocks_AndPreventsOverReplenishmentPastMaxBaseQty()
    {
        var dbName = $"{nameof(TC_W4_04_ConcurrentReplenishmentExecution_SerializesViaLocks_AndPreventsOverReplenishmentPastMaxBaseQty)}_{Guid.NewGuid():N}";
        var options = CreateInMemoryOptions(dbName);

        await using (var seedDb = new ErpKhoDbContext(options))
        {
            seedDb.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-CONC-01", Name = "Kho Kiểm Thử Đồng Thời", IsActive = true });
            seedDb.Units.Add(new Unit { Id = 1, Code = "PCS", Name = "Cái", IsActive = true });
            seedDb.Products.Add(new Product { Id = 30, Code = "SKU-CONC-30", Name = "Sản Phẩm Đồng Thời", UnitId = 1, IsActive = true });

            seedDb.WarehouseLocations.AddRange(
                new WarehouseLocation
                {
                    Id = 301,
                    WarehouseId = 1,
                    Code = "BULK-CONC-01",
                    Name = "Kệ Bulk Đồng Thời",
                    LocationType = WarehouseLocationType.Storage,
                    StorageClass = "RESERVE",
                    IsPickable = true,
                    IsReceivable = true,
                    IsActive = true
                },
                new WarehouseLocation
                {
                    Id = 302,
                    WarehouseId = 1,
                    Code = "PICK-CONC-01",
                    Name = "Ô Nhặt Lẻ Đồng Thời",
                    LocationType = WarehouseLocationType.Staging,
                    StorageClass = "PICK",
                    MinBaseQty = 20m,
                    MaxBaseQty = 100m,
                    ReplenishFromLocationId = 301,
                    IsPickable = true,
                    IsReceivable = true,
                    IsActive = true
                });

            var bulkStock = new InventoryStock
            {
                Id = 3001,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 30,
                LocationId = 301,
                Status = InventoryStatus.Available,
                Quantity = 200m
            };
            bulkStock.RefreshGrainHash();

            var pickStock = new InventoryStock
            {
                Id = 3002,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 30,
                LocationId = 302,
                Status = InventoryStatus.Available,
                Quantity = 10m
            };
            pickStock.RefreshGrainHash();

            seedDb.InventoryStocks.AddRange(bulkStock, pickStock);
            await seedDb.SaveChangesAsync();
        }

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Fire 2 concurrent replenishment executions each requesting 90 PCS into Pick Bin 302 (which has 10 PCS and MaxBaseQty = 100 PCS)
        async Task<WmsEndpointOutcome> RunConcurrentReplenishAsync()
        {
            await using var workerDb = new ErpKhoDbContext(options);
            var svc = new InventoryControlOperationsService(workerDb, whAuth.Object);
            return await svc.ExecuteReplenishmentAsync(
                new ExecuteReplenishmentInput(
                    WarehouseId: 1,
                    ProductId: 30,
                    FromLocationId: 301,
                    ToLocationId: 302,
                    Quantity: 90m,
                    ExecuteStockMutation: true),
                actorUserId: 1,
                warehouseAuth: whAuth.Object,
                cancellationToken: default);
        }

        var outcomes = await Task.WhenAll(RunConcurrentReplenishAsync(), RunConcurrentReplenishAsync());

        // Exactly one request succeeds (200 OK moving 90 PCS to reach Max=100) and the second sees Pick Bin already at Max capacity (409 Conflict)
        outcomes.Count(o => o.StatusCode == 200).Should().Be(1);
        outcomes.Count(o => o.StatusCode == 409).Should().Be(1);

        await using var verifyDb = new ErpKhoDbContext(options);
        var finalPickQty = await verifyDb.InventoryStocks.AsNoTracking()
            .Where(s => s.WarehouseId == 1 && s.ProductId == 30 && s.LocationId == 302)
            .SumAsync(s => s.Quantity);
        var finalBulkQty = await verifyDb.InventoryStocks.AsNoTracking()
            .Where(s => s.WarehouseId == 1 && s.ProductId == 30 && s.LocationId == 301)
            .SumAsync(s => s.Quantity);

        finalPickQty.Should().Be(100m, "Pick Bin must never over-replenish past MaxBaseQty (100 PCS)");
        finalBulkQty.Should().Be(110m, "Only 90 PCS should be deducted from Bulk location across concurrent requests");
    }
}
