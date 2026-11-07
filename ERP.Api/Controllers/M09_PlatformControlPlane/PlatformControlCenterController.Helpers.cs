using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    private static readonly HashSet<string> AllowedDowngradePreviewRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "BillingAdmin",
        "PlatformBillingAdmin",
        "SupportAdmin",
        "PlatformSupportAgent",
        "SecurityAuditor",
        "PlatformSecurityAuditor",
        "SupportReadOnly",
        "PlatformReadOnly",
        "Viewer"
    };

    private bool IsTrueCallerSuperAdmin(out string trueRole)
    {
        trueRole = (User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty).Trim();
        return AppRoles.IsSuperAdminPlatformRole(trueRole)
            || string.Equals(trueRole, AppRoles.Admin, StringComparison.OrdinalIgnoreCase);
    }

    private (string EffectiveRole, bool IsDowngradedPreview) ResolveEffectivePlatformRoleWithPreview()
    {
        var isSuperAdmin = IsTrueCallerSuperAdmin(out var trueRole);
        var previewRaw = HttpContext?.Request?.Headers["X-Platform-Role-Preview"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(previewRaw))
        {
            previewRaw = HttpContext?.Request?.Headers["X-Platform-Preview-Role"].FirstOrDefault();
        }

        var previewRole = previewRaw?.Trim() ?? string.Empty;
        if (isSuperAdmin && !string.IsNullOrWhiteSpace(previewRole) && AllowedDowngradePreviewRoles.Contains(previewRole))
        {
            return (previewRole, true);
        }

        if (isSuperAdmin)
        {
            return ("SuperAdmin", false);
        }

        return (string.IsNullOrWhiteSpace(trueRole) ? "SupportReadOnly" : trueRole, false);
    }

    private string ResolveActivePlatformRole() => ResolveEffectivePlatformRoleWithPreview().EffectiveRole;

    private IActionResult? EnsureCallerHasPlatformPermission(params string[] acceptedPermissions)
    {
        var principal = User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var (effectiveRole, isDowngradedPreview) = ResolveEffectivePlatformRoleWithPreview();
        if (effectiveRole is "SupportReadOnly" or "PlatformReadOnly" or "Viewer")
        {
            var readOnlyPerms = ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole(effectiveRole);
            if (!acceptedPermissions.Any(p => readOnlyPerms.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    code = "PLATFORM_ROLE_READONLY_FORBIDDEN",
                    errorCode = "PLATFORM_ROLE_READONLY_FORBIDDEN",
                    message = $"Vai trò '{effectiveRole}' chỉ có quyền xem (Read-Only), không được phép thực hiện thao tác thay đổi dữ liệu."
                });
            }
            return null;
        }

        if (!isDowngradedPreview)
        {
            var permissionClaims = principal.Claims
                .Where(c => c.Type is "permission" or "permissions")
                .Select(c => c.Value)
                .ToList();
            if (permissionClaims.Any(c => acceptedPermissions.Contains(c, StringComparer.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (permissionClaims.Count == 0 && IsTrueCallerSuperAdmin(out _))
            {
                return null;
            }
        }

        var roleDefaultPerms = ERP.Domain.Security.AppPermissions.GetDefaultPermissionsForRole(effectiveRole);
        if (acceptedPermissions.Any(p => roleDefaultPerms.Contains(p, StringComparer.OrdinalIgnoreCase)))
        {
            return null;
        }

        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            code = "PLATFORM_PERMISSION_REQUIRED",
            errorCode = "PLATFORM_PERMISSION_REQUIRED",
            message = $"Tài khoản quản trị vai trò '{effectiveRole}' không có quyền thực hiện thao tác này ({string.Join(" / ", acceptedPermissions)})."
        });
    }

    private string ResolveActorUsername()
    {
        var claimName = User?.FindFirstValue(ClaimTypes.Name)?.Trim() ?? User?.Identity?.Name?.Trim();
        if (!string.IsNullOrWhiteSpace(claimName))
        {
            return claimName;
        }

        var uid = GetActorUserId();
        if (uid.HasValue)
        {
            var dbUsername = db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == uid.Value)
                .Select(u => u.Username)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(dbUsername))
            {
                return dbUsername.Trim();
            }
        }

        return "platform_operator";
    }

    private (string BankCode, string BankName, string BankBin, string AccountNo, string AccountName, string DisplayAccountVi) ResolveVietQrBeneficiary()
    {
        var cfg = configuration ?? HttpContext?.RequestServices?.GetService<IConfiguration>();
        var (bankCode, bankName, bankBin, accountNo) = ERP.Infrastructure.Services.SaasBillingService.ResolveVietQrBankConfig(cfg);
        var accountName = ERP.Infrastructure.Services.SaasBillingService.ResolveVietQrAccountName(cfg);
        var displayVi = $"{bankName} • BIN {bankBin} • STK: {accountNo} ({accountName})";
        return (bankCode, bankName, bankBin, accountNo, accountName, displayVi);
    }

    private static string? CleanVietQrSetting(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        return trimmed.Contains("__SET_IN_", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed;
    }

    private static DateTime ConvertUtcToVietnamLocal(DateTime utcDateTime)
    {
        var normalizedUtc = utcDateTime.Kind == DateTimeKind.Utc
            ? utcDateTime
            : DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        foreach (var tzId in new[] { "SE Asia Standard Time", "Asia/Ho_Chi_Minh" })
        {
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, TimeZoneInfo.FindSystemTimeZoneById(tzId));
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return normalizedUtc.AddHours(7);
    }

    private static bool TryParseBankCreditTimestamp(
        string? rawCreditTs,
        out DateTime parsedCreditUtc,
        out string creditTimestampVi)
    {
        parsedCreditUtc = default;
        creditTimestampVi = string.Empty;
        if (string.IsNullOrWhiteSpace(rawCreditTs))
        {
            return false;
        }

        var trimmed = rawCreditTs.Trim();
        var nowVn = ConvertUtcToVietnamLocal(DateTime.UtcNow);
        if (DateTime.TryParseExact(
            trimmed,
            ["dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy HH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedVn))
        {
            if (parsedVn > nowVn.AddMinutes(1))
            {
                return false;
            }

            parsedCreditUtc = DateTime.SpecifyKind(parsedVn.AddHours(-7), DateTimeKind.Utc);
            creditTimestampVi = parsedVn.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            return true;
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            && dto.UtcDateTime <= DateTime.UtcNow.AddMinutes(1))
        {
            parsedCreditUtc = dto.UtcDateTime;
            creditTimestampVi = ConvertUtcToVietnamLocal(dto.UtcDateTime).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static (string? SuspendedBy, DateTime? SuspendedAtUtc, string? ReasonCategory) ExtractSuspensionAuditMetadata(AuditLog? log)
    {
        if (log is null || string.IsNullOrWhiteSpace(log.NewValues))
        {
            return (null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(log.NewValues);
            var root = doc.RootElement;
            var suspendedBy = root.TryGetProperty("suspendedBy", out var byProp) ? byProp.GetString() : null;
            var reasonCategory = root.TryGetProperty("reasonCategory", out var catProp) ? catProp.GetString() : null;
            DateTime? suspendedAtUtc = root.TryGetProperty("suspendedAtUtc", out var atProp) && atProp.TryGetDateTime(out var parsedAt) ? parsedAt : null;
            return (suspendedBy, suspendedAtUtc, reasonCategory);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static (string? ContractReference, string? EffectiveUntilDate) ExtractQuotaAuditMetadata(AuditLog? log)
    {
        if (log is null || string.IsNullOrWhiteSpace(log.NewValues))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(log.NewValues);
            var root = doc.RootElement;
            return (
                root.TryGetProperty("contractReference", out var refProp) ? refProp.GetString() : null,
                root.TryGetProperty("effectiveUntilDate", out var untilProp) ? untilProp.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string FormatSuspendedAtVi(DateTime? suspendedAtUtc, DateTime nowUtc)
    {
        if (!suspendedAtUtc.HasValue)
        {
            return "Vừa xong";
        }

        var utcVal = DateTime.SpecifyKind(suspendedAtUtc.Value, DateTimeKind.Utc);
        return (nowUtc - utcVal).TotalMinutes <= 5
            ? "Vừa xong"
            : ConvertUtcToVietnamLocal(utcVal).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
    }

    private string ResolveCorrelationId() =>
        HttpContext?.TraceIdentifier is { Length: > 0 } traceId ? traceId : $"CORR-PLATFORM-{Guid.NewGuid():N}"[..26];

    private int? GetActorUserId() =>
        int.TryParse(User?.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) && uid > 0 ? uid : null;
}
