using System.Net;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Repositories;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ERP.Api.Tests;

public sealed partial class InboundHttpIntegrationTests
{
    [ApprovalSqlServerFact]
    public async Task AccountSecurityHttpProtectsEmptyAggregateRaceReplayAndLockoutChanges()
    {
        await using var owned = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await owned.MigrateAndSeedAsync();
        await using var db = Context(owned.ConnectionString);
        var actor = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var target = await db.Users.SingleAsync(u => u.Role.RoleName == "Manager");
        await using var factory = Factory(owned.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var path = $"/api/users/{target.Id}/security";
        async Task<string> Read()
        {
            using var response = await Send(client, HttpMethod.Get, path, actor.Id, "Admin");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "isActive", "isLocked", "lockoutEnd", "rowVersion" },
                json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
            return json.RootElement.GetProperty("rowVersion").GetString()!;
        }
        var baseline = await Read();
        Assert.Equal(baseline, await Read());
        Assert.Empty(await db.UserSessions.Where(s => s.UserId == target.Id).ToListAsync());
        var body = JsonSerializer.Serialize(new { rowVersion = baseline });
        foreach (var invalid in new string?[] { null, "not-base64", Convert.ToBase64String(new byte[8]) })
        foreach (var command in new[] { "unlock", "revoke-sessions" })
        {
            using var response = await Send(client, HttpMethod.Post, $"{path}/{command}", actor.Id, "Admin", Guid.NewGuid().ToString(),
                JsonSerializer.Serialize(new { rowVersion = invalid }));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.", error.RootElement.GetProperty("message").GetString());
        }
        using (var noBody = await Send(client, HttpMethod.Post, $"{path}/unlock", actor.Id, "Admin", "qa-security-no-body"))
            Assert.Equal(HttpStatusCode.Conflict, noBody.StatusCode);
        foreach (var suffix in new[] { "", "/unlock", "/revoke-sessions" })
        {
            using var missing = await Send(client, suffix == "" ? HttpMethod.Get : HttpMethod.Post,
                $"/api/users/{int.MaxValue}/security{suffix}", actor.Id, "Admin", Guid.NewGuid().ToString(), suffix == "" ? null : body);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        Assert.Equal(0, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync(a => a.Action.StartsWith("Authentication.")));

        await using var blocker = Context(owned.ConnectionString);
        await using var blockingTransaction = await blocker.Database.BeginTransactionAsync();
        await PermissionAdministrationGuard.LockAsync(blocker, default);
        var commands = new[] { "unlock", "revoke-sessions" };
        var requests = commands.Select(command => Send(client, HttpMethod.Post, $"{path}/{command}", actor.Id, "Admin", $"qa-security-{command}", body)).ToArray();
        await Task.Delay(150);
        Assert.All(requests, request => Assert.False(request.IsCompleted));
        await blockingTransaction.CommitAsync();
        var responses = await Task.WhenAll(requests);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var winner = commands[Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.NoContent)];
        foreach (var response in responses) response.Dispose();
        Assert.Equal(1, await db.Users.Where(u => u.Id == target.Id).Select(u => u.SecurityRevision).SingleAsync());
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityName == "User" && a.EntityId == target.Id));
        using (var replay = await Send(client, HttpMethod.Post, $"{path}/{winner}", actor.Id, "Admin", $"qa-security-{winner}", body))
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        using (var mismatch = await Send(client, HttpMethod.Post, $"{path}/{winner}", actor.Id, "Admin", $"qa-security-{winner}", JsonSerializer.Serialize(new { rowVersion = await Read() })))
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        using (var stale = await Send(client, HttpMethod.Post, $"{path}/unlock", actor.Id, "Admin", "qa-security-stale", body))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var beforeLock = await Read();
        await new UserRepository(db).RecordFailedLoginAsync(target.Id, 1, DateTime.UtcNow, TimeSpan.FromMinutes(5));
        await new UserRepository(db).ResetLoginFailuresAsync(target.Id);
        Assert.True(await db.Users.AnyAsync(u => u.Id == target.Id && u.LockoutEnd > DateTime.UtcNow));
        using (var stale = await Send(client, HttpMethod.Post, $"{path}/unlock", actor.Id, "Admin", "qa-security-lock-stale", JsonSerializer.Serialize(new { rowVersion = beforeLock })))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var fresh = await Send(client, HttpMethod.Post, $"{path}/unlock", actor.Id, "Admin", "qa-security-unlock-fresh", JsonSerializer.Serialize(new { rowVersion = await Read() })))
            Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
        Assert.True(await db.Users.AnyAsync(u => u.Id == target.Id && u.LockoutEnd == null && u.FailedLoginCount == 0));
        var count = await db.IdempotencyRecords.CountAsync();
        await db.Users.Where(u => u.Id == actor.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, DateTime.UtcNow.AddMinutes(1)));
        using (var locked = await Send(client, HttpMethod.Post, $"{path}/{winner}", actor.Id, "Admin", $"qa-security-{winner}", body))
            Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        await db.Users.Where(u => u.Id == actor.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, (DateTime?)null).SetProperty(u => u.IsActive, false));
        using (var inactive = await Send(client, HttpMethod.Get, path, actor.Id, "Admin"))
            Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
        Assert.Equal(count, await db.IdempotencyRecords.CountAsync());
    }

    [ApprovalSqlServerFact]
    public async Task SecurityBoundarySerializesRefreshLoginAndAdministrativeRevocation()
    {
        await using var owned = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await owned.MigrateAndSeedAsync();
        await using var db = Context(owned.ConnectionString);
        var actor = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var target = await db.Users.Include(u => u.Role).SingleAsync(u => u.Role.RoleName == "Manager");
        var identity = new SecurityActor(actor.Id);
        var tokens = new TokenService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)),
            ["JwtSettings:Issuer"] = "qa", ["JwtSettings:Audience"] = "qa", ["JwtSettings:ExpiryMinutes"] = "15"
        }).Build());
        UserSessionService Sessions(ERP.Infrastructure.Persistence.ErpKhoDbContext ctx) => new(ctx, tokens, identity, new SessionSecurityOptions());
        var admin = new AccountAdminService(db, identity);
        var emptyToken = (await admin.GetAsync(target.Id)).RowVersion;
        var first = await Sessions(db).CreateAsync(target, new SessionContextDto());
        var afterCreate = (await admin.GetAsync(target.Id)).RowVersion;
        Assert.NotEqual(emptyToken, afterCreate);
        await Assert.ThrowsAsync<ConcurrencyException>(() => admin.UnlockAsync(target.Id, emptyToken));
        await db.UserSessions.Where(s => s.UserId == target.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastUsedAt, DateTime.UtcNow));
        Assert.Equal(afterCreate, (await admin.GetAsync(target.Id)).RowVersion);

        await using (var boundary = Context(owned.ConnectionString))
        await using (var transaction = await boundary.Database.BeginTransactionAsync())
        {
            await PermissionAdministrationGuard.LockAsync(boundary, default);
            await Sessions(boundary).RevokeUserSessionsAsAdminAsync(target.Id, afterCreate);
            await using var refreshDb = Context(owned.ConnectionString);
            await using var loginDb = Context(owned.ConnectionString);
            var refresh = Sessions(refreshDb).RefreshAsync(first.RefreshToken, new SessionContextDto());
            var login = Sessions(loginDb).CreateAsync(target, new SessionContextDto());
            await Task.Delay(150);
            Assert.False(refresh.IsCompleted);
            Assert.False(login.IsCompleted);
            await transaction.CommitAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => refresh);
            var newLogin = await login;
            Assert.NotEmpty(newLogin.AccessToken);
        }
        Assert.Equal(1, await db.UserSessions.CountAsync(s => s.UserId == target.Id && s.RevokedAt == null));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "Authentication.AdminRevokedUserSessions"));

        // Opposite ordering: completed rotation invalidates an administrator's earlier snapshot.
        var fresh = await Sessions(db).CreateAsync(target, new SessionContextDto());
        var beforeRotation = (await admin.GetAsync(target.Id)).RowVersion;
        await using (var rotationDb = Context(owned.ConnectionString))
        await using (var transaction = await rotationDb.Database.BeginTransactionAsync())
        {
            await PermissionAdministrationGuard.LockAsync(rotationDb, default);
            await Sessions(rotationDb).RefreshAsync(fresh.RefreshToken, new SessionContextDto());
            await using var revokeDb = Context(owned.ConnectionString);
            var revoke = Sessions(revokeDb).RevokeUserSessionsAsAdminAsync(target.Id, beforeRotation);
            await Task.Delay(150);
            Assert.False(revoke.IsCompleted);
            await transaction.CommitAsync();
            await Assert.ThrowsAsync<ConcurrencyException>(() => revoke);
        }
        await Sessions(db).RevokeUserSessionsAsAdminAsync(target.Id, (await admin.GetAsync(target.Id)).RowVersion);
        Assert.Equal(0, await db.UserSessions.CountAsync(s => s.UserId == target.Id && s.RevokedAt == null));
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "Authentication.AdminRevokedUserSessions"));
        var latest = (await admin.GetAsync(target.Id)).RowVersion;
        await db.Users.Where(u => u.Id == target.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Sessions(db).CreateAsync(target, new SessionContextDto()));
        Assert.NotEqual(latest, (await admin.GetAsync(target.Id)).RowVersion);
    }

    private sealed record SecurityActor(int UserId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public bool IsGlobalAdmin => false;
    }
}
