using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Interfaces;
using ERP.Application.Common;
using ERP.Domain.Entities;

namespace ERP.Application.Services
{
    public class AuthService : IAuthService
    {
        private static readonly HashSet<string> PlatformRoleNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "SuperAdmin",
            "PlatformSuperAdmin",
            "PlatformOwner",
            "PlatformAdmin",
            "PlatformOpsAdmin",
            "BillingAdmin",
            "PlatformBillingAdmin",
            "SupportAdmin",
            "PlatformSupportAgent",
            "SecurityAuditor",
            "PlatformSecurityAuditor",
            "SupportReadOnly",
            "PlatformReadOnly"
        };

        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly AuthSecurityOptions _securityOptions;
        private readonly IAuditLogRepository? _auditLogRepository;
        private readonly IUnitOfWork? _unitOfWork;
        private readonly IUserSessionService? _sessionService;
        private readonly ITotpLoginVerifier? _totpLoginVerifier;

        internal AuthService(IUserRepository userRepository, IPasswordHasherService passwordHasher, ITokenService tokenService)
            : this(userRepository, passwordHasher, tokenService, new AuthSecurityOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 }, null, null, null, null)
        {
        }

        public AuthService(
            IUserRepository userRepository,
            IPasswordHasherService passwordHasher,
            ITokenService tokenService,
            AuthSecurityOptions securityOptions,
            IAuditLogRepository? auditLogRepository,
            IUnitOfWork? unitOfWork,
            IUserSessionService? sessionService)
            : this(userRepository, passwordHasher, tokenService, securityOptions, auditLogRepository, unitOfWork, sessionService, null)
        {
        }

        public AuthService(
            IUserRepository userRepository,
            IPasswordHasherService passwordHasher,
            ITokenService tokenService,
            AuthSecurityOptions securityOptions,
            IAuditLogRepository? auditLogRepository,
            IUnitOfWork? unitOfWork,
            IUserSessionService? sessionService,
            ITotpLoginVerifier? totpLoginVerifier)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _securityOptions = securityOptions;
            _auditLogRepository = auditLogRepository;
            _unitOfWork = unitOfWork;
            _sessionService = sessionService;
            _totpLoginVerifier = totpLoginVerifier;
        }

        public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request, SessionContextDto context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");
            }

            var (normalizedUsername, resolvedTenantId, hasInvalidTenantSpecifier) =
                TenantContext.ResolveLoginIdentifier(request.Username, request.TenantId, request.TenantCode);
            if (hasInvalidTenantSpecifier || string.IsNullOrWhiteSpace(normalizedUsername))
            {
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");
            }

            var user = resolvedTenantId.HasValue
                ? await _userRepository.GetByUsernameWithRoleAsync(normalizedUsername, resolvedTenantId.Value, cancellationToken)
                : await _userRepository.GetByUsernameWithRoleAsync(normalizedUsername, cancellationToken);
            if (user == null || !user.IsActive)
            {
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");
            }

            var effectiveTenantId = user.TenantId > 0 ? user.TenantId : TenantContext.DefaultTenantId;
            var effectiveTenantCode = TenantContext.FormatTenantCode(effectiveTenantId);

            var nowUtc = DateTime.UtcNow;
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > nowUtc)
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");

            var isValid = _passwordHasher.VerifyPassword(user.PasswordHash, request.Password);
            if (!isValid)
            {
                await RecordFailedLoginAndAuditLockoutAsync(user.Id, effectiveTenantId, nowUtc, cancellationToken);
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");
            }

            var effectiveSecurityDomain = ResolveSecurityDomain(user);

            if (string.Equals(user.TotpEnrollmentStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.TotpCode) && string.IsNullOrWhiteSpace(request.RecoveryCode))
                {
                    return new LoginResponseDto
                    {
                        RequiresTwoFactor = true,
                        Token = string.Empty,
                        RefreshToken = string.Empty,
                        Username = user.Username,
                        Role = user.Role?.RoleName ?? string.Empty,
                        UserId = user.Id,
                        TenantId = effectiveTenantId,
                        TenantCode = effectiveTenantCode,
                        SecurityDomain = effectiveSecurityDomain
                    };
                }

                if (_totpLoginVerifier is null ||
                    !await _totpLoginVerifier.VerifyLoginSecondFactorAsync(user, request.TotpCode, request.RecoveryCode, cancellationToken))
                {
                    await RecordFailedLoginAndAuditLockoutAsync(user.Id, effectiveTenantId, nowUtc, cancellationToken);
                    throw new UnauthorizedAccessException("Mã xác thực 2 lớp (TOTP / Recovery Code) không hợp lệ hoặc đã hết hạn");
                }
            }

            if (user.FailedLoginCount != 0 || user.LockoutEnd.HasValue)
                await _userRepository.ResetLoginFailuresAsync(user.Id, cancellationToken);

            if (_sessionService is null)
            {
                return new LoginResponseDto
                {
                    Token = _tokenService.GenerateToken(user),
                    Username = user.Username,
                    Role = user.Role?.RoleName ?? string.Empty,
                    UserId = user.Id,
                    TenantId = effectiveTenantId,
                    TenantCode = effectiveTenantCode,
                    SecurityDomain = effectiveSecurityDomain
                };
            }

            var tokens = await _sessionService.CreateAsync(user, context, cancellationToken);

            if (_auditLogRepository is not null && _unitOfWork is not null)
            {
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = user.Id,
                    TenantId = effectiveTenantId,
                    SecurityDomain = effectiveSecurityDomain,
                    Action = "Authentication.LoginSucceeded",
                    EntityName = "UserSession",
                    Timestamp = nowUtc
                });
                await _unitOfWork.SaveChangesAsync();
            }

            return new LoginResponseDto
            {
                Token = tokens.AccessToken,
                AccessTokenExpiresAtUtc = tokens.AccessTokenExpiresAtUtc,
                RefreshToken = tokens.RefreshToken,
                RefreshTokenExpiresAtUtc = tokens.RefreshTokenExpiresAtUtc,
                Username = user.Username,
                Role = user.Role?.RoleName ?? string.Empty,
                UserId = user.Id,
                TenantId = effectiveTenantId,
                TenantCode = effectiveTenantCode,
                SecurityDomain = effectiveSecurityDomain
            };
        }

        public Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default) =>
            LoginAsync(request, new SessionContextDto(), cancellationToken);

        private static string ResolveSecurityDomain(User user)
        {
            if (string.Equals(user.SecurityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user.Role?.SecurityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(user.Role?.RoleName) && PlatformRoleNames.Contains(user.Role.RoleName.Trim())))
            {
                return "PLATFORM";
            }

            return "CUSTOMER_TENANT";
        }

        private async Task RecordFailedLoginAndAuditLockoutAsync(
            int userId,
            int effectiveTenantId,
            DateTime nowUtc,
            CancellationToken cancellationToken)
        {
            var result = await _userRepository.RecordFailedLoginAsync(
                userId,
                _securityOptions.MaxFailedAttempts,
                nowUtc,
                TimeSpan.FromMinutes(_securityOptions.LockoutMinutes),
                cancellationToken);
            if (result.FailedCount == _securityOptions.MaxFailedAttempts &&
                result.LockoutEnd > nowUtc &&
                _auditLogRepository is not null &&
                _unitOfWork is not null)
            {
                await _auditLogRepository.AddAsync(new AuditLog
                {
                    UserId = userId,
                    TenantId = effectiveTenantId,
                    Action = "Authentication.AccountLocked",
                    EntityName = "User",
                    EntityId = userId,
                    Timestamp = nowUtc
                });
                await _unitOfWork.SaveChangesAsync();
            }
        }
    }
}
