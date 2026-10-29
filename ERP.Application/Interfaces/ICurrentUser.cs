namespace ERP.Application.Interfaces;

public interface ICurrentUser
{
    int UserId { get; }
    int TenantId => 1;
    bool IsAuthenticated { get; }
    bool IsGlobalAdmin { get; }
    string Role => string.Empty;
    bool HasPermission(string permissionCode) =>
        IsGlobalAdmin || Role is "Admin" or "Manager" or "Accountant";
}
