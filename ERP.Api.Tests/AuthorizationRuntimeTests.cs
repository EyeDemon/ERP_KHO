using System.Net;
using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Headers;
using ERP.Api.Tests.Helpers;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace ERP.Api.Tests
{
    public class AuthorizationRuntimeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public AuthorizationRuntimeTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["JwtSettings:Secret"] = "test_secret_key_for_authorization_runtime_tests_minimum_32_bytes_long",
                        ["JwtSettings:Issuer"] = "ErpKhoApi",
                        ["JwtSettings:Audience"] = "ErpKhoClient",
                        ["JwtSettings:ExpiryMinutes"] = "60"
                    });
                });
                builder.ConfigureTestServices(services =>
                {
                    // Remove real DbContext
                    services.RemoveAll(typeof(DbContextOptions<ErpKhoDbContext>));
                    
                    // Add in-memory DbContext
                    services.AddDbContext<ErpKhoDbContext>(options => 
                        options.UseInMemoryDatabase("TestDb_Auth"));

                    // Setup Mock Authentication
                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = MockAuthenticationHandler.DefaultScheme;
                        options.DefaultChallengeScheme = MockAuthenticationHandler.DefaultScheme;
                    })
                    .AddScheme<AuthenticationSchemeOptions, MockAuthenticationHandler>(
                        MockAuthenticationHandler.DefaultScheme, options => { });
                });
            });
        }

        private HttpClient CreateClientWithRole(string? role)
        {
            var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            if (role != null)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(MockAuthenticationHandler.DefaultScheme);
                client.DefaultRequestHeaders.Add("X-Test-Role", role);
            }

            return client;
        }

        [Theory]
        [InlineData("POST", "/api/Products")]
        [InlineData("POST", "/api/Units")]
        [InlineData("POST", "/api/Warehouses")]
        [InlineData("POST", "/api/ImportReceipts")]
        [InlineData("POST", "/api/ExportReceipts")]
        [InlineData("POST", "/api/Stocktakes/1/approve")]
        [InlineData("GET", "/api/Reports/inventory-in-out-stock")]
        public async Task NoAuth_Returns401(string method, string url)
        {
            var client = CreateClientWithRole(null);
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            
            // Dummy content for POST to avoid 415/400 due to missing body
            if (method == "POST")
            {
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.InternalServerError)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new Exception($"500 Error: {error}");
            }

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("POST", "/api/Products", "Viewer")]
        [InlineData("PUT", "/api/Products/1", "Viewer")]
        [InlineData("DELETE", "/api/Products/1", "Viewer")]
        [InlineData("POST", "/api/Products", "WarehouseStaff")]
        [InlineData("POST", "/api/ImportReceipts", "Viewer")]
        [InlineData("POST", "/api/ImportReceipts", "WarehouseStaff")]
        [InlineData("POST", "/api/ImportReceipts/1/approve", "Viewer")]
        [InlineData("POST", "/api/ImportReceipts/1/approve", "WarehouseStaff")]
        [InlineData("POST", "/api/ExportReceipts", "Viewer")]
        [InlineData("POST", "/api/ExportReceipts/1/approve", "Viewer")]
        [InlineData("POST", "/api/ExportReceipts/1/cancel", "Viewer")]
        [InlineData("POST", "/api/Stocktakes/1/approve", "Viewer")]
        [InlineData("GET", "/api/Reports/inventory-in-out-stock?fromDate=2020-01-01", "WarehouseStaff")]
        public async Task ForbiddenRole_Returns403(string method, string url, string role)
        {
            var client = CreateClientWithRole(role);
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            
            if (method == "POST" || method == "PUT")
            {
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Theory]
        [InlineData("POST", "/api/Products", "Admin")]
        [InlineData("POST", "/api/Products", "Manager")]
        [InlineData("POST", "/api/ImportReceipts", "Admin")]
        [InlineData("POST", "/api/ImportReceipts", "Manager")]
        [InlineData("POST", "/api/ImportReceipts/1/approve", "Admin")]
        [InlineData("POST", "/api/ImportReceipts/1/approve", "Manager")]
        [InlineData("POST", "/api/ExportReceipts", "WarehouseStaff")]
        [InlineData("GET", "/api/Reports/inventory-in-out-stock?fromDate=2020-01-01", "Admin")]
        [InlineData("GET", "/api/Reports/inventory-in-out-stock?fromDate=2020-01-01", "Viewer")]
        public async Task AllowedRole_DoesNotReturn401Or403(string method, string url, string role)
        {
            var client = CreateClientWithRole(role);
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            
            if (method == "POST" || method == "PUT")
            {
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            }

            var response = await client.SendAsync(request);

            // It might return 400 Bad Request (invalid data) or 404 Not Found (mock entity not exist),
            // or even 200/201, but as long as it's NOT 401 or 403, authorization passed!
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
