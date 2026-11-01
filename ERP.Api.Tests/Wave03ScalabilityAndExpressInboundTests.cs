using System.Reflection;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public class Wave03ScalabilityAndExpressInboundTests
{
    private static ErpKhoDbContext CreateInMemoryDb(string name)
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"{name}_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ErpKhoDbContext(options);
    }

    [Fact]
    public async Task TC_W3_01_ImportAndExportPipelineSummary_ExecuteSqlGroupBy_WithoutInMemoryGetAll()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W3_01_ImportAndExportPipelineSummary_ExecuteSqlGroupBy_WithoutInMemoryGetAll));

        var now = DateTime.UtcNow;
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true });
        db.ImportReceipts.AddRange(
            new ImportReceipt { Id = 101, Code = "IR-101", WarehouseId = 1, Status = ReceiptStatus.Draft, CreatedBy = 1, Note = "Phiếu trả hàng RMA" },
            new ImportReceipt { Id = 102, Code = "IR-102", WarehouseId = 1, Status = ReceiptStatus.QcPending, CreatedBy = 1 },
            new ImportReceipt { Id = 103, Code = "IR-103", WarehouseId = 1, Status = ReceiptStatus.Posted, CreatedBy = 1 },
            new ImportReceipt { Id = 104, Code = "IR-104", WarehouseId = 1, Status = ReceiptStatus.Cancelled, CreatedBy = 1 });
        db.ExportReceipts.AddRange(
            new ExportReceipt { Id = 201, Code = "ER-201", WarehouseId = 1, Status = ReceiptStatus.Draft, CreatedBy = 1, MarketplaceOrderId = "SP-201", MarketplaceShipByUtc = now.AddMinutes(-30) },
            new ExportReceipt { Id = 202, Code = "ER-202", WarehouseId = 1, Status = ReceiptStatus.Approved, CreatedBy = 1, TrackingNumber = "GHN-202", MarketplaceShipByUtc = now.AddMinutes(45) },
            new ExportReceipt { Id = 203, Code = "ER-203", WarehouseId = 1, Status = ReceiptStatus.Dispatched, CreatedBy = 1 },
            new ExportReceipt { Id = 204, Code = "ER-204", WarehouseId = 1, Status = ReceiptStatus.Cancelled, CreatedBy = 1 });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Strict mocks: GetAllAsync must NEVER be invoked by pipeline summary endpoints
        var importSvcMock = new Mock<IImportReceiptService>(MockBehavior.Strict);
        var exportSvcMock = new Mock<IExportReceiptService>(MockBehavior.Strict);

        var importCtrl = new ImportReceiptsController(
            importSvcMock.Object,
            Mock.Of<IReceivingDiscrepancyService>(),
            new ImportReceiptOperationsService(db, whAuth.Object),
            whAuth.Object);

        var impPipeRes = await importCtrl.GetPipelineSummary(1, default);
        using var impPipeDoc = JsonSerializer.SerializeToDocument(impPipeRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        impPipeDoc.RootElement.GetProperty("totalReceipts").GetInt32().Should().Be(4);
        impPipeDoc.RootElement.GetProperty("draftOrReceivingCount").GetInt32().Should().Be(1);
        impPipeDoc.RootElement.GetProperty("qcOrApprovalPendingCount").GetInt32().Should().Be(1);
        impPipeDoc.RootElement.GetProperty("postedCount").GetInt32().Should().Be(1);
        impPipeDoc.RootElement.GetProperty("cancelledCount").GetInt32().Should().Be(1);

        var qcSumRes = await importCtrl.GetQcAndRmaSummary(1, default);
        using var qcSumDoc = JsonSerializer.SerializeToDocument(qcSumRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        qcSumDoc.RootElement.GetProperty("totalReceiptsInspected").GetInt32().Should().Be(4);
        qcSumDoc.RootElement.GetProperty("rmaEligibleReceiptsCount").GetInt32().Should().Be(1);

        var exportCtrl = new ExportReceiptsController(
            exportSvcMock.Object,
            new ExportReceiptOperationsService(db, whAuth.Object),
            whAuth.Object,
            Mock.Of<ICurrentUser>(),
            Mock.Of<IHandoverManifestService>());

        var expPipeRes = await exportCtrl.GetPipelineSummary(1, default);
        using var expPipeDoc = JsonSerializer.SerializeToDocument(expPipeRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        expPipeDoc.RootElement.GetProperty("totalReceipts").GetInt32().Should().Be(4);
        expPipeDoc.RootElement.GetProperty("draftCount").GetInt32().Should().Be(1);
        expPipeDoc.RootElement.GetProperty("approvedReadyCount").GetInt32().Should().Be(1);
        expPipeDoc.RootElement.GetProperty("dispatchedCount").GetInt32().Should().Be(1);
        expPipeDoc.RootElement.GetProperty("cancelledCount").GetInt32().Should().Be(1);
        expPipeDoc.RootElement.GetProperty("slaOverdueCount").GetInt32().Should().Be(1);
        expPipeDoc.RootElement.GetProperty("slaAtRiskUnder2HoursCount").GetInt32().Should().Be(1);
        expPipeDoc.RootElement.GetProperty("marketplacePrioritizedCount").GetInt32().Should().Be(2);

        importSvcMock.VerifyNoOtherCalls();
        exportSvcMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TC_W3_02_ImportAndExportGetAll_EnforceServerSidePagination_MaxPageSize100()
    {
        var importDtos = Enumerable.Range(1, 135)
            .Select(i => new ImportReceiptDto { Id = i, Code = $"IR-{i:D4}", Status = "Draft" })
            .ToList();
        var exportDtos = Enumerable.Range(1, 135)
            .Select(i => new ExportReceiptDto { Id = i, Code = $"ER-{i:D4}", Status = "Draft" })
            .ToList();

        var importSvc = new Mock<IImportReceiptService>();
        importSvc.Setup(x => x.GetAllAsync(null)).ReturnsAsync(importDtos);
        var exportSvc = new Mock<IExportReceiptService>();
        exportSvc.Setup(x => x.GetAllAsync()).ReturnsAsync(exportDtos);

        var importCtrl = new ImportReceiptsController(
            importSvc.Object,
            Mock.Of<IReceivingDiscrepancyService>(),
            Mock.Of<IImportReceiptOperationsService>(),
            Mock.Of<IWarehouseAuthorizationService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var impPage1 = (await importCtrl.GetAll(null, pageNumber: 1, pageSize: 500))
            .Should().BeOfType<OkObjectResult>().Subject;
        var impItems1 = ((IEnumerable<ImportReceiptDto>)impPage1.Value!).ToList();
        impItems1.Should().HaveCount(100);
        importCtrl.Response.Headers["X-Total-Count"].ToString().Should().Be("135");

        var impPage2 = (await importCtrl.GetAll(null, pageNumber: 2, pageSize: 100))
            .Should().BeOfType<OkObjectResult>().Subject;
        var impItems2 = ((IEnumerable<ImportReceiptDto>)impPage2.Value!).ToList();
        impItems2.Should().HaveCount(35);
        impItems2[0].Id.Should().Be(101);

        var exportCtrl = new ExportReceiptsController(
            exportSvc.Object,
            Mock.Of<IExportReceiptOperationsService>(),
            Mock.Of<IWarehouseAuthorizationService>(),
            Mock.Of<ICurrentUser>(),
            Mock.Of<IHandoverManifestService>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var expPage1 = (await exportCtrl.GetAll(pageNumber: 1, pageSize: 250))
            .Should().BeOfType<OkObjectResult>().Subject;
        var expItems1 = ((IEnumerable<ExportReceiptDto>)expPage1.Value!).ToList();
        expItems1.Should().HaveCount(100);
        exportCtrl.Response.Headers["X-Total-Count"].ToString().Should().Be("135");
    }

    [Fact]
    public async Task TC_W3_03_ExpressPostInbound_DraftNoQcReceipt_PostsDirectlyToStorageBin_AndWritesLedgerAndAudit()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W3_03_ExpressPostInbound_DraftNoQcReceipt_PostsDirectlyToStorageBin_AndWritesLedgerAndAudit));

        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true });
        db.Units.Add(new Unit { Id = 1, Code = "CAI", Name = "Cái", IsActive = true });
        db.WarehouseLocations.Add(new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BIN-EXP-01",
            Name = "Kệ lưu trữ nhanh 01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = true
        });
        var prod = new Product
        {
            Id = 301,
            Code = "SKU-EXP-301",
            Name = "Chuột quang không dây",
            UnitId = 1,
            MovingAverageCost = 100000m,
            IsActive = true
        };
        db.Products.Add(prod);
        db.ImportReceipts.Add(new ImportReceipt
        {
            Id = 501,
            Code = "PNK-EXP-501",
            WarehouseId = 1,
            Status = ReceiptStatus.Draft,
            CreatedBy = 1,
            Details =
            [
                new ImportReceiptDetail
                {
                    Id = 5011,
                    ImportReceiptId = 501,
                    ProductId = 301,
                    Product = prod,
                    Quantity = 15m,
                    ExpectedQuantity = 15m,
                    BaseExpectedQuantity = 15m,
                    ConversionFactor = 1m,
                    UnitPrice = 120000m,
                    RequiresQc = false
                }
            ]
        });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var importCtrl = new ImportReceiptsController(
            Mock.Of<IImportReceiptService>(),
            Mock.Of<IReceivingDiscrepancyService>(),
            new ImportReceiptOperationsService(db, whAuth.Object),
            whAuth.Object);

        var res = await importCtrl.ExpressPost(
            501,
            new ImportReceiptsController.ExpressPostImportReceiptRequest(
                TargetLocationId: 10,
                Note: "Nhập nhanh 1 bước vào kệ BIN-EXP-01"),
            default);

        var ok = res.Should().BeOfType<OkObjectResult>().Subject;
        using var doc = JsonSerializer.SerializeToDocument(ok.Value);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Posted");
        doc.RootElement.GetProperty("expressMode").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("targetLocationId").GetInt32().Should().Be(10);
        doc.RootElement.GetProperty("totalBasePostedQuantity").GetDecimal().Should().Be(15m);

        var receiptAfter = await db.ImportReceipts.Include(r => r.Details).SingleAsync(r => r.Id == 501);
        receiptAfter.Status.Should().Be(ReceiptStatus.Posted);
        receiptAfter.Details.Single().BasePostedQuantity.Should().Be(15m);

        var stockAfter = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == 1 && s.ProductId == 301 && s.LocationId == 10 && s.Status == InventoryStatus.Available);
        stockAfter.Quantity.Should().Be(15m);
        stockAfter.MovingAverageCost.Should().Be(120000m);

        var ledgerTx = await db.InventoryTransactions.SingleAsync(t =>
            t.ReferenceType == "ImportReceipt" && t.ReferenceId == 501);
        ledgerTx.TransactionType.Should().Be(TransactionType.Import);
        ledgerTx.Quantity.Should().Be(15m);
        ledgerTx.LocationId.Should().Be(10);

        (await db.AuditLogs.AnyAsync(a => a.EntityName == "ImportReceipt" && a.EntityId == 501 && a.Action == "ImportReceipt.ExpressPosted"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task TC_W3_04_ExpressPostInbound_QcRequiredReceipt_Returns422UnprocessableEntity()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W3_04_ExpressPostInbound_QcRequiredReceipt_Returns422UnprocessableEntity));

        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính", IsActive = true });
        var prod = new Product { Id = 401, Code = "SKU-QC-401", Name = "Thiết bị y tế bắt buộc QC", UnitId = 1, IsActive = true };
        db.Products.Add(prod);
        db.ImportReceipts.Add(new ImportReceipt
        {
            Id = 601,
            Code = "PNK-QC-601",
            WarehouseId = 1,
            Status = ReceiptStatus.Draft,
            CreatedBy = 1,
            Details =
            [
                new ImportReceiptDetail
                {
                    Id = 6011,
                    ImportReceiptId = 601,
                    ProductId = 401,
                    Product = prod,
                    Quantity = 5m,
                    ExpectedQuantity = 5m,
                    RequiresQc = true,
                    QcState = ReceiptLineQcState.QcPending
                }
            ]
        });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var importCtrl = new ImportReceiptsController(
            Mock.Of<IImportReceiptService>(),
            Mock.Of<IReceivingDiscrepancyService>(),
            new ImportReceiptOperationsService(db, whAuth.Object),
            whAuth.Object);

        var res = await importCtrl.ExpressPost(601, new ImportReceiptsController.ExpressPostImportReceiptRequest(), default);
        var unprocessable = res.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using var doc = JsonSerializer.SerializeToDocument(unprocessable.Value);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("EXPRESS_POST_REQUIRES_QC_COMPLETION");
    }

    [Fact]
    public async Task TC_W3_05_ExpressPostInbound_AlreadyPostedReceipt_Returns409Conflict()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W3_05_ExpressPostInbound_AlreadyPostedReceipt_Returns409Conflict));

        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính", IsActive = true });
        db.ImportReceipts.Add(new ImportReceipt
        {
            Id = 701,
            Code = "PNK-POSTED-701",
            WarehouseId = 1,
            Status = ReceiptStatus.Posted,
            CreatedBy = 1
        });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var importCtrl = new ImportReceiptsController(
            Mock.Of<IImportReceiptService>(),
            Mock.Of<IReceivingDiscrepancyService>(),
            new ImportReceiptOperationsService(db, whAuth.Object),
            whAuth.Object);

        var res = await importCtrl.ExpressPost(701, new ImportReceiptsController.ExpressPostImportReceiptRequest(), default);
        var conflict = res.Should().BeOfType<ConflictObjectResult>().Subject;
        using var doc = JsonSerializer.SerializeToDocument(conflict.Value);
        doc.RootElement.GetProperty("errorCode").GetString().Should().Be("RECEIPT_ALREADY_POSTED");
    }

    [Fact]
    public void TC_W3_06_AllApiControllers_ExposeCanonicalApiV1RouteAttribute()
    {
        var controllerTypes = typeof(ImportReceiptsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllerTypes.Should().NotBeEmpty();
        foreach (var ctrlType in controllerTypes)
        {
            var classRoutes = ctrlType.GetCustomAttributes<RouteAttribute>(inherit: true)
                .Select(r => r.Template)
                .ToList();
            classRoutes.Should().Contain(
                t => t.StartsWith("api/v1/", StringComparison.OrdinalIgnoreCase),
                $"{ctrlType.Name} must declare a class-level [Route(\"api/v1/...\")] attribute");

            var methods = ctrlType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var absRoutes = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .Select(a => a.Template)
                    .Where(t => !string.IsNullOrWhiteSpace(t) &&
                                (t!.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
                                 t.StartsWith("~/api/", StringComparison.OrdinalIgnoreCase)))
                    .Select(t => t!)
                    .ToList();

                if (absRoutes.Count > 0)
                {
                    absRoutes.Should().Contain(
                        t => t.StartsWith("/api/v1/", StringComparison.OrdinalIgnoreCase) ||
                             t.StartsWith("~/api/v1/", StringComparison.OrdinalIgnoreCase),
                        $"{ctrlType.Name}.{method.Name} has absolute /api/ route(s) and must include a matching /api/v1/ route attribute");
                }
            }
        }
    }
}
