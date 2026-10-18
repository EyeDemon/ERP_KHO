using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ERP.Api.Tests;

public sealed class MultiTenantHttpAuthAndLifecycleTests
{
    private const string JwtSecret = "wave11_multitenant_http_auth_test_secret_key_at_least_32_bytes_long";
    private const string JwtIssuer = "ErpKhoApi";
    private const string JwtAudience = "ErpKhoClient";

    [Fact]
    public async Task NonDefaultTenant202_FullHttpAuthRefreshLogoutAndCrossTenantFkPrevention_WorksEndToEnd()
    {
        var dbName = $"wave11-mt-http-{Guid.NewGuid():N}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = JwtSecret,
                ["JwtSettings:Issuer"] = JwtIssuer,
                ["JwtSettings:Audience"] = JwtAudience,
                ["JwtSettings:ExpiryMinutes"] = "30"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ErpKhoDbContext>();
                services.RemoveAll(typeof(DbContextOptions<ErpKhoDbContext>));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<ErpKhoDbContext>));
                services.AddDbContext<ErpKhoDbContext>(options =>
                    options.UseInMemoryDatabase(dbName));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = JwtIssuer;
                    options.TokenValidationParameters.ValidAudience = JwtAudience;
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
                });
            });
        });

        await ExecuteTenant101And202LifecycleScenarioAsync(factory);
    }

    [ApprovalSqlServerFact]
    public async Task NonDefaultTenant202_SqlServer_FullHttpAuthRefreshAndCrossTenantFkPrevention_WorksEndToEnd()
    {
        await using var owned = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await owned.MigrateAndSeedAsync();

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = owned.ConnectionString,
                ["JwtSettings:Secret"] = JwtSecret,
                ["JwtSettings:Issuer"] = JwtIssuer,
                ["JwtSettings:Audience"] = JwtAudience,
                ["JwtSettings:ExpiryMinutes"] = "30"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ErpKhoDbContext>();
                services.RemoveAll(typeof(DbContextOptions<ErpKhoDbContext>));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<ErpKhoDbContext>));
                services.AddDbContext<ErpKhoDbContext>(options =>
                    options.UseSqlServer(owned.ConnectionString));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = JwtIssuer;
                    options.TokenValidationParameters.ValidAudience = JwtAudience;
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
                });
            });
        });

        await ExecuteTenant101And202LifecycleScenarioAsync(factory);
    }

    private static async Task ExecuteTenant101And202LifecycleScenarioAsync(WebApplicationFactory<Program> factory)
    {
        const int tenant101 = 101;
        const int tenant202 = 202;
        const string sharedUsername = "admin";
        const string password101 = "Tenant101#Secret!";
        const string password202 = "Tenant202#Secret!";

        int admin101Id;
        int admin202Id;
        int unit101Id;
        int category101Id;
        int unit202Id;
        int category202Id;

        // 1. Seed User "admin" in Tenant 101 AND User "admin" in Tenant 202 (same username "admin" in same DB)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();

            var adminRole = await db.Roles
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.RoleName == AppRoles.Admin);

            if (adminRole is null)
            {
                adminRole = new Role { RoleName = AppRoles.Admin, Description = "Administrator" };
                db.Roles.Add(adminRole);
                await db.SaveChangesAsync();
            }

            await EnsurePermissionsForRoleAsync(db, adminRole, [
                AppPermissions.ProductRead,
                AppPermissions.ProductCreate,
                AppPermissions.ProductUpdate,
                AppPermissions.ProductDeactivate
            ]);

            db.UseTenant(tenant101);
            var user101 = new User
            {
                Username = sharedUsername,
                PasswordHash = hasher.HashPassword(password101),
                FullName = "Admin Tenant 101",
                RoleId = adminRole.Id,
                IsActive = true
            };
            var unit101 = new Unit { Code = "PCS-101", Name = "Cái 101", DecimalPlaces = 0, IsActive = true };
            var cat101 = new ProductCategory { Code = "CAT-101", Name = "Danh mục 101", IsActive = true };
            db.Users.Add(user101);
            db.Units.Add(unit101);
            db.ProductCategories.Add(cat101);
            await db.SaveChangesAsync();

            admin101Id = user101.Id;
            unit101Id = unit101.Id;
            category101Id = cat101.Id;

            db.UseTenant(tenant202);
            var user202 = new User
            {
                Username = sharedUsername,
                PasswordHash = hasher.HashPassword(password202),
                FullName = "Admin Tenant 202",
                RoleId = adminRole.Id,
                IsActive = true
            };
            var unit202 = new Unit { Code = "PCS-202", Name = "Cái 202", DecimalPlaces = 0, IsActive = true };
            var cat202 = new ProductCategory { Code = "CAT-202", Name = "Danh mục 202", IsActive = true };
            db.Users.Add(user202);
            db.Units.Add(unit202);
            db.ProductCategories.Add(cat202);
            await db.SaveChangesAsync();

            admin202Id = user202.Id;
            unit202Id = unit202.Id;
            category202Id = cat202.Id;

            admin202Id.Should().NotBe(admin101Id);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        // Wrong tenant password check: logging into Tenant 202 with Tenant 101's password must fail with 401
        using (var wrongPassResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = sharedUsername,
            Password = password101,
            TenantId = tenant202
        }))
        {
            wrongPassResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 2. Call POST /api/auth/login for User "admin" of Tenant 202 -> 200 OK, JWT claim tenant_id = 202, cookie erp_refresh
        string initialAccessToken;
        string initialRefreshCookie;
        using (var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = sharedUsername,
            Password = password202,
            TenantId = tenant202,
            TenantCode = "TENANT-202"
        }))
        {
            loginResp.StatusCode.Should().Be(HttpStatusCode.OK, await loginResp.Content.ReadAsStringAsync());
            var payload = await loginResp.Content.ReadFromJsonAsync<LoginResponseDto>();
            payload.Should().NotBeNull();
            payload!.TenantId.Should().Be(tenant202);
            payload.TenantCode.Should().Be("TENANT-202");
            payload.Token.Should().NotBeNullOrWhiteSpace();
            initialAccessToken = payload.Token;

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(initialAccessToken);
            jwt.Claims.FirstOrDefault(c => c.Type == TenantClaimTypes.TenantId)?.Value.Should().Be("202");
            jwt.Claims.FirstOrDefault(c => c.Type == TenantClaimTypes.TenantCode)?.Value.Should().Be("TENANT-202");

            initialRefreshCookie = ExtractRefreshCookieValue(loginResp);
            initialRefreshCookie.Should().NotBeNullOrWhiteSpace("login must set HttpOnly erp_refresh cookie for Tenant 202");
        }

        // Also verify tenant-qualified username format ("admin@TENANT-202") works without explicit TenantId field
        using (var loginByQualifiedNameResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = $"{sharedUsername}@TENANT-202",
            Password = password202
        }))
        {
            loginByQualifiedNameResp.StatusCode.Should().Be(HttpStatusCode.OK, await loginByQualifiedNameResp.Content.ReadAsStringAsync());
            var payload = await loginByQualifiedNameResp.Content.ReadFromJsonAsync<LoginResponseDto>();
            payload!.TenantId.Should().Be(tenant202);
        }

        // 3. Strip Authorization header, send ONLY cookie erp_refresh of Tenant 202 to POST /api/auth/refresh
        //    -> 200 OK, new Access Token still carries tenant_id = 202 (not blocked by Query Filter!)
        string rotatedAccessToken;
        string rotatedRefreshCookie;
        using (var refreshReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh"))
        {
            refreshReq.Headers.Authorization.Should().BeNull();
            refreshReq.Headers.Add("Cookie", $"erp_refresh={initialRefreshCookie}");

            using var refreshResp = await client.SendAsync(refreshReq);
            refreshResp.StatusCode.Should().Be(HttpStatusCode.OK, await refreshResp.Content.ReadAsStringAsync());

            using var doc = JsonDocument.Parse(await refreshResp.Content.ReadAsStringAsync());
            rotatedAccessToken = doc.RootElement.GetProperty("token").GetString()!;
            rotatedAccessToken.Should().NotBeNullOrWhiteSpace();

            var rotatedJwt = new JwtSecurityTokenHandler().ReadJwtToken(rotatedAccessToken);
            rotatedJwt.Claims.FirstOrDefault(c => c.Type == TenantClaimTypes.TenantId)?.Value
                .Should().Be("202", "rotated access token must preserve TenantId = 202 from UserSession/User");
            rotatedJwt.Claims.FirstOrDefault(c => c.Type == TenantClaimTypes.TenantCode)?.Value
                .Should().Be("TENANT-202");

            rotatedRefreshCookie = ExtractRefreshCookieValue(refreshResp);
            rotatedRefreshCookie.Should().NotBeNullOrWhiteSpace().And.NotBe(initialRefreshCookie);
        }

        // Verify GET /api/auth/me with rotated Tenant 202 JWT returns Tenant 202's admin user
        using (var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"))
        {
            meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedAccessToken);
            using var meResp = await client.SendAsync(meReq);
            meResp.StatusCode.Should().Be(HttpStatusCode.OK, await meResp.Content.ReadAsStringAsync());
            using var meDoc = JsonDocument.Parse(await meResp.Content.ReadAsStringAsync());
            meDoc.RootElement.GetProperty("id").GetInt32().Should().Be(admin202Id);
            meDoc.RootElement.GetProperty("username").GetString().Should().Be(sharedUsername);
        }

        // 4. Using JWT of Tenant 202, call POST /api/Products passing UnitId / CategoryId of Tenant 101 -> blocked!
        using (var crossUnitReq = new HttpRequestMessage(HttpMethod.Post, "/api/Products"))
        {
            crossUnitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedAccessToken);
            crossUnitReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            crossUnitReq.Content = JsonContent.Create(new CreateProductDto
            {
                Code = "SKU-CROSS-UNIT",
                Name = "Cross Tenant Unit Injection Attempt",
                UnitId = unit101Id, // Belongs to Tenant 101!
                CategoryId = category202Id
            });

            using var crossUnitResp = await client.SendAsync(crossUnitReq);
            crossUnitResp.StatusCode.Should().BeOneOf(
                [HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.NotFound],
                await crossUnitResp.Content.ReadAsStringAsync());
        }

        using (var crossCategoryReq = new HttpRequestMessage(HttpMethod.Post, "/api/Products"))
        {
            crossCategoryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedAccessToken);
            crossCategoryReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            crossCategoryReq.Content = JsonContent.Create(new CreateProductDto
            {
                Code = "SKU-CROSS-CAT",
                Name = "Cross Tenant Category Injection Attempt",
                UnitId = unit202Id,
                CategoryId = category101Id // Belongs to Tenant 101!
            });

            using var crossCategoryResp = await client.SendAsync(crossCategoryReq);
            crossCategoryResp.StatusCode.Should().BeOneOf(
                [HttpStatusCode.BadRequest, HttpStatusCode.Forbidden, HttpStatusCode.NotFound],
                await crossCategoryResp.Content.ReadAsStringAsync());
        }

        // Create a valid Product in Tenant 202 using Tenant 202's own UnitId and CategoryId -> 201 Created
        int createdProduct202Id;
        using (var validCreateReq = new HttpRequestMessage(HttpMethod.Post, "/api/Products"))
        {
            validCreateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedAccessToken);
            validCreateReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            validCreateReq.Content = JsonContent.Create(new CreateProductDto
            {
                Code = "SKU-VALID-202",
                Name = "Valid Tenant 202 Product",
                UnitId = unit202Id,
                CategoryId = category202Id
            });

            using var validCreateResp = await client.SendAsync(validCreateReq);
            validCreateResp.StatusCode.Should().Be(HttpStatusCode.Created, await validCreateResp.Content.ReadAsStringAsync());
            using var createdDoc = JsonDocument.Parse(await validCreateResp.Content.ReadAsStringAsync());
            createdProduct202Id = createdDoc.RootElement.GetProperty("id").GetInt32();
        }

        // Attempt to mutate existing Tenant 202 Product via PUT /api/Products/{id} to point UnitId to Tenant 101's UnitId -> 403 Forbidden
        using (var crossUpdateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/Products/{createdProduct202Id}"))
        {
            crossUpdateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedAccessToken);
            crossUpdateReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            crossUpdateReq.Content = JsonContent.Create(new UpdateProductDto
            {
                Name = "Attempt Cross-Tenant UnitId Update",
                UnitId = unit101Id, // Foreign key belonging to Tenant 101!
                IsActive = true
            });

            using var crossUpdateResp = await client.SendAsync(crossUpdateReq);
            crossUpdateResp.StatusCode.Should().BeOneOf(
                [HttpStatusCode.Forbidden, HttpStatusCode.BadRequest, HttpStatusCode.NotFound],
                await crossUpdateResp.Content.ReadAsStringAsync());
        }

        // Verify in DB that zero cross-tenant FK rows exist
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();

            var crossUnitExists = await db.Products.IgnoreQueryFilters()
                .AnyAsync(p => p.TenantId == tenant202 && (p.UnitId == unit101Id || p.CategoryId == category101Id));
            crossUnitExists.Should().BeFalse("zero cross-tenant foreign keys may ever be persisted");

            var savedProduct202 = await db.Products.IgnoreQueryFilters()
                .SingleAsync(p => p.Id == createdProduct202Id);
            savedProduct202.TenantId.Should().Be(tenant202);
            savedProduct202.UnitId.Should().Be(unit202Id);
            savedProduct202.CategoryId.Should().Be(category202Id);
        }

        // 5. Strip Authorization header and call POST /api/auth/logout with only Cookie: erp_refresh of Tenant 202
        using (var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
        {
            logoutReq.Headers.Authorization.Should().BeNull();
            logoutReq.Headers.Add("Cookie", $"erp_refresh={rotatedRefreshCookie}");
            using var logoutResp = await client.SendAsync(logoutReq);
            logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // After logout without JWT, the Tenant 202 session must be revoked in DB and subsequent API calls with rotatedAccessToken must return 401
        using (var afterLogoutMeReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me"))
        {
            afterLogoutMeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedAccessToken);
            using var afterLogoutMeResp = await client.SendAsync(afterLogoutMeReq);
            afterLogoutMeResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task Wave12_BatchedForeignKeyValidationAndCrossTenantAdminIsolation_AreEnforcedEndToEnd()
    {
        var dbName = $"multi_tenant_w12_{Guid.NewGuid():N}";
        const int tenant101 = 101;
        const int tenant202 = 202;
        const string pass101 = "Tenant101#Wave12!";
        const string pass202 = "Tenant202#Wave12!";

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = JwtSecret,
                ["JwtSettings:Issuer"] = JwtIssuer,
                ["JwtSettings:Audience"] = JwtAudience,
                ["JwtSettings:ExpiryMinutes"] = "30"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ErpKhoDbContext>();
                services.RemoveAll(typeof(DbContextOptions<ErpKhoDbContext>));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<ErpKhoDbContext>));
                services.AddDbContext<ErpKhoDbContext>(options =>
                    options.UseInMemoryDatabase(dbName)
                        .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning)));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = JwtIssuer;
                    options.TokenValidationParameters.ValidAudience = JwtAudience;
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
                });
            });
        });

        int admin101Id;
        int staff101Id;
        int wh101Id;
        int loc101Id;
        int prod101Id;
        int sharedProd101Id;
        int partner101Id;
        int inv101Id;
        int admin202Id;
        int staff202Id;
        int wh202Id;
        int loc202Id;
        int sharedProd202Id;
        int customer202Id;
        int poSupplier202Id;
        int altUnit202Id;
        int cat202Id;
        int inv202Id;
        var prod202Ids = new List<int>();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasherService>();

            var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == AppRoles.Admin);
            if (adminRole is null)
            {
                adminRole = new Role { RoleName = AppRoles.Admin };
                db.Roles.Add(adminRole);
                await db.SaveChangesAsync();
            }

            await EnsurePermissionsForRoleAsync(
                db,
                adminRole,
                [
                    AppPermissions.PermissionRead,
                    AppPermissions.PermissionAssign,
                    AppPermissions.RoleRead,
                    AppPermissions.RoleManage,
                    AppPermissions.UserRead,
                    AppPermissions.UserManage,
                    AppPermissions.UserWarehouseRead,
                    AppPermissions.UserWarehouseManage,
                    AppPermissions.WarehouseRead,
                    AppPermissions.ProductRead,
                    AppPermissions.ProductCreate,
                    AppPermissions.PartnerRead,
                    AppPermissions.PartnerUpdate,
                    AppPermissions.PartnerDeactivate,
                    AppPermissions.UomRead,
                    AppPermissions.UomManage,
                    AppPermissions.CategoryRead,
                    AppPermissions.CategoryManage
                ]);

            var staffRole = await db.Roles.FirstOrDefaultAsync(r => r.RoleName == AppRoles.WarehouseStaff);
            if (staffRole is null)
            {
                staffRole = new Role { RoleName = AppRoles.WarehouseStaff };
                db.Roles.Add(staffRole);
                await db.SaveChangesAsync();
            }

            // Grant read-only Viewer permissions on Partner Debts to staffRole (no PartnerUpdate)
            await EnsurePermissionsForRoleAsync(
                db,
                staffRole,
                [
                    AppPermissions.PartnerRead,
                    AppPermissions.ProductRead
                ]);

            db.UseTenant(tenant101);
            var admin101 = new User
            {
                Username = "admin",
                PasswordHash = hasher.HashPassword(pass101),
                FullName = "Admin Tenant 101",
                Email = "admin@tenant-101.local",
                RoleId = adminRole.Id,
                IsActive = true
            };
            var staff101 = new User
            {
                Username = "staff_101",
                PasswordHash = hasher.HashPassword(pass101),
                FullName = "Staff Tenant 101",
                Email = "staff@tenant-101.local",
                RoleId = staffRole.Id,
                IsActive = true
            };
            var wh101 = new Warehouse { Code = "WH-101", Name = "Kho 101", IsActive = true };
            var unit101 = new Unit { Code = "PCS", Name = "Cái 101", IsActive = true };
            var cat101 = new ProductCategory { Code = "CAT", Name = "Nhóm 101", IsActive = true };
            var partner101 = new BusinessPartner
            {
                Code = "PARTNER-SHARED",
                Name = "Đối tác Tenant 101",
                IsSupplier = true,
                IsCustomer = true,
                PayableBalance = 5_000_000m,
                ReceivableBalance = 0m,
                IsActive = true
            };
            db.Users.AddRange(admin101, staff101);
            db.Warehouses.Add(wh101);
            db.Units.Add(unit101);
            db.ProductCategories.Add(cat101);
            db.BusinessPartners.Add(partner101);
            await db.SaveChangesAsync();

            var loc101 = new WarehouseLocation
            {
                WarehouseId = wh101.Id,
                Code = "BIN-SHARED-A01",
                Name = "Kệ A01 Tenant 101",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsPickable = true,
                IsReceivable = true
            };
            var prod101 = new Product
            {
                Code = "SKU-101-FOREIGN",
                Name = "Sản phẩm Tenant 101",
                UnitId = unit101.Id,
                CategoryId = cat101.Id,
                IsActive = true
            };
            var sharedProd101 = new Product
            {
                Code = "SKU-SHARED-BC",
                Name = "Sản phẩm trùng mã Tenant 101",
                UnitId = unit101.Id,
                CategoryId = cat101.Id,
                IsActive = true
            };
            db.WarehouseLocations.Add(loc101);
            db.Products.AddRange(prod101, sharedProd101);
            await db.SaveChangesAsync();

            db.ProductBarcodes.Add(new ProductBarcode
            {
                ProductId = sharedProd101.Id,
                Value = "8938505970019"
            });
            var inv101 = new PartnerDebtInvoice
            {
                Code = "AP-101-0001",
                PartnerId = partner101.Id,
                DebtType = ERP.Domain.Enums.PartnerDebtType.AP,
                SourceType = "ImportReceipt",
                SourceId = 10101,
                SourceCode = "IR-101-0001",
                SubTotalAmount = 5_000_000m,
                TaxAmount = 0m,
                TotalAmount = 5_000_000m,
                PaidAmount = 0m,
                RemainingAmount = 5_000_000m,
                Status = ERP.Domain.Enums.PartnerDebtInvoiceStatus.Open,
                IssuedAtUtc = DateTime.UtcNow,
                CreatedBy = admin101.Id
            };
            var ledger101 = new PartnerDebtLedger
            {
                PartnerId = partner101.Id,
                Invoice = inv101,
                DebtType = ERP.Domain.Enums.PartnerDebtType.AP,
                TransactionType = ERP.Domain.Enums.PartnerDebtTransactionType.InvoiceCreated,
                ReferenceType = "ImportReceipt",
                ReferenceId = 10101,
                ReferenceCode = "IR-101-0001",
                AmountDelta = 5_000_000m,
                BalanceAfter = 5_000_000m,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = admin101.Id
            };
            db.PartnerDebtInvoices.Add(inv101);
            db.PartnerDebtLedgers.Add(ledger101);
            await db.SaveChangesAsync();

            admin101Id = admin101.Id;
            staff101Id = staff101.Id;
            wh101Id = wh101.Id;
            loc101Id = loc101.Id;
            prod101Id = prod101.Id;
            sharedProd101Id = sharedProd101.Id;
            partner101Id = partner101.Id;
            inv101Id = inv101.Id;

            db.UseTenant(tenant202);
            var admin202 = new User
            {
                Username = "admin",
                PasswordHash = hasher.HashPassword(pass202),
                FullName = "Admin Tenant 202",
                Email = "admin@tenant-202.local",
                RoleId = adminRole.Id,
                IsActive = true
            };
            var staff202 = new User
            {
                Username = "staff_202",
                PasswordHash = hasher.HashPassword(pass202),
                FullName = "Staff Tenant 202",
                Email = "staff@tenant-202.local",
                RoleId = staffRole.Id,
                IsActive = true
            };
            var wh202 = new Warehouse { Code = "WH-202", Name = "Kho 202", IsActive = true };
            var unit202 = new Unit { Code = "PCS", Name = "Cái 202", IsActive = true };
            var altUnit202 = new Unit { Code = "BOX12", Name = "Thùng 12 Cái 202", IsActive = true };
            var cat202 = new ProductCategory { Code = "CAT", Name = "Nhóm 202", IsActive = true };
            var cust202 = new BusinessPartner
            {
                Code = "CUST-202",
                Name = "Khách hàng 202",
                IsCustomer = true,
                PayableBalance = 0m,
                ReceivableBalance = 880_000m,
                IsActive = true
            };
            var poSup202 = new BusinessPartner
            {
                Code = "SUP-PO-202",
                Name = "Nhà cung cấp PO 202",
                IsSupplier = true,
                IsActive = true
            };
            db.Users.AddRange(admin202, staff202);
            db.Warehouses.Add(wh202);
            db.Units.AddRange(unit202, altUnit202);
            db.ProductCategories.Add(cat202);
            db.BusinessPartners.AddRange(cust202, poSup202);
            await db.SaveChangesAsync();

            for (var i = 1; i <= 20; i++)
            {
                var p202 = new Product
                {
                    Code = $"SKU-202-{i:D2}",
                    Name = $"Sản phẩm 202 #{i}",
                    UnitId = unit202.Id,
                    CategoryId = cat202.Id,
                    IsActive = true
                };
                db.Products.Add(p202);
            }
            await db.SaveChangesAsync();

            prod202Ids = await db.Products
                .OrderBy(p => p.Code)
                .Select(p => p.Id)
                .ToListAsync();
            prod202Ids.Should().HaveCount(20);

            db.ProductUoms.Add(new ProductUom
            {
                ProductId = prod202Ids[0],
                UnitId = altUnit202.Id,
                ConversionFactor = 12m,
                Version = 1,
                EffectiveFromUtc = DateTime.UtcNow.AddDays(-1),
                IsActive = true
            });
            db.PurchaseOrders.Add(new PurchaseOrder
            {
                Code = "PO-202-GUARD-01",
                SourceSystem = "ERP",
                ExternalPoId = "EXT-PO-202-GUARD-01",
                SupplierId = poSup202.Id,
                WarehouseId = wh202.Id,
                OrderDate = DateTime.UtcNow.Date,
                Status = ERP.Domain.Enums.PurchaseOrderStatus.Open,
                CreatedBy = admin202.Id,
                CreatedAtUtc = DateTime.UtcNow
            });

            var loc202 = new WarehouseLocation
            {
                WarehouseId = wh202.Id,
                Code = "BIN-SHARED-A01",
                Name = "Kệ A01 Tenant 202",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsPickable = true,
                IsReceivable = true
            };
            var sharedProd202 = new Product
            {
                Code = "SKU-SHARED-BC",
                Name = "Sản phẩm trùng mã Tenant 202",
                UnitId = unit202.Id,
                CategoryId = cat202.Id,
                IsActive = true
            };
            db.WarehouseLocations.Add(loc202);
            db.Products.Add(sharedProd202);
            await db.SaveChangesAsync();

            db.ProductBarcodes.Add(new ProductBarcode
            {
                ProductId = sharedProd202.Id,
                Value = "8938505970019"
            });
            var inv202 = new PartnerDebtInvoice
            {
                Code = "AR-202-0001",
                PartnerId = cust202.Id,
                DebtType = ERP.Domain.Enums.PartnerDebtType.AR,
                SourceType = "Shipment",
                SourceId = 20201,
                SourceCode = "SHP-202-0001",
                SubTotalAmount = 800_000m,
                TaxAmount = 80_000m,
                TotalAmount = 880_000m,
                PaidAmount = 0m,
                RemainingAmount = 880_000m,
                Status = ERP.Domain.Enums.PartnerDebtInvoiceStatus.Open,
                IssuedAtUtc = DateTime.UtcNow,
                CreatedBy = admin202.Id
            };
            var ledger202 = new PartnerDebtLedger
            {
                PartnerId = cust202.Id,
                Invoice = inv202,
                DebtType = ERP.Domain.Enums.PartnerDebtType.AR,
                TransactionType = ERP.Domain.Enums.PartnerDebtTransactionType.InvoiceCreated,
                ReferenceType = "Shipment",
                ReferenceId = 20201,
                ReferenceCode = "SHP-202-0001",
                AmountDelta = 880_000m,
                BalanceAfter = 880_000m,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedBy = admin202.Id
            };
            db.PartnerDebtInvoices.Add(inv202);
            db.PartnerDebtLedgers.Add(ledger202);
            await db.SaveChangesAsync();

            admin202Id = admin202.Id;
            staff202Id = staff202.Id;
            wh202Id = wh202.Id;
            loc202Id = loc202.Id;
            sharedProd202Id = sharedProd202.Id;
            customer202Id = cust202.Id;
            poSupplier202Id = poSup202.Id;
            altUnit202Id = altUnit202.Id;
            cat202Id = cat202.Id;
            inv202Id = inv202.Id;
        }

        // 1. Test 20-line batched SaveChangesAsync FK ownership validation in a fresh Tenant 202 DbContext (empty ChangeTracker)
        var dbOptions = factory.Services.GetRequiredService<DbContextOptions<ErpKhoDbContext>>();
        var tenant202Ctx = new TenantContext
        {
            TenantId = tenant202,
            TenantCode = "TENANT-202",
            IsSystemBypass = false
        };

        int so202OkId;
        int so202BadId;
        await using (var headerDb = new ErpKhoDbContext(dbOptions, tenantContext: tenant202Ctx))
        {
            var soOk = new SalesOrder
            {
                OrderCode = "SO-202-BATCH-20",
                ExternalOrderId = "EXT-202-BATCH-20",
                CustomerId = customer202Id,
                WarehouseId = wh202Id,
                Status = ERP.Domain.Enums.SalesOrderStatus.Released,
                CreatedBy = admin202Id,
                CreatedAt = DateTime.UtcNow
            };
            var soBad = new SalesOrder
            {
                OrderCode = "SO-202-BATCH-BAD",
                ExternalOrderId = "EXT-202-BATCH-BAD",
                CustomerId = customer202Id,
                WarehouseId = wh202Id,
                Status = ERP.Domain.Enums.SalesOrderStatus.Released,
                CreatedBy = admin202Id,
                CreatedAt = DateTime.UtcNow
            };
            headerDb.SalesOrders.AddRange(soOk, soBad);
            await headerDb.SaveChangesAsync();
            so202OkId = soOk.Id;
            so202BadId = soBad.Id;
        }

        await using (var batchOkDb = new ErpKhoDbContext(dbOptions, tenantContext: tenant202Ctx))
        {
            var lines = new List<SalesOrderLine>();
            for (var i = 0; i < 20; i++)
            {
                lines.Add(new SalesOrderLine
                {
                    SalesOrderId = so202OkId,
                    ExternalLineId = $"L-{i + 1:D2}",
                    ProductId = prod202Ids[i],
                    OrderedQuantity = 10m + i,
                    UomCode = "PCS"
                });
            }

            batchOkDb.SalesOrderLines.AddRange(lines);
            await batchOkDb.SaveChangesAsync();
            lines.Should().HaveCount(20);
            lines.Should().OnlyContain(l => l.TenantId == tenant202);
        }

        await using (var batchBadDb = new ErpKhoDbContext(dbOptions, tenantContext: tenant202Ctx))
        {
            for (var i = 0; i < 19; i++)
            {
                batchBadDb.SalesOrderLines.Add(new SalesOrderLine
                {
                    SalesOrderId = so202BadId,
                    ExternalLineId = $"BL-{i + 1:D2}",
                    ProductId = prod202Ids[i],
                    OrderedQuantity = 5m,
                    UomCode = "PCS"
                });
            }

            batchBadDb.SalesOrderLines.Add(new SalesOrderLine
            {
                SalesOrderId = so202BadId,
                ExternalLineId = "BL-20",
                ProductId = prod101Id, // Foreign ProductId belonging to Tenant 101
                OrderedQuantity = 5m,
                UomCode = "PCS"
            });

            var actCrossBatch = () => batchBadDb.SaveChangesAsync();
            await actCrossBatch.Should().ThrowAsync<ERP.Application.Exceptions.ForbiddenException>()
                .WithMessage("*403 Cross-Tenant Reference Denied*");
        }

        // 2. Log in as Admin of Tenant 202 via HTTP and verify complete cross-tenant admin isolation
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = false
        });

        using var loginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "admin",
            Password = pass202,
            TenantCode = "TENANT-202"
        });
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginPayload = await loginResp.Content.ReadFromJsonAsync<LoginResponseDto>();
        var token202 = loginPayload!.Token;

        // 2a. GET /api/permissions/users returns only Tenant 202 users (never Tenant 101 users)
        using (var usersReq = new HttpRequestMessage(HttpMethod.Get, "/api/permissions/users"))
        {
            usersReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var usersResp = await client.SendAsync(usersReq);
            usersResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var usersDoc = JsonDocument.Parse(await usersResp.Content.ReadAsStringAsync());
            var returnedIds = usersDoc.RootElement.EnumerateArray()
                .Select(x => x.GetProperty("id").GetInt32())
                .ToList();

            returnedIds.Should().Contain([admin202Id, staff202Id]);
            returnedIds.Should().NotContain([admin101Id, staff101Id]);
        }

        // 2b. Admin of Tenant 202 cannot lock, reset password, or delete a User of Tenant 101 -> 404 NotFound
        using (var lockForeignReq = new HttpRequestMessage(HttpMethod.Post, $"/api/account-security/{staff101Id}/lock"))
        {
            lockForeignReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            lockForeignReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            lockForeignReq.Content = JsonContent.Create(new { });
            using var lockForeignResp = await client.SendAsync(lockForeignReq);
            lockForeignResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var resetForeignReq = new HttpRequestMessage(HttpMethod.Post, $"/api/account-security/{staff101Id}/reset-password"))
        {
            resetForeignReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            resetForeignReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            resetForeignReq.Content = JsonContent.Create(new { TemporaryPassword = "NewTempPass!123", Reason = "Cross-tenant test" });
            using var resetForeignResp = await client.SendAsync(resetForeignReq);
            resetForeignResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var deleteForeignReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/account-security/{staff101Id}"))
        {
            deleteForeignReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var deleteForeignResp = await client.SendAsync(deleteForeignReq);
            deleteForeignResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // 2c. Admin of Tenant 202 cannot grant warehouse access across tenants (foreign user or foreign warehouse) -> 404 NotFound
        using (var grantForeignWhReq = new HttpRequestMessage(HttpMethod.Post, $"/api/user-warehouse-access/{staff202Id}"))
        {
            grantForeignWhReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            grantForeignWhReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            grantForeignWhReq.Content = JsonContent.Create(new GrantWarehouseAccessDto(wh101Id, "AAAAAAA="));
            using var grantForeignWhResp = await client.SendAsync(grantForeignWhReq);
            grantForeignWhResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var grantForeignUserReq = new HttpRequestMessage(HttpMethod.Post, $"/api/user-warehouse-access/{staff101Id}"))
        {
            grantForeignUserReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            grantForeignUserReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            grantForeignUserReq.Content = JsonContent.Create(new GrantWarehouseAccessDto(wh202Id, "AAAAAAA="));
            using var grantForeignUserResp = await client.SendAsync(grantForeignUserReq);
            grantForeignUserResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        // 2d. Sole active Admin of Tenant 202 is protected by Last Active Admin Guard independently of Tenant 101 -> 409 Conflict
        using (var deleteSoleAdminReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/account-security/{admin202Id}"))
        {
            deleteSoleAdminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var deleteSoleAdminResp = await client.SendAsync(deleteSoleAdminReq);
            deleteSoleAdminResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
            using var errDoc = JsonDocument.Parse(await deleteSoleAdminResp.Content.ReadAsStringAsync());
            errDoc.RootElement.GetProperty("message").GetString().Should().Contain("quản trị viên hợp lệ cuối cùng");
        }

        // 2e. Multi-Tenant Barcode Lookup Isolation (/api/barcode-lookup/resolve) when Tenant 101 & Tenant 202 have identical codes
        using (var resolveLocReq = new HttpRequestMessage(HttpMethod.Get, "/api/barcode-lookup/resolve?code=BIN-SHARED-A01"))
        {
            resolveLocReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var resolveLocResp = await client.SendAsync(resolveLocReq);
            resolveLocResp.StatusCode.Should().Be(HttpStatusCode.OK, await resolveLocResp.Content.ReadAsStringAsync());
            using var locDoc = JsonDocument.Parse(await resolveLocResp.Content.ReadAsStringAsync());
            locDoc.RootElement.GetProperty("entityType").GetString().Should().Be("Location");
            locDoc.RootElement.GetProperty("entityId").GetInt32().Should().Be(loc202Id).And.NotBe(loc101Id);
            locDoc.RootElement.GetProperty("warehouseId").GetInt32().Should().Be(wh202Id);
        }

        using (var resolveForeignWhReq = new HttpRequestMessage(HttpMethod.Get, $"/api/barcode-lookup/resolve?code=BIN-SHARED-A01&warehouseId={wh101Id}"))
        {
            resolveForeignWhReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var resolveForeignWhResp = await client.SendAsync(resolveForeignWhReq);
            resolveForeignWhResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var resolveProdCodeReq = new HttpRequestMessage(HttpMethod.Get, "/api/barcode-lookup/resolve?code=SKU-SHARED-BC"))
        {
            resolveProdCodeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var resolveProdCodeResp = await client.SendAsync(resolveProdCodeReq);
            resolveProdCodeResp.StatusCode.Should().Be(HttpStatusCode.OK, await resolveProdCodeResp.Content.ReadAsStringAsync());
            using var prodDoc = JsonDocument.Parse(await resolveProdCodeResp.Content.ReadAsStringAsync());
            prodDoc.RootElement.GetProperty("entityType").GetString().Should().Be("Product");
            prodDoc.RootElement.GetProperty("entityId").GetInt32().Should().Be(sharedProd202Id).And.NotBe(sharedProd101Id);
        }

        using (var resolveEanReq = new HttpRequestMessage(HttpMethod.Get, "/api/barcode-lookup/resolve?code=8938505970019"))
        {
            resolveEanReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var resolveEanResp = await client.SendAsync(resolveEanReq);
            resolveEanResp.StatusCode.Should().Be(HttpStatusCode.OK, await resolveEanResp.Content.ReadAsStringAsync());
            using var eanDoc = JsonDocument.Parse(await resolveEanResp.Content.ReadAsStringAsync());
            eanDoc.RootElement.GetProperty("entityType").GetString().Should().Be("Product");
            eanDoc.RootElement.GetProperty("entityId").GetInt32().Should().Be(sharedProd202Id).And.NotBe(sharedProd101Id);
        }

        // 2f. Multi-Tenant Partner Debt Isolation (/api/partner-debts/*) between Tenant 101 and Tenant 202
        using (var debtSummaryReq = new HttpRequestMessage(HttpMethod.Get, "/api/partner-debts/summary"))
        {
            debtSummaryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var debtSummaryResp = await client.SendAsync(debtSummaryReq);
            debtSummaryResp.StatusCode.Should().Be(HttpStatusCode.OK, await debtSummaryResp.Content.ReadAsStringAsync());
            using var sumDoc = JsonDocument.Parse(await debtSummaryResp.Content.ReadAsStringAsync());
            sumDoc.RootElement.GetProperty("totalPayableBalance").GetDecimal().Should().Be(0m);
            sumDoc.RootElement.GetProperty("totalReceivableBalance").GetDecimal().Should().Be(880_000m);
            sumDoc.RootElement.GetProperty("openApInvoiceCount").GetInt32().Should().Be(0);
            sumDoc.RootElement.GetProperty("openArInvoiceCount").GetInt32().Should().Be(1);
        }

        using (var listInvoicesReq = new HttpRequestMessage(HttpMethod.Get, "/api/partner-debts/invoices"))
        {
            listInvoicesReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var listInvoicesResp = await client.SendAsync(listInvoicesReq);
            listInvoicesResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var invListDoc = JsonDocument.Parse(await listInvoicesResp.Content.ReadAsStringAsync());
            var invIds = invListDoc.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToList();
            invIds.Should().ContainSingle().Which.Should().Be(inv202Id);
            invIds.Should().NotContain(inv101Id);
        }

        using (var foreignPartnerInvoicesReq = new HttpRequestMessage(HttpMethod.Get, $"/api/partner-debts/invoices?partnerId={partner101Id}"))
        {
            foreignPartnerInvoicesReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var foreignPartnerInvoicesResp = await client.SendAsync(foreignPartnerInvoicesReq);
            foreignPartnerInvoicesResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var emptyDoc = JsonDocument.Parse(await foreignPartnerInvoicesResp.Content.ReadAsStringAsync());
            emptyDoc.RootElement.GetArrayLength().Should().Be(0);
        }

        using (var getForeignInvoiceReq = new HttpRequestMessage(HttpMethod.Get, $"/api/partner-debts/invoices/{inv101Id}"))
        {
            getForeignInvoiceReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var getForeignInvoiceResp = await client.SendAsync(getForeignInvoiceReq);
            getForeignInvoiceResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var payForeignInvoiceReq = new HttpRequestMessage(HttpMethod.Post, $"/api/partner-debts/invoices/{inv101Id}/pay"))
        {
            payForeignInvoiceReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            payForeignInvoiceReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            payForeignInvoiceReq.Content = JsonContent.Create(new PayPartnerDebtDto
            {
                Amount = 100_000m,
                ReferenceCode = "CROSS-PAY-101",
                Note = "Attempt cross-tenant payment"
            });
            using var payForeignInvoiceResp = await client.SendAsync(payForeignInvoiceReq);
            payForeignInvoiceResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var foreignLedgerReq = new HttpRequestMessage(HttpMethod.Get, $"/api/partner-debts/ledger?partnerId={partner101Id}&invoiceId={inv101Id}"))
        {
            foreignLedgerReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            using var foreignLedgerResp = await client.SendAsync(foreignLedgerReq);
            foreignLedgerResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using var foreignLedgerDoc = JsonDocument.Parse(await foreignLedgerResp.Content.ReadAsStringAsync());
            foreignLedgerDoc.RootElement.GetArrayLength().Should().Be(0);
        }

        // 2g. Master Data Delete Guards (/api/business-partners/{id}, /api/Units/{id}, /api/product-categories/{id}) -> 400/409 (never 500)
        using (var deleteDebtPartnerReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/business-partners/{customer202Id}"))
        {
            deleteDebtPartnerReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            deleteDebtPartnerReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var deleteDebtPartnerResp = await client.SendAsync(deleteDebtPartnerReq);
            deleteDebtPartnerResp.StatusCode.Should().BeOneOf(
                [HttpStatusCode.BadRequest, HttpStatusCode.Conflict],
                await deleteDebtPartnerResp.Content.ReadAsStringAsync());
        }

        using (var deletePoPartnerReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/business-partners/{poSupplier202Id}"))
        {
            deletePoPartnerReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            deletePoPartnerReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var deletePoPartnerResp = await client.SendAsync(deletePoPartnerReq);
            deletePoPartnerResp.StatusCode.Should().BeOneOf(
                [HttpStatusCode.BadRequest, HttpStatusCode.Conflict],
                await deletePoPartnerResp.Content.ReadAsStringAsync());
        }

        using (var deleteUomUnitReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/Units/{altUnit202Id}"))
        {
            deleteUomUnitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            deleteUomUnitReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var deleteUomUnitResp = await client.SendAsync(deleteUomUnitReq);
            deleteUomUnitResp.StatusCode.Should().BeOneOf(
                [HttpStatusCode.BadRequest, HttpStatusCode.Conflict],
                await deleteUomUnitResp.Content.ReadAsStringAsync());
        }

        using (var deleteCategoryReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/product-categories/{cat202Id}"))
        {
            deleteCategoryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token202);
            deleteCategoryReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var deleteCategoryResp = await client.SendAsync(deleteCategoryReq);
            deleteCategoryResp.StatusCode.Should().BeOneOf(
                [HttpStatusCode.BadRequest, HttpStatusCode.Conflict],
                await deleteCategoryResp.Content.ReadAsStringAsync());
        }

        // 2h. RBAC Guard: Viewer / Read-Only Staff without PartnerUpdate permission gets 403 Forbidden on POST /api/partner-debts/invoices/{id}/pay
        using var staffLoginResp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto
        {
            Username = "staff_202",
            Password = pass202,
            TenantCode = "TENANT-202"
        });
        staffLoginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var staffLoginPayload = await staffLoginResp.Content.ReadFromJsonAsync<LoginResponseDto>();
        var staffToken202 = staffLoginPayload!.Token;

        using (var staffReadDebtReq = new HttpRequestMessage(HttpMethod.Get, "/api/partner-debts/invoices"))
        {
            staffReadDebtReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken202);
            using var staffReadDebtResp = await client.SendAsync(staffReadDebtReq);
            staffReadDebtResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var viewerPayDebtReq = new HttpRequestMessage(HttpMethod.Post, $"/api/partner-debts/invoices/{inv202Id}/pay"))
        {
            viewerPayDebtReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staffToken202);
            viewerPayDebtReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            viewerPayDebtReq.Content = JsonContent.Create(new PayPartnerDebtDto
            {
                Amount = 100_000m,
                ReferenceCode = "VIEWER-FORBIDDEN-PAY",
                Note = "Viewer should not be allowed to pay debt"
            });
            using var viewerPayDebtResp = await client.SendAsync(viewerPayDebtReq);
            viewerPayDebtResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    private static async Task EnsurePermissionsForRoleAsync(
        ErpKhoDbContext db,
        Role role,
        IEnumerable<string> permissionCodes)
    {
        foreach (var code in permissionCodes)
        {
            var perm = await db.Permissions.FirstOrDefaultAsync(p => p.Code == code);
            if (perm is null)
            {
                perm = new Permission(code, code);
                db.Permissions.Add(perm);
                await db.SaveChangesAsync();
            }

            var hasRolePerm = await db.RolePermissions
                .AnyAsync(rp => rp.RoleId == role.Id && rp.PermissionId == perm.Id);
            if (!hasRolePerm)
            {
                db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = perm.Id });
                await db.SaveChangesAsync();
            }
        }
    }

    private static string ExtractRefreshCookieValue(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return string.Empty;
        }

        foreach (var cookieHeader in cookies)
        {
            if (cookieHeader.StartsWith("erp_refresh=", StringComparison.OrdinalIgnoreCase))
            {
                var firstSegment = cookieHeader.Split(';', 2)[0];
                return Uri.UnescapeDataString(firstSegment["erp_refresh=".Length..]);
            }
        }

        return string.Empty;
    }
}
