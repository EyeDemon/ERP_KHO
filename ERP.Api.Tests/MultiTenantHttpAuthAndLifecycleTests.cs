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
