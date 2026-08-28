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
