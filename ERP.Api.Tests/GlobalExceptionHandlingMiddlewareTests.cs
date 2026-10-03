using ERP.Api.Middleware;
using ERP.Domain.Exceptions;
using ERP.Application.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace ERP.Api.Tests;

public class GlobalExceptionHandlingMiddlewareTests
{
    [Theory]
    [InlineData(1205, true)]
    [InlineData(2627, false)]
    public void Sql_server_deadlock_is_a_concurrency_conflict(int number, bool expected)
        => typeof(GlobalExceptionHandlingMiddleware)
            .GetMethod("IsSqlServerConcurrencyError", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [number]).Should().Be(expected);

    [Fact]
    public void Sql_server_deadlock_message_does_not_expose_provider_details()
        => typeof(GlobalExceptionHandlingMiddleware)
            .GetMethod("PublicMessage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, ["SQL provider detail", true])
            .Should().Be("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");

    [Fact]
    public async Task InvokeAsync_ServiceUnavailableException_ReturnsMaintenanceResponse()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new ServiceUnavailableException("Workflow xuất kho đang tạm dừng để bảo trì. Vui lòng thử lại sau."),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("message").GetString().Should().Contain("đang tạm dừng để bảo trì");
        body.Should().NotContain("stackTrace");
    }

    [Fact]
    public async Task InvokeAsync_ConcurrencyException_ReturnsConflictResponse()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new ConcurrencyException("Tồn kho vừa được thay đổi."),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("message").GetString().Should().Be("Tồn kho vừa được thay đổi.");
        body.Should().NotContain("stackTrace");
    }
}
