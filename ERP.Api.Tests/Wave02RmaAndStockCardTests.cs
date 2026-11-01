using System.Reflection;
using System.Text.Json;
using ClosedXML.Excel;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public class Wave02RmaAndStockCardTests
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
    public async Task TC_W2_01_ZeroOptionalServiceConstructorParams_StocktakeCompletedParity_AndTotpClientSecretRejected()
    {
        // 1. FND-008: Verify 0 controllers in ERP.Api have optional service interface constructor parameters
        var controllerTypes = typeof(ReportsController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllerTypes.Should().NotBeEmpty();
        foreach (var ctrlType in controllerTypes)
        {
            var ctors = ctrlType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            ctors.Should().ContainSingle($"{ctrlType.Name} must have a single public DI constructor without null-service convenience overloads");
            foreach (var param in ctors[0].GetParameters())
            {
                if (param.ParameterType.IsInterface)
                {
                    param.HasDefaultValue.Should().BeFalse(
                        $"{ctrlType.Name} constructor parameter '{param.Name}' ({param.ParameterType.Name}) must not have an optional default value");
                }
            }
        }

        // 2. CA-FND-030 (a): Stocktake Completed(6), Approved(3), Posted(9) parity in StocktakeOperationsService & ReportOperationsService
        await using var db = CreateInMemoryDb(nameof(TC_W2_01_ZeroOptionalServiceConstructorParams_StocktakeCompletedParity_AndTotpClientSecretRejected));
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính", IsActive = true });
        db.Stocktakes.AddRange(
            new Stocktake
            {
                Id = 101,
                Code = "ST-DRAFT-101",
                WarehouseId = 1,
                Status = StocktakeStatus.Draft,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-30)
            },
            new Stocktake
            {
                Id = 102,
                Code = "ST-COMPLETED-102",
                WarehouseId = 1,
                Status = StocktakeStatus.Completed,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-36),
                ApprovedAt = DateTime.UtcNow.AddHours(-2)
            },
            new Stocktake
            {
                Id = 103,
                Code = "ST-APPROVED-103",
                WarehouseId = 1,
                Status = StocktakeStatus.Approved,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-40),
                ApprovedAt = DateTime.UtcNow.AddHours(-4)
            });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var stOps = new StocktakeOperationsService(db, whAuth.Object);
        var stReasonOutcome = await stOps.GetReasonCodesAndAdjustmentsAsync(1, true, whAuth.Object, default);
        using var stDoc = JsonSerializer.SerializeToDocument(stReasonOutcome.Payload);
        // Only ST-DRAFT-101 is open; Completed(102) and Approved(103) are closed!
        stDoc.RootElement.GetProperty("openStocktakesCount").GetInt32().Should().Be(1);

        var repOps = new ReportOperationsService(db, whAuth.Object);
        var compOutcome = await repOps.GetAnalyticsSummaryAsync(whAuth.Object, 1, 30, default);
        using var compDoc = JsonSerializer.SerializeToDocument(compOutcome.Payload);
        // Both Completed(102) and Approved(103) are counted in stocktakeAccuracy.approvedStocktakes!
        compDoc.RootElement.GetProperty("stocktakeAccuracy").GetProperty("approvedStocktakes").GetInt32().Should().Be(2);

        // 3. CA-FND-030 (b): AccountSecurityController.VerifyTotp rejects client-supplied base32Secret / secretKey
        db.Users.Add(new User
        {
            Id = 501,
            Username = "sec_user_501",
            FullName = "Security User 501",
            PasswordHash = "HASH",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var secCtrl = new AccountSecurityController(
            Mock.Of<IAccountAdminService>(),
            Mock.Of<IUserSessionService>(),
            new WmsGovernanceOperationsService(db),
            Mock.Of<IPasswordHasherService>());

        var maliciousPayload = JsonSerializer.Deserialize<AccountSecurityController.VerifyTotpRequest>(
            """{"code":"123456","base32Secret":"JBSWY3DPEHPK3PXP"}""")!;
        maliciousPayload.HasClientSuppliedSecret().Should().BeTrue();

        var rejectRes = await secCtrl.VerifyTotp(501, maliciousPayload, default);
        var badReq = rejectRes.Should().BeOfType<BadRequestObjectResult>().Subject;
        using var errDoc = JsonSerializer.SerializeToDocument(badReq.Value);
        errDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("TOTP_CLIENT_SECRET_REJECTED");
    }

    [Fact]
    public async Task TC_W2_02_RmaIntake_MovesStockIntoQuarantine_WritesLedger_LeavesAvailableUnchanged_AndIsIdempotent()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W2_02_RmaIntake_MovesStockIntoQuarantine_WritesLedger_LeavesAvailableUnchanged_AndIsIdempotent));

        var wh = new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true };
        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var storageBin = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BIN-A01",
            Name = "Kệ lưu trữ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = true
        };
        var quarantineBin = new WarehouseLocation
        {
            Id = 11,
            WarehouseId = 1,
            Code = "QUARANTINE",
            Name = "Khu vực biệt trữ chờ kiểm định",
            LocationType = WarehouseLocationType.Staging,
            IsActive = true,
            IsPickable = false,
            IsReceivable = true,
            IsSystemManaged = true
        };
        var prod = new Product
        {
            Id = 201,
            Code = "SKU-RMA-201",
            Name = "Tai nghe Bluetooth chống ồn",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 500000m,
            IsActive = true
        };
        var availStock = new InventoryStock
        {
            Id = 2001,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 201,
            LocationId = 10,
            Status = InventoryStatus.Available,
            Quantity = 40m,
            ReservedQuantity = 0m,
            MovingAverageCost = 500000m
        };
        availStock.RefreshGrainHash();

        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.WarehouseLocations.AddRange(storageBin, quarantineBin);
        db.Products.Add(prod);
        db.InventoryStocks.Add(availStock);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(10);
        currentUser.SetupGet(x => x.Role).Returns(AppRoles.Manager);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(true);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnReceive)).Returns(true);

        var ops = new ImportReceiptOperationsService(db, whAuth.Object, currentUser.Object);
        var ctrl = new CustomerReturnsController(ops, whAuth.Object, currentUser.Object);

        var req = new CustomerReturnIntakeInput(
            WarehouseId: 1,
            ReturnCode: "RMA-2026-INTAKE-001",
            TrackingNumber: "GHN-RMA-2026-001",
            SalesOrderCode: "SO-2026-889",
            ReturnReason: "DEFECTIVE_AUDIO",
            QuarantineLocationId: 11,
            Lines:
            [
                new CustomerReturnIntakeLineInput(
                    ProductId: 201,
                    ProductCode: "SKU-RMA-201",
                    Quantity: 5m,
                    UnitCost: 500000m,
                    LotNumber: "LOT-RMA-01",
                    Condition: "GOOD")
            ]);

        var res1 = await ctrl.Intake(req, default);
        var ok1 = res1.Should().BeOfType<OkObjectResult>().Subject;
        using var doc1 = JsonSerializer.SerializeToDocument(ok1.Value);
        var returnId = doc1.RootElement.GetProperty("id").GetInt32();
        returnId.Should().BeGreaterThan(0);
        doc1.RootElement.GetProperty("status").GetString().Should().Be("QUARANTINED");
        doc1.RootElement.GetProperty("warehouseName").GetString().Should().Be("Kho Chính TP.HCM");
        doc1.RootElement.GetProperty("productCode").GetString().Should().Be("SKU-RMA-201");
        doc1.RootElement.GetProperty("productName").GetString().Should().Be("Tai nghe Bluetooth chống ồn");
        doc1.RootElement.GetProperty("quantity").GetDecimal().Should().Be(5m);
        doc1.RootElement.GetProperty("unitCost").GetDecimal().Should().Be(500000m);
        doc1.RootElement.GetProperty("condition").GetString().Should().Be("GOOD");
        doc1.RootElement.GetProperty("disposition").GetString().Should().Be("PENDING");
        doc1.RootElement.GetProperty("quarantineLocationCode").GetString().Should().Be("QUARANTINE");

        // Verify Available stock is untouched (40m) and Quarantine stock is +5m
        var availAfter = await db.InventoryStocks.SingleAsync(s => s.Id == 2001);
        availAfter.Quantity.Should().Be(40m);

        var quarantineAfter = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == 1 && s.ProductId == 201 && s.Status == InventoryStatus.Quarantine);
        quarantineAfter.Quantity.Should().Be(5m);
        quarantineAfter.LocationId.Should().Be(11);
        quarantineAfter.GrainHash.Should().NotBeNullOrEmpty();

        var ledgerTx = await db.InventoryTransactions.SingleAsync(t =>
            t.ReferenceType == "CustomerReturn" && t.ReferenceId == returnId);
        ledgerTx.TransactionType.Should().Be(TransactionType.Import);
        ledgerTx.InventoryStatus.Should().Be(InventoryStatus.Quarantine);
        ledgerTx.Quantity.Should().Be(5m);

        // Verify idempotency: second call with same ReturnCode/TrackingNumber returns existing RMA without mutating Quarantine stock again
        var res2 = await ctrl.Intake(req, default);
        var ok2 = res2.Should().BeOfType<OkObjectResult>().Subject;
        using var doc2 = JsonSerializer.SerializeToDocument(ok2.Value);
        doc2.RootElement.GetProperty("id").GetInt32().Should().Be(returnId);

        (await db.InventoryStocks.SingleAsync(s => s.Id == quarantineAfter.Id)).Quantity.Should().Be(5m);
        (await db.InventoryTransactions.CountAsync(t => t.ReferenceType == "CustomerReturn" && t.ReferenceId == returnId)).Should().Be(1);
    }

    [Fact]
    public async Task TC_W2_03_RmaInspectAndSplitDispose_MovesRestockToAvailable_AndWritesOffScrapFromQuarantine()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W2_03_RmaInspectAndSplitDispose_MovesRestockToAvailable_AndWritesOffScrapFromQuarantine));

        var wh = new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true };
        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var storageBin = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BIN-A01",
            Name = "Kệ lưu trữ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = true
        };
        var quarantineBin = new WarehouseLocation
        {
            Id = 11,
            WarehouseId = 1,
            Code = "QUARANTINE",
            Name = "Khu vực biệt trữ chờ kiểm định",
            LocationType = WarehouseLocationType.Staging,
            IsActive = true,
            IsPickable = false,
            IsReceivable = true,
            IsSystemManaged = true
        };
        var prod = new Product
        {
            Id = 301,
            Code = "SKU-RMA-301",
            Name = "Bàn phím cơ không dây",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 800000m,
            IsActive = true
        };

        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.WarehouseLocations.AddRange(storageBin, quarantineBin);
        db.Products.Add(prod);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(10);
        currentUser.SetupGet(x => x.Role).Returns(AppRoles.Manager);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(true);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnReceive)).Returns(true);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnInspect)).Returns(true);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnDisposition)).Returns(true);

        var ops = new ImportReceiptOperationsService(db, whAuth.Object, currentUser.Object);
        var ctrl = new CustomerReturnsController(ops, whAuth.Object, currentUser.Object);

        // Step 1: Intake 5 units into Quarantine
        var intakeRes = await ctrl.Intake(
            new CustomerReturnIntakeInput(
                WarehouseId: 1,
                TrackingNumber: "VTP-RMA-301",
                SalesOrderCode: "SO-301",
                ReturnReason: "CUSTOMER_EXCHANGE",
                QuarantineLocationId: 11,
                Lines:
                [
                    new CustomerReturnIntakeLineInput(
                        ProductId: 301,
                        ProductCode: "SKU-RMA-301",
                        Quantity: 5m,
                        UnitCost: 800000m)
                ]),
            default);
        using var intakeDoc = JsonSerializer.SerializeToDocument(intakeRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var returnId = intakeDoc.RootElement.GetProperty("id").GetInt32();
        var lineId = intakeDoc.RootElement.GetProperty("lines")[0].GetProperty("id").GetInt32();

        // Step 2: Inspect RMA
        var inspectRes = await ctrl.Inspect(
            returnId,
            new CustomerReturnInspectInput(
                Note: "3 cái đạt chuẩn nhập lại kệ tốt, 2 cái gãy switch hủy",
                Lines:
                [
                    new CustomerReturnInspectLineInput(
                        LineId: lineId,
                        ProductId: 301,
                        Condition: "GOOD",
                        Disposition: "RESTOCK_AVAILABLE",
                        RestockLocationId: 10)
                ]),
            default);
        using var inspectDoc = JsonSerializer.SerializeToDocument(inspectRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        inspectDoc.RootElement.GetProperty("status").GetString().Should().Be("INSPECTED");

        // Step 3: Split Disposition (3 RESTOCK_AVAILABLE to BIN-A01 + 2 SCRAP_DISPOSAL)
        var disposeRes = await ctrl.DisposeReturn(
            returnId,
            new CustomerReturnDisposeInput(
                RestockLocationId: 10,
                Note: "Phân luồng 3 nhập kệ A01, 2 xuất hủy",
                Splits:
                [
                    new CustomerReturnDispositionSplitInput(
                        LineId: lineId,
                        ProductId: 301,
                        Quantity: 3m,
                        Disposition: "RESTOCK_AVAILABLE",
                        RestockLocationId: 10),
                    new CustomerReturnDispositionSplitInput(
                        LineId: lineId,
                        ProductId: 301,
                        Quantity: 2m,
                        Disposition: "SCRAP_DISPOSAL")
                ]),
            default);

        using var disposeDoc = JsonSerializer.SerializeToDocument(disposeRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        disposeDoc.RootElement.GetProperty("status").GetString().Should().Be("COMPLETED");
        disposeDoc.RootElement.GetProperty("lines").GetArrayLength().Should().Be(2);

        // Quarantine must now be 0 (5 - 3 - 2 = 0), Available at BIN-A01 (LocationId=10) must be 3
        var quarantineBucket = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == 1 && s.ProductId == 301 && s.Status == InventoryStatus.Quarantine);
        quarantineBucket.Quantity.Should().Be(0m);

        var availableBucket = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == 1 && s.ProductId == 301 && s.Status == InventoryStatus.Available && s.LocationId == 10);
        availableBucket.Quantity.Should().Be(3m);

        var dispLedgers = await db.InventoryTransactions
            .Where(t => t.ReferenceId == returnId && t.Id > 1)
            .ToListAsync();
        dispLedgers.Should().HaveCount(3); // 2 StatusChange legs (-3 Quarantine, +3 Available) + 1 AdjustmentDecrease (2 Quarantine)
    }

    [Fact]
    public async Task TC_W2_04_RmaRbacCostMasking_PermissionAttributes_AndDoubleDisposeConflictGuard()
    {
        // 1. Verify RBAC PermissionAuthorize and IdempotentCommand attributes on CustomerReturnsController
        var ctrlType = typeof(CustomerReturnsController);
        ctrlType.GetMethod(nameof(CustomerReturnsController.GetAll))!
            .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permissions
            .Should().Contain(AppPermissions.ReturnRead);
        ctrlType.GetMethod(nameof(CustomerReturnsController.Intake))!
            .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permissions
            .Should().Contain(AppPermissions.ReturnReceive);
        ctrlType.GetMethod(nameof(CustomerReturnsController.Intake))!
            .GetCustomAttribute<IdempotentCommandAttribute>().Should().NotBeNull();
        ctrlType.GetMethod(nameof(CustomerReturnsController.Inspect))!
            .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permissions
            .Should().Contain(AppPermissions.ReturnInspect);
        ctrlType.GetMethod(nameof(CustomerReturnsController.DisposeReturn))!
            .GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permissions
            .Should().Contain(AppPermissions.ReturnDisposition);
        ctrlType.GetMethod(nameof(CustomerReturnsController.DisposeReturn))!
            .GetCustomAttribute<IdempotentCommandAttribute>().Should().NotBeNull();

        await using var db = CreateInMemoryDb(nameof(TC_W2_04_RmaRbacCostMasking_PermissionAttributes_AndDoubleDisposeConflictGuard));
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính", IsActive = true });
        db.Units.Add(new Unit { Id = 1, Code = "CAI", Name = "Cái", IsActive = true });
        db.WarehouseLocations.AddRange(
            new WarehouseLocation { Id = 10, WarehouseId = 1, Code = "BIN-A01", Name = "Kệ A01", LocationType = WarehouseLocationType.Storage, IsActive = true, IsPickable = true, IsReceivable = true },
            new WarehouseLocation { Id = 11, WarehouseId = 1, Code = "QUARANTINE", Name = "Biệt trữ", LocationType = WarehouseLocationType.Staging, IsActive = true, IsReceivable = true, IsSystemManaged = true });
        db.Products.Add(new Product { Id = 401, Code = "SKU-RMA-401", Name = "Sản phẩm RMA 401", UnitId = 1, MovingAverageCost = 250000m, IsActive = true });
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        // Staff user WITHOUT inventory.cost.read
        var staffUser = new Mock<ICurrentUser>();
        staffUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        staffUser.SetupGet(x => x.UserId).Returns(22);
        staffUser.SetupGet(x => x.Role).Returns(AppRoles.WarehouseStaff);
        staffUser.SetupGet(x => x.IsGlobalAdmin).Returns(false);
        staffUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(false);
        staffUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnReceive)).Returns(true);
        staffUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnDisposition)).Returns(true);

        var staffOps = new ImportReceiptOperationsService(db, whAuth.Object, staffUser.Object);
        var staffCtrl = new CustomerReturnsController(staffOps, whAuth.Object, staffUser.Object);

        var intakeRes = await staffCtrl.Intake(
            new CustomerReturnIntakeInput(
                WarehouseId: 1,
                TrackingNumber: "SPX-RMA-401",
                SalesOrderCode: "SO-401",
                ReturnReason: "DAMAGED",
                QuarantineLocationId: 11,
                Lines: [new CustomerReturnIntakeLineInput(ProductId: 401, Quantity: 4m, UnitCost: 250000m)]),
            default);
        using var intakeDoc = JsonSerializer.SerializeToDocument(intakeRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var returnId = intakeDoc.RootElement.GetProperty("id").GetInt32();

        // Cost must be masked for WarehouseStaff without inventory.cost.read
        intakeDoc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeTrue();
        intakeDoc.RootElement.GetProperty("lines")[0].GetProperty("unitCost").ValueKind.Should().Be(JsonValueKind.Null);

        // Split quantity mismatch must return 422 UnprocessableEntity (RMA_SPLIT_QUANTITY_MISMATCH)
        var mismatchRes = await staffCtrl.DisposeReturn(
            returnId,
            new CustomerReturnDisposeInput(
                RestockLocationId: 10,
                Splits:
                [
                    new CustomerReturnDispositionSplitInput(ProductId: 401, Quantity: 1m, Disposition: "RESTOCK_AVAILABLE", RestockLocationId: 10),
                    new CustomerReturnDispositionSplitInput(ProductId: 401, Quantity: 1m, Disposition: "SCRAP_DISPOSAL") // 1 + 1 = 2 != 4 Quantity
                ]),
            default);
        var mismatch422 = mismatchRes.Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        using var mismatchDoc = JsonSerializer.SerializeToDocument(mismatch422.Value);
        mismatchDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("RMA_SPLIT_QUANTITY_MISMATCH");

        // Valid dispose completes the return
        var validDispose = await staffCtrl.DisposeReturn(
            returnId,
            new CustomerReturnDisposeInput(
                Disposition: "RESTOCK_AVAILABLE",
                RestockLocationId: 10),
            default);
        validDispose.Should().BeOfType<OkObjectResult>();

        // Second dispose must be rejected with 409 Conflict (RMA_ALREADY_COMPLETED)
        var doubleDispose = await staffCtrl.DisposeReturn(
            returnId,
            new CustomerReturnDisposeInput(
                Disposition: "RESTOCK_AVAILABLE",
                RestockLocationId: 10),
            default);
        var conflict409 = doubleDispose.Should().BeOfType<ConflictObjectResult>().Subject;
        using var conflictDoc = JsonSerializer.SerializeToDocument(conflict409.Value);
        conflictDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("RMA_ALREADY_COMPLETED");
    }

    [Fact]
    public async Task TC_W2_05_ShipmentReturnToWarehouse_And_CompleteRtsReturn_AutoCreateAndLinkCustomerReturn()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W2_05_ShipmentReturnToWarehouse_And_CompleteRtsReturn_AutoCreateAndLinkCustomerReturn));

        var wh = new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true };
        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", IsActive = true };
        var quarantineBin = new WarehouseLocation
        {
            Id = 11,
            WarehouseId = 1,
            Code = "QUARANTINE",
            Name = "Khu vực biệt trữ",
            LocationType = WarehouseLocationType.Staging,
            IsActive = true,
            IsReceivable = true,
            IsSystemManaged = true
        };
        var prod = new Product
        {
            Id = 501,
            Code = "SKU-RTS-501",
            Name = "Máy lọc không khí mini",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 900000m,
            IsActive = true
        };
        var exportReceipt = new ExportReceipt
        {
            Id = 701,
            Code = "PXK-RTS-701",
            WarehouseId = 1,
            Status = ReceiptStatus.Dispatched,
            TrackingNumber = "GHN-RTS-701",
            CarrierCode = "GHN",
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            Details =
            [
                new ExportReceiptDetail
                {
                    Id = 7011,
                    ExportReceiptId = 701,
                    ProductId = 501,
                    Product = prod,
                    Quantity = 2m,
                    UnitPrice = 1200000m,
                    BaseUomIdSnapshot = 1,
                    BaseUomCodeSnapshot = "CAI"
                }
            ]
        };
        var pickingTask = new PickingTask
        {
            Id = 8010,
            TaskCode = "PICK-RTS-8010",
            WarehouseId = 1,
            SourceType = "ExportReceipt",
            SourceId = 701,
            SourceCode = "PXK-RTS-701",
            Status = PickingTaskStatus.Completed,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        };
        var packingSession = new PackingSession
        {
            Id = 8010,
            SessionCode = "PACK-RTS-8010",
            PickingTaskId = 8010,
            PickingTask = pickingTask,
            WarehouseId = 1,
            Status = PackingSessionStatus.Closed,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        };
        var shipment = new Shipment
        {
            Id = 801,
            ShipmentCode = "SHP-RTS-801",
            PackingSessionId = 8010,
            PackingSession = packingSession,
            WarehouseId = 1,
            SourceType = "ExportReceipt",
            SourceId = 701,
            SourceCode = "PXK-RTS-701",
            SealNumber = "GHN-RTS-701",
            Status = ShipmentStatus.DeliveryFailed,
            DeliveryFailedAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            RowVersion = [0, 0, 0, 0, 0, 0, 0, 1]
        };

        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.WarehouseLocations.Add(quarantineBin);
        db.Products.Add(prod);
        db.ExportReceipts.Add(exportReceipt);
        db.PickingTasks.Add(pickingTask);
        db.PackingSessions.Add(packingSession);
        db.Shipments.Add(shipment);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(1);
        currentUser.SetupGet(x => x.Role).Returns(AppRoles.Admin);
        currentUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);

        var shipmentService = new ShipmentService(db, whAuth.Object, currentUser.Object, Mock.Of<IStockReservationService>());
        var shipmentOps = new ShipmentOperationsService(db, whAuth.Object, currentUser.Object);
        var shipmentCtrl = new ShipmentsController(shipmentService, shipmentOps, whAuth.Object);

        // 1. ReturnInitiate on Shipment 801 auto-creates linked CustomerReturn
        var initRes = await shipmentCtrl.ReturnInitiate(
            801,
            new InitiateShipmentReturnDto
            {
                ReasonCode = "BUYER_UNREACHABLE",
                Note = "Giao 3 lần không nghe máy",
                RowVersion = Convert.ToBase64String(shipment.RowVersion)
            },
            default);
        initRes.Result.Should().BeOfType<OkObjectResult>();

        var linkedReturn = await db.CustomerReturns.Include(r => r.Lines).SingleAsync(r => r.ShipmentId == 801);
        linkedReturn.Status.Should().Be("QUARANTINED");
        linkedReturn.Lines.Should().ContainSingle();
        linkedReturn.Lines.Single().ProductId.Should().Be(501);
        linkedReturn.Lines.Single().Quantity.Should().Be(2m);

        // 2. CompleteRtsReturn moves the 2 units into Quarantine
        var rtsRes = await shipmentCtrl.CompleteRtsReturn(
            801,
            new CompleteShipmentRtsReturnRequest(
                QuarantineLocationId: 11,
                ProductId: 501,
                Quantity: 2m,
                Note: "RTS_BUYER_UNREACHABLE",
                IsSealedIntact: false),
            default);
        rtsRes.Should().BeOfType<OkObjectResult>();

        var quarantineStock = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == 1 && s.ProductId == 501 && s.Status == InventoryStatus.Quarantine);
        quarantineStock.Quantity.Should().Be(2m);

        // 3. W2-F03b: Duplicate ShipmentId across 2 ExportReceipts must not throw in GetAllAsync
        exportReceipt.ShipmentId = 801;
        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 702,
            Code = "PXK-RTS-702",
            WarehouseId = 1,
            ShipmentId = 801,
            Status = ReceiptStatus.Dispatched,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var allShipments = await shipmentService.ListAsync(1, null, default);
        allShipments.Should().ContainSingle(s => s.Id == 801 && s.ExportReceiptCode == "PXK-RTS-702");
    }

    [Fact]
    public async Task TC_W2_06_Circular200StockCardS12Dn_EnforcesRunningBalanceInvariant_AndExportsValidXlsx()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W2_06_Circular200StockCardS12Dn_EnforcesRunningBalanceInvariant_AndExportsValidXlsx));

        var wh = new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true };
        var unit = new Unit { Id = 1, Code = "THUNG", Name = "Thùng", DecimalPlaces = 0, IsActive = true };
        var binA = new WarehouseLocation { Id = 10, WarehouseId = 1, Code = "A-01-01", Name = "Kệ A01", LocationType = WarehouseLocationType.Storage, IsActive = true };
        var binB = new WarehouseLocation { Id = 11, WarehouseId = 1, Code = "B-01-01", Name = "Kệ B01", LocationType = WarehouseLocationType.Storage, IsActive = true };
        var prod = new Product
        {
            Id = 601,
            Code = "SKU-S12DN-601",
            Name = "Sữa bột công thức cao cấp 900g",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 400000m,
            IsActive = true
        };
        var lot = new InventoryLot
        {
            Id = 6001,
            ProductId = 601,
            LotNumber = "LOT-S12-2026",
            ManufactureDate = new DateTime(2026, 1, 1),
            ExpiryDate = new DateTime(2027, 12, 31)
        };

        var fromDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var toDate = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.WarehouseLocations.AddRange(binA, binB);
        db.Products.Add(prod);
        db.InventoryLots.Add(lot);

        // Current on-hand balance: 40 (opening) + 25 (import) - 15 (export) = 50m
        var stock = new InventoryStock
        {
            Id = 60001,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 601,
            LocationId = 10,
            LotId = 6001,
            Status = InventoryStatus.Available,
            Quantity = 50m,
            ReservedQuantity = 0m,
            MovingAverageCost = 400000m
        };
        stock.RefreshGrainHash();
        db.InventoryStocks.Add(stock);

        db.InventoryTransactions.AddRange(
            // Pre-period transactions (Before Oct 1 -> Opening Balance = +50 - 10 = 40)
            new InventoryTransaction
            {
                Id = 1,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 601,
                LocationId = 10,
                LotId = 6001,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 50m,
                UnitCost = 400000m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 101,
                TransactionDate = fromDate.AddDays(-10),
                CreatedBy = 1,
                Note = "Nhập tồn đầu kỳ"
            },
            new InventoryTransaction
            {
                Id = 2,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 601,
                LocationId = 10,
                LotId = 6001,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Export,
                Quantity = -10m,
                UnitCost = 400000m,
                ReferenceType = "ExportReceipt",
                ReferenceId = 102,
                TransactionDate = fromDate.AddDays(-5),
                CreatedBy = 1,
                Note = "Xuất bán tháng trước"
            },
            // In-period transactions: +25 Import, -15 Export, and intra-warehouse Move (-5 / +5)
            new InventoryTransaction
            {
                Id = 3,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 601,
                LocationId = 10,
                LotId = 6001,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Import,
                Quantity = 25m,
                UnitCost = 400000m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 201,
                TransactionDate = fromDate.AddDays(3),
                CreatedBy = 1,
                Note = "Nhập mua hàng lô mới"
            },
            new InventoryTransaction
            {
                Id = 4,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 601,
                LocationId = 10,
                FromLocationId = 10,
                ToLocationId = 11,
                LotId = 6001,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Move,
                Quantity = -5m,
                UnitCost = 400000m,
                ReferenceType = "Putaway",
                ReferenceId = 301,
                TransactionDate = fromDate.AddDays(5),
                CreatedBy = 1,
                Note = "Chuyển kệ nội bộ (chân nguồn)"
            },
            new InventoryTransaction
            {
                Id = 5,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 601,
                LocationId = 11,
                FromLocationId = 10,
                ToLocationId = 11,
                LotId = 6001,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Move,
                Quantity = 5m,
                UnitCost = 400000m,
                ReferenceType = "Putaway",
                ReferenceId = 301,
                TransactionDate = fromDate.AddDays(5),
                CreatedBy = 1,
                Note = "Chuyển kệ nội bộ (chân đích)"
            },
            new InventoryTransaction
            {
                Id = 6,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 601,
                LocationId = 10,
                LotId = 6001,
                InventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Export,
                Quantity = -15m,
                UnitCost = 400000m,
                ReferenceType = "ExportReceipt",
                ReferenceId = 202,
                TransactionDate = fromDate.AddDays(10),
                CreatedBy = 1,
                Note = "Xuất giao đại lý"
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

        var adminCtrl = new ReportsController(
            Mock.Of<IReportService>(),
            new ReportOperationsService(db, whAuth.Object),
            whAuth.Object,
            adminUser.Object);

        var cardRes = await adminCtrl.GetStockCard(
            warehouseId: 1,
            productId: 601,
            lotId: null,
            fromDate: fromDate,
            toDate: toDate,
            cancellationToken: default);

        var cardDto = cardRes.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<StockCardS12DnReportDto>().Subject;

        cardDto.FormCode.Should().Contain("S12-DN");
        cardDto.CircularReference.Should().Contain("200/2014/TT-BTC");
        cardDto.OpeningBalance.Should().Be(40m);
        cardDto.TotalInbound.Should().Be(25m);
        cardDto.TotalOutbound.Should().Be(15m);
        cardDto.ClosingBalance.Should().Be(50m);
        cardDto.CurrentOnHand.Should().Be(50m);
        cardDto.ReconciledWithStock.Should().BeTrue();
        cardDto.Rows.Should().HaveCount(2);
        cardDto.Rows[0].RunningBalance.Should().Be(65m); // 40 + 25
        cardDto.Rows[1].RunningBalance.Should().Be(50m); // 65 - 15

        // Excel export (.xlsx) returns valid OpenXML workbook with Circular 200 S12-DN headers
        var xlsxRes = await adminCtrl.ExportStockCard(1, 601, null, fromDate, toDate, default);
        var fileResult = xlsxRes.Should().BeOfType<FileContentResult>().Subject;
        fileResult.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        fileResult.FileContents.Length.Should().BeGreaterThanOrEqualTo(2000);
        fileResult.FileContents[0].Should().Be(0x50); // 'P'
        fileResult.FileContents[1].Should().Be(0x4B); // 'K'

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet("Thẻ kho S12-DN");
        ws.Should().NotBeNull();
        ws.Cell(1, 8).GetString().Should().Contain("Mẫu số S12-DN");
        ws.Cell(4, 1).GetString().Should().Contain("THẺ KHO");
    }

    [Fact]
    public void TC_W2_07_AllControllers_HaveZeroOptionalServiceParameters_And_ZeroFallbackHelpers()
    {
        var apiAssembly = typeof(ImportReceiptsController).Assembly;
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        controllerTypes.Should().NotBeEmpty();
        foreach (var ctrlType in controllerTypes)
        {
            var ctors = ctrlType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            ctors.Should().ContainSingle($"{ctrlType.Name} must expose a single public DI constructor");
            foreach (var param in ctors[0].GetParameters())
            {
                param.IsOptional.Should().BeFalse(
                    $"{ctrlType.Name} constructor parameter '{param.Name}' must not be optional (FND-008)");
            }
        }

        var fallbackMethods = apiAssembly.GetTypes()
            .Concat(typeof(ReportOperationsService).Assembly.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.Name.Contains("FallbackWhenNoOps", StringComparison.OrdinalIgnoreCase))
            .ToList();
        fallbackMethods.Should().BeEmpty("No FallbackWhenNoOps helper methods may remain in Api or Infrastructure");
    }

    [Fact]
    public async Task TC_W2_08_CustomerReturn_RefurbishRepack_And_RtvVendor_UpdateStockAndLedger()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W2_08_CustomerReturn_RefurbishRepack_And_RtvVendor_UpdateStockAndLedger));

        var wh = new Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true };
        var unit = new Unit { Id = 1, Code = "CAI", Name = "Cái", DecimalPlaces = 0, IsActive = true };
        var storageBin = new WarehouseLocation
        {
            Id = 10,
            WarehouseId = 1,
            Code = "BIN-A01",
            Name = "Kệ lưu trữ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true,
            IsReceivable = true
        };
        var quarantineBin = new WarehouseLocation
        {
            Id = 11,
            WarehouseId = 1,
            Code = "QUARANTINE",
            Name = "Khu vực biệt trữ",
            LocationType = WarehouseLocationType.Staging,
            IsActive = true,
            IsPickable = false,
            IsReceivable = true,
            IsSystemManaged = true
        };
        var prod = new Product
        {
            Id = 801,
            Code = "SKU-RMA-801",
            Name = "Loa thông minh kết nối Wi-Fi",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 600000m,
            IsActive = true
        };
        var pkgProd = new Product
        {
            Id = 802,
            Code = "PKG-BOX-802",
            Name = "Vỏ hộp carton tân trang",
            UnitId = 1,
            Unit = unit,
            MovingAverageCost = 15000m,
            IsActive = true
        };
        var pkgStock = new InventoryStock
        {
            Id = 8002,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 802,
            LocationId = 10,
            Status = InventoryStatus.Available,
            Quantity = 20m,
            ReservedQuantity = 0m,
            MovingAverageCost = 15000m
        };
        pkgStock.RefreshGrainHash();

        db.Warehouses.Add(wh);
        db.Units.Add(unit);
        db.WarehouseLocations.AddRange(storageBin, quarantineBin);
        db.Products.AddRange(prod, pkgProd);
        db.InventoryStocks.Add(pkgStock);
        await db.SaveChangesAsync();

        var whAuth = new Mock<IWarehouseAuthorizationService>();
        whAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([1]);
        whAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(10);
        currentUser.SetupGet(x => x.Role).Returns(AppRoles.Manager);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead)).Returns(true);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnReceive)).Returns(true);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnInspect)).Returns(true);
        currentUser.Setup(x => x.HasPermission(ERP.Domain.Security.AppPermissions.ReturnDisposition)).Returns(true);

        var ops = new ImportReceiptOperationsService(db, whAuth.Object, currentUser.Object);
        var ctrl = new CustomerReturnsController(ops, whAuth.Object, currentUser.Object);

        // Intake 6 units into Quarantine
        var intakeRes = await ctrl.Intake(
            new CustomerReturnIntakeInput(
                WarehouseId: 1,
                TrackingNumber: "GHN-RMA-801",
                SalesOrderCode: "SO-801",
                ReturnReason: "PACKAGING_TORN_OR_VENDOR_DEFECT",
                QuarantineLocationId: 11,
                Lines:
                [
                    new CustomerReturnIntakeLineInput(
                        ProductId: 801,
                        ProductCode: "SKU-RMA-801",
                        Quantity: 6m,
                        UnitCost: 600000m)
                ]),
            default);
        using var intakeDoc = JsonSerializer.SerializeToDocument(intakeRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        var returnId = intakeDoc.RootElement.GetProperty("id").GetInt32();
        var lineId = intakeDoc.RootElement.GetProperty("lines")[0].GetProperty("id").GetInt32();

        // Split disposition: 4 units REFURBISH_REPACK (consuming 4 packaging boxes) + 2 units RTV_VENDOR
        var disposeRes = await ctrl.DisposeReturn(
            returnId,
            new CustomerReturnDisposeInput(
                RestockLocationId: 10,
                Note: "4 cái đóng hộp tân trang nhập lại kệ tốt, 2 cái lỗi bo mạch xuất trả NCC (RTV)",
                Splits:
                [
                    new CustomerReturnDispositionSplitInput(
                        LineId: lineId,
                        ProductId: 801,
                        Quantity: 4m,
                        Disposition: "REFURBISH_REPACK",
                        RestockLocationId: 10,
                        PackagingProductId: 802,
                        PackagingQuantity: 4m),
                    new CustomerReturnDispositionSplitInput(
                        LineId: lineId,
                        ProductId: 801,
                        Quantity: 2m,
                        Disposition: "RTV_VENDOR")
                ]),
            default);
        using var disposeDoc = JsonSerializer.SerializeToDocument(disposeRes.Should().BeOfType<OkObjectResult>().Subject.Value);
        disposeDoc.RootElement.GetProperty("status").GetString().Should().Be("COMPLETED");

        // Quarantine stock for 801 must be 0 (6 - 4 - 2 = 0)
        var quarantineAfter = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == 1 && s.ProductId == 801 && s.Status == InventoryStatus.Quarantine);
        quarantineAfter.Quantity.Should().Be(0m);

        // Available stock for 801 at BIN-A01 (LocationId=10) must be 4 (from REFURBISH_REPACK)
        var availableAfter = await db.InventoryStocks.SingleAsync(s =>
            s.WarehouseId == 1 && s.ProductId == 801 && s.Status == InventoryStatus.Available && s.LocationId == 10);
        availableAfter.Quantity.Should().Be(4m);

        // Packaging stock for 802 must be 16 (20 - 4 consumed)
        var pkgAfter = await db.InventoryStocks.SingleAsync(s => s.Id == 8002);
        pkgAfter.Quantity.Should().Be(16m);

        // Verify immutable ledger transactions for REFURBISH_REPACK (StatusChange) and RTV_VENDOR (QualityDispositionRtv)
        var rtvTx = await db.InventoryTransactions.SingleAsync(t =>
            t.ReferenceId == returnId && t.ReferenceType == "QualityDispositionRtv");
        rtvTx.TransactionType.Should().Be(TransactionType.AdjustmentDecrease);
        rtvTx.InventoryStatus.Should().Be(InventoryStatus.Quarantine);
        rtvTx.Quantity.Should().Be(2m);

        var refurbishInTx = await db.InventoryTransactions.SingleAsync(t =>
            t.ReferenceId == returnId &&
            t.ReferenceType == "CustomerReturn" &&
            t.TransactionType == TransactionType.StatusChange &&
            t.InventoryStatus == InventoryStatus.Available);
        refurbishInTx.Quantity.Should().Be(4m);
    }
}
