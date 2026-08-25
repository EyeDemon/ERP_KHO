using System.Security.Cryptography;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ERP.Application.Tests;

[Collection(SqlServerExportStockCollection.Name)]
public sealed class SqlServerRefreshTokenRotationTests
{
    private static string ConnectionString => Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task LoginRotationAndReuse_StoresOnlyHashAndRevokesFamily()
    {
        var testUser = await CreateUserAsync();
        var userId = testUser.UserId;
        try
        {
            string firstRefresh;
            Guid familyId;
            await using (var db = CreateContext())
            {
                var user = await db.Users.Include(x => x.Role).SingleAsync(x => x.Id == userId);
                var service = CreateService(db, userId);
                var created = await service.CreateAsync(user, new SessionContextDto { IpAddress = "127.0.0.1", UserAgent = "integration-test" });
                firstRefresh = created.RefreshToken;
                var stored = await db.UserSessions.AsNoTracking().SingleAsync(x => x.UserId == userId);
                stored.RefreshTokenHash.Should().NotBe(firstRefresh);
                stored.RefreshTokenHash.Should().HaveLength(64);
                familyId = stored.RefreshTokenFamilyId;
            }

            await using (var db = CreateContext())
            {
                var rotated = await CreateService(db, userId).RefreshAsync(firstRefresh, new SessionContextDto());
                rotated.RefreshToken.Should().NotBe(firstRefresh);
                (await db.UserSessions.CountAsync(x => x.UserId == userId)).Should().Be(2);
                (await db.UserSessions.CountAsync(x => x.UserId == userId && x.RevokedAt == null)).Should().Be(1);
            }

            await using (var db = CreateContext())
            {
                var action = () => CreateService(db, userId).RefreshAsync(firstRefresh, new SessionContextDto());
                await action.Should().ThrowAsync<UnauthorizedAccessException>();
            }

            await using (var verify = CreateContext())
            {
                (await verify.UserSessions.CountAsync(x => x.RefreshTokenFamilyId == familyId && x.RevokedAt == null)).Should().Be(0);
            }
        }
        finally { await CleanupUserAsync(testUser); }
    }

    [SqlServerFact]
    public async Task ConcurrentRefresh_AllowsAtMostOneSuccess()
    {
        var testUser = await CreateUserAsync();
        var userId = testUser.UserId;
        try
        {
            string refresh;
            await using (var db = CreateContext())
            {
                var user = await db.Users.Include(x => x.Role).SingleAsync(x => x.Id == userId);
                refresh = (await CreateService(db, userId).CreateAsync(user, new SessionContextDto())).RefreshToken;
            }

            async Task<bool> AttemptAsync()
            {
                await using var db = CreateContext();
                try { await CreateService(db, userId).RefreshAsync(refresh, new SessionContextDto()); return true; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or DbUpdateException) { return false; }
            }

            var results = await Task.WhenAll(AttemptAsync(), AttemptAsync());
            results.Count(x => x).Should().Be(1);
        }
        finally { await CleanupUserAsync(testUser); }
    }

