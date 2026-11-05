using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace ERP.Api.Tests;

public sealed class Wave18RealSqlGovernanceAndImportTests
{
    [ApprovalSqlServerFact]
    public async Task RealSql_PermissionsCatalog_CustomerUsersIsolation_And_ImportReceiptListTotals_WorkEndToEnd()
    {
        await using var owned = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await owned.MigrateAndSeedAsync();

        var dbOptions = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(owned.ConnectionString)
            .Options;

        int qaAdminId;
        int tenantAdminId;
        int warehouseId;

        await using (var seedDb = new ErpKhoDbContext(dbOptions))
        {
            seedDb.UseTenant(1);

            var existingCodes = (await seedDb.Permissions.AsNoTracking().Select(p => p.Code).ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var code in ERP.Domain.Security.AppPermissions.All.Where(c => !existingCodes.Contains(c)))
            {
                seedDb.Permissions.Add(new Permission(code, $"Quyền {code}"));
            }
            await seedDb.SaveChangesAsync();
            var legacyDupDesc = string.Concat("Phê duyệt", " phê duyệt phiếu nhập");
            await seedDb.Permissions.Where(p => p.Code == AppPermissions.ApprovalApprove)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Description, legacyDupDesc));

            var rolesByName = await seedDb.Roles.ToDictionaryAsync(r => r.RoleName, StringComparer.OrdinalIgnoreCase);
            foreach (var (roleName, secDomain) in new[]
            {
                ("Admin", "CUSTOMER_TENANT"),
                ("SuperAdmin", "PLATFORM"),
                ("BillingAdmin", "PLATFORM"),
                ("SupportAdmin", "PLATFORM"),
                ("SecurityAuditor", "PLATFORM")
            })
            {
                if (!rolesByName.TryGetValue(roleName, out var role))
                {
                    role = new Role { RoleName = roleName, SecurityDomain = secDomain, Description = roleName };
                    seedDb.Roles.Add(role);
                    rolesByName[roleName] = role;
                }
                else
                {
                    role.SecurityDomain = secDomain;
                }
            }
            await seedDb.SaveChangesAsync();

            var requiredPermIds = await seedDb.Permissions
                .Where(p => p.Code == AppPermissions.UserManage || p.Code == "user_warehouse.read")
                .Select(p => p.Id).ToListAsync();
            var adminRoleId = rolesByName["Admin"].Id;
            foreach (var pid in requiredPermIds)
            {
                if (!await seedDb.RolePermissions.AnyAsync(rp => rp.RoleId == adminRoleId && rp.PermissionId == pid))
                    seedDb.RolePermissions.Add(new RolePermission { RoleId = adminRoleId, PermissionId = pid });
            }
            await seedDb.SaveChangesAsync();

            var seededUsers = new (string Username, string FullName, string Domain, string RoleName, string? Email)[]
            {
                ("qa_admin", "Chủ dự án (SuperAdmin)", "PLATFORM", "SuperAdmin", null),
                ("qa_billing_admin", "Quản trị Cước phí SaaS", "PLATFORM", "BillingAdmin", null),
                ("qa_support_admin", "Chuyên viên Hỗ trợ Kỹ thuật", "PLATFORM", "SupportAdmin", null),
                ("qa_security_auditor", "Kiểm toán An toàn Thông tin", "PLATFORM", "SecurityAuditor", null),
                ("tenant_admin", "Quản trị viên Đại Phát", "CUSTOMER_TENANT", "Admin", "owner@erpkho.vn")
            }.Select(u => new User
            {
                TenantId = 1,
                Username = u.Username,
                FullName = u.FullName,
                Email = u.Email,
                PasswordHash = "HASH",
                SecurityDomain = u.Domain,
                RoleId = rolesByName[u.RoleName].Id,
                IsActive = true
            }).ToList();

            seedDb.Users.AddRange(seededUsers);
            await seedDb.SaveChangesAsync();

            qaAdminId = seededUsers.Single(u => u.Username == "qa_admin").Id;
            tenantAdminId = seededUsers.Single(u => u.Username == "tenant_admin").Id;

            var warehouse = await seedDb.Warehouses.OrderBy(w => w.Id).FirstAsync();
            warehouseId = warehouse.Id;
            var product = await seedDb.Products.Include(p => p.Unit).OrderBy(p => p.Id).FirstAsync();

