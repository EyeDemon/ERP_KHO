using System.Security.Claims;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Authorization;

[AttributeUsage(AttributeTargets.Class|AttributeTargets.Method, AllowMultiple=true, Inherited=true)]
public sealed class PermissionAuthorizeAttribute : TypeFilterAttribute
{
    public string Permission { get; }
    public IReadOnlyList<string> Permissions { get; }

    public PermissionAuthorizeAttribute(string permission) : base(typeof(PermissionAuthorizationFilter))
    {
        Permission = permission;
        Permissions = [permission];
        Arguments = [permission];
        Order = int.MinValue;
    }

    public PermissionAuthorizeAttribute(string permission, params string[] additionalPermissions) : base(typeof(PermissionAuthorizationFilter))
    {
        Permission = permission;
        var all = new List<string> { permission };
        if (additionalPermissions is { Length: > 0 })
        {
            foreach (var p in additionalPermissions)
            {
                if (!string.IsNullOrWhiteSpace(p) && !all.Contains(p, StringComparer.Ordinal))
                    all.Add(p);
            }
        }
        Permissions = all;
        Arguments = all.Count == 1 ? [permission] : [all.ToArray()];
        Order = int.MinValue;
    }
}

public sealed class PermissionAuthorizationFilter : IAsyncAuthorizationFilter, IOrderedFilter
{
    public const string CachedPermissionsKey = HttpCurrentUser.CachedPermissionsKey;
    private readonly ErpKhoDbContext db;
    private readonly string[] _permissions;

    public PermissionAuthorizationFilter(ErpKhoDbContext db, string permission)
    {
        this.db = db;
        _permissions = [permission];
    }

    public PermissionAuthorizationFilter(ErpKhoDbContext db, string[] permissions)
    {
        this.db = db;
        _permissions = permissions is { Length: > 0 } ? permissions : [];
    }

    public int Order => int.MinValue;
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any())
        {
            return;
        }
        if (context.HttpContext.User.Identity?.IsAuthenticated != true || !int.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        { context.Result=new UnauthorizedObjectResult(new { message="Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." }); return; }
        try
        {
            if (context.HttpContext.Items.TryGetValue(HttpCurrentUser.CachedPermissionsKey, out var cachedObj) &&
                cachedObj is IReadOnlySet<string> cachedPerms)
            {
                if (!_permissions.Any(cachedPerms.Contains))
                {
                    LogPermissionDenied(context, userId);
                    context.Result = new ObjectResult(new { code = "PERMISSION_DENIED", errorCode = "PERMISSION_DENIED", message = "Bạn không có quyền thực hiện thao tác này." }) { StatusCode = StatusCodes.Status403Forbidden };
                }
                return;
            }

            var user = await db.Users.AsNoTracking().Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.IsActive,
                    u.LockoutEnd,
                    RoleName = u.Role.RoleName,
                    PermissionCodes = u.Role.Permissions.Select(rp => rp.Permission.Code).ToList()
                })
                .SingleOrDefaultAsync(CancellationToken.None);
            if (user is null || !user.IsActive || user.LockoutEnd > DateTime.UtcNow)
            {
                context.Result = new UnauthorizedObjectResult(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });
                return;
            }
            var permSet = new HashSet<string>(user.PermissionCodes.Where(c => !string.IsNullOrWhiteSpace(c)), StringComparer.OrdinalIgnoreCase);
            context.HttpContext.Items[HttpCurrentUser.EffectiveRoleKey] = user.RoleName;
            context.HttpContext.Items[HttpCurrentUser.CachedPermissionsKey] = permSet;
            var allowed = _permissions.Any(permSet.Contains);
            if (!allowed)
            {
                LogPermissionDenied(context, userId);
                context.Result = new ObjectResult(new { code = "PERMISSION_DENIED", errorCode = "PERMISSION_DENIED", message = "Bạn không có quyền thực hiện thao tác này." }) { StatusCode = StatusCodes.Status403Forbidden };
            }
        }
        catch
        {
            context.Result = new ObjectResult(new { message = "Không thể xác minh quyền truy cập. Vui lòng thử lại." }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }
    }

    private void LogPermissionDenied(AuthorizationFilterContext context, int userId)
    {
        var logger = context.HttpContext.RequestServices?.GetService<ILogger<PermissionAuthorizationFilter>>();
        var correlationId = context.HttpContext.Response.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? context.HttpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault()
            ?? context.HttpContext.TraceIdentifier;
        var tenantId = context.HttpContext.User.FindFirstValue(ERP.Application.Interfaces.TenantClaimTypes.TenantId) ?? "1";
        logger?.LogWarning(
            "SECURITY_RBAC_DENIED UserId={UserId} TenantId={TenantId} Path={Path} Method={Method} RequiredPermissions={Permissions} CorrelationId={CorrelationId}",
            userId,
            tenantId,
            context.HttpContext.Request.Path.Value,
            context.HttpContext.Request.Method,
            string.Join(",", _permissions),
            correlationId);
    }
}
