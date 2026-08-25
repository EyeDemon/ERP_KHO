using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ERP.Api.Tests;

public class SwaggerRuntimeTests
{
    [Fact]
    public async Task SwaggerJson_IsAvailable_InDevelopment()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["JwtSettings:Secret"] = "test_secret_key_for_swagger_runtime_tests_minimum_32_bytes_long",
                        ["JwtSettings:Issuer"] = "ErpKhoApi",
                        ["JwtSettings:Audience"] = "ErpKhoClient"
                    });
                });
            });

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
