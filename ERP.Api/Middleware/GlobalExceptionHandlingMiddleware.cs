using System.Net;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Exceptions;
using ERP.Domain.Exceptions;
using Microsoft.Data.SqlClient;

namespace ERP.Api.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private static readonly HashSet<string> UnprocessableErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADDRESS_RESOLUTION_FAILED",
        "ADDRESS_WARD_UNRESOLVED",
        "ADDRESS_PROVINCE_UNRESOLVED",
        "ADDRESS_DISTRICT_UNRESOLVED",
        "UNPROCESSABLE_ENTITY"
    };

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
        var isSqlServerConcurrencyError = IsSqlServerConcurrencyError(FindSqlException(exception)?.Number)
            || exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException;
        var statusCode = exception switch
        {
            BusinessRuleException business when business.Data["HttpStatusCode"] is 409 => (int)HttpStatusCode.Conflict,
            BusinessRuleException business when IsUnprocessableException(business) => (int)HttpStatusCode.UnprocessableEntity,
            BusinessRuleException => (int)HttpStatusCode.BadRequest,
            UnprocessableDomainException => (int)HttpStatusCode.UnprocessableEntity,
            ServiceUnavailableException => (int)HttpStatusCode.ServiceUnavailable,
            NotFoundException => (int)HttpStatusCode.NotFound,
            ForbiddenException => (int)HttpStatusCode.Forbidden,
            UnauthorizedAccessException => (int)HttpStatusCode.Unauthorized,
            ConcurrencyException => (int)HttpStatusCode.Conflict,
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => (int)HttpStatusCode.Conflict,
            _ when IsUnprocessableException(exception) => (int)HttpStatusCode.UnprocessableEntity,
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

        var publicMessage = ResolvePublicMessage(exception.Message, statusCode, isSqlServerConcurrencyError);
        var errorCode = exception.Data["ErrorCode"] as string
            ?? (exception is ConcurrencyException ce && !string.IsNullOrWhiteSpace(ce.ErrorCode) ? ce.ErrorCode : null)
            ?? (exception is UnprocessableDomainException ude && !string.IsNullOrWhiteSpace(ude.ErrorCode) ? ude.ErrorCode : null)
            ?? statusCode switch
            {
                (int)HttpStatusCode.NotFound => "RESOURCE_NOT_FOUND",
                (int)HttpStatusCode.Forbidden => "WAREHOUSE_ACCESS_DENIED",
                (int)HttpStatusCode.Unauthorized => "UNAUTHORIZED",
                (int)HttpStatusCode.Conflict => "CONCURRENCY_CONFLICT",
                (int)HttpStatusCode.UnprocessableEntity => "UNPROCESSABLE_ENTITY",
                (int)HttpStatusCode.BadRequest => "BUSINESS_RULE_VIOLATION",
                _ => "INTERNAL_SERVER_ERROR"
            };

        var response = new ErrorResponse
        {
            Success = false,
            Message = publicMessage,
            Code = errorCode,
            StatusCode = statusCode,
            Detail = statusCode == (int)HttpStatusCode.InternalServerError ? null : publicMessage,
            Error = new ErrorDetailEnvelope
            {
                Code = errorCode,
                Message = publicMessage
            }
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    internal static bool IsUnprocessableException(Exception exception)
    {
        if (exception is UnprocessableDomainException)
        {
            return true;
        }

        if (exception.Data["HttpStatusCode"] is 422 or HttpStatusCode.UnprocessableEntity)
        {
            return true;
        }

        if (exception.GetType().Name.Contains("Unprocessable", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (exception.Data["ErrorCode"] is string errorCode && UnprocessableErrorCodes.Contains(errorCode.Trim()))
        {
            return true;
        }

        return false;
    }

    internal static bool IsSqlServerConcurrencyError(int? number) => number == 1205;

    internal static string PublicMessage(string message, bool isSqlServerConcurrencyError)
        => isSqlServerConcurrencyError ? "Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại." : message;

    internal static string ResolvePublicMessage(string message, int statusCode, bool isSqlServerConcurrencyError)
        => statusCode == (int)HttpStatusCode.InternalServerError
            ? "Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau."
            : PublicMessage(message, isSqlServerConcurrencyError);

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
