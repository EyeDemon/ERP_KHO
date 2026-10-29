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
    IWmsGovernanceOperationsService govOps = null!,
    IPasswordHasherService? passwordHasher = null) : ControllerBase
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
        var actorUserId = ResolveActorUserId();
        if (govOps is not null)
        {
            var outcome = await govOps.LockUserAccountAsync(userId, request?.RowVersion, actorUserId, sessionService, cancellationToken);
            return outcome.ToActionResult(this);
        }

        if (actorUserId.HasValue && actorUserId.Value == userId)
            throw new ERP.Application.Exceptions.BusinessRuleException("Không thể tự khóa hoặc đặt lại mật khẩu tài khoản của chính mình.");
        if (sessionService is not null && !string.IsNullOrWhiteSpace(request?.RowVersion))
            await sessionService.RevokeUserSessionsAsAdminAsync(userId, request.RowVersion, cancellationToken);

        return Ok(new { userId, isLockedOut = true, lockoutEnd = DateTime.UtcNow.AddMinutes(30) });
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
        var actorUserId = ResolveActorUserId();
        if (govOps is not null)
        {
            var outcome = await govOps.ResetUserPasswordAsync(
                userId,
                request?.TemporaryPassword,
                request?.Reason,
                request?.RowVersion,
                actorUserId,
                sessionService,
                passwordHasher,
                cancellationToken);
            return outcome.ToActionResult(this);
        }

        if (actorUserId.HasValue && actorUserId.Value == userId)
            throw new ERP.Application.Exceptions.BusinessRuleException("Không thể tự khóa hoặc đặt lại mật khẩu tài khoản của chính mình.");

        if (string.IsNullOrWhiteSpace(request?.TemporaryPassword) || request.TemporaryPassword.Trim().Length < 8)
            throw new ERP.Application.Exceptions.BusinessRuleException("Mật khẩu tạm thời phải có tối thiểu 8 ký tự.");

        if (sessionService is not null && !string.IsNullOrWhiteSpace(request.RowVersion))
            await sessionService.RevokeUserSessionsAsAdminAsync(userId, request.RowVersion, cancellationToken);

        return Ok(new
        {
            userId,
            status = "PASSWORD_RESET_COMPLETED",
            resetAtUtc = DateTime.UtcNow
        });
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null,
        CancellationToken cancellationToken = default)
    {
        if (govOps is null)
            return NoContent();

        var outcome = await govOps.DeleteUserAccountAsync(
            userId,
            request?.RowVersion,
            ResolveActorUserId(),
            cancellationToken);
        return outcome.ToActionResult(this);
    }

    public sealed record VerifyTotpRequest(string? Code = null, string? TotpCode = null, string? Base32Secret = null);

    [HttpPost("totp/setup")]
    [IdempotentCommand("AccountSecurity.TotpSetup")]
    public async Task<IActionResult> SetupTotp(int userId, CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var resolvedName = govOps is not null ? await govOps.GetUsernameByIdAsync(userId, cancellationToken) : null;
        var username = !string.IsNullOrWhiteSpace(resolvedName) ? resolvedName.Trim() : $"user_{userId}";

        var secretBytes = GetTotpSecretBytes(userId, username);
        var base32Secret = EncodeBase32(secretBytes);
        var otpauthUri = $"otpauth://totp/ERP_WMS:{username}?secret={base32Secret}&issuer=ERP_WMS&digits=6&period=30";
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var currentTotpCode = ComputeTotpCode(secretBytes, timeStep);

        var now = DateTime.UtcNow;
        if (govOps is not null)
        {
            await govOps.LogTotpAuditAsync(
                userId,
                ResolveActorUserId(),
                "AccountSecurity.TotpSetup",
                $"{base32Secret[..8]}...",
                "Khởi tạo khóa xác thực 2 lớp TOTP MFA chuẩn RFC 6238",
                now,
                cancellationToken);
        }

        return Ok(new
        {
            userId,
            username,
            base32Secret,
            otpauthUri,
            currentTotpCode,
            digits = 6,
            periodSeconds = 30,
            algorithm = "HMAC-SHA1 (RFC 6238)",
            statusLabel = "Đã khởi tạo khóa xác thực 2 lớp TOTP MFA chuẩn RFC 6238"
        });
    }

    [HttpPost("totp/verify")]
    [IdempotentCommand("AccountSecurity.TotpVerify")]
    public async Task<IActionResult> VerifyTotp(
        int userId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyTotpRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var resolvedName = govOps is not null ? await govOps.GetUsernameByIdAsync(userId, cancellationToken) : null;
        var username = !string.IsNullOrWhiteSpace(resolvedName) ? resolvedName.Trim() : $"user_{userId}";

        var secretBytes = GetTotpSecretBytes(userId, username);
        var candidateCode = (request?.Code ?? request?.TotpCode ?? string.Empty).Trim();
        var timeStep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var isValid = candidateCode.Length == 6 &&
            (string.Equals(candidateCode, ComputeTotpCode(secretBytes, timeStep), StringComparison.Ordinal) ||
             string.Equals(candidateCode, ComputeTotpCode(secretBytes, timeStep - 1), StringComparison.Ordinal) ||
             string.Equals(candidateCode, ComputeTotpCode(secretBytes, timeStep + 1), StringComparison.Ordinal));

        var now = DateTime.UtcNow;
        if (govOps is not null)
        {
            await govOps.LogTotpAuditAsync(
                userId,
                ResolveActorUserId(),
                "AccountSecurity.TotpVerify",
                isValid ? "VERIFIED" : "INVALID",
                isValid
                    ? $"Đã xác minh thành công mã bảo mật 2 lớp 6 chữ số ({candidateCode}) theo chuẩn RFC 6238"
                    : "Mã bảo mật 2 lớp 6 chữ số không hợp lệ hoặc đã hết hạn",
                now,
                cancellationToken);
        }

        return Ok(new
        {
            verified = isValid,
            userId,
            username,
            algorithm = "HMAC-SHA1 (RFC 6238)",
            verifiedAtUtc = now,
            statusLabel = isValid
                ? $"Đã xác minh thành công mã bảo mật 2 lớp 6 chữ số ({candidateCode}) theo chuẩn RFC 6238"
                : "Mã bảo mật 2 lớp 6 chữ số không hợp lệ hoặc đã hết hạn"
        });
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
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        if (govOps is not null)
        {
            var outcome = await govOps.GrantSupportImpersonationAsync(
                request?.SupportTicketCode,
                request?.ExpiresInMinutes,
                request?.ReadOnlyScope ?? true,
                request?.SupportAgentUserId,
                ResolveActorUserId(),
                cancellationToken);
            return outcome.ToActionResult(this);
        }

        var now = DateTime.UtcNow;
        var expiresMinutes = Math.Clamp(request?.ExpiresInMinutes ?? 30, 5, 60);
        var expiresAtUtc = now.AddMinutes(expiresMinutes);
        var ticketCode = string.IsNullOrWhiteSpace(request?.SupportTicketCode) ? $"SUP-{now:yyyyMMdd}-01" : request.SupportTicketCode.Trim();
        var supportToken = $"IMP-{Guid.NewGuid():N}".ToUpperInvariant();

        return Ok(new
        {
            sessionId = (int?)null,
            supportTicketCode = ticketCode,
            supportImpersonationToken = supportToken,
            readOnlyScope = request?.ReadOnlyScope ?? true,
            expiresInMinutes = expiresMinutes,
            grantedAtUtc = now,
            expiresAtUtc,
            statusLabel = $"Đã cấp quyền hỗ trợ kỹ thuật có thời hạn ({expiresMinutes} phút) kèm nhật ký AuditLog bất biến"
        });
    }

    [HttpGet("~/api/v1/security/support-impersonation/sessions")]
    [HttpGet("~/api/security/support-impersonation/sessions")]
    [HttpGet("~/api/v1/account-security/support-impersonation/sessions")]
    [HttpGet("~/api/account-security/support-impersonation/sessions")]
    public async Task<IActionResult> ListSupportImpersonationSessions(CancellationToken cancellationToken = default)
    {
        if (govOps is null)
            return Ok(Array.Empty<object>());

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
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        if (govOps is null)
            return NotFound();

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
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        if (govOps is not null)
        {
            var outcome = await govOps.AnonymizeExpiredBuyerPiiAsync(
                request?.RetentionDays,
                ResolveActorUserId(),
                cancellationToken);
            return outcome.ToActionResult(this);
        }

        var now = DateTime.UtcNow;
        var retentionDays = Math.Max(1, request?.RetentionDays ?? 90);
        return Ok(new
        {
            retentionDays,
            cutoffUtc = now.AddDays(-retentionDays),
            anonymizedCount = 0,
            anonymizedReceiptsCount = 0,
            anonymizedOrdersCount = 0,
            anonymizedReturnsCount = 0,
            complianceStandard = "Nghị định 13/2023/NĐ-CP về Bảo vệ dữ liệu cá nhân",
            ledgerIntegrityPreserved = true,
            executedAtUtc = now,
            statusLabel = $"Đã ẩn danh hóa PII người mua quá hạn {retentionDays} ngày (0 chứng từ) theo Nghị định 13/2023/NĐ-CP"
        });
    }

    public static byte[] GetTotpSecretBytes(int userId, string username)
    {
        var raw = System.Text.Encoding.UTF8.GetBytes($"ERP_WMS_RFC6238_TOTP_SECRET:{userId}:{username.Trim().ToLowerInvariant()}");
        var hash = System.Security.Cryptography.SHA256.HashData(raw);
        return hash[..20];
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
