using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public sealed class Wave20PlatformAndPolishTests
{
    private static DbContextOptions<ErpKhoDbContext> CreateInMemoryOptions(string dbName) =>
        new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

    private static PlatformControlCenterController CreatePlatformController(ErpKhoDbContext db)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "1"),
            new(ClaimTypes.Name, "qa_admin"),
            new(ClaimTypes.Role, AppRoles.SuperAdmin),
            new(TenantClaimTypes.TenantId, "1"),
            new(TenantClaimTypes.SecurityDomain, "PLATFORM"),
            new("permission", AppPermissions.PlatformDashboardRead),
            new("permission", AppPermissions.PlatformTenantRead),
            new("permission", AppPermissions.PlatformBillingManage),
            new("permission", AppPermissions.PlatformBillingReconcile),
            new("permission", AppPermissions.PlatformAuditRead),
            new("permission", AppPermissions.PlatformIntegrationManage)
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            TraceIdentifier = $"CORR-W20-{Guid.NewGuid():N}"[..24]
        };

        var envMock = new Mock<IWebHostEnvironment>();
        envMock.SetupGet(x => x.EnvironmentName).Returns("Testing");

        return new PlatformControlCenterController(
            db,
            Mock.Of<IAuthService>(),
            envMock.Object,
            new SessionSecurityOptions())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    [Fact]
    public async Task TC_W20_01_CanonicalTenant1Identity_LocalizedDashboardAndWorkers_CurrencyUnitAndRolePolish()
    {
        var dbName = $"{nameof(TC_W20_01_CanonicalTenant1Identity_LocalizedDashboardAndWorkers_CurrencyUnitAndRolePolish)}_{Guid.NewGuid():N}";
        await using var db = new ErpKhoDbContext(CreateInMemoryOptions(dbName));
        db.UseTenant(1);

        var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Seed legacy/drifted Tenant-1 values & noisy UserSession audit rows to prove Wave-20 self-healing + filtering
        db.SaasTenants.Add(new SaasTenant
        {
            Id = 1,
            TenantId = 1,
            TenantCode = "TENANT-001",
            CompanyName = "Hệ thống Quản trị Kho ERP_KHO",
            TaxCode = "0109887766",
            LifecycleStatus = "ACTIVE",
            CreatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        db.Warehouses.Add(new Warehouse
        {
            Id = 1,
            TenantId = 1,
            Code = "WH01",
            Name = "Kho Trung tâm Quận 7",
            Address = "TP.HCM",
            IsActive = true
        });

        var roleSuperAdmin = new Role { Id = 1, RoleName = "SuperAdmin", SecurityDomain = "PLATFORM", RowVersion = [1, 0, 0, 1] };
        var roleAdmin = new Role { Id = 2, RoleName = "Admin", SecurityDomain = "CUSTOMER_TENANT", RowVersion = [1, 0, 0, 2] };
        var roleViewer = new Role { Id = 3, RoleName = "Viewer", SecurityDomain = "CUSTOMER_TENANT", RowVersion = [1, 0, 0, 3] };
        db.Roles.AddRange(roleSuperAdmin, roleAdmin, roleViewer);

        db.Users.AddRange(
            new User
            {
                Id = 1,
                TenantId = 1,
                Username = "qa_admin",
                FullName = "Quản trị viên Hệ thống",
                PasswordHash = "HASH",
                RoleId = 1,
                SecurityDomain = "PLATFORM",
                IsActive = true
            },
            new User
            {
                Id = 2,
                TenantId = 1,
                Username = "tenant_admin",
                FullName = "Quản trị viên Công ty Đại Phát",
                PasswordHash = "HASH",
                RoleId = 2,
                SecurityDomain = "CUSTOMER_TENANT",
                IsActive = true
            },
            new User
            {
                Id = 3,
                TenantId = 1,
                Username = "qa_viewer_wh01",
                FullName = "Kiểm toán Kho Trung tâm (WH01)",
                PasswordHash = "HASH",
                RoleId = 3,
                SecurityDomain = "CUSTOMER_TENANT",
                IsActive = true
            });

        db.SaasBillingInvoices.AddRange(
            new SaasBillingInvoice
            {
                Id = 1,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202610-001",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                BuyerTaxCode = "0109887766",
                BuyerLegalName = "CÔNG TY TNHH VẬN HÀNH KHO ERP_KHO",
                PaymentProvider = "VIETQR",
                Status = "PAID",
                CreatedAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                PaidAtUtc = new DateTime(2026, 10, 3, 9, 15, 0, DateTimeKind.Utc)
            },
            new SaasBillingInvoice
            {
                Id = 2,
                TenantId = 1,
                InvoiceCode = "INV-SAAS-202611-002",
                PlanCode = "GROWTH",
                SubTotalVnd = 1354545m,
                VatRatePercent = 10m,
                VatAmountVnd = 135455m,
                AmountVnd = 1490000m,
                BuyerTaxCode = "",
                BuyerLegalName = "Hệ thống Kho Vận Doanh Nghiệp",
                PaymentProvider = "VIETQR",
                Status = "UNPAID",
                CreatedAtUtc = new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc)
            });

        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 9001,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Authentication.LoginSucceeded",
                EntityName = "UserSession",
                EntityId = 77,
                NewValues = "SessionId=11111111-2222-3333-4444-555555555555",
                Timestamp = new DateTime(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc)
            },
            new AuditLog
            {
                Id = 9002,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_BILLING_MANUAL_RECONCILE",
                EntityName = "SaasBillingInvoice",
                EntityId = 1,
                NewValues = "{\"invoiceCode\":\"INV-SAAS-202610-001\",\"actorUsername\":\"qa_admin\",\"status\":\"PAID\"}",
                Reason = "Đối soát sao kê VietQR kỳ tháng 10/2026 khớp 1.490.000 đ • Công ty ERP_KHO",
                Result = "Success",
                Timestamp = new DateTime(2026, 11, 2, 8, 30, 0, DateTimeKind.Utc)
            },
            new AuditLog
            {
                Id = 9003,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "PLATFORM_TENANT_STATUS_CHANGED",
                EntityName = "SaasTenant",
                EntityId = 1,
                NewValues = "{\"tenantId\":1,\"tenantCode\":\"TENANT-001\",\"status\":\"ACTIVE\",\"actorUsername\":\"qa_admin\"}",
                Reason = "Khôi phục trạng thái chuẩn ACTIVE sau kiểm định đối kháng WAVE-17",
                Result = "Success",
                Timestamp = new DateTime(2026, 11, 3, 10, 11, 0, DateTimeKind.Utc)
            },
            new AuditLog
            {
                Id = 9004,
                TenantId = 1,
                UserId = 1,
                SecurityDomain = "PLATFORM",
                Action = "Đồng bộ bản tin tích hợp hệ thống",
                EntityName = "IntegrationOutboxBatch",
                EntityId = 12,
                NewValues = "Chuyển phát thành công lô bản tin tích hợp hệ thống (IR-QA-001, ER-QA-001)",
                Reason = "Chuyển phát thành công lô bản tin tích hợp hệ thống (IR-QA-001, ER-QA-001)",
                Result = "Success",
                Timestamp = new DateTime(2026, 11, 3, 11, 0, 0, DateTimeKind.Utc)
            });

        var unitThung = new Unit { Id = 10, TenantId = 1, Code = "THUNG", Name = "Thùng", DecimalPlaces = 0, IsActive = true };
        db.Units.Add(unitThung);
        db.Products.Add(new Product
        {
            Id = 501,
            TenantId = 1,
            Code = "SKU-W20-501",
            Name = "Thùng carton đóng gói 5 lớp",
            UnitId = 10,
            MovingAverageCost = 120000m,
            IsActive = true
        });
        db.ReceivingTolerancePolicies.Add(new ReceivingTolerancePolicy
        {
            Id = 201,
            TenantId = 1,
            ProductId = 501,
            PercentageTolerance = 0.05m,
            AbsoluteQuantityTolerance = 5m,
            OverageAllowed = true,
            RequiresApprovalOutsideTolerance = true,
            IsActive = true,
            EffectiveFromUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
        });
        db.CustomerReturns.Add(new CustomerReturn
        {
            Id = 301,
            TenantId = 1,
            WarehouseId = 1,
            ReturnCode = "RMA-202611-001",
            Status = "RECEIVED_IN_QUARANTINE",
            CreatedAtUtc = new DateTime(2026, 11, 3, 2, 0, 0, DateTimeKind.Utc),
            Lines =
            [
                new CustomerReturnLine
                {
                    Id = 3001,
                    TenantId = 1,
                    CustomerReturnId = 301,
                    ProductId = 501,
                    Quantity = 3m,
                    BaseUomId = 10,
                    BaseUomCode = "CTN",
                    TransactionUomCode = "CTN",
                    ConversionRateSnapshot = 1m,
                    UnitCost = 120000m,
                    Condition = "GOOD"
                }
            ]
        });

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var platformCtrl = CreatePlatformController(db);

        // 1. PL-03: Canonical TENANT-001 Legal Name & Tax Code across Platform & Customer SaaS Billing
        var tenantsOk = (await platformCtrl.GetTenants()).Should().BeOfType<OkObjectResult>().Subject;
        using (var tenantsDoc = JsonSerializer.SerializeToDocument(tenantsOk.Value, jsonOpts))
        {
            var t1 = tenantsDoc.RootElement.GetProperty("items")[0];
            t1.GetProperty("companyName").GetString().Should().Be("Công ty TNHH Thương mại & Kho vận Đại Phát");
            t1.GetProperty("taxCode").GetString().Should().Be("0318294012");
        }

        var platInvoicesOk = (await platformCtrl.GetBillingInvoices()).Should().BeOfType<OkObjectResult>().Subject;
        using (var invDoc = JsonSerializer.SerializeToDocument(platInvoicesOk.Value, jsonOpts))
        {
            foreach (var inv in invDoc.RootElement.GetProperty("items").EnumerateArray())
            {
                inv.GetProperty("companyName").GetString().Should().Be("Công ty TNHH Thương mại & Kho vận Đại Phát");
                inv.GetProperty("buyerLegalName").GetString().Should().Be("Công ty TNHH Thương mại & Kho vận Đại Phát");
                inv.GetProperty("buyerTaxCode").GetString().Should().Be("0318294012");
                inv.GetProperty("transferContent").GetString().Should().StartWith("ERPKHO");
            }
        }

        var saasBilling = new SaasBillingService(db);
        var subJson = JsonSerializer.Serialize(await saasBilling.GetSubscriptionAsync(), jsonOpts);
        subJson.Should().NotContain(" đ\"");
        using (var subDoc = JsonDocument.Parse(subJson))
        {
            subDoc.RootElement.GetProperty("companyName").GetString().Should().Be("Công ty TNHH Thương mại & Kho vận Đại Phát");
            subDoc.RootElement.GetProperty("taxCode").GetString().Should().Be("0318294012");
            foreach (var rInv in subDoc.RootElement.GetProperty("recentInvoices").EnumerateArray())
            {
                rInv.GetProperty("buyerLegalName").GetString().Should().Be("Công ty TNHH Thương mại & Kho vận Đại Phát");
                rInv.GetProperty("buyerTaxCode").GetString().Should().Be("0318294012");
            }
        }

        // 2. PL-01 & Round 2 Polish: Platform Dashboard consistent legal name + clean Vietnamese recentEvents (zero WAVE-, ACTIVE, ĐỒNG-BỘ-TÍCH-HỢP)
        var dashOk = (await platformCtrl.GetDashboard()).Should().BeOfType<OkObjectResult>().Subject;
        var dashJson = JsonSerializer.Serialize(dashOk.Value, jsonOpts);
        dashJson.Should().NotContain("Authentication.LoginSucceeded");
        dashJson.Should().NotContain("UserSession#");
        dashJson.Should().NotContain("(qa_admin)");
        dashJson.Should().NotContain("ERP_KHO");
        dashJson.Should().Contain("Công ty TNHH Thương mại & Kho vận Đại Phát");
        using (var dashDoc = JsonDocument.Parse(dashJson))
        {
            var root = dashDoc.RootElement;
            root.GetProperty("primaryTenantCode").GetString().Should().Be("TENANT-001");
            root.GetProperty("primaryTenantLegalName").GetString().Should().Be("Công ty TNHH Thương mại & Kho vận Đại Phát");
            root.GetProperty("highestResourceSummaryVi").GetString().Should().Contain("TENANT-001 — Công ty TNHH Thương mại & Kho vận Đại Phát");

            var eventsElement = root.GetProperty("recentEvents");
            var recentEventsJson = eventsElement.GetRawText();
            recentEventsJson.Should().NotContain("WAVE-");
            recentEventsJson.Should().NotContain("ACTIVE");
            recentEventsJson.Should().NotContain("ĐỒNG-BỘ-TÍCH-HỢP");

            var events = eventsElement.EnumerateArray().ToList();
            events.Count.Should().BeGreaterThanOrEqualTo(6);
            events.Select(e => e.GetProperty("actorDisplay").GetString())
                .Should().Contain("Chủ dự án (SuperAdmin)");
            events.Select(e => e.GetProperty("targetCode").GetString())
                .Should().Contain("Đồng bộ hệ thống");
            events.Select(e => e.GetProperty("summaryVi").GetString())
                .Should().Contain("Khôi phục trạng thái Đang hoạt động cho Công ty TNHH Thương mại & Kho vận Đại Phát (TENANT-001)");
        }

        var auditOk = (await platformCtrl.GetPlatformAuditLogs()).Should().BeOfType<OkObjectResult>().Subject;
        var auditJson = JsonSerializer.Serialize(auditOk.Value, jsonOpts);
        auditJson.Should().NotContain("Authentication.LoginSucceeded");
        auditJson.Should().NotContain("UserSession");

        // Custom tax profile update must be preserved (never overwritten back to Đại Phát)
        await saasBilling.UpdateTaxProfileAsync(
            new SaasTaxProfileUpdateRequest(
                TaxCode: "0312345678",
                CompanyName: "CÔNG TY TNHH KHO VẬN Á CHÂU",
                LegalRegisteredAddress: "Quận 1, TP.HCM",
                TaxpayerType: "ENTERPRISE"),
            actorUserId: 2);
        using (var customSubDoc = JsonSerializer.SerializeToDocument(await saasBilling.GetSubscriptionAsync(), jsonOpts))
        {
            customSubDoc.RootElement.GetProperty("companyName").GetString().Should().Be("CÔNG TY TNHH KHO VẬN Á CHÂU");
            customSubDoc.RootElement.GetProperty("taxCode").GetString().Should().Be("0312345678");
        }

        // 3. PL-02: Platform Workers 100% Vietnamese business labels (zero Outbox / GrainHash / sổ cái 9 chiều / DLQ in worker descriptions & scopes)
        var workersOk = (await platformCtrl.GetWorkersHealth()).Should().BeOfType<OkObjectResult>().Subject;
        using (var workersDoc = JsonSerializer.SerializeToDocument(workersOk.Value, jsonOpts))
        {
            foreach (var w in workersDoc.RootElement.GetProperty("workers").EnumerateArray())
            {
                var combined = $"{w.GetProperty("titleVi").GetString()} | {w.GetProperty("descriptionVi").GetString()} | {w.GetProperty("scope").GetString()}";
                combined.Should().NotContainEquivalentOf("Multi-Tenant Outbox");
                combined.Should().NotContainEquivalentOf("GrainHash");
                combined.Should().NotContainEquivalentOf("Webhook Retry Queue");
                combined.Should().NotContainEquivalentOf("sổ cái 9 chiều");
                combined.Should().NotContainEquivalentOf("Outbox");
                combined.Should().NotContainEquivalentOf("DLQ");
            }
        }

        // 4. DS-06: Tolerance policies use "đơn vị" (zero "\d+ ĐVT") & CustomerReturns return real Vietnamese UOM ("Thùng")
        var prodOps = new ProductOperationsService(db);
        var tolOutcome = await prodOps.GetTolerancePoliciesAsync(501, null);
        var tolJson = JsonSerializer.Serialize(tolOutcome.Payload, jsonOpts);
        tolJson.Should().Contain("±5 đơn vị");
        Regex.IsMatch(tolJson, @"\d+\s*ĐVT").Should().BeFalse();

        var importOps = new ImportReceiptOperationsService(db);
        var rmaListOutcome = await importOps.GetCustomerReturnsAsync(1);
        using (var rmaListDoc = JsonSerializer.SerializeToDocument(rmaListOutcome.Payload, jsonOpts))
        {
            var rma = rmaListDoc.RootElement[0];
            rma.GetProperty("unitName").GetString().Should().Be("Thùng");
            rma.GetProperty("unit").GetString().Should().Be("Thùng");
            rma.GetProperty("lines")[0].GetProperty("unitName").GetString().Should().Be("Thùng");
            rma.GetProperty("lines")[0].GetProperty("unit").GetString().Should().Be("Thùng");
        }

        var rmaDetailOutcome = await importOps.GetCustomerReturnByIdAsync(301);
        using (var rmaDetailDoc = JsonSerializer.SerializeToDocument(rmaDetailOutcome.Payload, jsonOpts))
        {
            rmaDetailDoc.RootElement.GetProperty("unitName").GetString().Should().Be("Thùng");
            rmaDetailDoc.RootElement.GetProperty("lines")[0].GetProperty("unitName").GetString().Should().Be("Thùng");
        }

        // 5. DS-10: Canonical human display names & Permissions Roles ("Quản trị công ty", "Kiểm toán nội bộ")
        var currentUserMock = new Mock<ICurrentUser>();
        currentUserMock.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUserMock.SetupGet(u => u.UserId).Returns(2);
        currentUserMock.SetupGet(u => u.TenantId).Returns(1);
        currentUserMock.SetupGet(u => u.Role).Returns("Admin");
        currentUserMock.Setup(u => u.HasPermission(It.IsAny<string>())).Returns(true);

        var sessionSvc = new UserSessionService(db, Mock.Of<ITokenService>(), currentUserMock.Object, new SessionSecurityOptions());
        using (var meAdminDoc = JsonSerializer.SerializeToDocument(await sessionSvc.GetAuthenticatedUserProfileAsync(1), jsonOpts))
        {
            meAdminDoc.RootElement.GetProperty("fullName").GetString().Should().Be("Chủ dự án (SuperAdmin)");
        }
        using (var meTenantDoc = JsonSerializer.SerializeToDocument(await sessionSvc.GetAuthenticatedUserProfileAsync(2), jsonOpts))
        {
            meTenantDoc.RootElement.GetProperty("fullName").GetString().Should().Be("Quản trị viên Đại Phát");
        }
        using (var meViewerDoc = JsonSerializer.SerializeToDocument(await sessionSvc.GetAuthenticatedUserProfileAsync(3), jsonOpts))
        {
            meViewerDoc.RootElement.GetProperty("fullName").GetString().Should().Be("Kiểm toán nội bộ WH01");
        }

        var permOps = new PermissionOperationsService(db, currentUserMock.Object, Mock.Of<IWarehouseAuthorizationService>());
        using (var rolesDoc = JsonSerializer.SerializeToDocument((await permOps.RolesAsync()).Payload, jsonOpts))
        {
            var labels = rolesDoc.RootElement.EnumerateArray().ToDictionary(
                r => r.GetProperty("roleCode").GetString()!,
                r => r.GetProperty("displayNameVi").GetString()!);
            labels["Admin"].Should().Be("Quản trị công ty");
            labels["Viewer"].Should().Be("Kiểm toán nội bộ");
        }
        using (var usersDoc = JsonSerializer.SerializeToDocument((await permOps.UsersAsync()).Payload, jsonOpts))
        {
            var namesByUsername = usersDoc.RootElement.EnumerateArray().ToDictionary(
                u => u.GetProperty("username").GetString()!,
                u => u.GetProperty("fullName").GetString()!);
            namesByUsername["tenant_admin"].Should().Be("Quản trị viên Đại Phát");
            namesByUsername["qa_viewer_wh01"].Should().Be("Kiểm toán nội bộ WH01");
        }
    }
}
