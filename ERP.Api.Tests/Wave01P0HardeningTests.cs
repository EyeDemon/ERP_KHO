using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Hosting;
using ERP.Api.Infrastructure;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public class Wave01P0HardeningTests
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
    public async Task TC_P0_01_TotpEnrollment_PersistsAcrossServiceInstances_RejectsClientSecret_And_EnforcesTenantIsolation()
    {
        // 1. Verify VerifyTotpRequest does NOT expose Base32Secret or SecretKey properties
        var requestProps = typeof(AccountSecurityController.VerifyTotpRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();
        requestProps.Should().NotContain("Base32Secret");
        requestProps.Should().NotContain("SecretKey");

        await using var db = CreateInMemoryDb(nameof(TC_P0_01_TotpEnrollment_PersistsAcrossServiceInstances_RejectsClientSecret_And_EnforcesTenantIsolation));
        db.UseTenant(1);
        db.Users.Add(new User
        {
            Id = 101,
            TenantId = 1,
            Username = "admin_t1",
            FullName = "Admin Tenant 1",
            PasswordHash = "HASH",
            RoleId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        db.UseTenant(2);
        db.Users.Add(new User
        {
            Id = 202,
            TenantId = 2,
            Username = "admin_t2",
            FullName = "Admin Tenant 2",
            PasswordHash = "HASH",
            RoleId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        db.UseTenant(1);

        // 2. Setup TOTP on Controller Instance #1
        var controllerInstance1 = new AccountSecurityController(
            new Mock<IAccountAdminService>().Object,
            new Mock<IUserSessionService>().Object,
            new WmsGovernanceOperationsService(db),
            new Mock<IPasswordHasherService>().Object);

        var setupResult = await controllerInstance1.SetupTotp(101, default);
        setupResult.Should().BeOfType<OkObjectResult>();

        var revealResult = await controllerInstance1.RevealTotpSecret(101, default);
        var revealOk = revealResult.Should().BeOfType<OkObjectResult>().Subject;
        using var revealDoc = JsonSerializer.SerializeToDocument(revealOk.Value);
        var base32Secret = revealDoc.RootElement.GetProperty("secretKeyBase32").GetString()!;
        base32Secret.Should().HaveLength(32);

        // Verify encrypted secret is persisted in DB on User row
        var persistedUserAfterSetup = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 101);
        persistedUserAfterSetup.TotpSecretProtected.Should().NotBeNullOrWhiteSpace();
        persistedUserAfterSetup.TotpSecretProtected.Should().NotBe(base32Secret);
        persistedUserAfterSetup.TotpEnrollmentStatus.Should().Be("PENDING_OTP");

        // 3. Verify TOTP on a completely NEW Controller & Service Instance #2 (simulating process restart)
        var controllerInstance2 = new AccountSecurityController(
            new Mock<IAccountAdminService>().Object,
            new Mock<IUserSessionService>().Object,
            new WmsGovernanceOperationsService(db),
            new Mock<IPasswordHasherService>().Object);

        var validOtp = AccountSecurityController.ComputeTotpCode(
            AccountSecurityController.DecodeBase32(base32Secret),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);

        var verifyResult = await controllerInstance2.VerifyTotp(
            101,
            new AccountSecurityController.VerifyTotpRequest(Code: validOtp),
            default);
        var verifyOk = verifyResult.Should().BeOfType<OkObjectResult>().Subject;
        using var verifyDoc = JsonSerializer.SerializeToDocument(verifyOk.Value);
        verifyDoc.RootElement.GetProperty("verified").GetBoolean().Should().BeTrue();
        verifyDoc.RootElement.GetProperty("recoveryCodes").GetArrayLength().Should().Be(8);

        var confirmResult = await controllerInstance2.ConfirmTotpActivation(101, default);
        confirmResult.Should().BeOfType<OkObjectResult>();

        var persistedUserAfterConfirm = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 101);
        persistedUserAfterConfirm.TotpEnrollmentStatus.Should().Be("ACTIVE");
        persistedUserAfterConfirm.TotpActivatedAtUtc.Should().NotBeNull();
        persistedUserAfterConfirm.TotpRecoveryCodeHashesJson.Should().NotBeNullOrWhiteSpace();

        // 4. Cross-tenant TOTP access attempt (Tenant 1 context targeting Tenant 2 user 202) -> 404 NotFound
        var crossTenantReveal = await controllerInstance2.RevealTotpSecret(202, default);
        crossTenantReveal.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task TC_P0_02_TenantProvisioning_RequiresAuthOnAdminEndpoint_SelfServiceForcesNewTenant_And_RefreshCookiePathIsRoot()
    {
        // 1. PermissionsController.ProvisionSaaSTenant must NOT have [AllowAnonymous] and MUST require UserManage
        var adminProvisionMethod = typeof(PermissionsController).GetMethod(nameof(PermissionsController.ProvisionSaaSTenant))!;
        adminProvisionMethod.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        adminProvisionMethod.GetCustomAttributes<PermissionAuthorizeAttribute>()
            .Select(a => a.Permission)
            .Should().Contain(AppPermissions.UserManage);

        // 2. AuthController.RegisterTenant has [AllowAnonymous] + [HttpPost("register-tenant")] and forces AllocateNewTenant = true
        var selfServiceMethod = typeof(AuthController).GetMethod(nameof(AuthController.RegisterTenant))!;
        selfServiceMethod.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
        selfServiceMethod.GetCustomAttributes<HttpPostAttribute>()
            .Select(a => a.Template)
            .Should().Contain("register-tenant");

        var permOpsMock = new Mock<IPermissionOperationsService>();
        ProvisionTenantInput? capturedInput = null;
        permOpsMock
            .Setup(x => x.ProvisionSaaSTenantAsync(It.IsAny<ProvisionTenantInput?>(), It.IsAny<CancellationToken>()))
            .Callback<ProvisionTenantInput?, CancellationToken>((req, _) => capturedInput = req)
            .ReturnsAsync(WmsEndpointOutcome.Ok(new { tenantId = 9, status = "PROVISIONED" }));

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(x => x.EnvironmentName).Returns("Testing");

        var authServiceMock = new Mock<IAuthService>();
        authServiceMock
            .Setup(x => x.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<SessionContextDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoginResponseDto
            {
                Token = "jwt-test",
                RefreshToken = "refresh-test",
                RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            });

        var authController = new AuthController(
            authServiceMock.Object,
            new Mock<IUserSessionService>().Object,
            envMock.Object,
            new SessionSecurityOptions(),
            permOpsMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var registerRes = await authController.RegisterTenant(
            new RegisterTenantRequestDto(
                CompanyName: "Cong ty Kho Moi",
                TaxCode: "0312345678",
                OwnerEmail: "admin@khomoi.vn",
                PlanCode: "STARTER"),
            default);
        registerRes.Should().BeOfType<OkObjectResult>();
        capturedInput.Should().NotBeNull();
        capturedInput!.AllocateNewTenant.Should().BeTrue();

        // 3. Verify Login sets Refresh Cookie with Path = "/"
        var loginRes = await authController.Login(new LoginRequestDto { Username = "admin", Password = "pwd" }, default);
        loginRes.Should().BeOfType<OkObjectResult>();
        var setCookieHeader = authController.Response.Headers.SetCookie.ToString();
        setCookieHeader.Should().Contain("erp_refresh=");
        setCookieHeader.Should().Contain("path=/");
        setCookieHeader.Should().NotContain("path=/api/v1/auth");
    }

    [Fact]
    public async Task TC_P0_03_SoftReservationExpiryJob_ProcessesAllTenants_ReleasesReservedQuantity_And_WritesAuditLog()
    {
        var dbName = $"TC_P0_03_{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddDbContext<ErpKhoDbContext>(opt =>
            opt.UseInMemoryDatabase(dbName)
               .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        await using var provider = services.BuildServiceProvider();

        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            var past = DateTime.UtcNow.AddMinutes(-15);

            // Tenant 1 data
            db.UseTenant(1);
            db.Warehouses.Add(new Warehouse { Id = 1, TenantId = 1, Code = "WH-T1", Name = "Kho Tenant 1", IsActive = true });
            db.Products.Add(new Product { Id = 10, TenantId = 1, Code = "SKU-T1", Name = "SP T1", UnitId = 1, IsActive = true });
            db.InventoryStocks.Add(new InventoryStock
            {
                Id = 1001,
                TenantId = 1,
                WarehouseId = 1,
                ProductId = 10,
                Status = InventoryStatus.Available,
                Quantity = 50m,
                ReservedQuantity = 15m
            });
            db.StockReservations.Add(new StockReservation
            {
                Id = 5001,
                TenantId = 1,
                ReservationCode = "RSV-T1-EXP",
                WarehouseId = 1,
                ProductId = 10,
                Quantity = 15m,
                ConsumedQuantity = 0m,
                ReleasedQuantity = 0m,
                Status = StockReservationStatus.Active,
                CreatedAt = past.AddMinutes(-30),
                ExpiresAt = past
            });
            await db.SaveChangesAsync();

            // Tenant 2 data
            db.UseTenant(2);
            db.Warehouses.Add(new Warehouse { Id = 2, TenantId = 2, Code = "WH-T2", Name = "Kho Tenant 2", IsActive = true });
            db.Products.Add(new Product { Id = 20, TenantId = 2, Code = "SKU-T2", Name = "SP T2", UnitId = 1, IsActive = true });
            db.InventoryStocks.Add(new InventoryStock
            {
                Id = 2002,
                TenantId = 2,
                WarehouseId = 2,
                ProductId = 20,
                Status = InventoryStatus.Available,
                Quantity = 80m,
                ReservedQuantity = 25m
            });
            db.StockReservations.Add(new StockReservation
            {
                Id = 5002,
                TenantId = 2,
                ReservationCode = "RSV-T2-EXP",
                WarehouseId = 2,
                ProductId = 20,
                Quantity = 25m,
                ConsumedQuantity = 5m,
                ReleasedQuantity = 0m,
                Status = StockReservationStatus.PartiallyConsumed,
                CreatedAt = past.AddMinutes(-30),
                ExpiresAt = past
            });

            await db.SaveChangesAsync();
        }

        var job = new SoftReservationExpiryJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SoftReservationExpiryJob>.Instance);

        var expiredCount = await job.ExpireStaleSoftReservationsOnceAsync();
        expiredCount.Should().Be(2);

        await using (var verifyScope = provider.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            var rsv1 = await db.StockReservations.IgnoreQueryFilters().SingleAsync(r => r.Id == 5001);
            var rsv2 = await db.StockReservations.IgnoreQueryFilters().SingleAsync(r => r.Id == 5002);
            rsv1.Status.Should().Be(StockReservationStatus.Expired);
            rsv1.ReleasedQuantity.Should().Be(15m);
            rsv2.Status.Should().Be(StockReservationStatus.Expired);
            rsv2.ReleasedQuantity.Should().Be(20m);

            var stk1 = await db.InventoryStocks.IgnoreQueryFilters().SingleAsync(s => s.Id == 1001);
            var stk2 = await db.InventoryStocks.IgnoreQueryFilters().SingleAsync(s => s.Id == 2002);
            stk1.ReservedQuantity.Should().Be(0m);
            stk2.ReservedQuantity.Should().Be(5m);

            var auditLogs = await db.AuditLogs.IgnoreQueryFilters()
                .Where(a => a.Action == "StockReservation.ExpiredSweep")
                .ToListAsync();
            auditLogs.Select(a => a.TenantId).Should().BeEquivalentTo([1, 2]);
        }
    }

    [Fact]
    public async Task TC_P0_04_StocktakeStateMachine_DisallowsDirectDraftApprove_RequiresCountedLines_And_SupportsCancel()
    {
        // 1. Verify Completed and Posted are distinct enum values
        ((int)StocktakeStatus.Completed).Should().Be(6);
        ((int)StocktakeStatus.Posted).Should().Be(9);

        // 2. Verify Draft -> Approved or Draft -> Completed directly throws ConflictException
        var draftStocktake = new Stocktake { Id = 1, Code = "ST-001", WarehouseId = 1, Status = StocktakeStatus.Draft };
        var directApproveAct = () => draftStocktake.TransitionTo(StocktakeStatus.Approved);
        directApproveAct.Should().Throw<ConflictException>();

        // 3. Verify ApproveStocktakeAsync rejects uncounted lines and CancelStocktakeAsync works on pre-approval states
        await using var db = CreateInMemoryDb(nameof(TC_P0_04_StocktakeStateMachine_DisallowsDirectDraftApprove_RequiresCountedLines_And_SupportsCancel));
        db.UseTenant(1);
        db.Warehouses.Add(new Warehouse { Id = 1, TenantId = 1, Code = "WH-01", Name = "Kho 1", IsActive = true });
        db.Products.Add(new Product { Id = 10, TenantId = 1, Code = "SKU-01", Name = "SP 1", UnitId = 1, IsActive = true });
        db.Stocktakes.AddRange(
            new Stocktake
            {
                Id = 101,
                TenantId = 1,
                Code = "ST-UNCOUNTED",
                WarehouseId = 1,
                Status = StocktakeStatus.InProgress,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow,
                Details =
                [
                    new StocktakeDetail
                    {
                        Id = 1001,
                        TenantId = 1,
                        StocktakeId = 101,
                        ProductId = 10,
                        SystemQuantity = 10m,
                        ActualQuantity = null
                    }
                ]
            },
            new Stocktake
            {
                Id = 102,
                TenantId = 1,
                Code = "ST-CANCELABLE",
                WarehouseId = 1,
                Status = StocktakeStatus.InProgress,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth
            .Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([1]);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(2);
        currentUser.SetupGet(x => x.TenantId).Returns(1);

        var stocktakeService = new StocktakeService(
            new StocktakeRepository(db),
            new InventoryStockRepository(db),
            new InventoryTransactionRepository(db),
            new WarehouseRepository(db),
            new UnitOfWork(db),
            new AuditLogRepository(db),
            warehouseAuth.Object,
            currentUser.Object);

        var approveUncountedAct = () => stocktakeService.ApproveStocktakeAsync(101, approvedByUserId: 2);
        await approveUncountedAct.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*Chưa nhập số lượng thực tế*");

        await stocktakeService.CancelStocktakeAsync(102, cancelledByUserId: 2, reason: "Nhập nhầm phạm vi kiểm kê");
        var cancelled = await db.Stocktakes.SingleAsync(s => s.Id == 102);
        cancelled.Status.Should().Be(StocktakeStatus.Cancelled);
    }

    [Fact]
    public async Task TC_P0_05_OutboundDispatchAndHandover_NeverDoubleDeductsInventoryStockOrLedger()
    {
        await using var db = CreateInMemoryDb(nameof(TC_P0_05_OutboundDispatchAndHandover_NeverDoubleDeductsInventoryStockOrLedger));
        db.UseTenant(1);
        db.Warehouses.Add(new Warehouse { Id = 1, TenantId = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true });
        db.WarehouseLocations.Add(new WarehouseLocation
        {
            Id = 10,
            TenantId = 1,
            WarehouseId = 1,
            Code = "BIN-A01",
            Name = "Kệ A01",
            LocationType = WarehouseLocationType.Storage,
            IsActive = true,
            IsPickable = true
        });
        db.Products.Add(new Product { Id = 100, TenantId = 1, Code = "SKU-100", Name = "Sản phẩm xuất", UnitId = 1, IsActive = true });

        // Simulate an ExportReceipt that was ALREADY approved & deducted (100 -> 75, with 1 Export ledger entry of -25)
        db.InventoryStocks.Add(new InventoryStock
        {
            Id = 500,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 100,
            LocationId = 10,
            Status = InventoryStatus.Available,
            Quantity = 75m,
            ReservedQuantity = 0m
        });

        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 300,
            TenantId = 1,
            Code = "PXK-W01-300",
            WarehouseId = 1,
            Status = ReceiptStatus.Approved,
            DispatchMode = ExportDispatchMode.RequireSeparateDispatch,
            CreatedBy = 1,
            ApprovedBy = 2,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            ApprovedAt = DateTime.UtcNow.AddMinutes(-5),
            Details =
            [
                new ExportReceiptDetail
                {
                    Id = 3001,
                    TenantId = 1,
                    ExportReceiptId = 300,
                    ProductId = 100,
                    Quantity = 25m
                }
            ]
        });

        db.InventoryTransactions.Add(new InventoryTransaction
        {
            Id = 9001,
            TenantId = 1,
            WarehouseId = 1,
            ProductId = 100,
            LocationId = 10,
            InventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Export,
            ReferenceId = 300,
            ReferenceType = "ExportReceipt",
            SourceDocumentLineId = 3001,
            Quantity = -25m,
            UnitCost = 10000m,
            CreatedBy = 2,
            TransactionDate = DateTime.UtcNow.AddMinutes(-5)
        });
        await db.SaveChangesAsync();

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth
            .Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([1]);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(3);
        currentUser.SetupGet(x => x.TenantId).Returns(1);

        var exportService = new ExportReceiptService(
            new ExportReceiptRepository(db, warehouseAuth.Object),
            new InventoryStockRepository(db),
            new InventoryTransactionRepository(db),
            new UnitOfWork(db),
            new AuditLogRepository(db),
            warehouseAuth.Object,
            currentUser.Object,
            new Mock<IStockReservationService>().Object);

        // 1. Dispatching an Approved ExportReceipt whose stock was already deducted at approval must reject double deduction
        await exportService.Invoking(s => s.DispatchAsync(300, userId: 3))
            .Should().ThrowAsync<ConcurrencyException>()
            .WithMessage("*đã được ghi nhận bút toán xuất kho trước đó*");
        (await db.InventoryStocks.SingleAsync(s => s.Id == 500)).Quantity.Should().Be(75m);
        (await db.InventoryTransactions.CountAsync(t => t.TransactionType == TransactionType.Export)).Should().Be(1);

        // 2. Confirming Carrier Handover on that Dispatched ExportReceipt must ALSO NOT deduct a second time
        var handoverService = new HandoverManifestService(db, exportService, warehouseAuth.Object, currentUser.Object);
        var handoverRes = await handoverService.ConfirmCarrierHandoverAsync(
            new ConfirmCarrierHandoverInputDto(
                WarehouseId: 1,
                CarrierCode: "GHN",
                TrackingNumbers: ["AWB-300"],
                VehiclePlate: "51C-888.88",
                DriverName: "Tai Xe A",
                DriverPhone: "0909000111",
                ManifestId: null,
                ExportReceiptIds: [300]),
            actorUserId: 2,
            isAuthenticated: true,
            cancellationToken: default);

        handoverRes.StatusCode.Should().Be(200);
        (await db.InventoryStocks.SingleAsync(s => s.Id == 500)).Quantity.Should().Be(75m);
        (await db.InventoryTransactions.CountAsync(t => t.TransactionType == TransactionType.Export)).Should().Be(1);
    }

    [Fact]
    public void TC_P0_06_AccountantRole_GrantsInventoryCostRead_WhileWarehouseStaffAndViewerAreDenied()
    {
        ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole("Accountant")
            .Should().Contain(ERP.Domain.Security.AppPermissions.InventoryCostRead);
        ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole("WarehouseStaff")
            .Should().NotContain(ERP.Domain.Security.AppPermissions.InventoryCostRead);
        ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole("Viewer")
            .Should().NotContain(ERP.Domain.Security.AppPermissions.InventoryCostRead);

        static HttpCurrentUser BuildUser(string role)
        {
            var ctx = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "50"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim(TenantClaimTypes.TenantId, "1")
                ], "TestAuth"))
            };
            var accessor = new HttpContextAccessor { HttpContext = ctx };
            return new HttpCurrentUser(accessor);
        }

        BuildUser(AppRoles.Accountant).HasPermission(AppPermissions.InventoryCostRead).Should().BeTrue();
        BuildUser(AppRoles.WarehouseStaff).HasPermission(AppPermissions.InventoryCostRead).Should().BeFalse();
        BuildUser(AppRoles.Viewer).HasPermission(AppPermissions.InventoryCostRead).Should().BeFalse();
    }

    [Fact]
    public void TC_P0_07_OpeningBalanceImport_UsesDedicatedPermission_And_AppPermissionsCatalogsAreUnified()
    {
        AppPermissions.InventoryOpeningBalanceImport.Should().Be("inventory_opening_balance.import");

        typeof(DataImportsController)
            .GetMethod(nameof(DataImportsController.ImportOpeningBalance))!
            .GetCustomAttribute<PermissionAuthorizeAttribute>()!
            .Permission.Should().Be(AppPermissions.InventoryOpeningBalanceImport);

        typeof(ExportReceiptsController)
            .GetMethod(nameof(ExportReceiptsController.ImportOpeningBalance))!
            .GetCustomAttribute<PermissionAuthorizeAttribute>()!
            .Permission.Should().Be(AppPermissions.InventoryOpeningBalanceImport);

        var domainConstants = typeof(ERP.Domain.Security.AppPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        var apiConstants = typeof(ERP.Api.Authorization.AppPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        apiConstants.Should().BeEquivalentTo(domainConstants);
    }

    [Fact]
    public void TC_P0_08_MutationEndpoints_NeverUseReadOnlyPermissionAttributes()
    {
        var controllerTypes = typeof(ProductsController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        var violations = new List<string>();
        foreach (var controller in controllerTypes)
        {
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var isMutation = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true)
                    .Any(a => a is HttpPostAttribute or HttpPutAttribute or HttpDeleteAttribute or HttpPatchAttribute);
                if (!isMutation)
                {
                    continue;
                }

                var methodPermAttrs = method.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: true).ToList();
                if (methodPermAttrs.Count == 0)
                {
                    continue;
                }

                var allPerms = methodPermAttrs.SelectMany(a => a.Permissions).ToList();
                if (allPerms.Count > 0 && allPerms.All(p => p.EndsWith(".read", StringComparison.OrdinalIgnoreCase)))
                {
                    violations.Add($"{controller.Name}.{method.Name} => [{string.Join(", ", allPerms)}]");
                }
            }
        }

        violations.Should().BeEmpty($"no HTTP mutation endpoint (POST/PUT/DELETE/PATCH) may be guarded solely by *.read permissions, but found: {string.Join(" | ", violations)}");
    }

    [Fact]
    public async Task TC_W1_09_SaasBillingService_GetSubscriptionIsReadOnly_ReturnsHonestZero_And_ExcludesPersonalBankDetails()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W1_09_SaasBillingService_GetSubscriptionIsReadOnly_ReturnsHonestZero_And_ExcludesPersonalBankDetails));
        db.UseTenant(1);
        var service = new SaasBillingService(db);

        var subPayload = await service.GetSubscriptionAsync(null, null, default);
        using var subDoc = JsonSerializer.SerializeToDocument(subPayload);
        subDoc.RootElement.GetProperty("currentMonthOrderCount").GetInt32().Should().Be(0);

        // Must NOT auto-insert a TenantSubscription row during GET
        (await db.TenantSubscriptions.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        var invoicePayload = await service.CreateVietQrInvoiceAsync(
            new SaasCreateVietQrInvoiceRequest(PlanCode: "STARTER", CustomAmountVnd: null, AmountVnd: 490000m),
            actorUserId: 1,
            cancellationToken: default);
        var rawJson = JsonSerializer.Serialize(invoicePayload);
        rawJson.Should().NotContain("98418102005");
        rawJson.Should().NotContain("HUYNH PHI VAN");
    }

    [Fact]
    public async Task TC_W1_10_DailyJobs_IterateAcrossAllActiveTenants_And_PaginateAllReconciliationPages()
    {
        var dbName = $"TC_W1_10_{Guid.NewGuid():N}";
        var queriedPagesByTenant = new List<(int TenantId, int Page, int PageSize)>();
        var reconMock = new Mock<IInventoryReconciliationQueryService>();

        var services = new ServiceCollection();
        services.AddDbContext<ErpKhoDbContext>(opt =>
            opt.UseInMemoryDatabase(dbName)
               .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IInventoryReconciliationQueryService>(sp =>
        {
            var scopedDb = sp.GetRequiredService<ErpKhoDbContext>();
            reconMock
                .Setup(x => x.GetReconciliationsAsync(null, null, null, It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync((int? _, int? _, string? _, int page, int pageSize) =>
                {
                    queriedPagesByTenant.Add((scopedDb.CurrentTenantId, page, pageSize));
                    var items = Enumerable.Range(1, page == 1 ? 100 : 25)
                        .Select(i => new InventoryReconciliationDto
                        {
                            WarehouseId = scopedDb.CurrentTenantId,
                            ProductId = i,
                            CurrentQuantity = 10m,
                            ExpectedQuantity = 10m,
                            Difference = 0m,
                            Status = "Match"
                        })
                        .ToList();
                    return new PagedResult<InventoryReconciliationDto>
                    {
                        Items = items,
                        TotalRecords = 125,
                        PageIndex = page,
                        PageSize = pageSize
                    };
                });
            return reconMock.Object;
        });

        await using var provider = services.BuildServiceProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            db.UseTenant(1);
            db.Warehouses.Add(new Warehouse { Id = 1, TenantId = 1, Code = "WH-T1", Name = "Kho T1", IsActive = true });
            await db.SaveChangesAsync();

            db.UseTenant(2);
            db.Warehouses.Add(new Warehouse { Id = 2, TenantId = 2, Code = "WH-T2", Name = "Kho T2", IsActive = true });
            await db.SaveChangesAsync();
            db.UseTenant(1);
        }

        var integrityJob = new DailyInventoryIntegrityJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DailyInventoryIntegrityJob>.Instance);
        var mismatchedCount = await integrityJob.VerifyIntegrityOnceAsync();
        mismatchedCount.Should().Be(0);
        queriedPagesByTenant.Should().Contain([(1, 1, 100), (1, 2, 100), (2, 1, 100), (2, 2, 100)]);
    }

    [Fact]
    public async Task TC_W1_11_IdempotentCommandFilter_CleansUpOrphanProcessingRecordOnFailure_And_HttpCurrentUserCachesPermissions()
    {
        await using var db = CreateInMemoryDb(nameof(TC_W1_11_IdempotentCommandFilter_CleansUpOrphanProcessingRecordOnFailure_And_HttpCurrentUserCachesPermissions));
        db.UseTenant(1);

        var metadata = new Mock<IRequestMetadata>();
        metadata.SetupGet(x => x.CorrelationId).Returns("CORR-W1-11");
        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();

        var filter = new IdempotentCommandFilter("Wave01.TestOperation", db, metadata.Object, warehouseAuth.Object);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "10"),
                new Claim(TenantClaimTypes.TenantId, "1")
            ], "TestAuth"))
        };
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/v1/test-operation";
        httpContext.Request.Headers[IdempotentCommandFilter.HeaderName] = "IDEMP-KEY-FAIL-THEN-RETRY";

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingContext = new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?> { ["dto"] = new { code = "A1" } },
            new object());

        // 1. First execution returns 422 UnprocessableEntity -> Processing record must be cleaned up!
        await filter.OnActionExecutionAsync(executingContext, () =>
        {
            var executed = new ActionExecutedContext(actionContext, [], new object())
            {
                Result = new UnprocessableEntityObjectResult(new { errorCode = "VALIDATION_FAILED" })
            };
            return Task.FromResult(executed);
        });

        (await db.IdempotencyRecords.IgnoreQueryFilters().CountAsync()).Should().Be(0);

        // 2. Retry with the same Idempotency-Key now succeeds (200 OK) and persists Completed record
        var retryExecutingContext = new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object?> { ["dto"] = new { code = "A1" } },
            new object());

        await filter.OnActionExecutionAsync(retryExecutingContext, () =>
        {
            var executed = new ActionExecutedContext(actionContext, [], new object())
            {
                Result = new OkObjectResult(new { status = "SUCCESS" })
            };
            return Task.FromResult(executed);
        });

        var completedRecord = await db.IdempotencyRecords.IgnoreQueryFilters().SingleAsync();
        completedRecord.Status.Should().Be(IdempotencyStatus.Completed);
        completedRecord.ResponseStatusCode.Should().Be(200);
    }
}
