using System.Text.Json;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Api.Middleware;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ERP.Api.Tests;

public sealed class Wave4SectorBRemediationTests
{
    private static ErpKhoDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase("Wave4SectorB_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ErpKhoDbContext(options);
    }

    [Theory]
    [InlineData("ImportReceipt.Create", true)]
    [InlineData("ImportReceipt.Approve", true)]
    [InlineData("ImportReceipt.Post", true)]
    [InlineData("Stocktake.Create", true)]
    [InlineData("Stocktake.Approve", true)]
    [InlineData("StockTransfer.Create", true)]
    [InlineData("StockTransfer.Dispatch", true)]
    [InlineData("StockTransfer.Receive", true)]
    [InlineData("StockTransfer.Complete", true)]
    [InlineData("StockReservation.Create", true)]
    [InlineData("StockReservation.Release", true)]
    [InlineData("PickingTask.Confirm", true)]
    [InlineData("PackingSession.Complete", true)]
    [InlineData("PutawayTask.Confirm", true)]
    [InlineData("Inbound.Create", true)]
    [InlineData("Outbound.Create", true)]
    [InlineData("ReceivingDiscrepancy.Resolve", true)]
    [InlineData("Approval.Reject", true)]
    [InlineData("DockYard.Appointment.Confirm", false)]
    [InlineData("StockTransfer.Approve", false)]
    public void RequiresSerializableIsolation_CoversWarehouseAndInventoryCommandPrefixes(string scope, bool expected)
    {
        IdempotentCommandFilter.RequiresSerializableIsolation(scope).Should().Be(expected);
    }

