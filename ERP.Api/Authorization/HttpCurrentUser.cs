using System.Security.Claims;
using ERP.Application.Interfaces;

namespace ERP.Api.Authorization;

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    internal const string EffectiveRoleKey = "DatabaseAuthorization.Role";
    private ClaimsPrincipal User => httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id
        : throw new UnauthorizedAccessException("Không xác định được danh tính người dùng.");

    public bool IsGlobalAdmin => string.Equals(Role, AppRoles.Admin, StringComparison.Ordinal);
    public string Role => httpContextAccessor.HttpContext?.Items[EffectiveRoleKey] as string
        ?? User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
}
