using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Interfaces;
using ERP.Application.Common;
using ERP.Domain.Entities;

namespace ERP.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IPasswordHasherService _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly AuthSecurityOptions _securityOptions;
        private readonly IAuditLogRepository? _auditLogRepository;
        private readonly IUnitOfWork? _unitOfWork;
        private readonly IUserSessionService? _sessionService;

        internal AuthService(IUserRepository userRepository, IPasswordHasherService passwordHasher, ITokenService tokenService)
            : this(userRepository, passwordHasher, tokenService, new AuthSecurityOptions { MaxFailedAttempts = 5, LockoutMinutes = 15 }, null, null, null)
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
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _securityOptions = securityOptions;
            _auditLogRepository = auditLogRepository;
            _unitOfWork = unitOfWork;
            _sessionService = sessionService;
        }

        public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request, SessionContextDto context, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");
            }

            var user = await _userRepository.GetByUsernameWithRoleAsync(request.Username.Trim(), cancellationToken);
            if (user == null || !user.IsActive)
            {
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");
            }

            var nowUtc = DateTime.UtcNow;
            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > nowUtc)
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");

            var isValid = _passwordHasher.VerifyPassword(user.PasswordHash, request.Password);
            if (!isValid)
            {
                var result = await _userRepository.RecordFailedLoginAsync(
                    user.Id, _securityOptions.MaxFailedAttempts, nowUtc,
                    TimeSpan.FromMinutes(_securityOptions.LockoutMinutes), cancellationToken);
                if (result.FailedCount == _securityOptions.MaxFailedAttempts && result.LockoutEnd > nowUtc && _auditLogRepository is not null && _unitOfWork is not null)
                {
                    await _auditLogRepository.AddAsync(new AuditLog
                    {
                        UserId = user.Id,
                        Action = "Authentication.AccountLocked",
                        EntityName = "User",
                        EntityId = user.Id,
                        Timestamp = nowUtc
                    });
                    await _unitOfWork.SaveChangesAsync();
                }
                throw new UnauthorizedAccessException("Tên đăng nhập hoặc mật khẩu không đúng");
            }

            if (user.FailedLoginCount != 0 || user.LockoutEnd.HasValue)
                await _userRepository.ResetLoginFailuresAsync(user.Id, cancellationToken);

            if (_sessionService is null)
            {
                return new LoginResponseDto { Token = _tokenService.GenerateToken(user), Username = user.Username, Role = user.Role?.RoleName ?? string.Empty, UserId = user.Id };
            }

            var tokens = await _sessionService.CreateAsync(user, context, cancellationToken);

            if (_auditLogRepository is not null && _unitOfWork is not null)
            {
                await _auditLogRepository.AddAsync(new AuditLog { UserId = user.Id, Action = "Authentication.LoginSucceeded", EntityName = "UserSession", Timestamp = nowUtc });
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
                UserId = user.Id
            };
        }

        public Task<LoginResponseDto> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default) =>
            LoginAsync(request, new SessionContextDto(), cancellationToken);
    }
}
