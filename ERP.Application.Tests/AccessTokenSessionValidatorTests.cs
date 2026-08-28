using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

public sealed class AccessTokenSessionValidatorTests
{
    [Fact]
    public async Task ActiveSession_IsAccepted_AndRevokedSessionIsRejected()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"access-session-{Guid.NewGuid():N}")
            .Options;
        await using var context = new ErpKhoDbContext(options);
        var role = new Role { Id = 1, RoleName = "Manager" };
        var user = new User { Id = 7, Username = "qa_session_test", PasswordHash = "not-used", RoleId = role.Id, Role = role, IsActive = true };
        var session = new UserSession
        {
            Id = Guid.NewGuid(), UserId = user.Id, User = user,
            RefreshTokenHash = new string('A', 64), RefreshTokenFamilyId = Guid.NewGuid(),
            AccessTokenJti = "active-jti", CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        context.AddRange(role, user, session);
        await context.SaveChangesAsync();

        var validator = new AccessTokenSessionValidator(context);
        (await validator.IsActiveAsync(user.Id, session.AccessTokenJti)).Should().BeTrue();

        session.RevokedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
        (await validator.IsActiveAsync(user.Id, session.AccessTokenJti)).Should().BeFalse();
    }

    [Fact]
    public async Task MissingExpiredLockedOrInactiveSession_IsRejected()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase($"access-session-invalid-{Guid.NewGuid():N}")
            .Options;
        await using var context = new ErpKhoDbContext(options);
        var role = new Role { Id = 2, RoleName = "Viewer" };
        var inactive = new User { Id = 11, Username = "inactive", PasswordHash = "x", RoleId = role.Id, Role = role, IsActive = false };
        var locked = new User { Id = 12, Username = "locked", PasswordHash = "x", RoleId = role.Id, Role = role, IsActive = true, LockoutEnd = DateTime.UtcNow.AddHours(1) };
        var expiredUser = new User { Id = 13, Username = "expired", PasswordHash = "x", RoleId = role.Id, Role = role, IsActive = true };
        context.AddRange(role, inactive, locked, expiredUser);
        context.UserSessions.AddRange(
            Session(inactive, "inactive-jti", DateTime.UtcNow.AddHours(1)),
            Session(locked, "locked-jti", DateTime.UtcNow.AddHours(1)),
            Session(expiredUser, "expired-jti", DateTime.UtcNow.AddMinutes(-1)));
        await context.SaveChangesAsync();

        var validator = new AccessTokenSessionValidator(context);
        (await validator.IsActiveAsync(1, "missing")).Should().BeFalse();
        (await validator.IsActiveAsync(inactive.Id, "inactive-jti")).Should().BeFalse();
        (await validator.IsActiveAsync(locked.Id, "locked-jti")).Should().BeFalse();
        (await validator.IsActiveAsync(expiredUser.Id, "expired-jti")).Should().BeFalse();
    }

    private static UserSession Session(User user, string jti, DateTime expiresAt) => new()
    {
        Id = Guid.NewGuid(), UserId = user.Id, User = user,
        RefreshTokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
        RefreshTokenFamilyId = Guid.NewGuid(), AccessTokenJti = jti,
        CreatedAt = DateTime.UtcNow, ExpiresAt = expiresAt
    };
}
