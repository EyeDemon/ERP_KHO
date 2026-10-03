using System.Text.RegularExpressions;
using ERP.Application.Interfaces;

namespace ERP.Api.Infrastructure;

public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context, IRequestMetadata metadata)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        metadata.CorrelationId = IsValid(supplied) ? supplied! : Guid.NewGuid().ToString("N");
        context.Response.Headers[HeaderName] = metadata.CorrelationId;
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = metadata.CorrelationId }))
            await next(context);
    }

    public static bool IsValid(string? value) => value is { Length: >= 1 and <= 64 } && SafeValue().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeValue();
}
