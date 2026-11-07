using System.Security.Claims;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class PlatformAuthorizeAttribute : TypeFilterAttribute
{
    public string? Permission { get; }

    public PlatformAuthorizeAttribute() : base(typeof(PlatformAuthorizationFilter))
    {
        Arguments = [string.Empty];
        Order = int.MinValue;
    }

    public PlatformAuthorizeAttribute(string permission) : base(typeof(PlatformAuthorizationFilter))
    {
        Permission = permission;
        Arguments = [permission ?? string.Empty];
        Order = int.MinValue;
    }
}

public sealed class PlatformAuthorizationFilter(ErpKhoDbContext db, string requiredPermission)
    : IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any())
        {
            return;
        }

        var principal = context.HttpContext.User;
        if (principal.Identity?.IsAuthenticated != true
            || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || userId <= 0)
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                code = "UNAUTHORIZED",
                errorCode = "UNAUTHORIZED",
                message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại."
            });
            return;
        }

        var isPlatformDomain = await IsPlatformCallerAsync(principal, userId, db, context.HttpContext.RequestAborted);
        if (!isPlatformDomain)
        {
            if (string.IsNullOrWhiteSpace(requiredPermission)
                && context.ActionDescriptor.RouteValues.TryGetValue("action", out var actionName)
                && string.Equals(actionName, "StopSupportImpersonation", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            LogPlatformDenied(context, userId, requiredPermission, "NON_PLATFORM_CALLER");
            context.Result = new ObjectResult(new
            {
                code = "PLATFORM_PERMISSION_REQUIRED",
                errorCode = "PLATFORM_PERMISSION_REQUIRED",
                message = "Thao tác này chỉ dành cho Quản trị viên Nền tảng (SaaS Control Center — SecurityDomain = PLATFORM)."
            })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        if (!string.IsNullOrWhiteSpace(requiredPermission))
        {
            var acceptedPermissions = GetEquivalentPermissions(requiredPermission);
            var permissionClaims = principal.Claims
                .Where(c => c.Type is "permission" or "permissions")
                .Select(c => c.Value)
                .ToList();
            var hasClaim = permissionClaims.Any(claim =>
                acceptedPermissions.Contains(claim, StringComparer.OrdinalIgnoreCase));
            var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var isRestrictedSubRole = role is "SupportAdmin" or "PlatformSupportAgent" or "BillingAdmin" or "PlatformBillingAdmin" or "SecurityAuditor" or "PlatformSecurityAuditor" or "PlatformReadOnly" or "SupportReadOnly" or "Viewer";
            var isFullSuperAdminWithoutExplicitClaims = !isRestrictedSubRole && permissionClaims.Count == 0 && AppRoles.IsSuperAdminPlatformRole(role);

            var allowed = permissionClaims.Count > 0
                ? hasClaim
                : isFullSuperAdminWithoutExplicitClaims || ERP.Domain.Security.AppPermissions
                    .GetDefaultPermissionsForRole(role)
                    .Any(p => acceptedPermissions.Contains(p, StringComparer.OrdinalIgnoreCase));

            if (!allowed)
            {
                LogPlatformDenied(context, userId, requiredPermission, "MISSING_PLATFORM_PERMISSION");
                if (string.Equals(requiredPermission, AppPermissions.PlatformBillingReconcile, StringComparison.OrdinalIgnoreCase))
                {
                    context.Result = new ObjectResult(new
                    {
                        code = "BILLING_RECONCILE_FORBIDDEN",
                        errorCode = "BILLING_RECONCILE_FORBIDDEN",
                        message = "Tài khoản không có quyền thực hiện đối soát thanh toán (platform.billing.reconcile)."
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }

                context.Result = new ObjectResult(new
                {
                    code = "PLATFORM_PERMISSION_REQUIRED",
                    errorCode = "PLATFORM_PERMISSION_REQUIRED",
                    message = $"Thiếu quyền quản trị nền tảng: {requiredPermission}."
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }
        }
    }

    private static void LogPlatformDenied(AuthorizationFilterContext context, int userId, string? requiredPermission, string reason)
    {
        var logger = context.HttpContext.RequestServices?.GetService<ILogger<PlatformAuthorizationFilter>>();
        var correlationId = context.HttpContext.Response.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? context.HttpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? context.HttpContext.TraceIdentifier;
        logger?.LogWarning(
            "SECURITY_PLATFORM_RBAC_DENIED UserId={UserId} Path={Path} Method={Method} RequiredPermission={RequiredPermission} Reason={Reason} CorrelationId={CorrelationId}",
            userId,
            context.HttpContext.Request.Path.Value,
            context.HttpContext.Request.Method,
            requiredPermission ?? "(platform-domain)",
            reason,
            correlationId);
    }

    public static IReadOnlyList<string> GetEquivalentPermissions(string requiredPermission) =>
        requiredPermission switch
        {
            AppPermissions.PlatformTenantSuspend or "platform.tenant.write" =>
                [AppPermissions.PlatformTenantSuspend, "platform.tenant.write"],
            AppPermissions.PlatformQuotaOverride or "platform.quota.write" =>
                [AppPermissions.PlatformQuotaOverride, "platform.quota.write"],
            _ => [requiredPermission]
        };

    public static async Task<bool> IsPlatformCallerAsync(
        ClaimsPrincipal? principal,
        int userId,
        ErpKhoDbContext? db,
        CancellationToken ct = default)
    {
        if (principal is null || principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var domainClaim = principal.FindFirstValue(TenantClaimTypes.SecurityDomain);
        var roleClaim = principal.FindFirstValue(ClaimTypes.Role);

        if (string.Equals(domainClaim, "CUSTOMER_TENANT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainClaim, "TENANT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(domainClaim, "PLATFORM", StringComparison.OrdinalIgnoreCase)
            || AppRoles.IsPlatformRole(roleClaim))
        {
            return true;
        }

        if (db is not null && userId > 0)
        {
            try
            {
                var dbUser = await db.Users
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(u => u.Id == userId && u.IsActive)
                    .Select(u => new
                    {
                        u.SecurityDomain,
                        u.TenantId,
                        RoleName = u.Role.RoleName,
                        RoleSecurityDomain = u.Role.SecurityDomain
                    })
                    .FirstOrDefaultAsync(ct);

                if (dbUser is not null)
                {
                    if (string.Equals(dbUser.SecurityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(dbUser.RoleSecurityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase)
                        || AppRoles.IsPlatformRole(dbUser.RoleName))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore DB lookup fallback errors in isolated unit tests.
            }
        }

        return false;
    }
}
