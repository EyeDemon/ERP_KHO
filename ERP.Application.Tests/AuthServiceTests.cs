using System;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace ERP.Application.Tests
{
    public class AuthServiceTests
    {
        private readonly Mock<IUserRepository> _mockUserRepo;
        private readonly Mock<IPasswordHasherService> _mockPasswordHasher;
        private readonly Mock<ITokenService> _mockTokenService;
        private readonly AuthService _authService;

        public AuthServiceTests()
        {
            _mockUserRepo = new Mock<IUserRepository>();
            _mockPasswordHasher = new Mock<IPasswordHasherService>();
            _mockTokenService = new Mock<ITokenService>();
            _authService = new AuthService(
                _mockUserRepo.Object,
                _mockPasswordHasher.Object,
                _mockTokenService.Object);
        }

        [Fact]
        public async Task LoginAsync_WithValidCredentials_ReturnsTokenAndUserInfo()
        {
            // Arrange
            var request = new LoginRequestDto { Username = "admin", Password = "ValidPassword123!" };
            var role = new Role { Id = 1, RoleName = "Admin" };
            var user = new User
            {
                Id = 1,
                Username = "admin",
                PasswordHash = "HASHED_STRING",
                Role = role,
                IsActive = true
            };

            _mockUserRepo.Setup(r => r.GetByUsernameWithRoleAsync("admin")).ReturnsAsync(user);
            _mockPasswordHasher.Setup(p => p.VerifyPassword("HASHED_STRING", "ValidPassword123!")).Returns(true);
            _mockTokenService.Setup(t => t.GenerateToken(user)).Returns("mocked.jwt.token");

            // Act
            var result = await _authService.LoginAsync(request);

            // Assert
            result.Should().NotBeNull();
            result.Token.Should().Be("mocked.jwt.token");
            result.Username.Should().Be("admin");
            result.Role.Should().Be("Admin");
        }

        [Fact]
        public async Task LoginAsync_WithInvalidUsername_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var request = new LoginRequestDto { Username = "nonexistent", Password = "password" };
            _mockUserRepo.Setup(r => r.GetByUsernameWithRoleAsync("nonexistent")).ReturnsAsync((User?)null);

            // Act
            var act = async () => await _authService.LoginAsync(request);

            // Assert
            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Tên đăng nhập hoặc mật khẩu không đúng");
        }

        [Fact]
        public async Task LoginAsync_WithInvalidPassword_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var request = new LoginRequestDto { Username = "admin", Password = "WrongPassword" };
            var role = new Role { Id = 1, RoleName = "Admin" };
            var user = new User
            {
                Id = 1,
                Username = "admin",
                PasswordHash = "HASHED_STRING",
                Role = role,
                IsActive = true
            };

            _mockUserRepo.Setup(r => r.GetByUsernameWithRoleAsync("admin")).ReturnsAsync(user);
            _mockPasswordHasher.Setup(p => p.VerifyPassword("HASHED_STRING", "WrongPassword")).Returns(false);

            // Act
            var act = async () => await _authService.LoginAsync(request);

            // Assert
            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Tên đăng nhập hoặc mật khẩu không đúng");
        }

        [Fact]
        public async Task LoginAsync_WithInactiveUser_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var request = new LoginRequestDto { Username = "inactive_user", Password = "ValidPassword" };
            var user = new User
            {
                Id = 2,
                Username = "inactive_user",
                PasswordHash = "HASHED_STRING",
                IsActive = false
            };

            _mockUserRepo.Setup(r => r.GetByUsernameWithRoleAsync("inactive_user")).ReturnsAsync(user);

            // Act
            var act = async () => await _authService.LoginAsync(request);

            // Assert
            await act.Should().ThrowAsync<UnauthorizedAccessException>()
                .WithMessage("Tên đăng nhập hoặc mật khẩu không đúng");
        }

        [Theory]
        [InlineData("", "password")]
        [InlineData("admin", "")]
        [InlineData("   ", "   ")]
        public async Task LoginAsync_WithEmptyCredentials_ThrowsUnauthorizedAccessException(string username, string password)
        {
            var request = new LoginRequestDto { Username = username, Password = password };
            var act = async () => await _authService.LoginAsync(request);
            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }

        [Fact]
        public void PasswordHasherService_HashAndVerify_IntegrationCheck()
        {
            // Arrange
            var hasher = new PasswordHasherService();
            var password = "SecureP@ssword2026!";

            // Act
            var hash = hasher.HashPassword(password);

            // Assert
            hash.Should().NotBeNullOrWhiteSpace();
            hash.Should().Contain(":");
            hasher.VerifyPassword(hash, password).Should().BeTrue();
            hasher.VerifyPassword(hash, "WrongPassword").Should().BeFalse();
            hasher.VerifyPassword(hash, "").Should().BeFalse();
            hasher.VerifyPassword("invalid:format", password).Should().BeFalse();
        }

        [Fact]
        public async Task LoginAsync_LockedAccount_UsesGenericFailureAndDoesNotIssueToken()
        {
            var user = new User { Id = 7, Username = "locked", PasswordHash = "HASH", IsActive = true, LockoutEnd = DateTime.UtcNow.AddMinutes(5) };
            _mockUserRepo.Setup(r => r.GetByUsernameWithRoleAsync("locked", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var action = () => _authService.LoginAsync(new LoginRequestDto { Username = "locked", Password = "correct" });

            await action.Should().ThrowAsync<UnauthorizedAccessException>().WithMessage("Tên đăng nhập hoặc mật khẩu không đúng");
            _mockTokenService.Verify(t => t.GenerateToken(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task LoginAsync_ValidPasswordAfterExpiredLockout_ResetsFailures()
        {
            var user = new User { Id = 8, Username = "recovered", PasswordHash = "HASH", IsActive = true, FailedLoginCount = 5, LockoutEnd = DateTime.UtcNow.AddMinutes(-1), Role = new Role { RoleName = "Viewer" } };
            _mockUserRepo.Setup(r => r.GetByUsernameWithRoleAsync("recovered", It.IsAny<CancellationToken>())).ReturnsAsync(user);
            _mockPasswordHasher.Setup(h => h.VerifyPassword("HASH", "correct")).Returns(true);
            _mockTokenService.Setup(t => t.GenerateToken(user)).Returns("token");

            await _authService.LoginAsync(new LoginRequestDto { Username = "recovered", Password = "correct" });

            _mockUserRepo.Verify(r => r.ResetLoginFailuresAsync(8, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
