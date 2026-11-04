using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting("Login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var totpCode = !string.IsNullOrWhiteSpace(request.TotpCode)
                ? request.TotpCode.Trim()
                : HttpContext?.Request?.Headers["X-Totp-Code"].FirstOrDefault()?.Trim();
            var recoveryCode = !string.IsNullOrWhiteSpace(request.RecoveryCode)
                ? request.RecoveryCode.Trim()
                : HttpContext?.Request?.Headers["X-Recovery-Code"].FirstOrDefault()?.Trim();

            var services = HttpContext?.RequestServices;
            var totpStepUp = services?.GetService<TotpLoginStepUpService>();
            var hasVerifier = services?.GetService<ITotpLoginVerifier>() is not null;

            var authRequest = (!hasVerifier && totpStepUp is not null)
                ? new LoginRequestDto
                {
                    Username = request.Username,
                    Password = request.Password,
                    TenantId = request.TenantId,
                    TenantCode = request.TenantCode
                }
                : new LoginRequestDto
                {
                    Username = request.Username,
                    Password = request.Password,
                    TenantId = request.TenantId,
                    TenantCode = request.TenantCode,
                    TotpCode = totpCode,
                    RecoveryCode = recoveryCode
                };

            var context = new SessionContextDto
            {
                IpAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString(),
                UserAgent = HttpContext?.Request?.Headers.UserAgent.ToString()
            };

            var response = await authService.LoginAsync(authRequest, context, ct);

            var isPlatformUser = string.Equals(response.SecurityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase)
                || AppRoles.IsPlatformRole(response.Role);
            if (!isPlatformUser)
            {
                await RevokePendingLoginSessionAsync(response, ct);
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    code = "PLATFORM_PORTAL_ACCESS_DENIED",
                    errorCode = "PLATFORM_PORTAL_ACCESS_DENIED",
                    message = "Tài khoản khách hàng thuê kho (CUSTOMER_TENANT) không được phép đăng nhập vào cổng SaaS Control Center (/platform)."
                });
            }

            var verifierAlreadyValidatedCode = totpStepUp?.HasAlreadyVerifiedInCurrentScope(response.UserId) == true;

            if (totpStepUp is not null && !verifierAlreadyValidatedCode)
            {
                var stepUp = await totpStepUp.ValidateLoginSecondFactorAsync(
                    request.Username,
                    totpCode,
                    recoveryCode,
                    HttpContext?.TraceIdentifier,
                    ct);

                if (!stepUp.Allowed)
                {
                    await RevokePendingLoginSessionAsync(response, ct);
                    return StatusCode(stepUp.StatusCode, new
                    {
                        code = stepUp.ErrorCode,
                        errorCode = stepUp.ErrorCode,
                        requiresTwoFactor = string.Equals(stepUp.ErrorCode, "TWO_FACTOR_REQUIRED", StringComparison.OrdinalIgnoreCase),
                        message = stepUp.Message
                    });
                }

                if (string.IsNullOrEmpty(response.Token) && response.UserId > 0)
                {
                    await IssuePostStepUpSessionAsync(response, context, ct);
                }
            }
            else if (response.RequiresTwoFactor)
            {
                await RevokePendingLoginSessionAsync(response, ct);
                return StatusCode(StatusCodes.Status401Unauthorized, new
                {
                    code = "TWO_FACTOR_REQUIRED",
                    errorCode = "TWO_FACTOR_REQUIRED",
                    requiresTwoFactor = true,
                    message = "Tài khoản đã kích hoạt bảo mật 2 lớp (2FA TOTP). Vui lòng nhập mã xác thực 6 chữ số hoặc mã khôi phục."
                });
            }

            if (!string.IsNullOrEmpty(response.RefreshToken))
            {
                Response.Cookies.Append(PlatformRefreshCookieName, response.RefreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) || (HttpContext?.Request?.IsHttps == true),
                    SameSite = string.Equals(sessionOptions?.SameSite, "Lax", StringComparison.OrdinalIgnoreCase) ? SameSiteMode.Lax : SameSiteMode.Strict,
                    Path = "/",
                    Expires = new DateTimeOffset(DateTime.SpecifyKind(response.RefreshTokenExpiresAtUtc, DateTimeKind.Utc)),
                    IsEssential = true
                });
                response.RefreshToken = string.Empty;
            }

            response.RequiresTwoFactor = false;
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { code = "UNAUTHORIZED", errorCode = "UNAUTHORIZED", message = ex.Message });
        }
    }

    private async Task RevokePendingLoginSessionAsync(LoginResponseDto response, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(response.RefreshToken))
        {
            return;
        }

        var sessionSvc = HttpContext?.RequestServices?.GetService<IUserSessionService>();
        if (sessionSvc is not null)
        {
            await sessionSvc.LogoutAsync(response.RefreshToken, response.UserId > 0 ? response.UserId : null, ct);
        }
        response.RefreshToken = string.Empty;
        response.Token = string.Empty;
    }

    private async Task IssuePostStepUpSessionAsync(LoginResponseDto response, SessionContextDto context, CancellationToken ct)
    {
        var user = await db.Users.IgnoreQueryFilters()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == response.UserId && u.IsActive, ct);
        if (user is null)
        {
            throw new UnauthorizedAccessException("Phiên đăng nhập không hợp lệ hoặc đã hết hạn.");
        }

        var sessionSvc = HttpContext?.RequestServices?.GetService<IUserSessionService>();
        if (sessionSvc is not null)
        {
            var tokens = await sessionSvc.CreateAsync(user, context, ct);
            response.Token = tokens.AccessToken;
            response.AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc;
            response.RefreshToken = tokens.RefreshToken;
            response.RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc;
            return;
        }

        var tokenSvc = HttpContext?.RequestServices?.GetService<ITokenService>();
        if (tokenSvc is not null)
        {
            response.Token = tokenSvc.GenerateToken(user);
        }
    }
}
