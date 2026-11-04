using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ERP.Api.Controllers;

public sealed record ResetPasswordAdminRequest(string? TemporaryPassword = null, string? Reason = null, string? RowVersion = null);

[ApiController]
[Route("api/users/{userId:int}/security")]
[Route("api/account-security/{userId:int}")]
[Route("api/v1/account-security/{userId:int}")]
[Authorize]
public sealed partial class AccountSecurityController(
    IAccountAdminService service,
    IUserSessionService sessionService,
    IWmsGovernanceOperationsService govOps,
    IPasswordHasherService passwordHasher) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.UserManage, AppPermissions.UserRead)]
    public async Task<IActionResult> Get(int userId, CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(userId, cancellationToken));

    [HttpPost("lock")]
    [PermissionAuthorize(AppPermissions.UserManage)]
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
    [PermissionAuthorize(AppPermissions.UserManage)]
    [IdempotentCommand("User.Unlock")]
    public async Task<IActionResult> Unlock(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null, CancellationToken cancellationToken = default)
    {
        await service.UnlockAsync(userId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("revoke-sessions")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    [IdempotentCommand("User.RevokeSessions")]
    public async Task<IActionResult> RevokeSessions(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] AccountSecurityCommandDto? request = null, CancellationToken cancellationToken = default)
    {
        await sessionService.RevokeUserSessionsAsAdminAsync(userId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [PermissionAuthorize(AppPermissions.UserManage)]
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
    [PermissionAuthorize(AppPermissions.UserManage)]
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

    public sealed record VerifyTotpRequest(string? Code = null, string? TotpCode = null, int? UserId = null)
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
    [PermissionAuthorize(AppPermissions.UserManage)]
    [IdempotentCommand("AccountSecurity.TotpSetup")]
    public async Task<IActionResult> SetupTotp(int userId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.SetupTotpAsync(userId, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/reveal-secret")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    [IdempotentCommand("AccountSecurity.TotpRevealSecret")]
    public async Task<IActionResult> RevealTotpSecret(int userId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.RevealTotpSecretAsync(userId, cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/verify")]
    [HttpPost("~/api/v1/account-security/totp/verify")]
    [HttpPost("~/api/account-security/totp/verify")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    [EnableRateLimiting("Login")]
    [IdempotentCommand("AccountSecurity.TotpVerify")]
    public async Task<IActionResult> VerifyTotp(
        int userId = 0,
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

        var effectiveUserId = userId > 0 ? userId : (request?.UserId ?? ResolveActorUserId() ?? 0);
        if (effectiveUserId <= 0)
        {
            return BadRequest(new { code = "INVALID_USER_ID", errorCode = "INVALID_USER_ID", message = "Không xác định được tài khoản cần xác thực TOTP." });
        }

        var candidateCode = (request?.Code ?? request?.TotpCode ?? string.Empty).Trim();
        var outcome = await govOps.VerifyTotpAsync(effectiveUserId, candidateCode, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/confirm-activation")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    [IdempotentCommand("AccountSecurity.TotpConfirmActivation")]
    public async Task<IActionResult> ConfirmTotpActivation(int userId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.ConfirmTotpActivationAsync(userId, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/cancel")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    [IdempotentCommand("AccountSecurity.TotpCancel")]
    public async Task<IActionResult> CancelTotpEnrollment(int userId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.UserManage))
            return Forbid();

        var outcome = await govOps.CancelTotpEnrollmentAsync(userId, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    [HttpPost("totp/regenerate-recovery-codes")]
    [PermissionAuthorize(AppPermissions.UserManage)]
    [EnableRateLimiting("Login")]
    [IdempotentCommand("AccountSecurity.TotpRegenerateRecoveryCodes")]
    public async Task<IActionResult> RegenerateRecoveryCodes(
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
                message = "Không chấp nhận khóa bí mật TOTP do client tự gửi lên."
            });
        }

        var candidateCode = (request?.Code ?? request?.TotpCode ?? string.Empty).Trim();
        var outcome = await govOps.RegenerateRecoveryCodesAsync(userId, candidateCode, ResolveActorUserId(), cancellationToken);
        return outcome.ToActionResult(this);
    }

    private int? ResolveActorUserId() =>
        int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;
}
