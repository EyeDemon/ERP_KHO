using System.Linq;
using ERP.Domain.Security;

namespace ERP.Application.Interfaces;

public interface ICurrentUser
{
    int UserId { get; }
    int TenantId => 1;
    bool IsAuthenticated { get; }
    bool IsGlobalAdmin { get; }
    string Role => string.Empty;
    bool HasPermission(string permissionCode) =>
        IsAuthenticated &&
        (IsGlobalAdmin ||
         AppPermissions.GetDefaultPermissionsForRole(Role)
             .Contains(permissionCode, System.StringComparer.OrdinalIgnoreCase));
}
