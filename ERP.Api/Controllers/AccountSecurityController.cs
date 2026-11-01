using ERP.Api.Authorization;
using ERP.Application.Interfaces;
using ERP.Application.DTOs;
using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

public sealed record ResetPasswordAdminRequest(string? TemporaryPassword = null, string? Reason = null, string? RowVersion = null);

[ApiController]
[Route("api/users/{userId:int}/security")]
[Route("api/account-security/{userId:int}")]
[Route("api/v1/account-security/{userId:int}")]
[Authorize]
[PermissionAuthorize(AppPermissions.UserManage)]
public sealed class AccountSecurityController(
    IAccountAdminService service,
    IUserSessionService sessionService,
    IWmsGovernanceOperationsService govOps,
    IPasswordHasherService passwordHasher) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int userId, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(userId, cancellationToken));

    [HttpPost("lock")]
    [IdempotentCommand("User.Lock")]
    public async Task<IActionResult> Lock(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await govOps.LockUserAccountAsync(userId, request?.RowVersion, ResolveActorUserId(), sessionService, cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("unlock")]
    [IdempotentCommand("User.Unlock")]
    public async Task<IActionResult> Unlock(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null, CancellationToken cancellationToken = default)
    {
        await service.UnlockAsync(userId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("revoke-sessions")]
    [IdempotentCommand("User.RevokeSessions")]
    public async Task<IActionResult> RevokeSessions(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null, CancellationToken cancellationToken = default)
    {
        await sessionService.RevokeUserSessionsAsAdminAsync(userId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [IdempotentCommand("User.ResetPassword")]
    public async Task<IActionResult> ResetPassword(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ResetPasswordAdminRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await govOps.ResetUserPasswordAsync(
            userId,
            request?.TemporaryPassword,
            request?.Reason,
            request?.RowVersion,
            ResolveActorUserId(),
            sessionService,
            passwordHasher,
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var outcome = await govOps.DeleteUserAccountAsync(
            userId,
            request?.RowVersion,
            ResolveActorUserId(),
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    public sealed record VerifyTotpRequest(string? Code = null, string? TotpCode = null)
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; init; }

        public bool HasClientSuppliedSecret() =>
            ExtensionData != null &&
            ExtensionData.Any(kvp =>
                (string.Equals(kvp.Key, "base32Secret", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(kvp.Key, "secretKey", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(kvp.Key, "secretKeyBase32", StringComparison.OrdinalIgnoreCase)) &&
                kvp.Value.ValueKind == System.Text.Json.JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(kvp.Value.GetString()));
    }

    [HttpPost("totp/setup")]
    [IdempotentCommand("AccountSecurity.TotpSetup")]
    public async Task<IActionResult> SetupTotp(int userId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.SetupTotpAsync(userId, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/reveal-secret")]
    [IdempotentCommand("AccountSecurity.TotpRevealSecret")]
    public async Task<IActionResult> RevealTotpSecret(int userId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.RevealTotpSecretAsync(userId, cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/verify")]
    [IdempotentCommand("AccountSecurity.TotpVerify")]
    public async Task<IActionResult> VerifyTotp(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyTotpRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        if (request?.HasClientSuppliedSecret() == true)
        {
            return BadRequest(new
            {
                code = "TOTP_CLIENT_SECRET_REJECTED",
                errorCode = "TOTP_CLIENT_SECRET_REJECTED",
                verified = false,
                message = "Không chấp nhận khóa bí mật TOTP do client tự gửi lên; hệ thống chỉ đối chiếu với khóa đã lưu bảo mật trên máy chủ."
            });
        }

        var candidateCode = (request?.Code ?? request?.TotpCode ?? string.Empty).Trim();
        var outcome = await govOps.VerifyTotpAsync(userId, candidateCode, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/confirm-activation")]
    [IdempotentCommand("AccountSecurity.TotpConfirmActivation")]
    public async Task<IActionResult> ConfirmTotpActivation(int userId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.ConfirmTotpActivationAsync(userId, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/cancel")]
    [IdempotentCommand("AccountSecurity.TotpCancel")]
    public async Task<IActionResult> CancelTotpEnrollment(int userId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.CancelTotpEnrollmentAsync(userId, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/regenerate-recovery-codes")]
    [IdempotentCommand("AccountSecurity.TotpRegenerateRecoveryCodes")]
    public async Task<IActionResult> RegenerateRecoveryCodes(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyTotpRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var candidateCode = (request?.Code ?? request?.TotpCode ?? string.Empty).Trim();
        var outcome = await govOps.RegenerateRecoveryCodesAsync(userId, candidateCode, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    public sealed record GrantSupportImpersonationRequest(
        string? SupportTicketCode = null,
        int? ExpiresInMinutes = null,
        bool ReadOnlyScope = true,
        int? SupportAgentUserId = null);

    public sealed record AnonymizeBuyerPiiRequest(
        int? RetentionDays = null,
        int? WarehouseId = null);

    [HttpPost("~/api/v1/security/support-impersonation/grant")]
    [HttpPost("~/api/security/support-impersonation/grant")]
    [HttpPost("~/api/v1/account-security/support-impersonation/grant")]
    [HttpPost("~/api/account-security/support-impersonation/grant")]
    [IdempotentCommand("AccountSecurity.GrantSupportImpersonation")]
    public async Task<IActionResult> GrantSupportImpersonation(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] GrantSupportImpersonationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.GrantSupportImpersonationAsync(
            request?.SupportTicketCode,
            request?.ExpiresInMinutes,
            request?.ReadOnlyScope ?? true,
            request?.SupportAgentUserId,
            ResolveActorUserId(),
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpGet("~/api/v1/security/support-impersonation/sessions")]
    [HttpGet("~/api/security/support-impersonation/sessions")]
    [HttpGet("~/api/v1/account-security/support-impersonation/sessions")]
    [HttpGet("~/api/account-security/support-impersonation/sessions")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> ListSupportImpersonationSessions(CancellationToken cancellationToken = default)
    {
        var outcome = await govOps.ListSupportImpersonationSessionsAsync(cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("~/api/v1/security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/v1/account-security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/account-security/support-impersonation/{sessionId:int}/revoke")]
    [IdempotentCommand("AccountSecurity.RevokeSupportImpersonation")]
    public async Task<IActionResult> RevokeSupportImpersonation(int sessionId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.RevokeSupportImpersonationAsync(sessionId, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("~/api/v1/security/anonymize-expired-buyer-pii")]
    [HttpPost("~/api/security/anonymize-expired-buyer-pii")]
    [HttpPost("~/api/v1/account-security/anonymize-expired-buyer-pii")]
    [HttpPost("~/api/account-security/anonymize-expired-buyer-pii")]
    [HttpPost("~/api/v1/account-security/pdpa/anonymize-expired-pii")]
    [IdempotentCommand("AccountSecurity.AnonymizeExpiredBuyerPii")]
    public async Task<IActionResult> AnonymizeExpiredBuyerPii(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AnonymizeBuyerPiiRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.AnonymizeExpiredBuyerPiiAsync(
            request?.RetentionDays,
            ResolveActorUserId(),
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    public static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }
        if (bitsLeft > 0)
        {
            output.Append(alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }
        return output.ToString();
    }

    public static byte[] DecodeBase32(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = base32.Trim().Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant();
        if (clean.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var bytes = new List<byte>((clean.Length * 5) / 8);
        int buffer = 0;
        int bitsLeft = 0;
        foreach (var ch in clean)
        {
            var val = alphabet.IndexOf(ch);
            if (val < 0)
            {
                return Array.Empty<byte>();
            }
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }
        return bytes.ToArray();
    }

    public static string ComputeTotpCode(byte[] secretBytes, long timeStep)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, timeStep);
        var hmac = System.Security.Cryptography.HMACSHA1.HashData(secretBytes, counterBytes);
        var offset = hmac[^1] & 0x0F;
        var binaryCode =
            ((hmac[offset] & 0x7F) << 24) |
            ((hmac[offset + 1] & 0xFF) << 16) |
            ((hmac[offset + 2] & 0xFF) << 8) |
            (hmac[offset + 3] & 0xFF);
        var otp = binaryCode % 1_000_000;
        return otp.ToString("D6");
    }

    private int? ResolveActorUserId() =>
        int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;
}
