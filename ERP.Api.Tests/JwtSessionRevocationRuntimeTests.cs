using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ERP.Api.Tests;

public sealed class JwtSessionRevocationRuntimeTests
{
    private const string Secret = "test_secret_key_for_session_revocation_runtime_tests_32_bytes";

    [Fact]
    public async Task RevokedSession_InvalidatesPreviouslyIssuedAccessToken()
    {
        var databaseName = $"jwt-session-{Guid.NewGuid():N}";
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = Secret,
                ["JwtSettings:Issuer"] = "ErpKhoApi",
                ["JwtSettings:Audience"] = "ErpKhoClient",
                ["JwtSettings:ExpiryMinutes"] = "60"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ErpKhoDbContext>();
                services.RemoveAll(typeof(DbContextOptions<ErpKhoDbContext>));
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<ErpKhoDbContext>));
                services.AddDbContext<ErpKhoDbContext>(options => options.UseInMemoryDatabase(databaseName));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.TokenValidationParameters.ValidIssuer = "ErpKhoApi";
                    options.TokenValidationParameters.ValidAudience = "ErpKhoClient";
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
                });
            });
        });

        const int userId = 91;
        string accessToken;
        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            var role = new Role { Id = 9, RoleName = "Manager" };
            var user = new User { Id = userId, Username = "qa_runtime_session", PasswordHash = "not-used", RoleId = role.Id, Role = role };
            const string jti = "runtime-session-jti";
            accessToken = CreateToken(userId, jti);
            context.AddRange(role, user, new UserSession
            {
                Id = Guid.NewGuid(), UserId = userId, User = user,
                RefreshTokenHash = new string('B', 64), RefreshTokenFamilyId = Guid.NewGuid(),
                AccessTokenJti = jti, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddHours(1)
            });
            await context.SaveChangesAsync();
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var activeResponse = await client.GetAsync("/api/Auth/sessions");
        activeResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"WWW-Authenticate: {string.Join(", ", activeResponse.Headers.WwwAuthenticate)}; body: {await activeResponse.Content.ReadAsStringAsync()}");

        using (var scope = factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ErpKhoDbContext>();
            var session = await context.UserSessions.SingleAsync();
            session.RevokedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }

        (await client.GetAsync("/api/Auth/sessions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string CreateToken(int userId, string jti)
    {
        var token = new JwtSecurityToken(
            issuer: "ErpKhoApi", audience: "ErpKhoClient",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "qa_runtime_session"),
                new Claim(ClaimTypes.Role, "Manager"),
                new Claim(JwtRegisteredClaimNames.Jti, jti)
            ],
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

}
