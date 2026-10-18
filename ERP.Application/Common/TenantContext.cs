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
            string.IsNullOrWhiteSpace(tenantCode) ? FormatTenantCode(tenantId) : tenantCode.Trim(),
            false);

    public static string FormatTenantCode(int tenantId) =>
        tenantId <= DefaultTenantId ? DefaultTenantCode : $"TENANT-{tenantId}";

    public static bool TryParseTenantCode(string? rawTenantCode, out int tenantId)
    {
        tenantId = 0;
        if (string.IsNullOrWhiteSpace(rawTenantCode))
        {
            return false;
        }

        var normalized = rawTenantCode.Trim();
        if (string.Equals(normalized, DefaultTenantCode, StringComparison.OrdinalIgnoreCase))
        {
            tenantId = DefaultTenantId;
            return true;
        }

        if (int.TryParse(normalized, out var directId) && directId > 0)
        {
            tenantId = directId;
            return true;
        }

        foreach (var prefix in new[] { "TENANT-", "TENANT_", "TENANT", "T-", "T_" })
        {
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(normalized[prefix.Length..].Trim(), out var parsedId)
                && parsedId > 0)
            {
                tenantId = parsedId;
                return true;
            }
        }

        return false;
    }

    public static (string Username, int? TenantId, bool HasInvalidTenantSpecifier) ResolveLoginIdentifier(
        string rawUsername,
        int? explicitTenantId = null,
        string? explicitTenantCode = null)
    {
        var username = (rawUsername ?? string.Empty).Trim();
        int? resolvedTenantId = null;

        if (explicitTenantId.HasValue)
        {
            if (explicitTenantId.Value <= 0)
            {
                return (username, null, true);
            }

            resolvedTenantId = explicitTenantId.Value;
        }

        if (!string.IsNullOrWhiteSpace(explicitTenantCode))
        {
            if (!TryParseTenantCode(explicitTenantCode, out var codeTenantId))
            {
                return (username, null, true);
            }

            if (resolvedTenantId.HasValue && resolvedTenantId.Value != codeTenantId)
            {
                return (username, null, true);
            }

            resolvedTenantId = codeTenantId;
        }

        var slashIndex = username.IndexOf('\\');
        if (slashIndex > 0 && slashIndex < username.Length - 1)
        {
            var prefix = username[..slashIndex].Trim();
            var account = username[(slashIndex + 1)..].Trim();
            if (!TryParseTenantCode(prefix, out var slashTenantId))
            {
                return (account, null, true);
            }

            if (resolvedTenantId.HasValue && resolvedTenantId.Value != slashTenantId)
            {
                return (account, null, true);
            }

            return (account, slashTenantId, false);
        }

        var atIndex = username.LastIndexOf('@');
        if (atIndex > 0 && atIndex < username.Length - 1)
        {
            var suffix = username[(atIndex + 1)..].Trim();
            if (TryParseTenantCode(suffix, out var atTenantId))
            {
                var account = username[..atIndex].Trim();
                if (resolvedTenantId.HasValue && resolvedTenantId.Value != atTenantId)
                {
                    return (account, null, true);
                }

                return (account, atTenantId, false);
            }
        }

        return (username, resolvedTenantId, false);
    }
}