    [Fact]
    public async Task GlobalExceptionHandlingMiddleware_ReturnsGenericMessage_AndHidesRawException_On500()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new Exception("Database secret leak: Server=10.0.0.1;Password=SuperSecretPassword;"),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("message").GetString()
            .Should().Be("Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.");
        body.Should().NotContain("SuperSecretPassword");
        body.Should().NotContain("10.0.0.1");
    }

    [Fact]
    public async Task ApprovalWorkflowService_RejectImportReceipt_VoidsStagedSerials_AndSyncsAsnState()
    {
        await using var db = CreateDbContext();

        var role = new Role { Id = 1, RoleName = "Manager" };
        var perm = new Permission("approval.reject", "Reject approvals") { Id = 1 };
        var rolePerm = new RolePermission { RoleId = 1, PermissionId = 1, Role = role, Permission = perm };
        var maker = new User { Id = 10, Username = "maker", FullName = "Maker User", PasswordHash = "h", RoleId = 1, Role = role, IsActive = true };
        var checker = new User { Id = 20, Username = "checker", FullName = "Checker User", PasswordHash = "h", RoleId = 1, Role = role, IsActive = true };
        var warehouse = new Warehouse { Id = 1, Code = "WH01", Name = "Main Warehouse" };
        var asn = new Asn
        {
            Id = 50,
            Code = "ASN-0050",
            WarehouseId = 1,
            Status = AsnStatus.Receiving,
            CreatedBy = 10
        };
        var receipt = new ImportReceipt
        {
            Id = 100,
            Code = "PN-0100",
            WarehouseId = 1,
            AsnId = 50,
            Status = ReceiptStatus.Received,
            CreatedBy = 10
        };
        var receiptDetail = new ImportReceiptDetail
        {
            Id = 1000,
            ImportReceiptId = 100,
            ImportReceipt = receipt,
            ProductId = 5,
            Quantity = 1m,
            ReceivedQuantity = 1m
        };
        var stagedSerial = new ImportReceiptInventoryIdentity
        {
            Id = 500,
            ImportReceiptDetailId = 1000,
            ImportReceiptDetail = receiptDetail,
            ProductId = 5,
            SerialNumber = "SN-STAGED-001",
            BaseQuantity = 1m,
            CreatedBy = 10
        };

        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.RolePermissions.Add(rolePerm);
        db.Users.AddRange(maker, checker);
        db.Warehouses.Add(warehouse);
        db.Asns.Add(asn);
        db.ImportReceipts.Add(receipt);
        db.ImportReceiptDetails.Add(receiptDetail);
        db.ImportReceiptInventoryIdentities.Add(stagedSerial);
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(20);
        currentUser.SetupGet(x => x.Role).Returns("Manager");

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var metadata = new Mock<IRequestMetadata>();
        metadata.SetupGet(x => x.CorrelationId).Returns("corr-w4-sec-b-05");

        var identityService = new Mock<IReceiptInventoryIdentityService>();
        var inboundPlanning = new Mock<IReceiptInboundPlanningIntegration>();

        var service = new ApprovalWorkflowService(
            db,
            currentUser.Object,
            warehouseAuth.Object,
            metadata.Object,
            null,
            null,
            identityService.Object,
            inboundPlanning.Object);

        var result = await service.RejectAsync("IMPORT_RECEIPT", 100, "Hàng không đạt chất lượng");

        result.DocumentType.Should().Be("ImportReceipt");
        result.DocumentId.Should().Be(100);

        var updatedReceipt = await db.ImportReceipts.SingleAsync(x => x.Id == 100);
        updatedReceipt.Status.Should().Be(ReceiptStatus.Cancelled);

        var remainingSerials = await db.ImportReceiptInventoryIdentities
            .Where(x => x.ImportReceiptDetailId == 1000)
            .ToListAsync();
        remainingSerials.Should().BeEmpty();

        var updatedAsn = await db.Asns.SingleAsync(x => x.Id == 50);
        updatedAsn.Status.Should().Be(AsnStatus.Arrived);

        identityService.Verify(x => x.GetAsync(100, It.IsAny<CancellationToken>()), Times.Once);
        inboundPlanning.Verify(x => x.MarkReceivingAsync(It.Is<ImportReceipt>(r => r.Id == 100), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StockTransferService_GetByIdAsync_ThrowsForbiddenException_WhenWarehouseNotAllowed()
    {
        await using var db = CreateDbContext();

        var user = new User { Id = 10, Username = "staff", FullName = "Staff", PasswordHash = "h", RoleId = 1, IsActive = true };
        var sourceWarehouse = new Warehouse { Id = 10, Code = "WH10", Name = "Source WH" };
        var destWarehouse = new Warehouse { Id = 20, Code = "WH20", Name = "Dest WH" };
        var transfer = new StockTransfer
        {
            Id = 77,
            Code = "ST-0077",
            SourceWarehouseId = 10,
            DestinationWarehouseId = 20,
            Status = StockTransferStatus.Draft,
            CreatedBy = 10,
            CreatedByUser = user,
            SourceWarehouse = sourceWarehouse,
            DestinationWarehouse = destWarehouse
        };

        db.Users.Add(user);
        db.Warehouses.AddRange(sourceWarehouse, destWarehouse);
        db.StockTransfers.Add(transfer);
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(99);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<int> { 99 });

        var service = new StockTransferService(
            db,
            new Mock<IInventoryStockRepository>().Object,
            warehouseAuth.Object,
            currentUser.Object);

        Func<Task> forbiddenAct = () => service.GetByIdAsync(77);
        await forbiddenAct.Should().ThrowAsync<ForbiddenException>();

        Func<Task> notFoundAct = () => service.GetByIdAsync(9999);
        await notFoundAct.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task StocktakeQueryService_GetStocktakeByIdAsync_ThrowsForbiddenException_WhenWarehouseNotAllowed()
    {
        await using var db = CreateDbContext();

        var user = new User { Id = 10, Username = "staff", FullName = "Staff", PasswordHash = "h", RoleId = 1, IsActive = true };
        var warehouse = new Warehouse { Id = 10, Code = "WH10", Name = "WH 10" };
        var stocktake = new Stocktake
        {
            Id = 88,
            Code = "KK-0088",
            WarehouseId = 10,
            Status = StocktakeStatus.Draft,
            CreatedBy = 10,
            CreatedByUser = user,
            Warehouse = warehouse
        };

        db.Users.Add(user);
        db.Warehouses.Add(warehouse);
        db.Stocktakes.Add(stocktake);
        await db.SaveChangesAsync();

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(10, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền truy cập kho được yêu cầu."));

        var queryService = new StocktakeQueryService(db, warehouseAuth.Object);

        Func<Task> act = () => queryService.GetStocktakeByIdAsync(88);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task StockReservationService_GetAsync_And_GetPageAsync_ThrowForbiddenException_WhenWarehouseNotAllowed()
    {
        await using var db = CreateDbContext();

        var warehouse = new Warehouse { Id = 10, Code = "WH10", Name = "WH 10" };
        var unit = new Unit { Id = 1, Code = "PCS", Name = "Pieces" };
        var product = new Product { Id = 5, Code = "P5", Name = "Prod 5", CategoryId = 1, UnitId = 1, Unit = unit };
        var reservation = new StockReservation
        {
            Id = 66,
            ReservationCode = "RES-0066",
            WarehouseId = 10,
            Warehouse = warehouse,
            ProductId = 5,
            Product = product,
            Status = StockReservationStatus.Active,
            Quantity = 5m,
            CreatedBy = 1
        };

        db.Warehouses.Add(warehouse);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.StockReservations.Add(reservation);
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(10, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền truy cập kho được yêu cầu."));

        var service = new StockReservationService(
            db,
            new Mock<IInventoryStockRepository>().Object,
            new Mock<IUnitOfWork>().Object,
            warehouseAuth.Object,
            currentUser.Object,
            new StockReservationOptions());

        Func<Task> actGet = () => service.GetAsync(66);
        await actGet.Should().ThrowAsync<ForbiddenException>();

        Func<Task> actPage = () => service.GetPageAsync(1, 10, warehouseId: 10, productId: null, status: null);
        await actPage.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task InventoryControlController_ThrowsForbiddenException_WhenWarehouseNotAuthorized()
    {
        await using var db = CreateDbContext();

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(42, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("Bạn không có quyền truy cập kho được yêu cầu."));

        var controller = new InventoryControlController(
            new Mock<IInventoryLockService>().Object,
            new Mock<IInventoryMovementService>().Object,
            db,
            warehouseAuth.Object);

        Func<Task> actReplenishment = () => controller.ReplenishmentSuggestions(db, warehouseAuth.Object, warehouseId: 42, cancellationToken: CancellationToken.None);
        await actReplenishment.Should().ThrowAsync<ForbiddenException>();

        Func<Task> actPlanning = () => controller.AdvancedPlanningSuite(db, warehouseAuth.Object, warehouseId: 42, cancellationToken: CancellationToken.None);
        await actPlanning.Should().ThrowAsync<ForbiddenException>();

        Func<Task> actSimulation = () => controller.RunWhatIfSimulation(
            db,
            warehouseAuth.Object,
            new InventoryControlController.WhatIfSimulationRequest(WarehouseId: 42, DemandMultiplier: 1.5m),
            CancellationToken.None);
        await actSimulation.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task AccountSecurityController_Lock_And_ResetPassword_DoNotDoubleIncrementSecurityRevision()
    {
        await using var db = CreateDbContext();

        var user = new User
        {
            Id = 77,
            Username = "target",
            FullName = "Target User",
            PasswordHash = "old-hash",
            RoleId = 1,
            IsActive = true,
            SecurityRevision = 2
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var rowVersionBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });
        var sessionService = new Mock<IUserSessionService>();
        sessionService
            .Setup(x => x.RevokeUserSessionsAsAdminAsync(77, rowVersionBase64, It.IsAny<CancellationToken>()))
            .Callback<int, string?, CancellationToken>((uid, _, _) =>
            {
                var tracked = db.Users.First(u => u.Id == uid);
                tracked.SecurityRevision++;
            })
            .Returns(Task.CompletedTask);

        var controller = new AccountSecurityController(
            new Mock<IAccountAdminService>().Object,
            sessionService.Object,
            db);

        await controller.Lock(77, new AccountSecurityCommandDto(rowVersionBase64));
        var afterLock = await db.Users.SingleAsync(u => u.Id == 77);
        afterLock.SecurityRevision.Should().Be(3);

        await controller.ResetPassword(77, new ResetPasswordAdminRequest("NewSecurePass#2026", "Admin reset", rowVersionBase64));
        var afterReset = await db.Users.SingleAsync(u => u.Id == 77);
        afterReset.SecurityRevision.Should().Be(4);
    }
}
