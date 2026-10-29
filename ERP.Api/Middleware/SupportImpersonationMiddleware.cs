using System.Security.Cryptography;
using System.Text;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Middleware;

public sealed class SupportImpersonationMiddleware(RequestDelegate next)
{
    public const string ImpersonationTokenHeader = "X-Support-Impersonation-Token";
    public const string SupportSessionCodeHeader = "X-Support-Session-Code";
    public const string ImpersonatedBySupportUserIdItemKey = "ImpersonatedBySupportUserId";
    public const string ImpersonationSessionCodeItemKey = "ImpersonationSessionCode";

    public async Task InvokeAsync(HttpContext context)
    {
        var tokenHeader = context.Request.Headers[ImpersonationTokenHeader].FirstOrDefault();
        var sessionCodeHeader = context.Request.Headers[SupportSessionCodeHeader].FirstOrDefault();
        var rawToken = !string.IsNullOrWhiteSpace(tokenHeader)
            ? tokenHeader.Trim()
            : sessionCodeHeader?.Trim();

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            await next(context);
            return;
        }

        var db = context.RequestServices.GetService<ErpKhoDbContext>();
        if (db is null)
        {
            await next(context);
            return;
        }

        var now = DateTime.UtcNow;
        var tokenSha256HexUpper = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
        var tokenSha256HexLower = tokenSha256HexUpper.ToLowerInvariant();

        var session = await db.SupportImpersonationSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.SessionCode == rawToken
                        || s.TokenHash == rawToken
                        || s.TokenHash == tokenSha256HexUpper
                        || s.TokenHash == tokenSha256HexLower)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(context.RequestAborted);

        var isActive = session is not null
            && string.Equals(session.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)
            && !session.RevokedAtUtc.HasValue
            && session.ExpiresAtUtc > now;

        if (!isActive || session is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "IMPERSONATION_SESSION_INVALID_OR_EXPIRED",
                errorCode = "IMPERSONATION_SESSION_INVALID_OR_EXPIRED",
                message = "Phiên hỗ trợ kỹ thuật không hợp lệ, đã bị thu hồi hoặc đã hết hạn."
            }, context.RequestAborted);
            return;
        }

        context.Items[ImpersonatedBySupportUserIdItemKey] = session.SupportAgentUserId;
        context.Items[ImpersonationSessionCodeItemKey] = session.SessionCode;

        if (session.ReadOnlyScope && IsMutationMethod(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "IMPERSONATION_READ_ONLY_ENFORCED",
                errorCode = "IMPERSONATION_READ_ONLY_ENFORCED",
                message = "Phiên hỗ trợ kỹ thuật chỉ có quyền xem (Read-Only), không được phép thay đổi dữ liệu."
            }, context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool IsMutationMethod(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);
}