    [SqlServerFact]
    public async Task InactiveLockedExpiredAndRevokedSessions_CannotRefresh()
    {
        var testUser = await CreateUserAsync();
        try
        {
            async Task<string> CreateRefreshAsync()
            {
                await using var db = CreateContext();
                var user = await db.Users.Include(x => x.Role).SingleAsync(x => x.Id == testUser.UserId);
                return (await CreateService(db, testUser.UserId).CreateAsync(user, new SessionContextDto())).RefreshToken;
            }

            var inactiveToken = await CreateRefreshAsync();
            await using (var db = CreateContext()) await db.Users.Where(x => x.Id == testUser.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
            await AssertRefreshRejectedAsync(testUser.UserId, inactiveToken);

            await using (var db = CreateContext()) await db.Users.Where(x => x.Id == testUser.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true).SetProperty(x => x.LockoutEnd, DateTime.UtcNow.AddMinutes(5)));
            var lockedToken = await CreateRefreshAsync();
            await AssertRefreshRejectedAsync(testUser.UserId, lockedToken);

            await using (var db = CreateContext()) await db.Users.Where(x => x.Id == testUser.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LockoutEnd, (DateTime?)null));
            var expiredToken = await CreateRefreshAsync();
            await using (var db = CreateContext()) await db.UserSessions.Where(x => x.UserId == testUser.UserId && x.RevokedAt == null).OrderByDescending(x => x.CreatedAt).Take(1).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
            await AssertRefreshRejectedAsync(testUser.UserId, expiredToken);

            var revokedToken = await CreateRefreshAsync();
            await using (var db = CreateContext()) await CreateService(db, testUser.UserId).LogoutAsync(revokedToken, testUser.UserId);
            await AssertRefreshRejectedAsync(testUser.UserId, revokedToken);
        }
        finally { await CleanupUserAsync(testUser); }
    }

    [SqlServerFact]
    public async Task SessionOwnershipLogoutAllAndAdminRevocation_AreEnforced()
    {
        var owner = await CreateUserAsync();
        var other = await CreateUserAsync();
        try
        {
            string ownerToken;
            Guid otherSessionId;
            await using (var db = CreateContext())
            {
                var ownerUser = await db.Users.Include(x => x.Role).SingleAsync(x => x.Id == owner.UserId);
                ownerToken = (await CreateService(db, owner.UserId).CreateAsync(ownerUser, new SessionContextDto())).RefreshToken;
                await CreateService(db, owner.UserId).CreateAsync(ownerUser, new SessionContextDto());
                var otherUser = await db.Users.Include(x => x.Role).SingleAsync(x => x.Id == other.UserId);
                await CreateService(db, other.UserId).CreateAsync(otherUser, new SessionContextDto());
                otherSessionId = await db.UserSessions.Where(x => x.UserId == other.UserId).Select(x => x.Id).SingleAsync();
            }

            await using (var db = CreateContext())
            {
                var ownerService = CreateService(db, owner.UserId);
                (await ownerService.GetCurrentUserSessionsAsync(ownerToken)).Should().HaveCount(2);
                var denied = () => ownerService.RevokeSessionAsync(otherSessionId);
                await denied.Should().ThrowAsync<UnauthorizedAccessException>();
                await ownerService.LogoutAsync(ownerToken, owner.UserId);
                await ownerService.LogoutAllAsync();
            }

            await using (var db = CreateContext())
            {
                (await db.UserSessions.CountAsync(x => x.UserId == owner.UserId && x.RevokedAt == null)).Should().Be(0);
                var admin = new UserSessionService(db, CreateTokenService(), new TestCurrentUser(owner.UserId, true), new SessionSecurityOptions());
                await admin.RevokeUserSessionsAsAdminAsync(other.UserId);
                (await db.UserSessions.CountAsync(x => x.UserId == other.UserId && x.RevokedAt == null)).Should().Be(0);
            }
        }
        finally { await CleanupUserAsync(owner); await CleanupUserAsync(other); }
    }

    private static async Task AssertRefreshRejectedAsync(int userId, string refreshToken)
    {
        await using var db = CreateContext();
        var action = () => CreateService(db, userId).RefreshAsync(refreshToken, new SessionContextDto());
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static UserSessionService CreateService(ErpKhoDbContext db, int userId) => new(
        db, CreateTokenService(), new TestCurrentUser(userId, false), new SessionSecurityOptions { RefreshTokenDays = 14, RetentionDays = 30 });

    private static TokenService CreateTokenService()
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = secret, ["JwtSettings:Issuer"] = "tests", ["JwtSettings:Audience"] = "tests", ["JwtSettings:ExpiryMinutes"] = "15"
        }).Build();
        return new TokenService(config);
    }

    private static async Task<TestUser> CreateUserAsync()
    {
        await using var db = CreateContext();
        var role = new Role { RoleName = $"SessionTest_{Guid.NewGuid():N}", Description = "Temporary SQL integration-test role" };
        db.Roles.Add(role);
        var user = new User { Username = $"QASESSION_{Guid.NewGuid():N}", PasswordHash = "not-used", FullName = "Session test", Role = role };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new TestUser(user.Id, role.Id);
    }

    private static async Task CleanupUserAsync(TestUser testUser)
    {
        await using var db = CreateContext();
        await db.UserSessions.Where(x => x.UserId == testUser.UserId).ExecuteDeleteAsync();
        await db.AuditLogs.Where(x => x.UserId == testUser.UserId).ExecuteDeleteAsync();
        await db.Users.Where(x => x.Id == testUser.UserId).ExecuteDeleteAsync();
        await db.Roles.Where(x => x.Id == testUser.RoleId).ExecuteDeleteAsync();
    }

    private static ErpKhoDbContext CreateContext() => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);
    private sealed record TestCurrentUser(int UserId, bool IsGlobalAdmin) : ICurrentUser { public bool IsAuthenticated => true; }
    private sealed record TestUser(int UserId, int RoleId);
}
