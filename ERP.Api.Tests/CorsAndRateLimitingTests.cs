using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using FluentAssertions;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERP.Api.Tests;

public sealed class CorsAndRateLimitingTests
{
    private static WebApplicationFactory<Program> CreateFactory(string environment = "Testing", bool includeCors = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "test_security_hardening_secret_key_minimum_32_bytes_long",
                ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=ERP_KHO;Trusted_Connection=True",
                ["Cors:AllowedOrigins:0"] = includeCors ? "https://allowed.test" : null,
                ["AuthSecurity:MaxFailedAttempts"] = "5",
                ["AuthSecurity:LockoutMinutes"] = "15",
                ["RateLimiting:Login:PermitLimit"] = "2",
                ["RateLimiting:Login:WindowSeconds"] = "60",
                ["RateLimiting:Api:PermitLimit"] = "100",
                ["RateLimiting:Api:WindowSeconds"] = "60"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthService>();
                services.AddSingleton<IAuthService, RejectingAuthService>();
            });
        });

    [Fact]
    public async Task Cors_AllowsConfiguredOriginAndRejectsOtherOrigin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var allowed = new HttpRequestMessage(HttpMethod.Options, "/api/Auth/login");
        allowed.Headers.Add("Origin", "https://allowed.test");
        allowed.Headers.Add("Access-Control-Request-Method", "POST");
        var allowedResponse = await client.SendAsync(allowed);
        allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain("https://allowed.test");

        using var denied = new HttpRequestMessage(HttpMethod.Options, "/api/Auth/login");
        denied.Headers.Add("Origin", "https://denied.test");
        denied.Headers.Add("Access-Control-Request-Method", "POST");
        var deniedResponse = await client.SendAsync(denied);
        deniedResponse.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task LoginRateLimit_Returns429AndRetryAfter()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        HttpResponseMessage? response = null;
        for (var i = 0; i < 10 && response?.StatusCode != HttpStatusCode.TooManyRequests; i++)
            response = await client.PostAsync("/api/Auth/login", new StringContent("{\"username\":\"unknown\",\"password\":\"wrong\"}", Encoding.UTF8, "application/json"));

        response!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public void ProductionWithoutCorsAllowlist_FailsFast()
    {
        using var factory = CreateFactory("Production", includeCors: false);
        var action = () => factory.CreateClient();
        action.Should().Throw<Exception>().Where(exception => exception.ToString().Contains("Cors:AllowedOrigins"));
    }

    private sealed class RejectingAuthService : IAuthService
    {
        public Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");
    }
}
