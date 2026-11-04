using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Api.Middleware;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public sealed partial class MultiRoleAuthorizationMatrixTests
{
    private static DbContextOptions<ErpKhoDbContext> CreateInMemoryOptions(string dbName) =>
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static IFormFile CreateBinaryFormFile(byte[] content, string fileName, string contentType = "application/pdf") =>
        new FormFile(new MemoryStream(content), 0, content.Length, "attachmentFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    [Fact]
    public void AllControllers_EnforcePermissionAttributes_ZeroRoleHardcoding_And_ZeroReadOnMutations()
    {
        var controllerTypes = typeof(PlatformControlCenterController).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();

        controllerTypes.Should().HaveCountGreaterThanOrEqualTo(40);

        foreach (var ctrl in controllerTypes)
        {
            var ctrlAuthAttrs = ctrl.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
            ctrlAuthAttrs.Should().OnlyContain(
                a => string.IsNullOrWhiteSpace(a.Roles),
                $"{ctrl.Name} must not hardcode [Authorize(Roles = ...)] (SEC-W17-001)");

            var hasControllerLevelGuard = ctrlAuthAttrs.Count > 0
                || ctrl.GetCustomAttributes<PlatformAuthorizeAttribute>(inherit: true).Any();

            var actions = ctrl.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !m.GetCustomAttributes<NonActionAttribute>().Any())
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .ToList();

            foreach (var action in actions)
            {
                var actionAuthAttrs = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
                actionAuthAttrs.Should().OnlyContain(
                    a => string.IsNullOrWhiteSpace(a.Roles),
                    $"{ctrl.Name}.{action.Name} must not hardcode [Authorize(Roles = ...)]");

                var allowAnon = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
                    || ctrl.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();
                if (!allowAnon)
                {
                    var hasActionGuard = actionAuthAttrs.Count > 0
                        || action.GetCustomAttributes<PlatformAuthorizeAttribute>(inherit: true).Any();
                    (hasControllerLevelGuard || hasActionGuard).Should().BeTrue(
                        $"{ctrl.Name}.{action.Name} must enforce authentication/authorization");
                }

                var isMutation = action.GetCustomAttributes<HttpMethodAttribute>()
                    .SelectMany(h => h.HttpMethods)
                    .Any(v => v is "POST" or "PUT" or "PATCH" or "DELETE");
                if (isMutation)
                {
                    var permAttrs = action.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: true).ToList();
                    if (permAttrs.Count > 0)
                    {
                        permAttrs.Any(a => a.Permissions.All(p => !p.EndsWith(".read", StringComparison.OrdinalIgnoreCase)))
                            .Should().BeTrue(
                                $"{ctrl.Name}.{action.Name} is a state-changing mutation and must enforce at least one non-read permission requirement");
                    }
                }
            }
        }
    }

    [Fact]
    public async Task TenRoleMatrix_PermissionBundles_And_CrossDomainIsolation_Enforced()
    {
        var platformRoles = new[] { "SuperAdmin", "BillingAdmin", "SupportAdmin", "SecurityAuditor" };
        var customerRoles = new[] { "Admin", "Manager", "Supervisor", "Accountant", "WarehouseStaff", "Viewer" };

        foreach (var role in platformRoles.Concat(customerRoles))
        {
            var bundle = ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole(role);
            bundle.Should().NotBeEmpty($"Role '{role}' must have a canonical permission bundle");
        }

        // Cost permission boundary (SEC-W17-014)
        foreach (var allowedCostRole in new[] { "Admin", "Manager", "Accountant", "SuperAdmin" })
        {
            ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole(allowedCostRole)
                .Should().Contain(ERP.Domain.Security.AppPermissions.InventoryCostRead);
        }

        foreach (var maskedCostRole in new[] { "Supervisor", "WarehouseStaff", "Viewer" })
        {
            ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole(maskedCostRole)
                .Should().NotContain(ERP.Domain.Security.AppPermissions.InventoryCostRead);
        }

        var dbName = $"{nameof(TenRoleMatrix_PermissionBundles_And_CrossDomainIsolation_Enforced)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));

        // 1. All 6 CUSTOMER_TENANT roles blocked from /api/v1/platform/* by PlatformAuthorizationFilter
        foreach (var custRole in customerRoles)
        {
            var httpCtx = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "50"),
                    new Claim(ClaimTypes.Name, $"cust_{custRole.ToLowerInvariant()}"),
                    new Claim(ClaimTypes.Role, custRole),
                    new Claim(TenantClaimTypes.TenantId, "1"),
                    new Claim(TenantClaimTypes.SecurityDomain, "CUSTOMER_TENANT"),
                    new Claim("permission", AppPermissions.PlatformDashboardRead)
                ], "TestAuth"))
            };
            var filterCtx = new AuthorizationFilterContext(
                new ActionContext(httpCtx, new RouteData(), new ActionDescriptor()),
                []);
            var filter = new PlatformAuthorizationFilter(db, AppPermissions.PlatformDashboardRead);
            await filter.OnAuthorizationAsync(filterCtx);

            var forbid = filterCtx.Result.Should().BeOfType<ObjectResult>().Subject;
            forbid.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            using var doc = JsonSerializer.SerializeToDocument(forbid.Value);
            doc.RootElement.GetProperty("errorCode").GetString().Should().Be("PLATFORM_PERMISSION_REQUIRED");
        }

        // 2. Pure PLATFORM sub-roles without active SupportImpersonationSession blocked from customer warehouse operational endpoints
        foreach (var purePlatRole in new[] { "BillingAdmin", "SupportAdmin", "SecurityAuditor" })
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "80"),
                new Claim(ClaimTypes.Name, $"plat_{purePlatRole.ToLowerInvariant()}"),
                new Claim(ClaimTypes.Role, purePlatRole),
                new Claim(TenantClaimTypes.TenantId, "1"),
                new Claim(TenantClaimTypes.SecurityDomain, "PLATFORM"),
                new Claim("permission", AppPermissions.ReceiptCreate)
            ], "TestAuth"));
            var nextCalled = false;
            var middleware = new SupportImpersonationMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });
            var httpCtx = new DefaultHttpContext
            {
                User = principal
            };
            httpCtx.Request.Path = "/api/v1/import-receipts";
            httpCtx.Request.Method = HttpMethods.Post;
            httpCtx.Response.Body = new System.IO.MemoryStream();

            await middleware.InvokeAsync(httpCtx);

            nextCalled.Should().BeFalse(
                $"Pure PLATFORM operator '{purePlatRole}' without active impersonation must not execute customer warehouse mutations");
            httpCtx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
            httpCtx.Response.Body.Position = 0;
            using var bodyDoc = await JsonDocument.ParseAsync(httpCtx.Response.Body);
            bodyDoc.RootElement.GetProperty("errorCode").GetString().Should().Be("PLATFORM_DIRECT_WAREHOUSE_ACCESS_FORBIDDEN");
        }
    }

    [Fact]
    public async Task BillingReconcile_EnforcesSoloOwnerOneStep_Vs_BillingAdminFourEyes_Duplicate409_Missing404_And_FakePdf400()
    {
        var dbName = $"{nameof(BillingReconcile_EnforcesSoloOwnerOneStep_Vs_BillingAdminFourEyes_Duplicate409_Missing404_And_FakePdf400)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        db.Users.AddRange(
            new User { Id = 1, TenantId = 1, Username = "qa_admin", FullName = "Chủ dự án", PasswordHash = "h", RoleId = 1, IsActive = true, SecurityDomain = "PLATFORM" },
            new User { Id = 2, TenantId = 1, Username = "qa_billing_admin", FullName = "Quản trị Cước phí", PasswordHash = "h", RoleId = 2, IsActive = true, SecurityDomain = "PLATFORM" });
        db.SaasBillingInvoices.AddRange(
            new SaasBillingInvoice { Id = 201, TenantId = 1, InvoiceCode = "INV-SAAS-202611-201", PlanCode = "GROWTH", AmountVnd = 1490000m, Status = "UNPAID", PaymentProvider = "VIETQR", CreatedAtUtc = DateTime.UtcNow.AddDays(-2) },
            new SaasBillingInvoice { Id = 202, TenantId = 1, InvoiceCode = "INV-SAAS-202611-202", PlanCode = "GROWTH", AmountVnd = 1490000m, Status = "UNPAID", PaymentProvider = "VIETQR", CreatedAtUtc = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();

        static PlatformControlCenterController CreateCtrl(ErpKhoDbContext ctx, int uid, string username, string role, params string[] perms)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, uid.ToString()),
                new(ClaimTypes.Name, username),
                new(ClaimTypes.Role, role),
                new(TenantClaimTypes.TenantId, "1"),
                new(TenantClaimTypes.SecurityDomain, "PLATFORM")
            };
            claims.AddRange(perms.Select(p => new Claim("permission", p)));
            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(x => x.EnvironmentName).Returns("Testing");
            return new PlatformControlCenterController(ctx, Mock.Of<IAuthService>(), env.Object, new SessionSecurityOptions())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")) }
                }
            };
        }

        var validPdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\n%%EOF");
        var fakePdfBytes = Encoding.UTF8.GetBytes("NOT_A_PDF_HEADER_AT_ALL");

        var superCtrl = CreateCtrl(db, 1, "qa_admin", "SuperAdmin", AppPermissions.PlatformBillingReconcile, AppPermissions.PlatformBillingManage);
        var billingCtrl = CreateCtrl(db, 2, "qa_billing_admin", "BillingAdmin", AppPermissions.PlatformBillingReconcile, AppPermissions.PlatformBillingManage);

        // 1. Missing invoice -> 404 INVOICE_NOT_FOUND
        var notFound = (await superCtrl.ReconcileBillingInvoice(
            999999,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(BankTransactionRef: "FT263070001", BankCreditTimestampVi: "02/11/2026 15:00", ReceivedAmountVnd: 1490000m, ReasonNote: "Đối soát hóa đơn không tồn tại"),
            evidenceFile: CreateBinaryFormFile(validPdfBytes, "unc.pdf"))).Should().BeOfType<NotFoundObjectResult>().Subject;
        notFound.StatusCode.Should().Be(StatusCodes.Status404NotFound);

        // 2. Fake PDF magic bytes -> 400 INVALID_FILE_SIGNATURE
        var fakePdfRes = (await superCtrl.ReconcileBillingInvoice(
            201,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(BankTransactionRef: "FT263070001", BankCreditTimestampVi: "02/11/2026 15:00", ReceivedAmountVnd: 1490000m, ReasonNote: "Đối soát thử tệp PDF giả mạo chữ ký"),
            evidenceFile: CreateBinaryFormFile(fakePdfBytes, "fake.pdf"))).Should().BeOfType<ObjectResult>().Subject;
        fakePdfRes.StatusCode.Should().Be(StatusCodes.Status400BadRequest);

        // 3. SuperAdmin 1-step Solo-Owner reconcile (no approverUsername required) -> 200 OK
        var soloOk = (await superCtrl.ReconcileBillingInvoice(
            201,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(BankTransactionRef: "FT263070001", BankCreditTimestampVi: "02/11/2026 15:00", ReceivedAmountVnd: 1490000m, ReasonNote: "Chủ dự án xác nhận sao kê ngân hàng 1 bước"),
            evidenceFile: CreateBinaryFormFile(validPdfBytes, "unc_201.pdf"))).Should().BeOfType<OkObjectResult>().Subject;
        using (var soloDoc = JsonSerializer.SerializeToDocument(soloOk.Value))
        {
            soloDoc.RootElement.GetProperty("status").GetString().Should().Be("PAID");
            soloDoc.RootElement.GetProperty("ownerAuditStampVi").GetString().Should().StartWith("Xác nhận thủ công bởi Chủ dự án lúc ");
        }

        // 4. Duplicate BankTransactionRef across invoices -> 409 Conflict
        var dup409 = (await superCtrl.ReconcileBillingInvoice(
            202,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(BankTransactionRef: "FT263070001", BankCreditTimestampVi: "02/11/2026 15:05", ReceivedAmountVnd: 1490000m, ReasonNote: "Kiểm tra chặn mã giao dịch ngân hàng bị trùng"),
            evidenceFile: CreateBinaryFormFile(validPdfBytes, "unc_dup.pdf"))).Should().BeOfType<ConflictObjectResult>().Subject;
        dup409.StatusCode.Should().Be(StatusCodes.Status409Conflict);

        // 5. BillingAdmin without approver -> 422 BILLING_SOD_SELF_APPROVAL_FORBIDDEN; with independent PLATFORM approver -> 200 OK
        var sod422 = (await billingCtrl.ReconcileBillingInvoice(
            202,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(BankTransactionRef: "FT263070002", BankCreditTimestampVi: "02/11/2026 15:10", ReceivedAmountVnd: 1490000m, ReasonNote: "BillingAdmin thiếu người phê duyệt độc lập"),
            evidenceFile: CreateBinaryFormFile(validPdfBytes, "unc_202.pdf"))).Should().BeOfType<UnprocessableEntityObjectResult>().Subject;
        sod422.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity);

        var billing4EyesOk = (await billingCtrl.ReconcileBillingInvoice(
            202,
            new PlatformControlCenterController.ReconcilePlatformInvoiceRequest(BankTransactionRef: "FT263070002", BankCreditTimestampVi: "02/11/2026 15:10", ReceivedAmountVnd: 1490000m, ApproverUsername: "qa_admin", ReasonNote: "BillingAdmin lập và SuperAdmin duyệt độc lập 4 mắt"),
            evidenceFile: CreateBinaryFormFile(validPdfBytes, "unc_202.pdf"))).Should().BeOfType<OkObjectResult>().Subject;
        using var bDoc = JsonSerializer.SerializeToDocument(billing4EyesOk.Value);
        bDoc.RootElement.GetProperty("status").GetString().Should().Be("PAID");
    }

    [Fact]
    public async Task TotpTwoFactor_EnforcesLoginStepUp_ProtectsSecret_BlocksReplay_And_RedactsAudit()
    {
        var dbName = $"{nameof(TotpTwoFactor_EnforcesLoginStepUp_ProtectsSecret_BlocksReplay_And_RedactsAudit)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        var user = new User { Id = 10, TenantId = 1, Username = "totp_tester", FullName = "Kiểm định 2FA", PasswordHash = "hash", RoleId = 1, IsActive = true, SecurityDomain = "CUSTOMER_TENANT" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var dpProvider = DataProtectionProvider.Create("ERP_KHO.TotpTests");
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUserMock.SetupGet(x => x.UserId).Returns(10);
        currentUserMock.SetupGet(x => x.TenantId).Returns(1);

        var govSvc = new WmsGovernanceOperationsService(db, warehouseAccess: null, currentUser: currentUserMock.Object, dataProtectionProvider: dpProvider);
        var setupOutcome = await govSvc.SetupTotpAsync(userId: 10, actorUserId: 10);
        setupOutcome.StatusCode.Should().Be(200);

        var revealOutcome = await govSvc.RevealTotpSecretAsync(userId: 10);
        revealOutcome.StatusCode.Should().Be(200);

        string manualBase32Key;
        using (var revealDoc = JsonSerializer.SerializeToDocument(revealOutcome.Payload))
        {
            manualBase32Key = revealDoc.RootElement.GetProperty("base32Secret").GetString()!;
            manualBase32Key.Should().NotBeNullOrWhiteSpace();
        }

        // Secret stored in Users & AuditLogs must be DataProtection-encrypted / redacted, NEVER plaintext Base32 (SEC-W17-008)
        var reloadedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == 10);
        reloadedUser.TotpSecretProtected.Should().NotBeNullOrWhiteSpace();
        reloadedUser.TotpSecretProtected.Should().NotContain(manualBase32Key);
        var enrollLog = await db.AuditLogs.SingleAsync(a => a.Action == "AccountSecurity.TotpSetup");
        enrollLog.NewValues.Should().NotContain(manualBase32Key);

        var secretBytes = WmsGovernanceOperationsService.DecodeTotpBase32(manualBase32Key);
        var enrollStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var enrollCode = WmsGovernanceOperationsService.ComputeRfc6238TotpCode(secretBytes, enrollStep - 1);

        var verifyOutcome = await govSvc.VerifyTotpAsync(userId: 10, candidateCode: enrollCode, actorUserId: 10);
        verifyOutcome.StatusCode.Should().Be(200);

        var activateOutcome = await govSvc.ConfirmTotpActivationAsync(userId: 10, actorUserId: 10);
        activateOutcome.StatusCode.Should().Be(200);

        var verifyLogsJson = string.Join(" ", await db.AuditLogs.Select(a => a.NewValues ?? string.Empty).ToListAsync());
        verifyLogsJson.Should().NotContain(enrollCode, "Raw OTP codes must never be persisted into AuditLogs (SEC-W17-009)");

        var stepUp = new TotpLoginStepUpService(db, dpProvider);

        // 1. Login without OTP -> 401 TWO_FACTOR_REQUIRED
        var missingOtp = await stepUp.ValidateLoginSecondFactorAsync("totp_tester", null, null, "CORR-L1");
        missingOtp.Allowed.Should().BeFalse();
        missingOtp.StatusCode.Should().Be(401);
        missingOtp.ErrorCode.Should().Be("TWO_FACTOR_REQUIRED");

        // 2. Login with wrong OTP -> 401 INVALID_TWO_FACTOR_CODE
        var wrongOtp = await stepUp.ValidateLoginSecondFactorAsync("totp_tester", "000000", null, "CORR-L2");
        wrongOtp.Allowed.Should().BeFalse();
        wrongOtp.StatusCode.Should().Be(401);

        // 3. Login with valid current-step OTP -> 200 Allowed
        var validLoginCode = WmsGovernanceOperationsService.ComputeRfc6238TotpCode(secretBytes, enrollStep);
        var validOtp = await stepUp.ValidateLoginSecondFactorAsync("totp_tester", validLoginCode, null, "CORR-L3");
        validOtp.Allowed.Should().BeTrue();
        validOtp.StatusCode.Should().Be(200);

        // 4. Replay of the exact same time-step OTP -> 401 rejected
        var replayOtp = await stepUp.ValidateLoginSecondFactorAsync("totp_tester", validLoginCode, null, "CORR-L4");
        replayOtp.Allowed.Should().BeFalse();
        replayOtp.StatusCode.Should().Be(401);

        // 5. Brute-force >= 5 failed attempts within 10 minutes -> 429 TWO_FACTOR_LOCKED_OUT
        for (var i = 0; i < 4; i++)
        {
            await stepUp.ValidateLoginSecondFactorAsync("totp_tester", "999999", null, $"CORR-BF-{i}");
        }
        var lockedOut = await stepUp.ValidateLoginSecondFactorAsync("totp_tester", validLoginCode, null, "CORR-BF-LOCK");
        lockedOut.Allowed.Should().BeFalse();
        lockedOut.StatusCode.Should().Be(429);
        lockedOut.ErrorCode.Should().Be("TWO_FACTOR_LOCKED_OUT");
    }

    [Fact]
    public async Task CostMasking_And_WebhookHmacSignature_Enforced()
    {
        var dbName = $"{nameof(CostMasking_And_WebhookHmacSignature_Enforced)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        var wh = new Warehouse { Id = 1, TenantId = 1, Code = "WH01", Name = "Kho Trung Tâm", IsActive = true };
        var prod = new Product { Id = 1, TenantId = 1, Code = "SKU-COST-1", Name = "Sản phẩm Giá vốn", MovingAverageCost = 450000m, IsActive = true };
        var stock = new InventoryStock { Id = 1, TenantId = 1, WarehouseId = 1, ProductId = 1, Status = InventoryStatus.Available, Quantity = 10m, MovingAverageCost = 450000m };
        stock.RefreshGrainHash();
        db.Warehouses.Add(wh);
        db.Products.Add(prod);
        db.InventoryStocks.Add(stock);
        await db.SaveChangesAsync();

        var reportOps = new ReportOperationsService(db);
        var maskedOutcome = await reportOps.GetOwnerGrossProfitAsync(1, canReadCost: false);
        maskedOutcome.StatusCode.Should().Be(StatusCodes.Status200OK);
        using (var maskedDoc = JsonSerializer.SerializeToDocument(maskedOutcome.Payload))
        {
            maskedDoc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeTrue();
            maskedDoc.RootElement.GetProperty("movingAverageCogs").ValueKind.Should().Be(JsonValueKind.Null);
            maskedDoc.RootElement.GetProperty("grossProfit").ValueKind.Should().Be(JsonValueKind.Null);
        }

        var allowedOutcome = await reportOps.GetOwnerGrossProfitAsync(1, canReadCost: true);
        allowedOutcome.StatusCode.Should().Be(StatusCodes.Status200OK);
        using (var doc = JsonSerializer.SerializeToDocument(allowedOutcome.Payload))
        {
            doc.RootElement.GetProperty("costHiddenByRbac").GetBoolean().Should().BeFalse();
            doc.RootElement.GetProperty("movingAverageCogs").ValueKind.Should().Be(JsonValueKind.Number);
        }

        // Webhook HMAC verification rejects missing, forged, and static bypass tokens (SEC-W17-012)
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SaasBilling:VietQrWebhookSecret"] = "unit-test-vietqr-hmac-secret-key-2026"
            })
            .Build();

        SaasBillingService.VerifyVietQrWebhookSignature(config, null, "INV-SAAS-202611-002", "GROWTH", 1490000m).Should().BeFalse();
        SaasBillingService.VerifyVietQrWebhookSignature(config, "VALID_VIETQR_HMAC", "INV-SAAS-202611-002", "GROWTH", 1490000m).Should().BeFalse();
        SaasBillingService.VerifyVietQrWebhookSignature(config, "deadbeef", "INV-SAAS-202611-002", "GROWTH", 1490000m).Should().BeFalse();

        var genuineSig = SaasBillingService.ComputeVietQrSignature("unit-test-vietqr-hmac-secret-key-2026", "INV-SAAS-202611-002", "GROWTH", 1490000m);
        SaasBillingService.VerifyVietQrWebhookSignature(config, genuineSig, "INV-SAAS-202611-002", "GROWTH", 1490000m).Should().BeTrue();
    }
}
