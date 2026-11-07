using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Api.Middleware;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public sealed partial class MultiRoleAuthorizationMatrixTests
{
    [Fact]
    public async Task Forensic_SEC_W17_001_PublicRegisterTenant_BlocksReservedUsername_ClampsPlan_And_Returns409WithoutPasswordOverwrite()
    {
        var dbName = $"{nameof(Forensic_SEC_W17_001_PublicRegisterTenant_BlocksReservedUsername_ClampsPlan_And_Returns409WithoutPasswordOverwrite)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Roles.Add(new Role { Id = 1, RoleName = "Admin", SecurityDomain = "CUSTOMER_TENANT" });
        db.SaasTenants.Add(new SaasTenant
        {
            Id = 1,
            TenantId = 1,
            TenantCode = "TEN-EXISTING-01",
            CompanyName = "Công ty Hiện Hữu",
            TaxCode = "0319998887",
            OwnerEmail = "owner@existing.vn",
            LifecycleStatus = "ACTIVE",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-10)
        });
        db.Users.Add(new User
        {
            Id = 10,
            TenantId = 1,
            Username = "existing_owner",
            FullName = "Chủ Thuê Bao Cũ",
            PasswordHash = "ORIGINAL_HASH_DO_NOT_OVERWRITE",
            RoleId = 1,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var hasher = new Mock<IPasswordHasherService>();
        hasher.Setup(x => x.HashPassword(It.IsAny<string>())).Returns("NEW_HASH_VALUE");
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.EnvironmentName).Returns("Testing");

        var permOps = new PermissionOperationsService(db, passwordHasher: hasher.Object);
        var authCtrl = new AuthController(
            Mock.Of<IAuthService>(),
            Mock.Of<IUserSessionService>(),
            env.Object,
            new SessionSecurityOptions(),
            permOps)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        // 1. Reserved username 'qa_admin' -> 400 RESERVED_USERNAME_FORBIDDEN
        var reservedRes = (await authCtrl.RegisterTenant(new RegisterTenantRequestDto(
            CompanyName: "Công ty Thử Nghiệm",
            TaxCode: "0311112223",
            OwnerEmail: "test@demo.vn",
            PlanCode: "STARTER",
            OwnerUsername: "qa_admin",
            OwnerPassword: "StrongPassword!123"))).Should().BeAssignableTo<ObjectResult>().Subject;
        reservedRes.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        using (var doc = JsonSerializer.SerializeToDocument(reservedRes.Value))
        {
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("RESERVED_USERNAME_FORBIDDEN");
        }

        // 2. Public registration requesting ENTERPRISE -> clamped to STARTER
        var clampedRes = (await authCtrl.RegisterTenant(new RegisterTenantRequestDto(
            CompanyName: "Công ty Đăng Ký Công Khai",
            TaxCode: "0317776665",
            OwnerEmail: "public@demo.vn",
            PlanCode: "ENTERPRISE",
            OwnerUsername: "public_owner_w17",
            OwnerPassword: "StrongPassword!123"))).Should().BeOfType<OkObjectResult>().Subject;
        using (var doc = JsonSerializer.SerializeToDocument(clampedRes.Value))
        {
            doc.RootElement.GetProperty("planCode").GetString().Should().Be("STARTER");
        }
        var createdTenant = await db.SaasTenants.IgnoreQueryFilters().SingleAsync(t => t.TaxCode == "0317776665");
        var createdSub = await db.TenantSubscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == createdTenant.TenantId);
        createdSub.PlanCode.Should().Be("STARTER");

        // 3. Duplicate OwnerUsername or TaxCode -> 409 TENANT_OR_USERNAME_ALREADY_EXISTS and zero PasswordHash overwrite
        foreach (var dupReq in new[]
        {
            new RegisterTenantRequestDto("Công ty Trùng User", "0315554443", "dup1@demo.vn", null, "STARTER", "existing_owner", "AttackerPass!999"),
            new RegisterTenantRequestDto("Công ty Trùng MST", "0319998887", "dup2@demo.vn", null, "STARTER", "brand_new_owner", "AttackerPass!999")
        })
        {
            var dupRes = (await authCtrl.RegisterTenant(dupReq)).Should().BeAssignableTo<ObjectResult>().Subject;
            dupRes.StatusCode.Should().Be(StatusCodes.Status409Conflict);
            using var dupDoc = JsonSerializer.SerializeToDocument(dupRes.Value);
            dupDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("TENANT_OR_USERNAME_ALREADY_EXISTS");
        }

        var preservedOwner = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == 10);
        preservedOwner.PasswordHash.Should().Be("ORIGINAL_HASH_DO_NOT_OVERWRITE");
    }

    [Fact]
    public async Task Forensic_SEC_W17_002_ImpersonationRedaction_StopAuth_And_PurePlatformCustomerApiBlock()
    {
        var dbName = $"{nameof(Forensic_SEC_W17_002_ImpersonationRedaction_StopAuth_And_PurePlatformCustomerApiBlock)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        var rawSecretToken = "RAW-IMPERSONATION-BEARER-SECRET";
        var tokenHashHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawSecretToken)));
        db.SupportImpersonationSessions.Add(new SupportImpersonationSession
        {
            Id = 5,
            TenantId = 1,
            SessionCode = "IMP-FULL-SECRET-9876",
            TokenHash = tokenHashHex,
            SupportAgentUserId = 88,
            SupportTicketCode = "SUP-W17-002",
            ReadOnlyScope = true,
            Status = "ACTIVE",
            GrantedByUserId = 1,
            GrantedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(28)
        });
        await db.SaveChangesAsync();

        // 1. ListSupportImpersonationSessions redacts TokenHash and masks SessionCode; controller requires UserManage only
        var govSvc = new WmsGovernanceOperationsService(db);
        var listOutcome = await govSvc.ListSupportImpersonationSessionsAsync();
        var listJson = JsonSerializer.Serialize(listOutcome.Payload);
        listJson.Should().NotContain("tokenHash", because: "TokenHash must never be exposed in API responses");
        listJson.Should().NotContain(tokenHashHex);
        listJson.Should().NotContain("IMP-FULL-SECRET-9876");
        listJson.Should().Contain("IMP-****9876");

        var listMethod = typeof(AccountSecurityController).GetMethod(nameof(AccountSecurityController.ListSupportImpersonationSessions))!;
        var listPermAttr = listMethod.GetCustomAttribute<PermissionAuthorizeAttribute>()!;
        listPermAttr.Permissions.Should().Equal(AppPermissions.UserManage);

        // 2. StopSupportImpersonation has no [AllowAnonymous] and rejects unauthenticated callers with 401
        var stopMethod = typeof(PlatformControlCenterController).GetMethod(nameof(PlatformControlCenterController.StopSupportImpersonation))!;
        stopMethod.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Should().BeEmpty();
        stopMethod.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Should().NotBeEmpty();

        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(x => x.EnvironmentName).Returns("Testing");
        var anonCtrl = new PlatformControlCenterController(db, Mock.Of<IAuthService>(), env.Object, new SessionSecurityOptions())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var anonStopRes = await anonCtrl.StopSupportImpersonation(new PlatformControlCenterController.StopSupportImpersonationRequest(SessionId: 5));
        anonStopRes.Should().BeOfType<UnauthorizedObjectResult>();

        // 3. Pure PLATFORM operators without X-Support-Impersonation-Token blocked on GET /api/v1/sales-orders and GET /api/v1/reports/owner-gross-profit
        foreach (var purePlatformRole in new[] { "SupportAdmin", "BillingAdmin", "SecurityAuditor" })
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "88"),
                new Claim(ClaimTypes.Name, $"op_{purePlatformRole.ToLowerInvariant()}"),
                new Claim(ClaimTypes.Role, purePlatformRole),
                new Claim(TenantClaimTypes.TenantId, "1"),
                new Claim(TenantClaimTypes.SecurityDomain, "PLATFORM")
            ], "TestAuth"));

            foreach (var customerPath in new[] { "/api/v1/sales-orders", "/api/v1/reports/owner-gross-profit" })
            {
                var nextCalled = false;
                var mw = new SupportImpersonationMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
                var ctx = new DefaultHttpContext { User = principal };
                ctx.Request.Method = HttpMethods.Get;
                ctx.Request.Path = customerPath;
                ctx.Response.Body = new MemoryStream();

                await mw.InvokeAsync(ctx);
                nextCalled.Should().BeFalse();
                ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
                ctx.Response.Body.Position = 0;
                using var err = await JsonDocument.ParseAsync(ctx.Response.Body);
                err.RootElement.GetProperty("errorCode").GetString().Should().Be("PLATFORM_DIRECT_WAREHOUSE_ACCESS_FORBIDDEN");
            }
        }
    }

    [Fact]
    public async Task Forensic_SEC_W17_010_And_016_WarehouseNullScope_Command403Log_And_ThreePlDepositorBoundOwner()
    {
        var dbName = $"{nameof(Forensic_SEC_W17_010_And_016_WarehouseNullScope_Command403Log_And_ThreePlDepositorBoundOwner)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Warehouses.AddRange(
            new Warehouse { Id = 1, TenantId = 1, Code = "WH-01", Name = "Kho 1", IsActive = true },
            new Warehouse { Id = 2, TenantId = 1, Code = "WH-02", Name = "Kho 2", IsActive = true });
        db.Products.Add(new Product { Id = 100, TenantId = 1, Code = "SKU-100", Name = "Sản phẩm 100", MovingAverageCost = 50m, IsActive = true });
        db.UserWarehouses.Add(new UserWarehouse { UserId = 55, WarehouseId = 1 });
        db.InventoryStocks.AddRange(
            new InventoryStock { Id = 1, TenantId = 1, WarehouseId = 1, ProductId = 100, Quantity = 10m, MovingAverageCost = 50m, Status = InventoryStatus.Available },
            new InventoryStock { Id = 2, TenantId = 1, WarehouseId = 2, ProductId = 100, Quantity = 999m, MovingAverageCost = 50m, Status = InventoryStatus.Available });
        await db.SaveChangesAsync();

        var scopedUser = new Mock<ICurrentUser>();
        scopedUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        scopedUser.SetupGet(x => x.UserId).Returns(55);
        scopedUser.SetupGet(x => x.TenantId).Returns(1);
        scopedUser.SetupGet(x => x.Role).Returns("Manager");
        scopedUser.SetupGet(x => x.IsGlobalAdmin).Returns(false);
        scopedUser.Setup(x => x.HasPermission(It.IsAny<string>())).Returns(true);

        var loggerMock = new Mock<ILogger<WarehouseAuthorizationService>>();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "55"),
                new Claim(ClaimTypes.Role, "Manager"),
                new Claim(TenantClaimTypes.TenantId, "1")
            ], "TestAuth"))
        };
        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var whAuth = new WarehouseAuthorizationService(db, scopedUser.Object, loggerMock.Object, accessor);

        // 1. RebuildBalances(null) & GetValuation(null) only touch/return Warehouse 1
        var reservationMock = new Mock<IStockReservationService>();
        reservationMock.Setup(x => x.RebuildBalancesAsync(1, null, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var reconCtrl = new InventoryReconciliationController(
            Mock.Of<IInventoryReconciliationQueryService>(),
            new WmsGovernanceOperationsService(db, whAuth, scopedUser.Object),
            whAuth,
            reservationMock.Object,
            scopedUser.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        await reconCtrl.RebuildBalances(warehouseId: null, productId: null);
        reservationMock.Verify(x => x.RebuildBalancesAsync(1, null, It.IsAny<CancellationToken>()), Times.Once);
        reservationMock.Verify(x => x.RebuildBalancesAsync(null, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        reservationMock.Verify(x => x.RebuildBalancesAsync(2, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);

        var valRes = (await reconCtrl.GetValuation(warehouseId: null, productId: null)).Should().BeOfType<OkObjectResult>().Subject;
        using (var valDoc = JsonSerializer.SerializeToDocument(valRes.Value))
        {
            valDoc.RootElement.GetProperty("totalOnHandQuantity").GetDecimal().Should().Be(10m);
        }

        // 2. DataImportsController.ImportOpeningBalance on Warehouse 2 throws ForbiddenException (403) + structured warning log (SEC-W17-016)
        var importCtrl = new DataImportsController(Mock.Of<IDataImportService>(), whAuth, scopedUser.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        await FluentActions.Awaiting(() => importCtrl.ImportOpeningBalance(
                new DataImportOpeningBalanceRequest { WarehouseId = 2 }))
            .Should().ThrowAsync<ForbiddenException>();

        loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("SECURITY_WAREHOUSE_SCOPE_DENIED")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        // 3. 3PL Depositor bound to Owner 10 spoofing authenticatedOwnerId=20 & requestedOwnerId=20 -> 403 TENANT_DEPOSITOR_ACCESS_DENIED
        var helperMethod = typeof(InventoryControlController).GetMethod("ResolveServerBoundOwnerId", BindingFlags.Instance | BindingFlags.NonPublic)!;
        helperMethod.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Should().BeEmpty();
        var portalMethod = typeof(InventoryControlController).GetMethod(nameof(InventoryControlController.GetThreePlDepositorPortal))!;
        portalMethod.GetCustomAttributes<HttpGetAttribute>(inherit: true).Select(a => a.Template).Should().Contain("3pl-depositor-portal");

        var depositorHttpCtx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "55"),
                new Claim(ClaimTypes.Role, "Viewer"),
                new Claim(TenantClaimTypes.TenantId, "1"),
                new Claim("owner_id", "10"),
                new Claim("permission", AppPermissions.InventoryRead)
            ], "TestAuth"))
        };
        var invCtrl = new InventoryControlController(
            Mock.Of<IInventoryLockService>(),
            Mock.Of<IInventoryMovementService>(),
            new InventoryControlOperationsService(db, whAuth, scopedUser.Object),
            whAuth)
        {
            ControllerContext = new ControllerContext { HttpContext = depositorHttpCtx }
        };

        var spoofRes = (await invCtrl.GetThreePlDepositorPortal(
            ownerId: 20,
            requestedOwnerId: 20,
            authenticatedOwnerId: 20,
            warehouseId: 1)).Should().BeOfType<ObjectResult>().Subject;
        spoofRes.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        using var spoofDoc = JsonSerializer.SerializeToDocument(spoofRes.Value);
        spoofDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("TENANT_DEPOSITOR_ACCESS_DENIED");
    }

    [Fact]
    public async Task Forensic_SEC_W17_012_PlatformLoginAndTotpRateLimiting_And_ZeroOtpEchoInVerifyTotp()
    {
        var platformLogin = typeof(PlatformControlCenterController).GetMethod(nameof(PlatformControlCenterController.Login))!;
        platformLogin.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName.Should().Be("Login");

        var verifyTotpAction = typeof(AccountSecurityController).GetMethod(nameof(AccountSecurityController.VerifyTotp))!;
        verifyTotpAction.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName.Should().Be("Login");

        var dbName = $"{nameof(Forensic_SEC_W17_012_PlatformLoginAndTotpRateLimiting_And_ZeroOtpEchoInVerifyTotp)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);
        db.Users.Add(new User { Id = 31, TenantId = 1, Username = "mfa_user_w17", FullName = "Người dùng MFA", PasswordHash = "h", RoleId = 2, IsActive = true });
        await db.SaveChangesAsync();

        var govSvc = new WmsGovernanceOperationsService(db);
        await govSvc.SetupTotpAsync(userId: 31, actorUserId: 31);
        var revealOutcome = await govSvc.RevealTotpSecretAsync(userId: 31);
        using var revealDoc = JsonSerializer.SerializeToDocument(revealOutcome.Payload);
        var base32Secret = revealDoc.RootElement.GetProperty("base32Secret").GetString()!;

        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30L;
        var validOtp = PermissionOperationsService.ComputeTotpCode(PermissionOperationsService.DecodeBase32(base32Secret), timeStep);

        var verifyOutcome = await govSvc.VerifyTotpAsync(userId: 31, candidateCode: validOtp, actorUserId: 31);
        verifyOutcome.StatusCode.Should().Be(StatusCodes.Status200OK);
        var verifyJson = JsonSerializer.Serialize(verifyOutcome.Payload);
        verifyJson.Should().NotContain(validOtp, because: "VerifyTotpAsync must never echo the submitted 6-digit OTP back in the response");
    }

    [Fact]
    public async Task Forensic_SEC_W17_013_HttpCurrentUser_RevokedDbRolePermission_OverridesStaleJwtPermissionClaim()
    {
        var dbName = $"{nameof(Forensic_SEC_W17_013_HttpCurrentUser_RevokedDbRolePermission_OverridesStaleJwtPermissionClaim)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Roles.Add(new Role { Id = 2, RoleName = "Manager", SecurityDomain = "CUSTOMER_TENANT" });
        db.Permissions.AddRange(
            new Permission(AppPermissions.InventoryRead, "Xem tồn kho") { Id = 10 },
            new Permission(AppPermissions.InventoryCostRead, "Xem giá vốn") { Id = 11 });
        db.RolePermissions.Add(new RolePermission { RoleId = 2, PermissionId = 10 }); // inventory.cost.read revoked in DB!
        db.Users.Add(new User { Id = 77, TenantId = 1, Username = "mgr_revoked", FullName = "Quản lý", PasswordHash = "h", RoleId = 2, IsActive = true });
        await db.SaveChangesAsync();

        ClaimsPrincipal BuildStalePrincipal() => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "77"),
            new Claim(ClaimTypes.Name, "mgr_revoked"),
            new Claim(ClaimTypes.Role, "Manager"),
            new Claim(TenantClaimTypes.TenantId, "1"),
            new Claim("permission", AppPermissions.InventoryRead),
            new Claim("permission", AppPermissions.InventoryCostRead) // Stale JWT claim!
        ], "TestAuth"));

        // 1. Preloaded PermissionAuthorizationFilter.CachedPermissionsKey authoritative over stale JWT claim
        var preloadedCtx = new DefaultHttpContext { User = BuildStalePrincipal() };
        preloadedCtx.Items[PermissionAuthorizationFilter.CachedPermissionsKey] =
            new HashSet<string>([AppPermissions.InventoryRead], StringComparer.OrdinalIgnoreCase);
        var preloadedUser = new HttpCurrentUser(new HttpContextAccessor { HttpContext = preloadedCtx });
        preloadedUser.HasPermission(AppPermissions.InventoryRead).Should().BeTrue();
        preloadedUser.HasPermission(AppPermissions.InventoryCostRead).Should().BeFalse();

        // 2. Direct DB RolePermissions lookup authoritative over stale JWT claim
        var services = new ServiceCollection();
        services.AddSingleton(db);
        var dbCtx = new DefaultHttpContext
        {
            User = BuildStalePrincipal(),
            RequestServices = services.BuildServiceProvider()
        };
        var dbBackedUser = new HttpCurrentUser(new HttpContextAccessor { HttpContext = dbCtx });
        dbBackedUser.HasPermission(AppPermissions.InventoryRead).Should().BeTrue();
        dbBackedUser.HasPermission(AppPermissions.InventoryCostRead).Should().BeFalse();
    }

    [Fact]
    public async Task Forensic_QA_W17_R1_004_WarehouseStaff_MoveHandlingUnit_OutOfScopeWarehouse2_Returns403Forbidden()
    {
        var dbName = $"{nameof(Forensic_QA_W17_R1_004_WarehouseStaff_MoveHandlingUnit_OutOfScopeWarehouse2_Returns403Forbidden)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Warehouses.AddRange(
            new Warehouse { Id = 1, TenantId = 1, Code = "WH01", Name = "Kho Chính WH01", IsActive = true },
            new Warehouse { Id = 2, TenantId = 1, Code = "WH02", Name = "Kho Phụ WH02", IsActive = true },
            new Warehouse { Id = 3, TenantId = 1, Code = "WH03", Name = "Kho Ngừng Hoạt Động WH03", IsActive = false });
        db.Users.Add(new User
        {
            Id = 88,
            TenantId = 1,
            Username = "qa_staff_wh01",
            FullName = "Nhân viên kho WH01",
            PasswordHash = "h",
            RoleId = 5,
            IsActive = true
        });
        db.UserWarehouses.Add(new UserWarehouse { UserId = 88, WarehouseId = 1, CreatedBy = 1 });
        // Seed HU-QA-001 inside WH01 so we also verify zero cross-warehouse fallback when warehouseId=2 is requested!
        db.HandlingUnits.Add(new HandlingUnit
        {
            Id = 501,
            TenantId = 1,
            HuCode = "HU-QA-001",
            Barcode = "HU-QA-001",
            WarehouseId = 1,
            Type = HandlingUnitType.Pallet,
            Status = HandlingUnitStatus.InUse,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var staffUser = new Mock<ICurrentUser>();
        staffUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        staffUser.SetupGet(x => x.UserId).Returns(88);
        staffUser.SetupGet(x => x.TenantId).Returns(1);
        staffUser.SetupGet(x => x.Role).Returns("WarehouseStaff");
        staffUser.SetupGet(x => x.IsGlobalAdmin).Returns(false);
        staffUser.Setup(x => x.HasPermission(AppPermissions.InventoryMovementCreate)).Returns(true);

        var httpCtx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "88"),
                new Claim(ClaimTypes.Name, "qa_staff_wh01"),
                new Claim(ClaimTypes.Role, "WarehouseStaff"),
                new Claim(TenantClaimTypes.TenantId, "1"),
                new Claim("permission", AppPermissions.InventoryMovementCreate)
            ], "TestAuth"))
        };
        var accessor = new HttpContextAccessor { HttpContext = httpCtx };
        var whAuth = new WarehouseAuthorizationService(db, staffUser.Object, null, accessor);
        var movementService = new InventoryMovementService(db, whAuth, staffUser.Object, Mock.Of<IInventoryLockEvaluator>());
        var controller = new InventoryMovementsController(movementService, staffUser.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpCtx }
        };

        async Task<(int StatusCode, string Code)> InvokeThroughMiddlewareAsync(Func<Task> action)
        {
            var mwCtx = new DefaultHttpContext();
            mwCtx.Response.Body = new MemoryStream();
            var middleware = new GlobalExceptionHandlingMiddleware(
                async _ => await action(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
            await middleware.InvokeAsync(mwCtx);
            mwCtx.Response.Body.Position = 0;
            using var doc = await JsonDocument.ParseAsync(mwCtx.Response.Body);
            var code = doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() ?? string.Empty : string.Empty;
            return (mwCtx.Response.StatusCode, code);
        }

        // 1. Out-of-scope active WH02 (warehouseId=2) -> deterministic 403 WAREHOUSE_ACCESS_DENIED (never 500 or cross-warehouse 200)
        var (wh02Status, wh02Code) = await InvokeThroughMiddlewareAsync(() =>
            controller.MoveHandlingUnit(new InventoryMovementsController.FlexibleMoveHandlingUnitRequest(
                HandlingUnitCode: "HU-QA-001",
                WarehouseId: 2,
                DestinationLocationCode: "BIN-A01-03",
                Reason: "Probe out-of-scope WH02 write by WH01 staff")));
        wh02Status.Should().Be(StatusCodes.Status403Forbidden);
        wh02Code.Should().Be("WAREHOUSE_ACCESS_DENIED");

        // 2. Out-of-scope deactivated WH03 (warehouseId=3) -> deterministic 403 WAREHOUSE_ACCESS_DENIED (never 500)
        var (wh03Status, wh03Code) = await InvokeThroughMiddlewareAsync(() =>
            controller.MoveHandlingUnit(new InventoryMovementsController.FlexibleMoveHandlingUnitRequest(
                HandlingUnitCode: "HU-QA-001",
                WarehouseId: 3,
                DestinationLocationCode: "BIN-A01-03",
                Reason: "Probe out-of-scope inactive WH03 write by WH01 staff")));
        wh03Status.Should().Be(StatusCodes.Status403Forbidden);
        wh03Code.Should().Be("WAREHOUSE_ACCESS_DENIED");

        // 3. In-scope WH01 missing HandlingUnitCode -> 400 HANDLING_UNIT_REQUIRED; missing DestinationLocationCode -> 400 DESTINATION_LOCATION_REQUIRED (no BIN-A01-03 fallback)
        var (missingHuStatus, missingHuCode) = await InvokeThroughMiddlewareAsync(() =>
            controller.MoveHandlingUnit(new InventoryMovementsController.FlexibleMoveHandlingUnitRequest(
                WarehouseId: 1,
                DestinationLocationCode: "BIN-A01-03")));
        missingHuStatus.Should().Be(StatusCodes.Status400BadRequest);
        missingHuCode.Should().Be("HANDLING_UNIT_REQUIRED");

        var (missingDestStatus, missingDestCode) = await InvokeThroughMiddlewareAsync(() =>
            controller.MoveHandlingUnit(new InventoryMovementsController.FlexibleMoveHandlingUnitRequest(
                HandlingUnitCode: "HU-QA-001",
                WarehouseId: 1)));
        missingDestStatus.Should().Be(StatusCodes.Status400BadRequest);
        missingDestCode.Should().Be("DESTINATION_LOCATION_REQUIRED");

        // 4. Defense-in-depth: KeyNotFoundException maps to 404 RESOURCE_NOT_FOUND in GlobalExceptionHandlingMiddleware
        var (keyNotFoundStatus, keyNotFoundCode) = await InvokeThroughMiddlewareAsync(() =>
            throw new KeyNotFoundException("Không tìm thấy bản ghi kiểm thử."));
        keyNotFoundStatus.Should().Be(StatusCodes.Status404NotFound);
        keyNotFoundCode.Should().Be("RESOURCE_NOT_FOUND");
    }
}
