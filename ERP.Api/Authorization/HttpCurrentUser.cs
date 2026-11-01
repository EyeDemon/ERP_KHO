using System.Security.Claims;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Authorization;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    internal const string EffectiveRoleKey = "DatabaseAuthorization.Role";
    internal const string EffectivePermissionsCacheKey = "DatabaseAuthorization.Permissions";
    internal const string CachedPermissionsKey = "DatabaseAuthorization.CachedPermissionSet";

    private sealed record CachedRolePermissions(
        bool HasDatabaseRolePermissions,
        HashSet<string> GrantedCodes);

    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw new UnauthorizedAccessException("Không xác định được danh tính người dùng.");

    public int TenantId => IsAuthenticated
        && int.TryParse(User.FindFirstValue(TenantClaimTypes.TenantId), out var tenantId)
        && tenantId > 0
        ? tenantId
        : 1;

    public bool IsGlobalAdmin => string.Equals(Role, AppRoles.Admin, StringComparison.Ordinal);
    public string Role => httpContextAccessor.HttpContext?.Items[EffectiveRoleKey] as string
        ?? User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public bool HasPermission(string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
        {
            return false;
        }

        var normalizedCode = permissionCode.Trim();
        var effectiveRole = Role;
        var hasExplicitClaim = User.Claims.Any(c =>
            (c.Type is "permission" or "permissions")
            && string.Equals(c.Value, normalizedCode, StringComparison.OrdinalIgnoreCase));

        var isRestrictedRole =
            (string.Equals(effectiveRole, AppRoles.WarehouseStaff, StringComparison.OrdinalIgnoreCase)
             || string.Equals(effectiveRole, AppRoles.Viewer, StringComparison.OrdinalIgnoreCase))
            && !string.Equals(effectiveRole, AppRoles.Admin, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(effectiveRole, AppRoles.Manager, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(effectiveRole, "Accountant", StringComparison.OrdinalIgnoreCase);

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            if (httpContext.Items.TryGetValue(CachedPermissionsKey, out var preloadedObj) &&
                preloadedObj is IReadOnlySet<string> preloadedSet)
            {
                if (preloadedSet.Contains(normalizedCode))
                {
                    return true;
                }

                if (!IsGlobalAdmin && !hasExplicitClaim)
                {
                    return false;
                }
            }

            if (httpContext.Items[EffectivePermissionsCacheKey] is not CachedRolePermissions cached)
            {
                cached = LoadRolePermissionsFromDb(httpContext, effectiveRole);
                httpContext.Items[EffectivePermissionsCacheKey] = cached;
                if (cached.HasDatabaseRolePermissions)
                {
                    httpContext.Items[CachedPermissionsKey] = cached.GrantedCodes;
                }
            }

            if (cached.HasDatabaseRolePermissions)
            {
                if (cached.GrantedCodes.Contains(normalizedCode))
                {
                    return true;
                }

                if (!IsGlobalAdmin)
                {
                    return false;
                }
            }
        }

        if (isRestrictedRole
            && (string.Equals(normalizedCode, ERP.Domain.Security.AppPermissions.InventoryCostRead, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedCode, ERP.Domain.Security.AppPermissions.InventoryOpeningBalanceImport, StringComparison.OrdinalIgnoreCase))
            && !hasExplicitClaim)
        {
            return false;
        }

        if (IsGlobalAdmin || hasExplicitClaim)
        {
            return true;
        }

        return ERP.Domain.Security.AppPermissions
            .GetDefaultPermissionsForRole(effectiveRole)
            .Contains(normalizedCode, StringComparer.OrdinalIgnoreCase);
    }

    private CachedRolePermissions LoadRolePermissionsFromDb(HttpContext httpContext, string effectiveRole)
    {
        var db = httpContext.RequestServices?.GetService<ErpKhoDbContext>();
        if (db is null)
        {
            return new CachedRolePermissions(false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        try
        {
            var roleId = 0;
            if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) && uid > 0)
            {
                roleId = db.Users
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(u => u.Id == uid && u.IsActive)
                    .Select(u => u.RoleId)
                    .FirstOrDefault();
            }

            if (roleId <= 0 && !string.IsNullOrWhiteSpace(effectiveRole))
            {
                roleId = db.Roles
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(r => r.RoleName == effectiveRole)
                    .Select(r => r.Id)
                    .FirstOrDefault();
            }

            if (roleId <= 0 || !db.RolePermissions.IgnoreQueryFilters().AsNoTracking().Any())
            {
                return new CachedRolePermissions(false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            var codes = db.RolePermissions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(rp => rp.RoleId == roleId && rp.Permission != null && rp.Permission.Code != null)
                .Select(rp => rp.Permission.Code.Trim())
                .ToList();

            return new CachedRolePermissions(
                true,
                new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return new CachedRolePermissions(false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }
}
