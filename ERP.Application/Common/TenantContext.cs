using ERP.Application.Interfaces;

namespace ERP.Application.Common;

public sealed record TenantContext(
    int TenantId = TenantContext.DefaultTenantId,
    string TenantCode = TenantContext.DefaultTenantCode,
    bool IsSystemBypass = false) : ITenantContext
{
    public const int DefaultTenantId = 1;
    public const string DefaultTenantCode = "DEFAULT";

    public static TenantContext Default { get; } = new(DefaultTenantId, DefaultTenantCode, false);
    public static TenantContext SystemBypass { get; } = new(DefaultTenantId, "SYSTEM", true);

    public static TenantContext ForTenant(int tenantId, string? tenantCode = null) =>
        new(
            tenantId > 0 ? tenantId : throw new ArgumentOutOfRangeException(nameof(tenantId), "TenantId must be positive."),
            string.IsNullOrWhiteSpace(tenantCode) ? $"TENANT-{tenantId}" : tenantCode.Trim(),
            false);
}
