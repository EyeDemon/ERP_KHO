using System.Net;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Exceptions;
using ERP.Domain.Exceptions;
using Microsoft.Data.SqlClient;

namespace ERP.Api.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var isSqlServerConcurrencyError = IsSqlServerConcurrencyError(FindSqlException(exception)?.Number);
        var statusCode = exception switch
        {
            BusinessRuleException business when business.Data["HttpStatusCode"] is 409 => (int)HttpStatusCode.Conflict,
            BusinessRuleException => (int)HttpStatusCode.BadRequest,
            ServiceUnavailableException => (int)HttpStatusCode.ServiceUnavailable,
            NotFoundException => (int)HttpStatusCode.NotFound,
            ForbiddenException => (int)HttpStatusCode.Forbidden,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            ConcurrencyException => (int)HttpStatusCode.Conflict,
            _ when isSqlServerConcurrencyError => (int)HttpStatusCode.Conflict,
            _ => (int)HttpStatusCode.InternalServerError
        };

        if (statusCode == (int)HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred");
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception: {Message}", exception.Message);
        }

        var publicMessage = PublicMessage(exception.Message, isSqlServerConcurrencyError);
        var response = new ErrorResponse
        {
            Success = false,
            Message = publicMessage,
            Code = exception.Data["ErrorCode"] as string,
            StatusCode = statusCode,
            Detail = statusCode == (int)HttpStatusCode.InternalServerError ? null : publicMessage
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    internal static bool IsSqlServerConcurrencyError(int? number) => number == 1205;

    internal static string PublicMessage(string message, bool isSqlServerConcurrencyError)
        => isSqlServerConcurrencyError ? "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại." : message;

    private static SqlException? FindSqlException(Exception? error)
    {
        while (error is not null)
        {
            if (error is SqlException sql) return sql;
            error = error.InnerException;
        }
        return null;
    }
}
