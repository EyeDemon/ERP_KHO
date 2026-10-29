using System.Security.Claims;
using ERP.Application.Common;
using ERP.Application.Interfaces;

namespace ERP.Api.Authorization;

/// <summary>
/// Resolves the active tenant strictly from cryptographically verified JWT claims.
/// Unsigned HTTP headers (e.g. X-Tenant-Id), query parameters, or request body fields
/// are NEVER trusted for standard requests to prevent Cross-Tenant Spoofing / IDOR.
/// </summary>
public sealed class HttpTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public int TenantId
    {
        get
        {
            var principal = User;
            if (principal?.Identity?.IsAuthenticated == true)
            {
                var rawTenantId = principal.FindFirstValue(TenantClaimTypes.TenantId);
                if (int.TryParse(rawTenantId, out var parsedTenantId) && parsedTenantId > 0)
                {
                    return parsedTenantId;
                }
            }

            return TenantContext.DefaultTenantId;
        }
    }

    public string TenantCode
    {
        get
        {
            var principal = User;
            if (principal?.Identity?.IsAuthenticated == true)
            {
                var claimCode = principal.FindFirstValue(TenantClaimTypes.TenantCode);
                if (!string.IsNullOrWhiteSpace(claimCode))
                {
                    return claimCode;
                }

                return $"TENANT-{TenantId}";
            }

            return TenantContext.DefaultTenantCode;
        }
    }

    public bool IsSystemBypass => false;
}
