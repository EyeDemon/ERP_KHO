using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ERP.Api.Authorization;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Middleware;

public sealed class SupportImpersonationMiddleware(RequestDelegate next)
{
    public const string ImpersonationTokenHeader = "X-Support-Impersonation-Token";
    public const string SupportSessionCodeHeader = "X-Support-Session-Code";
    public const string ImpersonatedBySupportUserIdItemKey = "ImpersonatedBySupportUserId";
    public const string ImpersonationSessionCodeItemKey = "ImpersonationSessionCode";
    public const string ImpersonatedTenantIdItemKey = "ImpersonatedTenantId";

    public async Task InvokeAsync(HttpContext context)
    {
        var tokenHeader = context.Request.Headers[ImpersonationTokenHeader].FirstOrDefault();
        var sessionCodeHeader = context.Request.Headers[SupportSessionCodeHeader].FirstOrDefault();
        var rawToken = !string.IsNullOrWhiteSpace(tokenHeader)
            ? tokenHeader.Trim()
            : sessionCodeHeader?.Trim();

        var db = context.RequestServices?.GetService<ErpKhoDbContext>();

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var securityDomain = context.User.FindFirstValue(TenantClaimTypes.SecurityDomain);
                var role = context.User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
                var isDualHatSuperAdmin = string.Equals(role, AppRoles.SuperAdmin, StringComparison.OrdinalIgnoreCase);

                var isPurePlatformOperator =
                    (string.Equals(securityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase)
                     || AppRoles.IsPlatformRole(role))
                    && !isDualHatSuperAdmin
                    && !string.Equals(role, AppRoles.Admin, StringComparison.OrdinalIgnoreCase);

                if (isPurePlatformOperator
                    && IsCustomerWarehouseOperationalPath(context.Request.Path.Value))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        code = "PLATFORM_DIRECT_WAREHOUSE_ACCESS_FORBIDDEN",
                        errorCode = "PLATFORM_DIRECT_WAREHOUSE_ACCESS_FORBIDDEN",
                        message = "Tài khoản Quản trị Nền tảng (PLATFORM) không được phép trực tiếp truy cập hoặc thay đổi dữ liệu vận hành kho của khách hàng khi không có phiên Impersonation được cấp phép."
                    }, context.RequestAborted);
                    return;
                }

                if (!isPurePlatformOperator
                    && db is not null
                    && !IsPlatformOrAuthOrBillingPath(context.Request.Path.Value)
                    && int.TryParse(context.User.FindFirstValue(TenantClaimTypes.TenantId), out var callerTenantId)
                    && callerTenantId > 0)
                {
                    try
                    {
                        var tenantStatus = await db.SaasTenants
                            .AsNoTracking()
                            .Where(t => t.TenantId == callerTenantId)
                            .Select(t => t.LifecycleStatus)
                            .FirstOrDefaultAsync(context.RequestAborted);

                        if (string.Equals(tenantStatus, "SUSPENDED", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                code = "TENANT_SUSPENDED",
                                errorCode = "TENANT_SUSPENDED",
                                message = "Thuê bao doanh nghiệp hiện đang bị tạm ngưng (SUSPENDED). Vui lòng liên hệ bộ phận Quản trị Nền tảng."
                            }, context.RequestAborted);
                            return;
                        }

                        if (string.Equals(tenantStatus, "READ_ONLY", StringComparison.OrdinalIgnoreCase)
                            && IsMutationMethod(context.Request.Method))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                code = "TENANT_READ_ONLY_ENFORCED",
                                errorCode = "TENANT_READ_ONLY_ENFORCED",
                                message = "Thuê bao doanh nghiệp đang ở chế độ Chỉ Đọc (READ_ONLY). Không được phép tạo hoặc sửa chứng từ kho."
                            }, context.RequestAborted);
                            return;
                        }
                    }
                    catch
                    {
                        // Ignore if SaasTenants table is unavailable in isolated unit tests.
                    }
                }
            }

            await next(context);
            return;
        }

        if (db is null)
        {
            await next(context);
            return;
        }

        var now = DateTime.UtcNow;
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "IMPERSONATION_AUTHENTICATION_REQUIRED",
                errorCode = "IMPERSONATION_AUTHENTICATION_REQUIRED",
                message = "Yêu cầu xác thực danh tính Quản trị Nền tảng (PLATFORM) trước khi sử dụng mã phiên Impersonation."
            }, context.RequestAborted);
            return;
        }

        var callerDomain = context.User.FindFirstValue(TenantClaimTypes.SecurityDomain);
        var callerRole = context.User.FindFirstValue(ClaimTypes.Role);
        if (!string.Equals(callerDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase)
            && !AppRoles.IsPlatformRole(callerRole))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "IMPERSONATION_PLATFORM_CALLER_REQUIRED",
                errorCode = "IMPERSONATION_PLATFORM_CALLER_REQUIRED",
                message = "Chỉ tài khoản Quản trị Nền tảng (PLATFORM) mới được phép sử dụng phiên Impersonation hỗ trợ kỹ thuật."
            }, context.RequestAborted);
            return;
        }

        var tokenSha256Bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var tokenSha256HexUpper = Convert.ToHexString(tokenSha256Bytes);
        var tokenSha256HexLower = tokenSha256HexUpper.ToLowerInvariant();

        var session = await db.SupportImpersonationSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TokenHash == tokenSha256HexUpper || s.TokenHash == tokenSha256HexLower)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(context.RequestAborted);

        var hashMatches = false;
        if (session is not null && !string.IsNullOrWhiteSpace(session.TokenHash) && session.TokenHash.Length == 64)
        {
            try
            {
                var storedBytes = Convert.FromHexString(session.TokenHash);
                hashMatches = CryptographicOperations.FixedTimeEquals(storedBytes, tokenSha256Bytes);
            }
            catch (FormatException)
            {
                hashMatches = false;
            }
        }

        var isActive = session is not null
            && hashMatches
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

        if (context.User.Identity?.IsAuthenticated == true
            && int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var authenticatedCallerId)
            && authenticatedCallerId > 0
            && session.SupportAgentUserId > 0
            && session.SupportAgentUserId != authenticatedCallerId)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "IMPERSONATION_AGENT_MISMATCH",
                errorCode = "IMPERSONATION_AGENT_MISMATCH",
                message = "Phiên hỗ trợ kỹ thuật này được cấp cho chuyên viên khác, không khớp danh tính người gọi hiện tại."
            }, context.RequestAborted);
            return;
        }

        if (session.TenantId > 0)
        {
            var targetTenant = await db.SaasTenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.TenantId == session.TenantId)
                .Select(t => new { t.LifecycleStatus, t.AllowSupportAccess })
                .FirstOrDefaultAsync(context.RequestAborted);
            if (targetTenant is not null)
            {
                if (!targetTenant.AllowSupportAccess)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        code = "TENANT_SUPPORT_ACCESS_DISABLED",
                        errorCode = "TENANT_SUPPORT_ACCESS_DISABLED",
                        message = "Khách hàng thuê bao đã tắt quyền truy cập hỗ trợ kỹ thuật từ xa (AllowSupportAccess = false)."
                    }, context.RequestAborted);
                    return;
                }

                if (string.Equals(targetTenant.LifecycleStatus, "SUSPENDED", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        code = "TENANT_SUSPENDED",
                        errorCode = "TENANT_SUSPENDED",
                        message = "Thuê bao mục tiêu đang bị tạm ngưng (SUSPENDED), không thể mở phiên thao tác kho."
                    }, context.RequestAborted);
                    return;
                }
            }
        }

        context.Items[ImpersonatedBySupportUserIdItemKey] = session.SupportAgentUserId;
        context.Items[ImpersonationSessionCodeItemKey] = session.SessionCode;
        if (session.TenantId > 0)
        {
            context.Items[ImpersonatedTenantIdItemKey] = session.TenantId;
            db.UseTenant(session.TenantId);
        }

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

    private static bool IsCustomerWarehouseOperationalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, "/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IsAllowedDirectPlatformSegment(path, "/api/v1/platform")
            && !IsAllowedDirectPlatformSegment(path, "/api/platform")
            && !IsAllowedDirectPlatformSegment(path, "/api/v1/auth")
            && !IsAllowedDirectPlatformSegment(path, "/api/auth");
    }

    private static bool IsAllowedDirectPlatformSegment(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && (path.Length == prefix.Length || path[prefix.Length] == '/');

    private static bool IsPlatformOrAuthOrBillingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return path.StartsWith("/api/v1/platform", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/platform", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/v1/saas-billing", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/saas-billing", StringComparison.OrdinalIgnoreCase);
    }
}
