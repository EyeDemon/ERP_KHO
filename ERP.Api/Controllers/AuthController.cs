using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using ERP.Application.Common;
using System.Security.Claims;

namespace ERP.Api.Controllers
{
    public sealed record RefreshRequestDto(string? RefreshToken = null);

    [ApiController]
    [Route("api/[controller]")]
    [Route("api/v1/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly IUserSessionService _sessionService;
        private readonly IWebHostEnvironment _environment;
        private readonly SessionSecurityOptions _sessionOptions;
        private const string RefreshCookieName = "erp_refresh";

        public AuthController(IAuthService authService, IUserSessionService sessionService, IWebHostEnvironment environment, SessionSecurityOptions sessionOptions)
        {
            _authService = authService;
            _sessionService = sessionService;
            _environment = environment;
            _sessionOptions = sessionOptions;
        }

        [HttpPost("login")]
        [EnableRateLimiting("Login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _authService.LoginAsync(request, RequestContext(), cancellationToken);
                if (!string.IsNullOrEmpty(response.RefreshToken))
                {
                    SetRefreshCookie(response.RefreshToken, response.RefreshTokenExpiresAtUtc);
                    response.RefreshToken = string.Empty;
                }
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new { message = ex.Message });
            }
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        [EnableRateLimiting("Refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequestDto? request = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var refreshToken = Request.Cookies[RefreshCookieName];
                if (string.IsNullOrWhiteSpace(refreshToken))
                    refreshToken = request?.RefreshToken;
                var result = await _sessionService.RefreshAsync(refreshToken ?? string.Empty, RequestContext(), cancellationToken);
                SetRefreshCookie(result.RefreshToken, result.RefreshTokenExpiresAtUtc);
                return Ok(new { token = result.AccessToken, accessToken = result.AccessToken, accessTokenExpiresAtUtc = result.AccessTokenExpiresAtUtc });
            }
            catch (UnauthorizedAccessException)
            {
                DeleteRefreshCookie();
                return Unauthorized(new { message = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn." });
            }
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        [EnableRateLimiting("SessionMutation")]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            int? actor = User.Identity?.IsAuthenticated == true ? int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value) : null;
            await _sessionService.LogoutAsync(Request.Cookies[RefreshCookieName] ?? string.Empty, actor, cancellationToken);
            DeleteRefreshCookie();
            return NoContent();
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me(CancellationToken cancellationToken)
        {
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                return Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." });
            var user = await _sessionService.GetAuthenticatedUserProfileAsync(userId, cancellationToken);
            return user is null ? Unauthorized(new { message = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại." }) : Ok(user);
        }

        [HttpPost("logout-all")]
        [Authorize]
        [EnableRateLimiting("SessionMutation")]
        public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
        {
            await _sessionService.LogoutAllAsync(cancellationToken);
            DeleteRefreshCookie();
            return NoContent();
        }

        [HttpGet("sessions")]
        [Authorize]
        public async Task<IActionResult> Sessions(CancellationToken cancellationToken) =>
            Ok(await _sessionService.GetCurrentUserSessionsAsync(Request.Cookies[RefreshCookieName], cancellationToken));

        [HttpDelete("sessions/{sessionId:guid}")]
        [Authorize]
        [EnableRateLimiting("SessionMutation")]
        public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
        {
            await _sessionService.RevokeSessionAsync(sessionId, cancellationToken);
            return NoContent();
        }

        private SessionContextDto RequestContext()
        {
            var httpContext = ControllerContext.HttpContext;
            return httpContext is null ? new SessionContextDto() : new SessionContextDto
            {
                IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = httpContext.Request.Headers.UserAgent.ToString()
            };
        }

        private void SetRefreshCookie(string token, DateTime expiresUtc) => Response.Cookies.Append(RefreshCookieName, token, CookieOptions(expiresUtc));
        private void DeleteRefreshCookie() => Response.Cookies.Delete(RefreshCookieName, CookieOptions(DateTime.UnixEpoch));

        private CookieOptions CookieOptions(DateTime expiresUtc) => new()
        {
            HttpOnly = true,
            Secure = !_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"),
            SameSite = Enum.TryParse<SameSiteMode>(_sessionOptions.SameSite, true, out var mode) ? mode : SameSiteMode.Strict,
            Path = "/api/Auth",
            Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresUtc, DateTimeKind.Utc)),
            IsEssential = true
        };
    }
}
