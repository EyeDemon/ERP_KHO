namespace ERP.Application.Interfaces;

public static class TenantClaimTypes
{
    public const string TenantId = "tenant_id";
    public const string TenantCode = "tenant_code";
}

public interface ITenantContext
{
    int TenantId { get; }
    string TenantCode { get; }
    bool IsSystemBypass { get; }
}
