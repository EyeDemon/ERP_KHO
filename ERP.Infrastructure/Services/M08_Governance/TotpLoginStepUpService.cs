using System.Security.Cryptography;
using System.Text.Json;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class TotpLoginStepUpService(
    ErpKhoDbContext db,
    IDataProtectionProvider? dataProtectionProvider = null,
    TimeProvider? timeProvider = null) : ITotpLoginVerifier
{
    private readonly HashSet<int> _verifiedUserIdsInScope = [];

    public bool HasAlreadyVerifiedInCurrentScope(int userId) =>
        userId > 0 && _verifiedUserIdsInScope.Contains(userId);

    public async Task<bool> VerifyLoginSecondFactorAsync(
        User user,
        string? totpCode,
        string? recoveryCode,
        CancellationToken cancellationToken = default)
    {
        if (user is null) return false;
        var outcome = await ValidateLoginSecondFactorAsync(
            user.Username,
            totpCode,
            recoveryCode,
            correlationId: null,
            cancellationToken);
        if (outcome.Allowed && user.Id > 0)
        {
            _verifiedUserIdsInScope.Add(user.Id);
        }
        return outcome.Allowed;
    }
    public async Task<(bool Allowed, int StatusCode, string ErrorCode, string Message)> ValidateLoginSecondFactorAsync(
        string username, string? totpCode, string? recoveryCode, string? correlationId, CancellationToken ct = default)
    {
        var trimmedUsername = username?.Trim() ?? string.Empty;
        if (trimmedUsername.Length == 0) return (true, 200, "", "");

        var user = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == trimmedUsername && u.IsActive, ct);
        if (user is null) return (true, 200, "", "");

        var latestTotpAudit = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => (a.EntityId == user.Id || a.UserId == user.Id) && a.EntityName == "AccountSecurityTotp" &&
                        (a.Action == "ACCOUNT_TOTP_VERIFIED" || a.Action == "ACCOUNT_TOTP_ENROLLED" ||
                         a.Action == "ACCOUNT_TOTP_DISABLED" || a.Action == "ACCOUNT_TOTP_RESET"))
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

        var usesAuditState = latestTotpAudit is not null;
        string? encryptedSecret;
        List<string> recoveryCodeHashes;
        long? lastVerifiedStep = null;
        long? lastLoginStep = null;

        if (usesAuditState)
        {
            if (!TryParseAuditTotpState(latestTotpAudit!.NewValues, out var status, out encryptedSecret, out recoveryCodeHashes, out lastVerifiedStep, out lastLoginStep) ||
                !string.Equals(status, "ACTIVE", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(encryptedSecret))
                return (true, 200, "", "");
        }
        else
        {
            if (!string.Equals(user.TotpEnrollmentStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(user.TotpSecretProtected))
                return (true, 200, "", "");

            encryptedSecret = user.TotpSecretProtected;
            recoveryCodeHashes = ParseJsonStringList(user.TotpRecoveryCodeHashesJson);
            var latestStepMarker = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(a => (a.EntityId == user.Id || a.UserId == user.Id) &&
                            (a.Action == "ACCOUNT_TOTP_LOGIN_PASSED" || a.Action == "AccountSecurity.TotpVerify") &&
                            a.OldValues != null && a.OldValues.StartsWith("step:"))
                .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
                .Select(a => a.OldValues).FirstOrDefaultAsync(ct);
            if (latestStepMarker is not null && long.TryParse(latestStepMarker.AsSpan(5), out var parsedStep))
                lastLoginStep = parsedStep;
        }

        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var nowUtc = utcNow.UtcDateTime;

        var failedCount = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(a => (a.EntityId == user.Id || a.UserId == user.Id) &&
                             a.Action == "ACCOUNT_TOTP_LOGIN_FAILED" && a.Timestamp >= nowUtc.AddMinutes(-10), ct);
        if (failedCount >= 5)
            return (false, 429, "TWO_FACTOR_LOCKED_OUT", "Tài khoản tạm khóa đăng nhập 2 lớp 10 phút do nhập sai mã xác thực quá 5 lần.");

        if (string.IsNullOrWhiteSpace(totpCode) && string.IsNullOrWhiteSpace(recoveryCode))
            return (false, 401, "TWO_FACTOR_REQUIRED", "Tài khoản đã bật bảo mật 2 lớp (TOTP). Vui lòng nhập mã xác thực 6 chữ số hoặc mã khôi phục.");

        byte[] secretBytes;
        try
        {
            secretBytes = WmsGovernanceOperationsService.DecodeTotpBase32(UnprotectSecret(encryptedSecret!));
        }
        catch (CryptographicException)
        {
            return (false, 401, "INVALID_TWO_FACTOR_CODE", "Mã xác thực 2 lớp (TOTP) hoặc mã khôi phục không hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(totpCode))
        {
            var cleanOtp = totpCode.Trim();
            var currentStep = utcNow.ToUnixTimeSeconds() / 30;
            long? matchedStep = null;
            if (secretBytes.Length >= 10 && cleanOtp.Length == 6)
            {
                foreach (var step in new[] { currentStep, currentStep - 1, currentStep + 1 })
                {
                    if (string.Equals(cleanOtp, WmsGovernanceOperationsService.ComputeRfc6238TotpCode(secretBytes, step), StringComparison.Ordinal))
                    {
                        matchedStep = step;
                        break;
                    }
                }
            }

            if (matchedStep.HasValue && matchedStep.Value != lastVerifiedStep && matchedStep.Value != lastLoginStep)
            {
                if (usesAuditState)
                    db.AuditLogs.Add(BuildStateAuditLog(user, encryptedSecret!, recoveryCodeHashes, matchedStep.Value, matchedStep.Value, correlationId, nowUtc));
                db.AuditLogs.Add(BuildLoginOutcomeAudit(user, "ACCOUNT_TOTP_LOGIN_PASSED", $"step:{matchedStep.Value}", "{\"result\":\"PASSED\"}", "Success", correlationId, nowUtc));
                await db.SaveChangesAsync(ct);
                return (true, 200, "", "");
            }
        }
        else if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            var candidateHash = WmsGovernanceOperationsService.HashRecoveryCode(NormalizeRecoveryCode(recoveryCode));
            var matchedIndex = recoveryCodeHashes.FindIndex(h => string.Equals(h, candidateHash, StringComparison.OrdinalIgnoreCase));
            if (matchedIndex >= 0)
            {
                recoveryCodeHashes.RemoveAt(matchedIndex);
                if (usesAuditState)
                {
                    db.AuditLogs.Add(BuildStateAuditLog(user, encryptedSecret!, recoveryCodeHashes, lastVerifiedStep, lastLoginStep, correlationId, nowUtc));
                }
                else
                {
                    var trackedUser = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == user.Id, ct);
                    if (trackedUser is not null) trackedUser.TotpRecoveryCodeHashesJson = JsonSerializer.Serialize(recoveryCodeHashes);
                }

                db.AuditLogs.Add(BuildLoginOutcomeAudit(user, "ACCOUNT_TOTP_LOGIN_PASSED", null, "{\"result\":\"RECOVERY_CODE_CONSUMED\"}", "Success", correlationId, nowUtc));
                await db.SaveChangesAsync(ct);
                return (true, 200, "", "");
            }
        }

        db.AuditLogs.Add(BuildLoginOutcomeAudit(user, "ACCOUNT_TOTP_LOGIN_FAILED", null, "{\"result\":\"FAILED\"}", "Failed", correlationId, nowUtc));
        await db.SaveChangesAsync(ct);
        return (false, 401, "INVALID_TWO_FACTOR_CODE", "Mã xác thực 2 lớp (TOTP) hoặc mã khôi phục không hợp lệ.");
    }

    private string UnprotectSecret(string protectedPayload)
    {
        var provider = dataProtectionProvider ?? WmsGovernanceOperationsService.LazyFallbackDataProtectionProvider.Value;
        try { return provider.CreateProtector(WmsGovernanceOperationsService.AccountSecurityTotpPurpose).Unprotect(protectedPayload); }
        catch (CryptographicException) { return provider.CreateProtector(WmsGovernanceOperationsService.LegacyUserTotpPurpose).Unprotect(protectedPayload); }
    }

    private static string NormalizeRecoveryCode(string raw)
    {
        var clean = raw.Trim().Replace(" ", "").ToUpperInvariant();
        return clean.Length == 8 && !clean.Contains('-') ? $"{clean[..4]}-{clean[4..8]}" : clean;
    }

    private static AuditLog BuildLoginOutcomeAudit(
        User user, string action, string? oldValues, string newValues, string result, string? correlationId, DateTime nowUtc) =>
        new()
        {
            TenantId = user.TenantId, UserId = user.Id, EntityName = "AccountSecurityTotp", EntityId = user.Id,
            Action = action, OldValues = oldValues, NewValues = newValues, CorrelationId = correlationId, Result = result, Timestamp = nowUtc
        };

    private static AuditLog BuildStateAuditLog(
        User user, string encryptedSecret, List<string> hashes, long? lastVerified, long? lastLogin, string? correlationId, DateTime nowUtc) =>
        new()
        {
            TenantId = user.TenantId, UserId = user.Id, EntityName = "AccountSecurityTotp", EntityId = user.Id,
            Action = "ACCOUNT_TOTP_VERIFIED",
            NewValues = JsonSerializer.Serialize(new { status = "ACTIVE", encryptedSecret, recoveryCodeHashes = hashes, lastVerifiedTimeStep = lastVerified, lastLoginTimeStep = lastLogin }),
            CorrelationId = correlationId, Result = "Success", Timestamp = nowUtc
        };

    private static List<string> ParseJsonStringList(string? json) =>
        string.IsNullOrWhiteSpace(json) ? [] : (JsonSerializer.Deserialize<List<string>>(json) ?? []);

    private static bool TryParseAuditTotpState(
        string? json, out string? status, out string? secret, out List<string> hashes, out long? lastVerified, out long? lastLogin)
    {
        (status, secret, hashes, lastVerified, lastLogin) = (null, null, [], null, null);
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            status = root.TryGetProperty("status", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            secret = root.TryGetProperty("encryptedSecret", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            if (root.TryGetProperty("recoveryCodeHashes", out var r) && r.ValueKind == JsonValueKind.Array)
                hashes = r.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
            if (root.TryGetProperty("lastVerifiedTimeStep", out var lv) && lv.TryGetInt64(out var vStep)) lastVerified = vStep;
            if (root.TryGetProperty("lastLoginTimeStep", out var ll) && ll.TryGetInt64(out var lStep)) lastLogin = lStep;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