            var supplier = new BusinessPartner
            {
                TenantId = 1,
                Code = "NCC-W18-SQL",
                Name = "Nhà cung cấp kiểm thử SQL Wave 18",
                IsSupplier = true,
                IsActive = true
            };
            seedDb.BusinessPartners.Add(supplier);
            await seedDb.SaveChangesAsync();

            seedDb.ImportReceipts.Add(new ImportReceipt
            {
                TenantId = 1,
                Code = "PNK-202611-001",
                WarehouseId = warehouse.Id,
                SupplierId = supplier.Id,
                SupplierCodeSnapshot = supplier.Code,
                SupplierNameSnapshot = supplier.Name,
                Status = ReceiptStatus.Received,
                CreatedBy = tenantAdminId,
                CreatedAt = DateTime.UtcNow.AddMinutes(5),
                Note = "Phiếu nhập kho lô thiết bị tháng 11 chờ kiểm thu",
                Details =
                [
                    new ImportReceiptDetail
                    {
                        ProductId = product.Id,
                        Quantity = 20m, ExpectedQuantity = 20m, ReceivedQuantity = 20m,
                        ObservedQuantity = 20m, FinalReceivedQuantity = 20m, AcceptedQuantity = 20m, PostedQuantity = 0m,
                        BaseExpectedQuantity = 20m, BaseReceivedQuantity = 20m, BaseObservedQuantity = 20m,
                        BaseFinalReceivedQuantity = 20m, BaseAcceptedQuantity = 20m, BasePostedQuantity = 0m,
                        OperationUnitId = product.UnitId, OperationUnitCodeSnapshot = product.Unit.Code, OperationUnitDecimalPlaces = product.Unit.DecimalPlaces,
                        BaseUnitId = product.UnitId, BaseUnitCodeSnapshot = product.Unit.Code, BaseUnitDecimalPlaces = product.Unit.DecimalPlaces,
                        ConversionFactor = 1m, ConversionVersion = 1, UnitPrice = 250_000m, TaxPercent = 10m
                    }
                ]
            });
            await seedDb.SaveChangesAsync();
        }

        // Fresh DbContext against real SQL Server (zero ChangeTracker navigation caching)
        await using var verifyDb = new ErpKhoDbContext(dbOptions);
        verifyDb.UseTenant(1);

        var httpCtx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, tenantAdminId.ToString()),
                new Claim(ClaimTypes.Name, "tenant_admin"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(TenantClaimTypes.TenantId, "1"),
                new Claim(TenantClaimTypes.SecurityDomain, "CUSTOMER_TENANT"),
                new Claim("permission", AppPermissions.PermissionRead),
                new Claim("permission", AppPermissions.UserRead),
                new Claim("permission", AppPermissions.UserManage),
                new Claim("permission", AppPermissions.ReceiptRead),
                new Claim("permission", AppPermissions.InventoryCostRead)
            ], "TestAuth"))
        };
        var currentUser = new HttpCurrentUser(new HttpContextAccessor { HttpContext = httpCtx });
        var warehouseAuthMock = new Mock<IWarehouseAuthorizationService>();
        warehouseAuthMock.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([warehouseId]);

        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var permOps = new PermissionOperationsService(verifyDb, currentUser, warehouseAuthMock.Object);
        var permCtrl = new PermissionsController(permOps, warehouseAuthMock.Object, currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = httpCtx }
        };

        // 1. QA-W18-BE-PERMISSIONS-CATALOG-500: GET /api/v1/permissions on real SQL Server returns 200 OK with 141 permissions & 0 platform.*
        var catalogOk = (await permCtrl.Catalog(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;
        using (var catDoc = JsonSerializer.SerializeToDocument(catalogOk.Value, webJson))
        {
            var items = catDoc.RootElement.EnumerateArray().ToList();
            items.Should().HaveCount(141);
            items.Select(x => x.GetProperty("code").GetString()!).Should().NotContain(c => c.StartsWith("platform.", StringComparison.OrdinalIgnoreCase));
            items.Single(x => x.GetProperty("code").GetString() == AppPermissions.ApprovalApprove).GetProperty("description").GetString().Should().Be("Phê duyệt phiếu nhập kho");
            items.Single(x => x.GetProperty("code").GetString() == AppPermissions.ApprovalReject).GetProperty("description").GetString().Should().Be("Từ chối phê duyệt phiếu nhập kho");
        }

        // 2. QA-W18-FND-02-API: GET /api/v1/permissions/users excludes all PLATFORM operators while returning tenant_admin
        var usersOk = (await permCtrl.Users(CancellationToken.None)).Should().BeOfType<OkObjectResult>().Subject;
        using (var usersDoc = JsonSerializer.SerializeToDocument(usersOk.Value, webJson))
        {
            var usernames = usersDoc.RootElement.EnumerateArray().Select(x => x.GetProperty("username").GetString()!).ToList();
            usernames.Should().Contain("tenant_admin");
            usernames.Should().NotContain(["qa_admin", "qa_billing_admin", "qa_support_admin", "qa_security_auditor"]);
        }

        // Cross-plane Customer Admin -> Platform User inspection/mutation must return 404 USER_NOT_FOUND
        var accountAdmin = new AccountAdminService(verifyDb, currentUser);
        (await FluentActions.Invoking(() => accountAdmin.GetAsync(qaAdminId)).Should().ThrowAsync<NotFoundException>())
            .Which.Data["ErrorCode"].Should().Be("USER_NOT_FOUND");
        var whAccess = new UserWarehouseAccessService(verifyDb, currentUser, warehouseAuthMock.Object);
        (await FluentActions.Invoking(() => whAccess.GetForUserAsync(qaAdminId)).Should().ThrowAsync<NotFoundException>())
            .Which.Data["ErrorCode"].Should().Be("USER_NOT_FOUND");

        var govOps = new WmsGovernanceOperationsService(verifyDb, warehouseAuthMock.Object, currentUser);
        (await FluentActions.Invoking(() => govOps.LockUserAccountAsync(qaAdminId, null, tenantAdminId, null)).Should().ThrowAsync<NotFoundException>())
            .Which.Data["ErrorCode"].Should().Be("USER_NOT_FOUND");
        (await FluentActions.Invoking(() => govOps.ResetUserPasswordAsync(qaAdminId, "TempPass#2026!", "probe", null, tenantAdminId, null, null)).Should().ThrowAsync<NotFoundException>())
            .Which.Data["ErrorCode"].Should().Be("USER_NOT_FOUND");
        (await FluentActions.Invoking(() => govOps.EnsureTenantUserOwnershipAsync(qaAdminId, warehouseId)).Should().ThrowAsync<NotFoundException>())
            .Which.Data["ErrorCode"].Should().Be("USER_NOT_FOUND");
        (await govOps.SetupTotpAsync(qaAdminId, tenantAdminId)).StatusCode.Should().Be(StatusCodes.Status404NotFound);

        // 3. QA-W18-BE-IMPORT-LIST-ZERO-TOTAL: GET /api/v1/import-receipts on fresh SQL Server DbContext hydrates Details & TotalAmount = 5,500,000
        var importRepo = new ImportReceiptRepository(verifyDb, warehouseAuthMock.Object);
        var importService = new ImportReceiptService(
            importRepo, Mock.Of<IInventoryStockRepository>(), Mock.Of<IInventoryTransactionRepository>(),
            Mock.Of<IWarehouseRepository>(), Mock.Of<IProductRepository>(), Mock.Of<IUnitOfWork>(),
            Mock.Of<IAuditLogRepository>(), warehouseAuthMock.Object, currentUser, Mock.Of<IUserRepository>());
        var importCtrl = new ImportReceiptsController(
            importService, Mock.Of<IReceivingDiscrepancyService>(), Mock.Of<IImportReceiptOperationsService>(), warehouseAuthMock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = httpCtx }
        };

        var listOk = (await importCtrl.GetAll()).Should().BeOfType<OkObjectResult>().Subject;
        var dtos = listOk.Value.Should().BeAssignableTo<IEnumerable<ImportReceiptDto>>().Subject.ToList();
        var topReceipt = dtos.First(x => x.Code == "PNK-202611-001");
        topReceipt.Details.Should().ContainSingle();
        topReceipt.SubTotalAmount.Should().Be(5_000_000m);
        topReceipt.TaxAmount.Should().Be(500_000m);
        topReceipt.TotalAmount.Should().Be(5_500_000m);
    }
}
