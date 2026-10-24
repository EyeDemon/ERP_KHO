using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Middleware;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public class Wave48To51FullClosureTests
{
    [Fact]
    public async Task Wave48To50_FullFiveLayerClosure_PersistsAll8Entities_ExcelTemplates_ThermalAwb_AndEnforcesInvariants()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        // 1. ImportTemplatesController real .xlsx binary download
        var templatesController = new ImportTemplatesController();
        foreach (var tplType in new[] { "products", "opening-balance", "marketplace-orders", "partners" })
        {
            var fileRes = templatesController.DownloadTemplate(tplType).Should().BeOfType<FileContentResult>().Subject;
            fileRes.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            fileRes.FileContents.Length.Should().BeGreaterThan(1000);
        }

        // 2. ShipmentsController VN 3-level administrative units & address normalization
        var shipmentsController = new ShipmentsController(new Mock<IShipmentService>().Object, db);
        var vnRes = await shipmentsController.GetVnAdministrativeUnits("79", null, "Bến Nghé", default);
        vnRes.Should().BeOfType<OkObjectResult>();
        (await db.VnAdministrativeUnits.CountAsync()).Should().BeGreaterThan(0);
        (await db.CarrierLocationMappings.CountAsync()).Should().BeGreaterThan(0);

        var normRes = await shipmentsController.NormalizeVietnameseAddress(
            new NormalizeVnAddressRequest(RawAddress: "123 Lê Lợi, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh", CarrierCode: "GHN"),
            default);
        using var normDoc = JsonSerializer.SerializeToDocument(normRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        normDoc.RootElement.GetProperty("matched").GetBoolean().Should().BeTrue();
        normDoc.RootElement.GetProperty("provinceCode").GetString().Should().Be("79");

        // 3. ExportReceiptsController Thermal Print 100x150mm HTML + PDF Base64 + Reprint Watermark
        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 951,
            Code = "XK-AWB-951",
            WarehouseId = 1,
            Status = ReceiptStatus.Approved,
            TrackingNumber = "GHN-AWB-951",
            CarrierCode = "GHN",
            AwbPrintCount = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var exportController = new ExportReceiptsController(new Mock<IExportReceiptService>().Object, db);
        var awbRes = await exportController.BatchPrintAwb(
            new ExportReceiptsController.BatchPrintAwbRequest(ReceiptIds: new List<int> { 951 }, ReprintReason: "Rách tem nhiệt"),
            default);
        using var awbDoc = JsonSerializer.SerializeToDocument(awbRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        awbDoc.RootElement.GetProperty("printHtmlDocument").GetString().Should().Contain("100mm 150mm").And.Contain("REPRINT - BẢN SAO");
        awbDoc.RootElement.GetProperty("pdfBase64").GetString().Should().NotBeNullOrWhiteSpace();

        // 4. PackingSessionsController PackingVideoEvidence table persistence
        var packingController = new PackingSessionsController(new Mock<IPackingService>().Object, db);
        var recVideoRes = await packingController.RecordPackingVideoEvidence(
            new PackingSessionsController.RecordPackingVideoEvidenceRequest(
                TrackingNumber: "GHN-VID-501",
                OrderCode: "SO-VID-501",
                CameraStationId: "CAM-PACK-02"),
            default);
        recVideoRes.Should().BeOfType<OkObjectResult>();
        (await db.PackingVideoEvidences.CountAsync(e => e.TrackingNumber == "GHN-VID-501")).Should().Be(1);

        var getVideoRes = await packingController.GetPackingVideoEvidence("GHN-VID-501", default);
        using var getVideoDoc = JsonSerializer.SerializeToDocument(getVideoRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        getVideoDoc.RootElement.GetProperty("cameraStationId").GetString().Should().Be("CAM-PACK-02");

        // 5. ImportReceiptsController CustomerReturns & CustomerReturnLines persistence
        db.Products.Add(new Product
        {
            Id = 501,
            Code = "SKU-RMA-501",
            Name = "Sản phẩm RMA 501",
            CategoryId = 1,
            UnitId = 1,
            MovingAverageCost = 100000m,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var importController = new ImportReceiptsController(new Mock<IImportReceiptService>().Object, null, db);
        var rmaIntakeRes = await importController.RmaIntakeCheck(
            new ImportReceiptsController.RmaIntakeCheckRequest(
                WarehouseId: 1,
                ProductId: 501,
                Quantity: 3m,
                ReturnReference: "RET-TRACK-501",
                ReasonCode: "DEFECTIVE_PACKAGING"),
            default);
        rmaIntakeRes.Should().BeOfType<OkObjectResult>();
        (await db.CustomerReturns.CountAsync()).Should().Be(1);
        (await db.CustomerReturnLines.CountAsync()).Should().Be(1);

        var listReturnsRes = await importController.GetCustomerReturns(1, default);
        listReturnsRes.Should().BeOfType<OkObjectResult>();

        // 6. AccountSecurityController SupportImpersonationSession persistence & revoke
        var securityController = new AccountSecurityController(
            new Mock<IAccountAdminService>().Object,
            new Mock<IUserSessionService>().Object,
            db);
        var grantRes = await securityController.GrantSupportImpersonation(
            new AccountSecurityController.GrantSupportImpersonationRequest("SUP-501", 30, true, 99),
            default);
        using var grantDoc = JsonSerializer.SerializeToDocument(grantRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var sessionId = grantDoc.RootElement.GetProperty("sessionId").GetInt32();
        sessionId.Should().BeGreaterThan(0);

        var revokeRes = await securityController.RevokeSupportImpersonation(sessionId, default);
        revokeRes.Should().BeOfType<OkObjectResult>();
        var revokedSession = await db.SupportImpersonationSessions.FindAsync(sessionId);
        revokedSession!.Status.Should().Be("REVOKED");

        // 7. StocktakesController StocktakeAdjustmentProposals & OD-14 (< 1% AND <= 500,000 VND)
        db.InventoryStocks.Add(new InventoryStock
        {
            WarehouseId = 1,
            ProductId = 501,
            Status = InventoryStatus.Available,
            Quantity = 1000m,
            MovingAverageCost = 100000m,
            LastUpdated = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var stocktakesController = new StocktakesController(
            new Mock<IStocktakeService>().Object,
            new Mock<IStocktakeQueryService>().Object,
            db);
        var propRes = await stocktakesController.CreateAdjustmentProposal(
            new StocktakesController.CreateAdjustmentProposalRequest(
                WarehouseId: 1,
                ProductId: 501,
                DeltaQuantity: 1m,
                ReasonCode: "COUNT_VARIANCE",
                Note: "Chênh lệch nhỏ dưới 1% và dưới 500k"),
            default);
        propRes.Should().BeOfType<OkObjectResult>();
        var autoProposal = await db.StocktakeAdjustmentProposals.OrderByDescending(p => p.Id).FirstAsync();
        autoProposal.IsAutoApproved.Should().BeTrue();
        autoProposal.Status.Should().Be("APPROVED");

        var propHighValRes = await stocktakesController.CreateAdjustmentProposal(
            new StocktakesController.CreateAdjustmentProposalRequest(
                WarehouseId: 1,
                ProductId: 501,
                DeltaQuantity: 6m,
                ReasonCode: "COUNT_VARIANCE",
                Note: "Chênh lệch 600k vượt ngưỡng 500k"),
            default);
        propHighValRes.Should().BeOfType<OkObjectResult>();
        var pendingProposal = await db.StocktakeAdjustmentProposals.OrderByDescending(p => p.Id).FirstAsync();
        pendingProposal.IsAutoApproved.Should().BeFalse();
        pendingProposal.Status.Should().Be("PENDING_APPROVAL");

        // 8. PermissionsController ProvisionSaaSTenant(AllocateNewTenant: true) preserves qa_admin on TenantId = 1
        db.Roles.Add(new Role { Id = 1, RoleName = "Admin" });
        db.Users.Add(new User
        {
            Id = 1,
            TenantId = 1,
            Username = "qa_admin",
            FullName = "QA Admin",
            PasswordHash = "HASH",
            RoleId = 1,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var permissionsController = new PermissionsController(db);
        var provT1Res = await permissionsController.ProvisionSaaSTenant(
            new PermissionsController.ProvisionTenantRequest("STARTER", "Tenant 1 Shop", "0310000001", AllocateNewTenant: false),
            default);
        provT1Res.Should().BeOfType<OkObjectResult>();

        var provT2Res = await permissionsController.ProvisionSaaSTenant(
            new PermissionsController.ProvisionTenantRequest("PRO", "Tenant 2 Shop", "0310000002", AllocateNewTenant: true),
            default);
        using var provT2Doc = JsonSerializer.SerializeToDocument(provT2Res.Should().BeOfType<OkObjectResult>().Subject.Value);
        var allocatedTenantId = provT2Doc.RootElement.GetProperty("tenantId").GetInt32();
        allocatedTenantId.Should().BeGreaterThan(1);

        var qaAdminAfter = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Username == "qa_admin");
        qaAdminAfter.TenantId.Should().Be(1);
        db.CurrentTenantId.Should().Be(1);

        var newTenantOwner = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Username == $"tenant_{allocatedTenantId}_owner");
        newTenantOwner.Should().NotBeNull();
        newTenantOwner!.TenantId.Should().Be(allocatedTenantId);
    }

    [Fact]
    public async Task Wave53_OD04_ApproveAndDispatchExportReceipt_ConsumesReservation_DeductsStock_AndWritesLedgerWithMac()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W53_OD04_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var warehouse = new Warehouse { Id = 1, Code = "WH-HCM", Name = "Kho Chính HCM", IsActive = true };
        var location = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "PICK-01",
            Name = "Kệ Nhặt A1",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = true
        };
        var product = new Product
        {
            Id = 101,
            Code = "SKU-W53-101",
            Name = "Bàn Phím Cơ W53",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 420000m,
            IsActive = true
        };
        var stock = new InventoryStock
        {
            Id = 1001,
            WarehouseId = 1,
            ProductId = 101,
            Product = product,
            LocationId = 10,
            Location = location,
            Status = InventoryStatus.Available,
            Quantity = 50m,
            ReservedQuantity = 0m,
            AllocatedQuantity = 0m,
            MovingAverageCost = 420000m
        };
        stock.RefreshGrainHash();
        var receipt = new ExportReceipt
        {
            Id = 501,
            Code = "PXK-W53-501",
            WarehouseId = 1,
            Status = ReceiptStatus.Draft,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail
                {
                    Id = 5011,
                    ExportReceiptId = 501,
                    ProductId = 101,
                    Product = product,
                    Quantity = 6m,
                    UnitPrice = 750000m
                }
            ]
        };

        db.Units.Add(unit);
        db.Warehouses.Add(warehouse);
        db.WarehouseLocations.Add(location);
        db.Products.Add(product);
        db.InventoryStocks.Add(stock);
        db.ExportReceipts.Add(receipt);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(2);
        currentUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);
        currentUser.Setup(x => x.HasPermission(It.IsAny<string>())).Returns(true);

        var stockRepo = new InventoryStockRepository(db);
        var uow = new UnitOfWork(db);
        var reservationService = new StockReservationService(
            db,
            stockRepo,
            uow,
            whAuth.Object,
            currentUser.Object,
            new ERP.Application.Options.StockReservationOptions());
        var exportService = new ExportReceiptService(
            new ExportReceiptRepository(db, whAuth.Object),
            stockRepo,
            new InventoryTransactionRepository(db),
            uow,
            new AuditLogRepository(db),
            whAuth.Object,
            currentUser.Object,
            reservationService);

        await exportService.ApproveAndReserveAsync(501, 2);

        var stockAfterApprove = await db.InventoryStocks.SingleAsync(s => s.Id == 1001);
        stockAfterApprove.Quantity.Should().Be(50m);
        stockAfterApprove.ReservedQuantity.Should().Be(6m);

        currentUser.SetupGet(x => x.UserId).Returns(3);
        await exportService.DispatchAsync(501, 3);

        var stockAfterDispatch = await db.InventoryStocks.SingleAsync(s => s.Id == 1001);
        stockAfterDispatch.Quantity.Should().Be(44m);
        stockAfterDispatch.ReservedQuantity.Should().Be(0m);

        var reservationAfter = await db.StockReservations.SingleAsync(r => r.SourceType == "ExportReceipt" && r.SourceId == 501);
        reservationAfter.Status.Should().Be(StockReservationStatus.Consumed);
        reservationAfter.ConsumedQuantity.Should().Be(6m);

        var exportTx = await db.InventoryTransactions.SingleAsync(t => t.ReferenceType == "ExportReceipt" && t.ReferenceId == 501);
        exportTx.TransactionType.Should().Be(TransactionType.Export);
        exportTx.Quantity.Should().Be(6m);
        exportTx.UnitCost.Should().Be(420000m);
    }

    [Fact]
    public async Task Wave53_OD05_ConfirmCarrierHandover_DeductsRealStock_ConsumesReservation_AndWritesExportLedger()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W53_OD05_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-HCM", Name = "Kho Chính HCM", IsActive = true });
        var prod = new Product
        {
            Id = 201,
            Code = "SKU-W53-201",
            Name = "Tai Nghe Bluetooth W53",
            UnitId = 1,
            MovingAverageCost = 180000m,
            IsActive = true
        };
        db.Products.Add(prod);
        var stock = new InventoryStock
        {
            Id = 2001,
            WarehouseId = 1,
            ProductId = 201,
            Status = InventoryStatus.Available,
            Quantity = 30m,
            ReservedQuantity = 4m,
            AllocatedQuantity = 4m,
            MovingAverageCost = 180000m
        };
        stock.RefreshGrainHash();
        db.InventoryStocks.Add(stock);

        var reservation = new StockReservation
        {
            Id = 3001,
            WarehouseId = 1,
            ProductId = 201,
            SourceType = "ExportReceipt",
            SourceId = 601,
            SourceCode = "PXK-W53-601",
            Quantity = 4m,
            AllocatedQuantity = 4m,
            ConsumedQuantity = 0m,
            Status = StockReservationStatus.Active,
            Allocations =
            [
                new StockAllocation
                {
                    Id = 30011,
                    WarehouseId = 1,
                    ProductId = 201,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = 4m,
                    Status = StockAllocationStatus.Active
                }
            ]
        };
        db.StockReservations.Add(reservation);

        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 601,
            Code = "PXK-W53-601",
            WarehouseId = 1,
            Status = ReceiptStatus.Approved,
            CarrierCode = "GHN",
            TrackingNumber = "GHN-W53-601",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail
                {
                    Id = 6011,
                    ExportReceiptId = 601,
                    ProductId = 201,
                    Product = prod,
                    Quantity = 4m,
                    UnitPrice = 350000m
                }
            ]
        });
        await db.SaveChangesAsync();

        var controller = new ExportReceiptsController(new Mock<IExportReceiptService>().Object, db);
        var res = await controller.ConfirmCarrierHandover(
            new ExportReceiptsController.ConfirmCarrierHandoverRequest(
                WarehouseId: 1,
                CarrierCode: "GHN",
                TrackingNumbers: ["GHN-W53-601"],
                ExportReceiptIds: [601]),
            default);

        using var doc = JsonSerializer.SerializeToDocument(res.Should().BeOfType<OkObjectResult>().Subject.Value);
        doc.RootElement.GetProperty("Status").GetString().Should().Be("SEALED");
        doc.RootElement.GetProperty("dispatchedCount").GetInt32().Should().Be(1);

        var updatedStock = await db.InventoryStocks.SingleAsync(s => s.Id == 2001);
        updatedStock.Quantity.Should().Be(26m);
        updatedStock.ReservedQuantity.Should().Be(0m);
        updatedStock.AllocatedQuantity.Should().Be(0m);

        var updatedReservation = await db.StockReservations.Include(r => r.Allocations).SingleAsync(r => r.Id == 3001);
        updatedReservation.Status.Should().Be(StockReservationStatus.Consumed);
        updatedReservation.ConsumedQuantity.Should().Be(4m);
        updatedReservation.Allocations.Single().Status.Should().Be(StockAllocationStatus.Picked);

        var updatedReceipt = await db.ExportReceipts.SingleAsync(r => r.Id == 601);
        updatedReceipt.Status.Should().Be(ReceiptStatus.Dispatched);

        var exportLedger = await db.InventoryTransactions.SingleAsync(t => t.ReferenceType == "ExportReceipt" && t.ReferenceId == 601);
        exportLedger.TransactionType.Should().Be(TransactionType.Export);
        Math.Abs(exportLedger.Quantity).Should().Be(4m);
        exportLedger.UnitCost.Should().Be(180000m);
    }

    [Fact]
    public async Task Wave53_OD08_RebuildBalancesAsync_PreservesMultiBinQuantities_AcrossLocationsInSameWarehouse()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W53_OD08_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var locReceiving = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "RCV-01",
            Name = "Cửa Nhận Hàng",
            LocationType = WarehouseLocationType.Receiving,
            IsActive = true,
            IsReceivable = true
        };
        var locStorage = new WarehouseLocation
        {
            Id = 11,
            WarehouseId = 1,
            Code = "STR-A01",
            Name = "Kệ Lưu Trữ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true
        };
        db.WarehouseLocations.AddRange(locReceiving, locStorage);

        // Two bins in the SAME warehouse for the SAME product with drifted quantities (999m and 0m)
        var bin1 = new InventoryStock
        {
            Id = 101,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 301,
            LocationId = 10,
            Location = locReceiving,
            Status = InventoryStatus.Available,
            Quantity = 999m,
            ReservedQuantity = 0m
        };
        var bin2 = new InventoryStock
        {
            Id = 102,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 301,
            LocationId = 11,
            Location = locStorage,
            Status = InventoryStatus.Available,
            Quantity = 0m,
            ReservedQuantity = 0m
        };
        bin1.RefreshGrainHash();
        bin2.RefreshGrainHash();
        db.InventoryStocks.AddRange(bin1, bin2);

        // Ledger: +100 imported at RCV-01 (10), then 65 moved from RCV-01 (10) -> STR-A01 (11)
        db.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                Id = 1,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 301,
                LocationId = 10,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 100m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-10)
            },
            new InventoryTransaction
            {
                Id = 2,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 301,
                LocationId = 10,
                FromLocationId = 10,
                ToLocationId = 11,
                InventoryStatus = InventoryStatus.Available,
                FromInventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Move,
                Quantity = -65m,
                ReferenceType = "Putaway",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-5)
            },
            new InventoryTransaction
            {
                Id = 3,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 301,
                LocationId = 11,
                FromLocationId = 10,
                ToLocationId = 11,
                InventoryStatus = InventoryStatus.Available,
                FromInventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Move,
                Quantity = 65m,
                ReferenceType = "Putaway",
                ReferenceId = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-5)
            });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);
        currentUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);

        var service = new StockReservationService(
            db,
            new InventoryStockRepository(db),
            new UnitOfWork(db),
            whAuth.Object,
            currentUser.Object,
            new ERP.Application.Options.StockReservationOptions());
        var rebuiltCount = await service.RebuildBalancesAsync(1, 301, default);
        rebuiltCount.Should().Be(2);

        var rcvAfter = await db.InventoryStocks.SingleAsync(s => s.Id == 101);
        var strAfter = await db.InventoryStocks.SingleAsync(s => s.Id == 102);
        rcvAfter.Quantity.Should().Be(35m);
        strAfter.Quantity.Should().Be(65m);
    }

    [Fact]
    public async Task Wave53_OD11_RbacCostMasking_MasksCostsForWarehouseStaff_AcrossAllFourServicesAndControllers()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W53_OD11_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var warehouse = new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
        var supplier = new BusinessPartner { Id = 10, Code = "SUP-01", Name = "NCC Minh Việt", IsSupplier = true, IsActive = true };
        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var product = new Product
        {
            Id = 401,
            Code = "SKU-COST-401",
            Name = "Màn Hình 4K",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 4500000m,
            IsActive = true
        };
        db.Warehouses.Add(warehouse);
        db.BusinessPartners.Add(supplier);
        db.Units.Add(unit);
        db.Products.Add(product);

        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 701,
            Code = "PXK-COST-701",
            WarehouseId = 1,
            Status = ReceiptStatus.Dispatched,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail
                {
                    Id = 7011,
                    ExportReceiptId = 701,
                    ProductId = 401,
                    Product = product,
                    Quantity = 2m,
                    UnitPrice = 6200000m
                }
            ]
        });

        db.StocktakeAdjustmentProposals.Add(new StocktakeAdjustmentProposal
        {
            Id = 801,
            TenantId = 1,
            ProposalCode = "ADJ-801",
            WarehouseId = 1,
            ProductId = 401,
            SystemQuantity = 10m,
            CountedQuantity = 9m,
            DeltaQuantity = -1m,
            VariancePct = 10m,
            UnitCost = 4500000m,
            VarianceValue = 4500000m,
            Status = "PENDING",
            ReasonCode = "DAMAGED",
            CreatedBy = 1,
            CreatedAtUtc = DateTime.UtcNow
        });

        db.PurchaseOrders.Add(new PurchaseOrder
        {
            Id = 901,
            ExternalPoId = "EXT-PO-901",
            SourceSystem = "ERP",
            Code = "PO-COST-901",
            SupplierId = 10,
            Supplier = supplier,
            WarehouseId = 1,
            Warehouse = warehouse,
            Status = PurchaseOrderStatus.Open,
            OrderDate = DateTime.UtcNow.Date,
            Currency = "VND",
            RowVersion = [1, 0, 0, 0, 0, 0, 0, 1],
            CreatedAtUtc = DateTime.UtcNow,
            Lines =
            [
                new PurchaseOrderLine
                {
                    Id = 9011,
                    PurchaseOrderId = 901,
                    ExternalLineId = "EXT-L-1",
                    LineNo = 1,
                    ProductId = 401,
                    Product = product,
                    OrderedQuantity = 5m,
                    BaseOrderedQuantity = 5m,
                    OperationUnitId = 1,
                    BaseUnitId = 1,
                    OperationUnitCodeSnapshot = "CAI",
                    BaseUnitCodeSnapshot = "CAI",
                    ConversionFactorSnapshot = 1m,
                    UnitPrice = 4500000m,
                    TaxPercent = 10m
                }
            ]
        });
        await db.SaveChangesAsync();

        var staffUser = new Mock<ICurrentUser>();
        staffUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        staffUser.SetupGet(x => x.UserId).Returns(15);
        staffUser.SetupGet(x => x.Role).Returns(AppRoles.WarehouseStaff);
        staffUser.SetupGet(x => x.IsGlobalAdmin).Returns(false);
        staffUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(false);

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // 1. ReportsController.GetOwnerGrossProfit masks COGS & NetPocketProfit for WarehouseStaff
        var reportsController = new ReportsController(new Mock<IReportService>().Object, db, whAuth.Object, staffUser.Object);
        var pnlRes = await reportsController.GetOwnerGrossProfit(1, default);
        using var pnlDoc = JsonSerializer.SerializeToDocument(pnlRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        pnlDoc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeTrue();
        pnlDoc.RootElement.GetProperty("movingAverageCogs").ValueKind.Should().Be(JsonValueKind.Null);
        pnlDoc.RootElement.GetProperty("netPocketProfit").ValueKind.Should().Be(JsonValueKind.Null);

        // 2. ExportReceiptsController.GetMisaExcelExport masks donGiaVonMac & thanhTienGiaVon for WarehouseStaff
        var exportController = new ExportReceiptsController(new Mock<IExportReceiptService>().Object, db, whAuth.Object, staffUser.Object);
        var misaRes = await exportController.GetMisaExcelExport(1, "json", default);
        using var misaDoc = JsonSerializer.SerializeToDocument(misaRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        misaDoc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeTrue();
        misaDoc.RootElement.GetProperty("totalCostAmount").ValueKind.Should().Be(JsonValueKind.Null);
        misaDoc.RootElement.GetProperty("rows")[0].GetProperty("donGiaVonMac").ValueKind.Should().Be(JsonValueKind.Null);
        misaDoc.RootElement.GetProperty("rows")[0].GetProperty("thanhTienGiaVon").ValueKind.Should().Be(JsonValueKind.Null);

        // 3. StocktakesController.GetReasonCodesAndAdjustments masks unitCost & varianceValue for WarehouseStaff
        var stocktakesController = new StocktakesController(
            new Mock<IStocktakeService>().Object,
            new Mock<IStocktakeQueryService>().Object,
            db,
            whAuth.Object,
            staffUser.Object);
        var stRes = await stocktakesController.GetReasonCodesAndAdjustments(1, default);
        using var stDoc = JsonSerializer.SerializeToDocument(stRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        stDoc.RootElement.GetProperty("recentProposals")[0].GetProperty("unitCost").ValueKind.Should().Be(JsonValueKind.Null);
        stDoc.RootElement.GetProperty("recentProposals")[0].GetProperty("varianceValue").ValueKind.Should().Be(JsonValueKind.Null);

        // 4. InboundPlanningService.GetPurchaseOrderAsync masks UnitPrice & TotalAmount for WarehouseStaff
        var inboundService = new InboundPlanningService(db, whAuth.Object, staffUser.Object);
        var poDto = await inboundService.GetPurchaseOrderAsync(901, default);
        poDto.Lines.Single().UnitPrice.Should().Be(0m);
        poDto.SubTotalAmount.Should().Be(0m);
        poDto.TotalAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Wave53_OD10_And_OD12_ZeroFakeOutbox_EmptyMarketplaceShopsAndEmptyOutbox_ReturnZeroWithoutAutoSeeding()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W53_OD10_12_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true });
        await db.SaveChangesAsync();

        // 1. Empty MarketplaceShops -> SyncMarketplaceInventory does NOT auto-seed a fake shop or outbox message
        var soController = new SalesOrdersController(new Mock<ISalesOrderBackorderService>().Object, db);
        var syncRes = await soController.SyncMarketplaceInventory(
            new SalesOrdersController.SyncMarketplaceInventoryRequest(WarehouseId: 1, ShopId: null),
            default);
        using var syncDoc = JsonSerializer.SerializeToDocument(syncRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        syncDoc.RootElement.GetProperty("shopCount").GetInt32().Should().Be(0);
        syncDoc.RootElement.GetProperty("syncedCount").GetInt32().Should().Be(0);
        (await db.MarketplaceShops.CountAsync()).Should().Be(0);
        (await db.IntegrationOutboxMessages.CountAsync()).Should().Be(0);

        // 2. Empty IntegrationOutboxMessages -> GetOutboxMessages & DispatchPendingOutboxMessages return 0 without auto-seeding
        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var recController = new InventoryReconciliationController(
            new Mock<IInventoryReconciliationQueryService>().Object,
            db,
            whAuth.Object);
        var statusRes = await recController.GetOutboxMessages(1, default);
        using var statusDoc = JsonSerializer.SerializeToDocument(statusRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        statusDoc.RootElement.GetProperty("totalMessages").GetInt32().Should().Be(0);

        var dispatchRes = await recController.DispatchPendingOutboxMessages(
            1,
            new InventoryReconciliationController.DispatchOutboxRequest(WarehouseId: 1),
            default);
        using var dispatchDoc = JsonSerializer.SerializeToDocument(dispatchRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        dispatchDoc.RootElement.GetProperty("dispatchedCount").GetInt32().Should().Be(0);
        (await db.IntegrationOutboxMessages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Wave53_ZeroFakeRevenue_GetOwnerGrossProfit_WhenNoDispatchedExportReceipts_ReturnsZeroRevenue()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W53_ZeroFakeRev_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var prod = new Product
        {
            Id = 501,
            Code = "SKU-NODISP-501",
            Name = "Sản Phẩm Chưa Xuất Kho",
            UnitId = 1,
            MovingAverageCost = 100000m,
            IsActive = true
        };
        db.Products.Add(prod);
        db.ExportReceipts.AddRange(
            new ExportReceipt
            {
                Id = 1,
                Code = "PXK-DRAFT-01",
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow,
                Details = [new ExportReceiptDetail { Id = 11, ProductId = 501, Product = prod, Quantity = 10m, UnitPrice = 250000m }]
            },
            new ExportReceipt
            {
                Id = 2,
                Code = "PXK-APPROVED-02",
                WarehouseId = 1,
                Status = ReceiptStatus.Approved,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow,
                Details = [new ExportReceiptDetail { Id = 22, ProductId = 501, Product = prod, Quantity = 5m, UnitPrice = 250000m }]
            });
        await db.SaveChangesAsync();

        var reportsController = new ReportsController(new Mock<IReportService>().Object, db);
        var res = await reportsController.GetOwnerGrossProfit(1, default);
        using var doc = JsonSerializer.SerializeToDocument(res.Should().BeOfType<OkObjectResult>().Subject.Value);
        doc.RootElement.GetProperty("grossRevenue").GetDecimal().Should().Be(0m);
        doc.RootElement.GetProperty("netRevenue").GetDecimal().Should().Be(0m);
        doc.RootElement.GetProperty("movingAverageCogs").GetDecimal().Should().Be(0m);
        doc.RootElement.GetProperty("netPocketProfit").GetDecimal().Should().Be(0m);
        doc.RootElement.GetProperty("skuBreakdown").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Wave53_OD13_NormalizeVietnameseAddress_UnresolvedWard_Returns422AddressWardUnresolved()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W53_OD13_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var controller = new ShipmentsController(new Mock<IShipmentService>().Object, db);
        var res = await controller.NormalizeVietnameseAddress(
            new NormalizeVnAddressRequest(
                RawAddress: "Số 999 Đường Không Tồn Tại, Phường Vũ Trụ Sao Hỏa",
                Province: "TP. Hồ Chí Minh",
                District: "Quận 1",
                Ward: "Phường Vũ Trụ Sao Hỏa Không Có Thật",
                CarrierCode: "GHN"),
            default);

        var unprocessable = res.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        unprocessable.StatusCode.Should().Be(422);
        using var doc = JsonSerializer.SerializeToDocument(unprocessable.Value);
        doc.RootElement.GetProperty("matched").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("ADDRESS_WARD_UNRESOLVED");
    }

    [Fact]
    public async Task Wave53_OD15_SupportImpersonationMiddleware_ReadOnlySession_BlocksPostPutDeleteWith403()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W53_OD15_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        db.SupportImpersonationSessions.Add(new SupportImpersonationSession
        {
            Id = 1,
            TenantId = 1,
            SessionCode = "SUP-RO-TOKEN-001",
            TokenHash = "SUP-RO-TOKEN-001",
            SupportAgentUserId = 99,
            SupportTicketCode = "TICKET-W53",
            ReadOnlyScope = true,
            Status = "ACTIVE",
            GrantedByUserId = 1,
            GrantedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(25)
        });
        await db.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        var serviceProvider = services.BuildServiceProvider();

        var nextCalledCount = 0;
        var middleware = new SupportImpersonationMiddleware(_ =>
        {
            nextCalledCount++;
            return Task.CompletedTask;
        });

        // 1. GET request with ReadOnlyScope=true passes through (200 OK)
        var getContext = new DefaultHttpContext { RequestServices = serviceProvider };
        getContext.Request.Method = HttpMethods.Get;
        getContext.Request.Headers[SupportImpersonationMiddleware.ImpersonationTokenHeader] = "SUP-RO-TOKEN-001";
        getContext.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(getContext);
        getContext.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        nextCalledCount.Should().Be(1);
        getContext.Items[SupportImpersonationMiddleware.ImpersonatedBySupportUserIdItemKey].Should().Be(99);

        // 2. POST/PUT/DELETE requests with ReadOnlyScope=true are blocked with 403 IMPERSONATION_READ_ONLY_ENFORCED
        foreach (var method in new[] { HttpMethods.Post, HttpMethods.Put, HttpMethods.Delete })
        {
            var mutContext = new DefaultHttpContext { RequestServices = serviceProvider };
            mutContext.Request.Method = method;
            mutContext.Request.Headers[SupportImpersonationMiddleware.ImpersonationTokenHeader] = "SUP-RO-TOKEN-001";
            mutContext.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(mutContext);
            mutContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            nextCalledCount.Should().Be(1);

            mutContext.Response.Body.Position = 0;
            using var errDoc = await JsonDocument.ParseAsync(mutContext.Response.Body);
            errDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("IMPERSONATION_READ_ONLY_ENFORCED");
        }
    }

    [Fact]
    public void Wave54_GrainHash_RefreshAndComputeCanonical_ProduceIdentical32ByteSha256Hash()
    {
        var stock = new InventoryStock
        {
            TenantId = 1,
            WarehouseId = 2,
            LocationId = 10,
            ProductId = 50,
            LotId = null,
            SerialId = null,
            HandlingUnitId = null,
            Status = InventoryStatus.Available,
            OwnerId = null
        };

        stock.RefreshGrainHash();
        var computed = InventoryStock.ComputeCanonicalGrainHash(
            tenantId: 1,
            warehouseId: 2,
            locationId: 10,
            productId: 50,
            lotId: null,
            serialId: null,
            handlingUnitId: null,
            status: InventoryStatus.Available,
            ownerId: null);

        stock.GrainHash.Should().NotBeNull();
        stock.GrainHash!.Length.Should().Be(32);
        computed.Length.Should().Be(32);
        stock.GrainHash.SequenceEqual(computed).Should().BeTrue();

        var differentLocationHash = InventoryStock.ComputeCanonicalGrainHash(
            tenantId: 1,
            warehouseId: 2,
            locationId: 11,
            productId: 50,
            lotId: null,
            serialId: null,
            handlingUnitId: null,
            status: InventoryStatus.Available,
            ownerId: null);
        var differentOwnerHash = InventoryStock.ComputeCanonicalGrainHash(
            tenantId: 1,
            warehouseId: 2,
            locationId: 10,
            productId: 50,
            lotId: null,
            serialId: null,
            handlingUnitId: null,
            status: InventoryStatus.Available,
            ownerId: 99);

        stock.GrainHash.SequenceEqual(differentLocationHash).Should().BeFalse();
        stock.GrainHash.SequenceEqual(differentOwnerHash).Should().BeFalse();
    }

    [Fact]
    public async Task Wave54_ExportReceiptsAndManifest_NoNullDbFake_RequiresAuthActor_DispatchesViaDomainServiceWithLedgerAndOutbox()
    {
        var exportServiceMock = new Mock<IExportReceiptService>();
        Action nullDbAct = () => _ = new HandoverManifestService(null!, exportServiceMock.Object);
        nullDbAct.Should().Throw<InvalidOperationException>();

        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W54_Manifest_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        db.Users.Add(new User
        {
            Id = 7,
            TenantId = 1,
            Username = "manifest_actor",
            PasswordHash = "HASH",
            FullName = "Nhân Viên Bàn Giao",
            RoleId = 1,
            IsActive = true
        });
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-W54", Name = "Kho Bàn Giao W54", IsActive = true });
        db.Products.Add(new Product
        {
            Id = 101,
            Code = "SKU-W54-101",
            Name = "Sản Phẩm Xuất Bàn Giao",
            UnitId = 1,
            MovingAverageCost = 80000m,
            IsActive = true
        });
        db.InventoryStocks.Add(new InventoryStock
        {
            Id = 1,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 101,
            Status = InventoryStatus.Available,
            Quantity = 20m,
            ReservedQuantity = 5m,
            AllocatedQuantity = 5m,
            MovingAverageCost = 80000m
        });
        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 501,
            Code = "PXK-W54-501",
            WarehouseId = 1,
            Status = ReceiptStatus.Approved,
            TrackingNumber = "GHN-W54-501",
            CarrierCode = "GHN",
            CreatedBy = 7,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail
                {
                    Id = 5001,
                    ExportReceiptId = 501,
                    ProductId = 101,
                    Quantity = 5m,
                    UnitPrice = 150000m
                }
            ]
        });
        await db.SaveChangesAsync();

        var service = new HandoverManifestService(db, exportServiceMock.Object);

        // Authenticated request without valid actorUserId must throw UnauthorizedAccessException
        Func<Task> unauthAct = async () => await service.ScanHandoverAwbAsync(
            manifestIdRoute: null,
            request: new ScanHandoverAwbInputDto(null, 1, "GHN", "GHN-W54-501", 501, "51C-888.99", "Nguyễn Văn Tài", "0909123456"),
            actorUserId: null,
            isAuthenticated: true);
        await unauthAct.Should().ThrowAsync<UnauthorizedAccessException>();

        // Valid authenticated actor scans and seals manifest -> dispatches receipt, deducts stock, records ledger, outbox, and audit log
        var scanRes = await service.ScanHandoverAwbAsync(
            manifestIdRoute: null,
            request: new ScanHandoverAwbInputDto(null, 1, "GHN", "GHN-W54-501", 501, "51C-888.99", "Nguyễn Văn Tài", "0909123456"),
            actorUserId: 7,
            isAuthenticated: true);
        scanRes.StatusCode.Should().Be(200);
        using var scanDoc = JsonSerializer.SerializeToDocument(scanRes.Payload);
        var manifestId = scanDoc.RootElement.GetProperty("manifestId").GetInt32();

        var sealRes = await service.SealHandoverManifestAsync(
            id: manifestId,
            request: new SealHandoverManifestInputDto("Nguyễn Văn Tài", "0909123456", "51C-888.99", null),
            actorUserId: 7,
            isAuthenticated: true);
        sealRes.StatusCode.Should().Be(200);

        exportServiceMock.Verify(x => x.DispatchAsync(501, 7), Times.Once);

        var updatedManifest = await db.HandoverManifests.SingleAsync(m => m.Id == manifestId);
        updatedManifest.Status.Should().Be("SEALED");

        var updatedReceipt = await db.ExportReceipts.SingleAsync(r => r.Id == 501);
        updatedReceipt.Status.Should().Be(ReceiptStatus.Dispatched);

        var updatedStock = await db.InventoryStocks.SingleAsync(s => s.Id == 1);
        updatedStock.Quantity.Should().Be(15m);
        updatedStock.ReservedQuantity.Should().Be(0m);
        updatedStock.AllocatedQuantity.Should().Be(0m);

        var ledgerTx = await db.InventoryTransactions.SingleAsync(t => t.ReferenceType == "ExportReceipt" && t.ReferenceId == 501);
        ledgerTx.Quantity.Should().Be(-5m);
        ledgerTx.UnitCost.Should().Be(80000m);

        (await db.IntegrationOutboxMessages.AnyAsync(o => o.AggregateType == "ExportReceipt" && o.AggregateId == 501 && o.EventType == "ExportReceiptDispatched")).Should().BeTrue();
        (await db.AuditLogs.AnyAsync(a => a.EntityName == "HandoverManifest" && a.EntityId == manifestId && a.Action == "HandoverManifest.Seal")).Should().BeTrue();
    }

    [Fact]
    public async Task Wave54_ShipmentFinancialLedgerAndStocktakeProposal_HaveExplicitPrecisionAndRowVersion()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W54_Precision_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var shipmentLedgerType = db.Model.FindEntityType(typeof(ShipmentFinancialLedger));
        shipmentLedgerType.Should().NotBeNull();
        foreach (var prop in shipmentLedgerType!.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            prop.GetPrecision().Should().Be(18, $"ShipmentFinancialLedger.{prop.Name} must have precision 18");
            prop.GetScale().Should().Be(4, $"ShipmentFinancialLedger.{prop.Name} must have scale 4");
        }
        var shipmentRowVersion = shipmentLedgerType.FindProperty(nameof(ShipmentFinancialLedger.RowVersion));
        shipmentRowVersion.Should().NotBeNull();
        shipmentRowVersion!.IsConcurrencyToken.Should().BeTrue();

        var proposalType = db.Model.FindEntityType(typeof(StocktakeAdjustmentProposal));
        proposalType.Should().NotBeNull();
        foreach (var prop in proposalType!.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            prop.GetPrecision().Should().Be(18, $"StocktakeAdjustmentProposal.{prop.Name} must have precision 18");
            prop.GetScale().Should().Be(4, $"StocktakeAdjustmentProposal.{prop.Name} must have scale 4");
        }
        var proposalRowVersion = proposalType.FindProperty(nameof(StocktakeAdjustmentProposal.RowVersion));
        proposalRowVersion.Should().NotBeNull();
        proposalRowVersion!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void Wave54_StocktakeStatus_DedicatedEnumAndDomainTransitionGuards_WorkCorrectly()
    {
        typeof(Stocktake).GetProperty(nameof(Stocktake.Status))!.PropertyType.Should().Be(typeof(StocktakeStatus));

        var stocktake = new Stocktake
        {
            Id = 10,
            Code = "ST-W54-010",
            WarehouseId = 1,
            Status = StocktakeStatus.Draft,
            CreatedBy = 1
        };

        stocktake.TransitionTo(StocktakeStatus.Approved, actorUserId: 9);
        stocktake.Status.Should().Be(StocktakeStatus.Approved);
        stocktake.ApprovedBy.Should().Be(9);
        stocktake.ApprovedAt.Should().NotBeNull();

        Action invalidBackwardTransition = () => stocktake.TransitionTo(StocktakeStatus.Draft, actorUserId: 9);
        var ex = invalidBackwardTransition.Should().Throw<ERP.Domain.Exceptions.ConflictException>().Which;
        ex.ErrorCode.Should().Be("DOC_INVALID_STATE_TRANSITION");

        var unprocessable = new ERP.Domain.Exceptions.UnprocessableDomainException(
            "Chuyển trạng thái không hợp lệ",
            "DOC_INVALID_STATE_TRANSITION");
        unprocessable.ErrorCode.Should().Be("DOC_INVALID_STATE_TRANSITION");
    }

    [Fact]
    public async Task Wave54_SplitControllers_HandoverManifests_DataImports_SaasBilling_WorkOnCanonicalV1Routes()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W54_SplitCtrl_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true });
        db.WarehouseLocations.Add(new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "A-01-01",
            Name = "Kệ A1",
            IsActive = true,
            IsPickable = true
        });
        db.Products.Add(new Product
        {
            Id = 201,
            Code = "SKU-SPLIT-201",
            Name = "Sản Phẩm Kiểm Thử Split Controller",
            UnitId = 1,
            MovingAverageCost = 50000m,
            IsActive = true
        });
        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 301,
            Code = "PXK-SPLIT-301",
            WarehouseId = 1,
            Status = ReceiptStatus.Approved,
            TrackingNumber = "GHN-SPLIT-301",
            CarrierCode = "GHN",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var exportServiceMock = new Mock<IExportReceiptService>();

        // 1. HandoverManifestsController
        var handoverCtrl = new HandoverManifestsController(null, db, exportServiceMock.Object);
        var scanRes = await handoverCtrl.ScanHandoverAwb(
            manifestIdRoute: null,
            request: new ExportReceiptsController.ScanHandoverAwbRequest(
                ManifestId: null,
                WarehouseId: 1,
                CarrierCode: "GHN",
                TrackingNumber: "GHN-SPLIT-301",
                ExportReceiptId: 301,
                VehiclePlate: "51D-123.45",
                DriverName: "Trần Văn Giao",
                DriverPhone: "0912345678"),
            cancellationToken: default);
        scanRes.Should().BeOfType<OkObjectResult>();

        var listRes = await handoverCtrl.GetHandoverManifests(1, "GHN", default);
        using var listDoc = JsonSerializer.SerializeToDocument(listRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        listDoc.RootElement.GetProperty("totalCount").GetInt32().Should().Be(1);

        // 2. DataImportsController
        var dataImportsCtrl = new DataImportsController(exportServiceMock.Object, db);
        var tplRes = dataImportsCtrl.GetDataImportTemplates().Should().BeOfType<OkObjectResult>().Subject;
        using var tplDoc = JsonSerializer.SerializeToDocument(tplRes.Value);
        tplDoc.RootElement.GetProperty("templates").GetArrayLength().Should().BeGreaterThan(0);

        var importRes = await dataImportsCtrl.ImportOpeningBalance(
            new ExportReceiptsController.ImportOpeningBalanceRequest(
                WarehouseId: 1,
                Items:
                [
                    new ExportReceiptsController.OpeningBalanceItemRequest(
                        ProductCode: "SKU-SPLIT-201",
                        LocationCode: "A-01-01",
                        Quantity: 25m,
                        UnitCost: 60000m,
                        LotNumber: null,
                        ExpiryDate: null)
                ]),
            default,
            null);
        importRes.Should().BeOfType<OkObjectResult>();
        (await db.InventoryStocks.SingleAsync(s => s.ProductId == 201)).Quantity.Should().Be(25m);

        // 3. SaasBillingController
        var saasBillingCtrl = new SaasBillingController(db);
        var subRes = await saasBillingCtrl.GetSubscription(null, null, default);
        subRes.Should().BeOfType<OkObjectResult>();

        var qrRes = await saasBillingCtrl.GenerateVietQrInvoice(
            new PermissionsController.CreateVietQrInvoiceRequest(PlanCode: "GROWTH", AmountVnd: 1490000m),
            default);
        using var qrDoc = JsonSerializer.SerializeToDocument(qrRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        qrDoc.RootElement.GetProperty("planCode").GetString().Should().Be("GROWTH");
    }

    [Fact]
    public async Task Wave54_PickingPathSort_And_AlternateBinSuggestion_ReturnSortedPickableBins()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W54_AltBins_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var today = DateTime.UtcNow.Date;
        var prod = new Product { Id = 10, Code = "SKU-FEFO-10", Name = "Sữa Tươi FEFO", UnitId = 1, IsActive = true };
        var locExcluded = new WarehouseLocation { Id = 101, WarehouseId = 1, Code = "A-01-01", Name = "Kệ Gốc Thiếu Hàng", IsActive = true, IsBlocked = false, IsPickable = true };
        var locFefo2 = new WarehouseLocation { Id = 102, WarehouseId = 1, Code = "B-02-02", Name = "Kệ FEFO Xa Hơn", IsActive = true, IsBlocked = false, IsPickable = true };
        var locFefo1 = new WarehouseLocation { Id = 103, WarehouseId = 1, Code = "A-02-01", Name = "Kệ FEFO Gần Nhất", IsActive = true, IsBlocked = false, IsPickable = true };
        var locExpired = new WarehouseLocation { Id = 104, WarehouseId = 1, Code = "A-01-02", Name = "Kệ Hàng Hết Hạn", IsActive = true, IsBlocked = false, IsPickable = true };

        var lotSoon = new InventoryLot { Id = 201, ProductId = 10, LotNumber = "LOT-SOON", ExpiryDate = today.AddDays(5) };
        var lotLater = new InventoryLot { Id = 202, ProductId = 10, LotNumber = "LOT-LATER", ExpiryDate = today.AddDays(30) };
        var lotExpired = new InventoryLot { Id = 203, ProductId = 10, LotNumber = "LOT-EXPIRED", ExpiryDate = today.AddDays(-1) };

        db.Products.Add(prod);
        db.WarehouseLocations.AddRange(locExcluded, locFefo2, locFefo1, locExpired);
        db.InventoryLots.AddRange(lotSoon, lotLater, lotExpired);
        db.InventoryStocks.AddRange(
            new InventoryStock { Id = 1, WarehouseId = 1, ProductId = 10, Product = prod, LocationId = 101, Location = locExcluded, LotId = 201, Lot = lotSoon, Status = InventoryStatus.Available, Quantity = 10m },
            new InventoryStock { Id = 2, WarehouseId = 1, ProductId = 10, Product = prod, LocationId = 102, Location = locFefo2, LotId = 202, Lot = lotLater, Status = InventoryStatus.Available, Quantity = 20m },
            new InventoryStock { Id = 3, WarehouseId = 1, ProductId = 10, Product = prod, LocationId = 103, Location = locFefo1, LotId = 201, Lot = lotSoon, Status = InventoryStatus.Available, Quantity = 15m },
            new InventoryStock { Id = 4, WarehouseId = 1, ProductId = 10, Product = prod, LocationId = 104, Location = locExpired, LotId = 203, Lot = lotExpired, Status = InventoryStatus.Available, Quantity = 50m });
        await db.SaveChangesAsync();

        var pickingServiceMock = new Mock<IPickingService>();
        pickingServiceMock
            .Setup(x => x.GetAsync(77, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PickingTaskDto
            {
                Id = 77,
                TaskCode = "PICK-W54-077",
                WarehouseId = 1,
                Status = "InProgress",
                Lines =
                [
                    new PickingTaskLineDto
                    {
                        Id = 701,
                        ProductId = 10,
                        ProductCode = "SKU-FEFO-10",
                        SourceLocationId = 101,
                        RequestedQuantity = 10m,
                        PickedQuantity = 2m,
                        RemainingQuantity = 8m
                    }
                ]
            });

        var controller = new PickingTasksController(pickingServiceMock.Object, db);
        var res = await controller.GetAlternateBins(77, productId: 10, excludeLocationId: 101, cancellationToken: default);
        using var doc = JsonSerializer.SerializeToDocument(res.Should().BeOfType<OkObjectResult>().Subject.Value);

        doc.RootElement.GetProperty("totalCandidates").GetInt32().Should().Be(2);
        var bins = doc.RootElement.GetProperty("alternateBins");
        bins.GetArrayLength().Should().Be(2);

        // First candidate must be LOT-SOON (expires in 5 days) at location A-02-01 (103)
        bins[0].GetProperty("locationId").GetInt32().Should().Be(103);
        bins[0].GetProperty("locationCode").GetString().Should().Be("A-02-01");
        bins[0].GetProperty("lotNumber").GetString().Should().Be("LOT-SOON");
        bins[0].GetProperty("availableQuantity").GetDecimal().Should().Be(15m);

        // Second candidate must be LOT-LATER (expires in 30 days) at location B-02-02 (102)
        bins[1].GetProperty("locationId").GetInt32().Should().Be(102);
        bins[1].GetProperty("locationCode").GetString().Should().Be("B-02-02");
        bins[1].GetProperty("lotNumber").GetString().Should().Be("LOT-LATER");
        bins[1].GetProperty("availableQuantity").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Wave54_ImportReceiptMacPreview_CalculatesExactWeightedAverageDelta()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W54_MacPreview_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var prod = new Product
        {
            Id = 301,
            Code = "SKU-MAC-301",
            Name = "Hạt Điều Rang Muối",
            UnitId = 1,
            MovingAverageCost = 100000m,
            IsActive = true
        };
        db.Products.Add(prod);
        db.InventoryStocks.Add(new InventoryStock
        {
            Id = 1,
            WarehouseId = 1,
            ProductId = 301,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            MovingAverageCost = 100000m
        });
        db.ImportReceipts.Add(new ImportReceipt
        {
            Id = 901,
            Code = "PNK-W54-901",
            WarehouseId = 1,
            Status = ReceiptStatus.ReadyToPost,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ImportReceiptDetail
                {
                    Id = 9001,
                    ImportReceiptId = 901,
                    ProductId = 301,
                    Product = prod,
                    Quantity = 10m,
                    BaseExpectedQuantity = 10m,
                    BaseAcceptedQuantity = 10m,
                    ConversionFactor = 1m,
                    UnitPrice = 150000m
                }
            ]
        });
        await db.SaveChangesAsync();

        var controller = new ImportReceiptsController(new Mock<IImportReceiptService>().Object, null, db);
        var res = await controller.GetMacPreview(901, default);
        using var doc = JsonSerializer.SerializeToDocument(res.Should().BeOfType<OkObjectResult>().Subject.Value);

        doc.RootElement.GetProperty("importReceiptId").GetInt32().Should().Be(901);
        doc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeFalse();

        var line = doc.RootElement.GetProperty("lines")[0];
        line.GetProperty("oldQuantity").GetDecimal().Should().Be(10m);
        line.GetProperty("oldMovingAverageCost").GetDecimal().Should().Be(100000m);
        line.GetProperty("incomingQuantity").GetDecimal().Should().Be(10m);
        line.GetProperty("receiptUnitCost").GetDecimal().Should().Be(150000m);
        line.GetProperty("projectedTotalQuantity").GetDecimal().Should().Be(20m);
        line.GetProperty("projectedMovingAverageCost").GetDecimal().Should().Be(125000m);
        line.GetProperty("macDelta").GetDecimal().Should().Be(25000m);
        line.GetProperty("macDeltaPct").GetDecimal().Should().Be(25.00m);
        line.GetProperty("projectedInventoryValuation").GetDecimal().Should().Be(2500000m);
    }

    [Fact]
    public async Task Wave55_OwnerGrossProfit_ReturnsNonEmptySkuValuations_And_HandoverManifests_ReturnPositivePackageCounts()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W55_ValAndHandover_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var wh = new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính WH01", IsActive = true };
        var unit = new Unit { Id = 1, Code = "THUNG", Name = "Thùng", DecimalPlaces = 0, IsActive = true };
        var prod1 = new Product
        {
            Id = 501,
            Code = "QA-E2E-001",
            Name = "Thùng linh kiện điện tử QA-01",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 120000m,
            IsActive = true
        };
        var prod2 = new Product
        {
            Id = 502,
            Code = "QA-LOT-002",
            Name = "Hộp cảm biến nhiệt độ QA-02",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 85000m,
            IsActive = true
        };

        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.Products.AddRange(prod1, prod2);
        db.InventoryStocks.AddRange(
            new InventoryStock
            {
                Id = 5001,
                TenantId = 1,
                WarehouseId = 1,
                Warehouse = wh,
                ProductId = 501,
                Product = prod1,
                Status = InventoryStatus.Available,
                Quantity = 40m,
                ReservedQuantity = 5m,
                MovingAverageCost = 120000m
            },
            new InventoryStock
            {
                Id = 5002,
                TenantId = 1,
                WarehouseId = 1,
                Warehouse = wh,
                ProductId = 502,
                Product = prod2,
                Status = InventoryStatus.Available,
                Quantity = 25m,
                ReservedQuantity = 0m,
                MovingAverageCost = 85000m
            });

        var manifestSealed = new HandoverManifest
        {
            Id = 601,
            TenantId = 1,
            ManifestCode = "MAN-VTP-20261021-01",
            WarehouseId = 1,
            CarrierCode = "VTP",
            DriverName = "Nguyễn Văn Hùng",
            DriverPhone = "0909333444",
            VehiclePlate = "51D-668.88",
            Status = "SEALED",
            TotalPackages = 0,
            CreatedBy = 1,
            CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
            SealedBy = 1,
            SealedAtUtc = DateTime.UtcNow.AddHours(-1),
            Lines =
            [
                new HandoverManifestLine { Id = 6001, ManifestId = 601, ExportReceiptId = 1, TrackingNumber = "VTP8938505001", ScannedAtUtc = DateTime.UtcNow.AddHours(-1), ScannedBy = 1 },
                new HandoverManifestLine { Id = 6002, ManifestId = 601, ExportReceiptId = 1, TrackingNumber = "VTP8938505002", ScannedAtUtc = DateTime.UtcNow.AddHours(-1), ScannedBy = 1 },
                new HandoverManifestLine { Id = 6003, ManifestId = 601, ExportReceiptId = 1, TrackingNumber = "VTP8938505003", ScannedAtUtc = DateTime.UtcNow.AddHours(-1), ScannedBy = 1 }
            ]
        };
        var manifestOpen = new HandoverManifest
        {
            Id = 602,
            TenantId = 1,
            ManifestCode = "MAN-GHN-20261021-02",
            WarehouseId = 1,
            CarrierCode = "GHN",
            DriverName = "Trần Minh Tuấn",
            DriverPhone = "0909555666",
            VehiclePlate = "51C-889.12",
            Status = "OPEN",
            TotalPackages = 0,
            CreatedBy = 1,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
            Lines =
            [
                new HandoverManifestLine { Id = 6004, ManifestId = 602, ExportReceiptId = 2, TrackingNumber = "GHN8938505002", ScannedAtUtc = DateTime.UtcNow.AddMinutes(-20), ScannedBy = 1 },
                new HandoverManifestLine { Id = 6005, ManifestId = 602, ExportReceiptId = 2, TrackingNumber = "GHN8938505005", ScannedAtUtc = DateTime.UtcNow.AddMinutes(-15), ScannedBy = 1 }
            ]
        };
        db.HandoverManifests.AddRange(manifestSealed, manifestOpen);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // 1a. Admin/Manager with inventory.cost.read -> skuValuations has >= 2 SKUs with positive cost & valuation
        var adminUser = new Mock<ICurrentUser>();
        adminUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        adminUser.SetupGet(x => x.UserId).Returns(1);
        adminUser.SetupGet(x => x.Role).Returns(AppRoles.Admin);
        adminUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);
        adminUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(true);

        var reportsAdminCtrl = new ReportsController(new Mock<IReportService>().Object, db, whAuth.Object, adminUser.Object);
        var adminRes = await reportsAdminCtrl.GetOwnerGrossProfit(1, default);
        using var adminDoc = JsonSerializer.SerializeToDocument(adminRes.Should().BeOfType<OkObjectResult>().Subject.Value);

        adminDoc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeFalse();
        var skuValuations = adminDoc.RootElement.GetProperty("skuValuations");
        skuValuations.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        adminDoc.RootElement.GetProperty("skuItems").GetArrayLength().Should().Be(skuValuations.GetArrayLength());
        adminDoc.RootElement.GetProperty("items").GetArrayLength().Should().Be(skuValuations.GetArrayLength());

        foreach (var item in skuValuations.EnumerateArray())
        {
            var qty = item.GetProperty("onHandQuantity").GetDecimal();
            var mac = item.GetProperty("movingAverageCost").GetDecimal();
            var totalVal = item.GetProperty("totalValuationAmount").GetDecimal();
            qty.Should().BeGreaterThan(0m);
            mac.Should().BeGreaterThan(0m);
            totalVal.Should().Be(Math.Round(qty * mac, 0));
        }

        // 1b. WarehouseStaff without inventory.cost.read -> movingAverageCost == null & totalValuationAmount == null
        var staffUser = new Mock<ICurrentUser>();
        staffUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        staffUser.SetupGet(x => x.UserId).Returns(15);
        staffUser.SetupGet(x => x.Role).Returns(AppRoles.WarehouseStaff);
        staffUser.SetupGet(x => x.IsGlobalAdmin).Returns(false);
        staffUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(false);

        var reportsStaffCtrl = new ReportsController(new Mock<IReportService>().Object, db, whAuth.Object, staffUser.Object);
        var staffRes = await reportsStaffCtrl.GetOwnerGrossProfit(1, default);
        using var staffDoc = JsonSerializer.SerializeToDocument(staffRes.Should().BeOfType<OkObjectResult>().Subject.Value);

        staffDoc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeTrue();
        var maskedValuations = staffDoc.RootElement.GetProperty("skuValuations");
        maskedValuations.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        foreach (var item in maskedValuations.EnumerateArray())
        {
            item.GetProperty("onHandQuantity").GetDecimal().Should().BeGreaterThan(0m);
            item.GetProperty("movingAverageCost").ValueKind.Should().Be(JsonValueKind.Null);
            item.GetProperty("totalValuationAmount").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // 2. HandoverManifestsController.GetHandoverManifests -> totalPackages >= 1 and Vietnamese statusLabel
        var handoverCtrl = new HandoverManifestsController(null, db, new Mock<IExportReceiptService>().Object, whAuth.Object, adminUser.Object);
        var manifestRes = await handoverCtrl.GetHandoverManifests(1, null, default);
        using var manifestDoc = JsonSerializer.SerializeToDocument(manifestRes.Should().BeOfType<OkObjectResult>().Subject.Value);

        var manifestItems = manifestDoc.RootElement.GetProperty("items");
        manifestItems.GetArrayLength().Should().Be(2);

        // Ordered by Id DESC -> first is 602 (OPEN, 2 lines), second is 601 (SEALED, 3 lines)
        manifestItems[0].GetProperty("manifestCode").GetString().Should().Be("MAN-GHN-20261021-02");
        manifestItems[0].GetProperty("totalPackages").GetInt32().Should().Be(2);
        manifestItems[0].GetProperty("totalParcels").GetInt32().Should().Be(2);
        manifestItems[0].GetProperty("awbCount").GetInt32().Should().Be(2);
        manifestItems[0].GetProperty("statusLabel").GetString().Should().Be("Đang mở nhận kiện");

        manifestItems[1].GetProperty("manifestCode").GetString().Should().Be("MAN-VTP-20261021-01");
        manifestItems[1].GetProperty("totalPackages").GetInt32().Should().Be(3);
        manifestItems[1].GetProperty("totalParcels").GetInt32().Should().Be(3);
        manifestItems[1].GetProperty("awbCount").GetInt32().Should().Be(3);
        manifestItems[1].GetProperty("statusLabel").GetString().Should().Be("Đã niêm phong & Bàn giao");
    }

    [Fact]
    public async Task Wave56_StocktakeCountAttemptsAndAutoApproval_EvaluatesOnePercentAnd500kThreshold()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W56_StocktakeAutoApproval_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var wh = new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var prodWithin = new Product
        {
            Id = 601,
            Code = "SKU-WITHIN-601",
            Name = "Cáp tín hiệu trong ngưỡng tự động duyệt",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 40000m,
            IsActive = true
        };
        var prodExceed = new Product
        {
            Id = 602,
            Code = "SKU-EXCEED-602",
            Name = "Cảm biến công nghiệp vượt ngưỡng duyệt",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 300000m,
            IsActive = true
        };
        var loc = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BIN-A01-01",
            Name = "Kệ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true
        };
        var stocktake = new Stocktake
        {
            Id = 701,
            Code = "KK-W56-701",
            WarehouseId = 1,
            Warehouse = wh,
            Status = StocktakeStatus.InProgress,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            Details =
            [
                // System 200, Actual 199 -> variance -1 (0.5% <= 1.0%) and 40,000 VND (<= 500,000 VND) -> autoApproveEligible = true
                new StocktakeDetail
                {
                    Id = 7001,
                    StocktakeId = 701,
                    ProductId = 601,
                    Product = prodWithin,
                    LocationId = 10,
                    Location = loc,
                    SystemQuantity = 200m,
                    ActualQuantity = 199m,
                    DifferenceQuantity = -1m,
                    Note = "Đếm lần 1"
                },
                // System 100, Actual 96 -> variance -4 (4.0% > 1.0%) and 1,200,000 VND (> 500,000 VND) -> requiresManagerApproval = true
                new StocktakeDetail
                {
                    Id = 7002,
                    StocktakeId = 701,
                    ProductId = 602,
                    Product = prodExceed,
                    LocationId = 10,
                    Location = loc,
                    SystemQuantity = 100m,
                    ActualQuantity = 96m,
                    DifferenceQuantity = -4m,
                    Note = "Đếm lần 1"
                }
            ]
        };

        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.Products.AddRange(prodWithin, prodExceed);
        db.WarehouseLocations.Add(loc);
        db.Stocktakes.Add(stocktake);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var adminUser = new Mock<ICurrentUser>();
        adminUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        adminUser.SetupGet(x => x.UserId).Returns(1);
        adminUser.SetupGet(x => x.Role).Returns(AppRoles.Admin);
        adminUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);
        adminUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(true);

        var controller = new StocktakesController(
            new Mock<IStocktakeService>().Object,
            new Mock<IStocktakeQueryService>().Object,
            db,
            whAuth.Object,
            adminUser.Object);

        // 1. Submit Recount Attempt #2 for detail 7002 and evaluate auto-approval
        var evalRes = await controller.EvaluateAutoApproval(
            "701",
            new StocktakesController.EvaluateAutoApprovalRequest(
                ThresholdPct: 1.0m,
                ThresholdValueVnd: 500000m,
                RecountSubmissions:
                [
                    new StocktakesController.RecountSubmissionDto(
                        DetailId: 7002,
                        ProductId: 602,
                        CountedQuantity: 97m,
                        Note: "Đếm chéo lần 2 xác nhận thiếu 3 cái")
                ]),
            default);

        using var evalDoc = JsonSerializer.SerializeToDocument(evalRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        evalDoc.RootElement.GetProperty("autoApprovalThresholdPct").GetDecimal().Should().Be(1.0m);
        evalDoc.RootElement.GetProperty("autoApprovalThresholdValueVnd").GetDecimal().Should().Be(500000m);
        evalDoc.RootElement.GetProperty("autoApprovalDecision").GetString().Should().Be("ESCALATED_TO_MANAGER_APPROVAL");

        var lines = evalDoc.RootElement.GetProperty("lines").EnumerateArray().ToList();
        lines.Should().HaveCount(2);

        var lineWithin = lines.Single(x => x.GetProperty("productCode").GetString() == "SKU-WITHIN-601");
        lineWithin.GetProperty("variancePct").GetDecimal().Should().BeLessThanOrEqualTo(1.0m);
        lineWithin.GetProperty("varianceValueVnd").GetDecimal().Should().BeLessThanOrEqualTo(500000m);
        lineWithin.GetProperty("autoApproveEligible").GetBoolean().Should().BeTrue();
        lineWithin.GetProperty("requiresManagerApproval").GetBoolean().Should().BeFalse();
        lineWithin.GetProperty("approvalStatus").GetString().Should().Be("AUTO_APPROVED_WITHIN_THRESHOLD");

        var lineExceed = lines.Single(x => x.GetProperty("productCode").GetString() == "SKU-EXCEED-602");
        lineExceed.GetProperty("countedQty").GetDecimal().Should().Be(97m);
        lineExceed.GetProperty("variancePct").GetDecimal().Should().Be(3.0m);
        lineExceed.GetProperty("varianceValueVnd").GetDecimal().Should().Be(900000m);
        lineExceed.GetProperty("autoApproveEligible").GetBoolean().Should().BeFalse();
        lineExceed.GetProperty("requiresManagerApproval").GetBoolean().Should().BeTrue();
        lineExceed.GetProperty("attemptCount").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        lineExceed.GetProperty("attemptHistory").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        // 2. GET count-attempts-and-auto-approval returns consistent multi-round history
        var getRes = await controller.GetCountAttemptsAndAutoApproval("701", null, 1.0m, 500000m, default);
        using var getDoc = JsonSerializer.SerializeToDocument(getRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        getDoc.RootElement.GetProperty("summary").GetProperty("autoApprovedCount").GetInt32().Should().Be(1);
        getDoc.RootElement.GetProperty("summary").GetProperty("requiresManagerApprovalCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Wave56_CodAndOwnerGrossProfit_IncludeWithheldTax15PctAndFixed3000Fee()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W56_TaxAndFee_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var wh = new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var prod = new Product
        {
            Id = 701,
            Code = "SKU-TAX-701",
            Name = "Tai nghe Bluetooth True Wireless",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 400000m,
            IsActive = true
        };
        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.Products.Add(prod);

        // Dispatched export receipt: 2 units * 1,000,000 VND = 2,000,000 VND gross revenue
        var exportReceipt = new ExportReceipt
        {
            Id = 801,
            Code = "PXK-TAX-801",
            WarehouseId = 1,
            Status = ReceiptStatus.Dispatched,
            TrackingNumber = "GHN-TAX-801",
            CarrierCode = "GHN",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            Details =
            [
                new ExportReceiptDetail
                {
                    Id = 8011,
                    ExportReceiptId = 801,
                    ProductId = 701,
                    Product = prod,
                    Quantity = 2m,
                    UnitPrice = 1000000m
                }
            ]
        };
        db.ExportReceipts.Add(exportReceipt);

        db.ShipmentFinancialLedgers.Add(new ShipmentFinancialLedger
        {
            Id = 901,
            ExportReceiptId = 801,
            ExportReceipt = exportReceipt,
            TrackingNumber = "GHN-TAX-801",
            CarrierCode = "GHN",
            ExpectedCodAmount = 2000000m,
            ActualCodCollected = 2000000m,
            EstimatedShippingFee = 30000m,
            ActualShippingFee = 32000m,
            FeeOverchargeVariance = 2000m,
            PlatformCommissionFee = 120000m,
            ReturnFee = 0m,
            NetPayoutAmount = 1848000m,
            ReconciliationStatus = "DISCREPANCY",
            SettledAtUtc = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var adminUser = new Mock<ICurrentUser>();
        adminUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        adminUser.SetupGet(x => x.UserId).Returns(1);
        adminUser.SetupGet(x => x.Role).Returns(AppRoles.Admin);
        adminUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);
        adminUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(true);

        var reportsController = new ReportsController(new Mock<IReportService>().Object, db, whAuth.Object, adminUser.Object);

        // 1. COD & Settlement Reconciliation verifies 1% VAT + 0.5% PIT = 1.5% and 3,000 VND fixed order fee
        var codRes = await reportsController.GetCodAndSettlementReconciliation(1, null, default);
        using var codDoc = JsonSerializer.SerializeToDocument(codRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        codDoc.RootElement.GetProperty("totalExpectedCod").GetDecimal().Should().Be(2000000m);
        codDoc.RootElement.GetProperty("totalVatWithheldAmount").GetDecimal().Should().Be(20000m); // 1% of 2,000,000
        codDoc.RootElement.GetProperty("totalPitWithheldAmount").GetDecimal().Should().Be(10000m); // 0.5% of 2,000,000
        codDoc.RootElement.GetProperty("totalWithheldTaxAmount").GetDecimal().Should().Be(30000m); // 1.5% total
        codDoc.RootElement.GetProperty("totalFixedOrderProcessingFee").GetDecimal().Should().Be(3000m); // 1 order * 3,000
        codDoc.RootElement.GetProperty("taxPolicyReference").GetString().Should().Contain("Nghị định 70/2025/NĐ-CP");

        // 2. Owner Gross Profit verifies withheldVatAmount, withheldPitAmount, withheldTaxTotalAmount, fixedInfrastructureFeeTotal, netProfitAfterWithheldTax
        var pnlRes = await reportsController.GetOwnerGrossProfit(1, default);
        using var pnlDoc = JsonSerializer.SerializeToDocument(pnlRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        pnlDoc.RootElement.GetProperty("grossRevenue").GetDecimal().Should().Be(2000000m);
        pnlDoc.RootElement.GetProperty("withheldVatAmount").GetDecimal().Should().Be(20000m);
        pnlDoc.RootElement.GetProperty("withheldPitAmount").GetDecimal().Should().Be(10000m);
        pnlDoc.RootElement.GetProperty("withheldTaxTotalAmount").GetDecimal().Should().Be(30000m);
        pnlDoc.RootElement.GetProperty("fixedInfrastructureFeeTotal").GetDecimal().Should().Be(3000m);
        var netPocket = pnlDoc.RootElement.GetProperty("netPocketProfit").GetDecimal();
        pnlDoc.RootElement.GetProperty("netProfitAfterWithheldTax").GetDecimal().Should().Be(netPocket - 30000m - 3000m);
    }

    [Fact]
    public async Task Wave56_PackingRecommendCarton3D_And_DekitDisassemble_ExecuteCorrectly()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W56_Packing3DAndDekit_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var wh = new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
        var loc = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BIN-A01-01",
            Name = "Kệ Lưu Trữ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true
        };
        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var kitProd = new Product
        {
            Id = 801,
            Code = "KIT-COMBO-801",
            Name = "Bộ Kit Quà Tặng Cao Cấp",
            UnitId = 1,
            Unit = unit,
            UnitWeightKg = 1.2m,
            UnitVolumeM3 = 0.0045m,
            MovingAverageCost = 350000m,
            IsActive = true
        };
        var compProd = new Product
        {
            Id = 802,
            Code = "COMP-ITEM-802",
            Name = "Sản Phẩm Thành Phần Lẻ",
            UnitId = 1,
            Unit = unit,
            UnitWeightKg = 0.5m,
            UnitVolumeM3 = 0.0020m,
            MovingAverageCost = 150000m,
            IsActive = true
        };
        db.Warehouses.Add(wh);
        db.WarehouseLocations.Add(loc);
        db.Units.Add(unit);
        db.Products.AddRange(kitProd, compProd);

        db.ProductBundleComponents.Add(new ProductBundleComponent
        {
            Id = 1,
            ParentProductId = 801,
            ParentProduct = kitProd,
            ComponentProductId = 802,
            ComponentProduct = compProd,
            ComponentQuantity = 2m,
            IsActive = true
        });

        var kitStock = new InventoryStock
        {
            Id = 8001,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 801,
            Product = kitProd,
            LocationId = 10,
            Location = loc,
            Status = InventoryStatus.Available,
            Quantity = 5m,
            ReservedQuantity = 0m
        };
        var compStock = new InventoryStock
        {
            Id = 8002,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 802,
            Product = compProd,
            LocationId = 10,
            Location = loc,
            Status = InventoryStatus.Available,
            Quantity = 10m,
            ReservedQuantity = 0m
        };
        kitStock.RefreshGrainHash();
        compStock.RefreshGrainHash();
        db.InventoryStocks.AddRange(kitStock, compStock);

        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 850,
            Code = "PXK-CARTON-850",
            WarehouseId = 1,
            Status = ReceiptStatus.Approved,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail
                {
                    Id = 8501,
                    ExportReceiptId = 850,
                    ProductId = 801,
                    Product = kitProd,
                    Quantity = 2m,
                    UnitPrice = 500000m
                }
            ]
        });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new PackingSessionsController(new Mock<IPackingService>().Object, db, whAuth.Object);

        // 1. 3D Cartonization & DIM Weight Calculator
        var cartonRes = await controller.RecommendCarton(
            "850",
            new PackingSessionsController.RecommendCartonRequest(
                DimDivisor: 5000m,
                ServiceType: "ROAD_STANDARD",
                FragileBubbleWrapAllowancePct: 15m),
            default);

        using var cartonDoc = JsonSerializer.SerializeToDocument(cartonRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        cartonDoc.RootElement.GetProperty("recommendedCartonCode").GetString().Should().BeOneOf("CARTON-S", "CARTON-M", "CARTON-L", "CARTON-XL");
        cartonDoc.RootElement.GetProperty("totalItemWeightKg").GetDecimal().Should().Be(2.4m); // 2 * 1.2kg
        cartonDoc.RootElement.GetProperty("rawVolumeCm3").GetDecimal().Should().Be(9000m); // 2 * 4500cm3
        cartonDoc.RootElement.GetProperty("dimWeightKg").GetDecimal().Should().BeGreaterThan(0m);
        cartonDoc.RootElement.GetProperty("billableWeightKg").GetDecimal().Should().BeGreaterThanOrEqualTo(2.4m);

        // 2. De-kitting / Disassemble 2 kits -> Kit stock 5 -> 3, Component stock 10 -> 14 (+4 units)
        var dekitRes = await controller.ExecuteKittingVas(
            new PackingSessionsController.ExecuteKittingVasRequest(
                WarehouseId: 1,
                KitProductId: 801,
                LocationId: 10,
                KitQuantity: 2m,
                OperationMode: "DEKIT_DISASSEMBLE",
                ExecuteStockMutation: true),
            default);

        using var dekitDoc = JsonSerializer.SerializeToDocument(dekitRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        dekitDoc.RootElement.GetProperty("operationMode").GetString().Should().Be("DEKIT_DISASSEMBLE");
        dekitDoc.RootElement.GetProperty("executedStockMutation").GetBoolean().Should().BeTrue();

        var updatedKitStock = await db.InventoryStocks.SingleAsync(s => s.Id == 8001);
        var updatedCompStock = await db.InventoryStocks.SingleAsync(s => s.Id == 8002);
        updatedKitStock.Quantity.Should().Be(3m);
        updatedCompStock.Quantity.Should().Be(14m);

        var dekitTxs = await db.InventoryTransactions.Where(t => t.ReferenceType == "VAS_DEKIT").ToListAsync();
        dekitTxs.Should().Contain(t => t.ProductId == 801 && t.TransactionType == TransactionType.TransferOut && t.Quantity == 2m);
        dekitTxs.Should().Contain(t => t.ProductId == 802 && t.TransactionType == TransactionType.TransferIn && t.Quantity == 4m);
    }

    [Fact]
    public async Task Wave56_MoveHandlingUnitLpn_And_AutoBreakBulkReplenish_ExecuteAtomically()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W56_LpnAndBreakBulk_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var wh = new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
        var locRcv = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "RCV-01",
            Name = "Khu Tiếp Nhận",
            LocationType = WarehouseLocationType.Receiving,
            IsActive = true,
            IsPickable = false
        };
        var locPick = new WarehouseLocation
        {
            Id = 11,
            WarehouseId = 1,
            Code = "BIN-A01-01",
            Name = "Kệ Nhặt Lẻ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true
        };
        var unitPcs = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var unitCarton = new Unit { Id = 2, Code = "THUNG24", Name = "Thùng 24 Cái", DecimalPlaces = 0, IsActive = true };
        var prod1 = new Product
        {
            Id = 901,
            Code = "SKU-LPN-901",
            Name = "Linh Kiện Pallet 901",
            UnitId = 1,
            Unit = unitPcs,
            UnitWeightKg = 0.5m,
            UnitVolumeM3 = 0.002m,
            IsActive = true
        };
        var prod2 = new Product
        {
            Id = 902,
            Code = "SKU-LPN-902",
            Name = "Linh Kiện Pallet 902",
            UnitId = 1,
            Unit = unitPcs,
            UnitWeightKg = 0.4m,
            UnitVolumeM3 = 0.0015m,
            IsActive = true
        };
        db.Warehouses.Add(wh);
        db.WarehouseLocations.AddRange(locRcv, locPick);
        db.Units.AddRange(unitPcs, unitCarton);
        db.Products.AddRange(prod1, prod2);

        db.ProductUoms.Add(new ProductUom
        {
            Id = 1,
            ProductId = 901,
            Product = prod1,
            UnitId = 2,
            Unit = unitCarton,
            ConversionFactor = 24m,
            Version = 1,
            EffectiveFromUtc = DateTime.UtcNow.AddDays(-10),
            IsActive = true
        });

        var palletHu = new HandlingUnit
        {
            Id = 50,
            HuCode = "PALLET-LPN-001",
            WarehouseId = 1,
            Type = HandlingUnitType.Pallet,
            Status = HandlingUnitStatus.InUse,
            Barcode = "BC-PALLET-LPN-001",
            Sscc = "008938505000000099",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.HandlingUnits.Add(palletHu);

        var huBucket1 = new InventoryStock
        {
            Id = 9001,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 901,
            Product = prod1,
            LocationId = 10,
            Location = locRcv,
            HandlingUnitId = 50,
            Status = InventoryStatus.Available,
            Quantity = 8m
        };
        var huBucket2 = new InventoryStock
        {
            Id = 9002,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 902,
            Product = prod2,
            LocationId = 10,
            Location = locRcv,
            HandlingUnitId = 50,
            Status = InventoryStatus.Available,
            Quantity = 6m
        };
        var reserveBulkStock = new InventoryStock
        {
            Id = 9003,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 901,
            Product = prod1,
            LocationId = 10,
            Location = locRcv,
            HandlingUnitId = null,
            Status = InventoryStatus.Available,
            Quantity = 48m
        };
        huBucket1.RefreshGrainHash();
        huBucket2.RefreshGrainHash();
        reserveBulkStock.RefreshGrainHash();
        db.InventoryStocks.AddRange(huBucket1, huBucket2, reserveBulkStock);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);
        currentUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);

        var controller = new InventoryMovementsController(
            null!,
            db,
            new Mock<IInventoryLockEvaluator>().Object,
            currentUser.Object,
            whAuth.Object);

        // 1. 1-Scan Full Pallet / LPN Move moves both buckets (8 + 6 = 14 units) from RCV-01 to BIN-A01-01
        var moveRes = await controller.MoveHandlingUnit(
            new InventoryMovementsController.FlexibleMoveHandlingUnitRequest(
                HandlingUnitCode: "PALLET-LPN-001",
                WarehouseId: 1,
                DestinationLocationCode: "BIN-A01-01",
                Reason: "Cất nguyên Pallet 1 chạm vào kệ A01"),
            default);

        using var moveDoc = JsonSerializer.SerializeToDocument(moveRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        moveDoc.RootElement.GetProperty("handlingUnitCode").GetString().Should().Be("PALLET-LPN-001");
        moveDoc.RootElement.GetProperty("destinationLocationCode").GetString().Should().Be("BIN-A01-01");
        moveDoc.RootElement.GetProperty("movedBucketCount").GetInt32().Should().Be(2);
        moveDoc.RootElement.GetProperty("totalMovedQuantity").GetDecimal().Should().Be(14m);

        var movedDestStocks = await db.InventoryStocks
            .Where(s => s.HandlingUnitId == 50 && s.LocationId == 11 && s.Quantity > 0m)
            .ToListAsync();
        movedDestStocks.Should().HaveCount(2);
        movedDestStocks.Sum(s => s.Quantity).Should().Be(14m);

        // 2. Auto Break-Bulk Replenishment converts 1 carton (24 base units) of SKU 901 from RCV-01 to BIN-A01-01
        var breakBulkRes = await controller.AutoBreakBulkReplenish(
            new AutoBreakBulkReplenishRequestDto(
                WarehouseId: 1,
                ProductId: 901,
                CartonQuantity: 1m,
                MinPickFaceThresholdQty: 100m,
                ExecuteImmediately: true),
            warehouseId: 1,
            productId: 901,
            executeImmediately: true,
            cancellationToken: default);

        using var bbDoc = JsonSerializer.SerializeToDocument(breakBulkRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        bbDoc.RootElement.GetProperty("executedImmediately").GetBoolean().Should().BeTrue();
        bbDoc.RootElement.GetProperty("totalCartonQty").GetDecimal().Should().Be(1m);
        bbDoc.RootElement.GetProperty("totalBaseUnitQty").GetDecimal().Should().Be(24m);

        var updatedReserveStock = await db.InventoryStocks.SingleAsync(s => s.Id == 9003);
        updatedReserveStock.Quantity.Should().Be(24m); // 48 - 24 = 24 remaining in reserve
    }

    [Fact]
    public async Task Wave56_PutToWallSlots_And_SortValidation_EnforceSlotMatch()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W56_PutToWall_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var wh = new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
        var loc = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BIN-A01-01",
            Name = "Kệ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true
        };
        var prod1 = new Product { Id = 1001, Code = "SKU-WALL-01", Name = "Sản phẩm Chia chọn 01", UnitId = 1, IsActive = true };
        var prod2 = new Product { Id = 1002, Code = "SKU-WALL-02", Name = "Sản phẩm Chia chọn 02", UnitId = 1, IsActive = true };
        var task = new PickingTask
        {
            Id = 301,
            TaskCode = "PICK-WALL-301",
            WarehouseId = 1,
            Warehouse = wh,
            SourceType = "SalesOrder",
            SourceId = 501,
            SourceCode = "SO-WALL-501",
            Status = PickingTaskStatus.InProgress,
            Priority = 1,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Lines =
            [
                new PickingTaskLine
                {
                    Id = 3011,
                    PickingTaskId = 301,
                    Sequence = 1,
                    ProductId = 1001,
                    Product = prod1,
                    SourceLocationId = 10,
                    SourceLocation = loc,
                    RequestedQuantity = 3m,
                    PickedQuantity = 3m
                },
                new PickingTaskLine
                {
                    Id = 3012,
                    PickingTaskId = 301,
                    Sequence = 2,
                    ProductId = 1002,
                    Product = prod2,
                    SourceLocationId = 10,
                    SourceLocation = loc,
                    RequestedQuantity = 2m,
                    PickedQuantity = 2m
                }
            ]
        };

        db.Warehouses.Add(wh);
        db.WarehouseLocations.Add(loc);
        db.Products.AddRange(prod1, prod2);
        db.PickingTasks.Add(task);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var controller = new PickingTasksController(new Mock<IPickingService>().Object, db, whAuth.Object);

        // 1. GetPutToWallSlots returns 8 slots (SLOT-01 .. SLOT-08)
        var slotsRes = await controller.GetPutToWallSlots("301", 1, default);
        using var slotsDoc = JsonSerializer.SerializeToDocument(slotsRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        slotsDoc.RootElement.GetProperty("totalSlots").GetInt32().Should().Be(8);
        slotsDoc.RootElement.GetProperty("activeSlotsCount").GetInt32().Should().Be(2);
        slotsDoc.RootElement.GetProperty("slots").GetArrayLength().Should().Be(8);

        // 2. Scanning correct SKU + SLOT-01 succeeds
        var sortOkRes = await controller.PutToWallSort(
            "301",
            new PickingTasksController.PutToWallSortRequest(
                ScannedSkuOrBarcode: "SKU-WALL-01",
                ScannedSlotCode: "SLOT-01",
                Quantity: 1m),
            default);
        using var sortOkDoc = JsonSerializer.SerializeToDocument(sortOkRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        sortOkDoc.RootElement.GetProperty("matched").GetBoolean().Should().BeTrue();
        sortOkDoc.RootElement.GetProperty("sortedSlotCode").GetString().Should().Be("SLOT-01");
        sortOkDoc.RootElement.GetProperty("slotSortedTotal").GetDecimal().Should().Be(1m);
        sortOkDoc.RootElement.GetProperty("slotRemainingQty").GetDecimal().Should().Be(2m);

        // 3. Scanning SKU-WALL-01 into wrong slot SLOT-02 returns 422 PUT_TO_WALL_WRONG_SLOT
        var sortWrongRes = await controller.PutToWallSort(
            "301",
            new PickingTasksController.PutToWallSortRequest(
                ScannedSkuOrBarcode: "SKU-WALL-01",
                ScannedSlotCode: "SLOT-02",
                Quantity: 1m),
            default);
        var unprocessable = sortWrongRes.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        unprocessable.StatusCode.Should().Be(422);
        using var wrongDoc = JsonSerializer.SerializeToDocument(unprocessable.Value);
        wrongDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("PUT_TO_WALL_WRONG_SLOT");
        wrongDoc.RootElement.GetProperty("expectedSlotCode").GetString().Should().Be("SLOT-01");
    }

    [Fact]
    public async Task Wave56_CustomerReturnsAging7Days_And_ChannelSkuMappings_And_3PlStorageSnapshot()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"W56_RtsChannel3Pl_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        await using var db = new ErpKhoDbContext(options);

        var wh = new Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
        var loc = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BIN-A01-01",
            Name = "Kệ Lưu Trữ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true
        };
        var owner = new BusinessPartner
        {
            Id = 20,
            Code = "OWN-3PL-01",
            Name = "Chủ Hàng 3PL Minh Phát",
            IsCustomer = true,
            IsActive = true
        };
        var prod = new Product
        {
            Id = 1101,
            Code = "SKU-3PL-1101",
            Name = "Thùng Sữa Dinh Dưỡng 3PL",
            CategoryId = 1,
            UnitId = 1,
            UnitWeightKg = 4.5m,
            UnitVolumeM3 = 0.06m,
            MovingAverageCost = 450000m,
            IsActive = true
        };
        db.Warehouses.Add(wh);
        db.WarehouseLocations.Add(loc);
        db.BusinessPartners.Add(owner);
        db.Products.Add(prod);

        var stock = new InventoryStock
        {
            Id = 11001,
            TenantId = 1,
            WarehouseId = 1,
            Warehouse = wh,
            ProductId = 1101,
            Product = prod,
            LocationId = 10,
            Location = loc,
            OwnerId = 20,
            Status = InventoryStatus.Available,
            Quantity = 40m,
            ReservedQuantity = 4m
        };
        stock.RefreshGrainHash();
        db.InventoryStocks.Add(stock);

        db.CustomerReturns.AddRange(
            new CustomerReturn
            {
                Id = 401,
                ReturnCode = "RMA-RTS-OVERDUE-09D",
                WarehouseId = 1,
                TrackingNumber = "GHN-RTS-OVERDUE-09D",
                OrderNumber = "SO-RTS-09D",
                ReturnReason = "RTS_BUYER_UNREACHABLE",
                Status = "PENDING_INSPECTION",
                CreatedBy = 1,
                CreatedAtUtc = DateTime.UtcNow.AddDays(-9)
            },
            new CustomerReturn
            {
                Id = 402,
                ReturnCode = "RMA-RTS-48H-CLAIM",
                WarehouseId = 1,
                TrackingNumber = "SPX-RTS-48H-CLAIM",
                OrderNumber = "SO-RTS-48H",
                ReturnReason = "RTS_DAMAGED_PARCEL",
                Status = "PENDING_INSPECTION",
                CreatedBy = 1,
                CreatedAtUtc = DateTime.UtcNow.AddHours(-12)
            });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // 1. CustomerReturns aging > 7 days alert & 48h claim window countdown
        var importCtrl = new ImportReceiptsController(new Mock<IImportReceiptService>().Object, null, db, whAuth.Object);
        var returnsRes = await importCtrl.GetCustomerReturns(1, default);
        using var returnsDoc = JsonSerializer.SerializeToDocument(returnsRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var returnItems = returnsDoc.RootElement.EnumerateArray().ToList();
        returnItems.Should().HaveCount(2);

        var overdueItem = returnItems.Single(x => x.GetProperty("ReturnCode").GetString() == "RMA-RTS-OVERDUE-09D");
        overdueItem.GetProperty("returnAgingDays").GetInt32().Should().BeGreaterThanOrEqualTo(9);
        overdueItem.GetProperty("isOverdueInTransit").GetBoolean().Should().BeTrue();
        overdueItem.GetProperty("overdueAlertLevel").GetString().Should().Be("CRITICAL_OVERDUE_7D");

        var claimItem = returnItems.Single(x => x.GetProperty("ReturnCode").GetString() == "RMA-RTS-48H-CLAIM");
        claimItem.GetProperty("isOverdueInTransit").GetBoolean().Should().BeFalse();
        claimItem.GetProperty("disputeRemainingHours").GetDecimal().Should().BeGreaterThan(20m);
        claimItem.GetProperty("disputeClaimStatus").GetString().Should().Be("PENDING_VIDEO_EVIDENCE");

        // 2. Multi-Channel SKU Mapping (1 internal SKU -> Shopee Combo x2)
        var productsCtrl = new ProductsController(new Mock<IProductService>().Object, db);
        var upsertRes = await productsCtrl.UpsertChannelSkuMapping(
            new ProductsController.UpsertChannelSkuMappingRequest(
                ChannelCode: "SHOPEE",
                ShopName: "Gian hàng Shopee Official",
                ChannelSkuCode: "SP-COMBO-02",
                ChannelItemId: "SP-ITEM-1101",
                ProductId: 1101,
                ConversionFactor: 2m,
                IsActive: true),
            default);
        using var upsertDoc = JsonSerializer.SerializeToDocument(upsertRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        upsertDoc.RootElement.GetProperty("channelSkuCode").GetString().Should().Be("SP-COMBO-02");
        upsertDoc.RootElement.GetProperty("conversionFactor").GetDecimal().Should().Be(2m);

        var listMappingsRes = await productsCtrl.GetChannelSkuMappings(1101, null, "SHOPEE", default);
        using var listMappingsDoc = JsonSerializer.SerializeToDocument(listMappingsRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var mappingRow = listMappingsDoc.RootElement.GetProperty("items")[0];
        mappingRow.GetProperty("internalAvailableQty").GetDecimal().Should().Be(36m); // 40 - 4 reserved
        mappingRow.GetProperty("maxChannelSellableQty").GetDecimal().Should().Be(18m); // floor(36 / 2) = 18 combos

        // 3. 3PL Daily Storage Billing Snapshot (CBM / Pallet / Bin fee calculation)
        var reportsCtrl = new ReportsController(new Mock<IReportService>().Object, db, whAuth.Object);
        var snapRes = await reportsCtrl.GetOrCaptureThreePlStorageSnapshot(1, 20, default);
        using var snapDoc = JsonSerializer.SerializeToDocument(snapRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        snapDoc.RootElement.GetProperty("ownerCount").GetInt32().Should().Be(1);
        snapDoc.RootElement.GetProperty("totalOccupiedBins").GetInt32().Should().Be(1);
        snapDoc.RootElement.GetProperty("totalVolumeCbm").GetDecimal().Should().Be(2.4m); // 40 * 0.06m3 = 2.4 CBM
        snapDoc.RootElement.GetProperty("totalPalletEquivalent").GetDecimal().Should().Be(2.0m); // 2.4 / 1.2 = 2.0 pallets
        snapDoc.RootElement.GetProperty("totalEstimatedDailyStorageFeeVnd").GetDecimal().Should().Be(41000m); // (2 * 18,000) + (1 * 5,000)
    }
}
