using System.Net;
using System.Text.Json;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERP.Api.Tests;

public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task LiveHealth_IsAvailableWithoutDatabaseDetails()
    {
        await using var factory = CreateFactory(healthEnabled: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("healthy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadyHealth_UsesConfiguredTestDatabaseAndReturnsOnlyStatus()
    {
        await using var factory = CreateFactory(healthEnabled: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", json.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("connection", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthEndpoints_CanBeDisabledExplicitlyOutsideDevelopment()
    {
        await using var factory = CreateFactory(healthEnabled: false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool healthEnabled) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "test_health_secret_key_minimum_32_bytes_long",
                ["JwtSettings:Issuer"] = "ErpKhoApi",
                ["JwtSettings:Audience"] = "ErpKhoClient",
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=HealthTestPlaceholder;Trusted_Connection=True",
                ["Cors:AllowedOrigins:0"] = "https://allowed.test",
                ["Health:Enabled"] = healthEnabled.ToString()
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ErpKhoDbContext>();
                services.RemoveAll<DbContextOptions<ErpKhoDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ErpKhoDbContext>>();
                services.AddDbContext<ErpKhoDbContext>(options => options.UseInMemoryDatabase($"health-{Guid.NewGuid():N}"));
            });
        });
}
