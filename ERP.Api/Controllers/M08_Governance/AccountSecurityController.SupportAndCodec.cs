using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

public sealed partial class AccountSecurityController
{
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
    [PermissionAuthorize(AppPermissions.UserManage)]
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
    [PermissionAuthorize(AppPermissions.UserManage)]
    public async Task<IActionResult> ListSupportImpersonationSessions(CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.ListSupportImpersonationSessionsAsync(cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("~/api/v1/security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/v1/account-security/support-impersonation/{sessionId:int}/revoke")]
    [HttpPost("~/api/account-security/support-impersonation/{sessionId:int}/revoke")]
    [PermissionAuthorize(AppPermissions.UserManage)]
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
    [PermissionAuthorize(AppPermissions.UserManage)]
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
}
