using System.Net;
using System.Text.Json;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ERP.Api.Tests;

public sealed partial class InboundHttpIntegrationTests
{
    [ApprovalSqlServerFact]
    public async Task Location_deactivation_and_move_overlap_without_stock_entering_an_inactive_destination()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var admin = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var warehouse = await db.Warehouses.SingleAsync();
        var product = await db.Products.Include(x => x.Unit).SingleAsync();
        var source = new WarehouseLocation { WarehouseId=warehouse.Id, Code="QA-RECEIVING", Name="Vị trí nhận", LocationType=WarehouseLocationType.Receiving, IsReceivable=true };
        var destination = new WarehouseLocation { WarehouseId=warehouse.Id, Code="QA-STORAGE", Name="Vị trí lưu", LocationType=WarehouseLocationType.Storage, IsPickable=true };
        var receipt = Receipt("QA-LOCATION-RACE", warehouse.Id, admin.Id, product, ReceiptStatus.Posted, 0);
        db.AddRange(source, destination, receipt); await db.SaveChangesAsync();
        var task = new PutawayTask { ReceiptId=receipt.Id, WarehouseId=warehouse.Id, CreatedBy=admin.Id, AssignedUserId=admin.Id, Status=PutawayTaskStatus.Assigned };
        var item = new PutawayTaskItem { PutawayTask=task, ReceiptLineId=receipt.Details.Single().Id, ProductId=product.Id,
            InventoryStatus=InventoryStatus.Available, SourceLocationId=source.Id, OperationUnitId=product.UnitId, BaseUnitId=product.UnitId,
            OperationUnitCodeSnapshot=product.Unit.Code, BaseUnitCodeSnapshot=product.Unit.Code, ConversionFactorSnapshot=1, ConversionVersionSnapshot=1,
            RequiredOperationQuantity=5, RequiredBaseQuantity=5 };
        db.AddRange(item, new InventoryStock { ProductId=product.Id, WarehouseId=warehouse.Id, LocationId=source.Id, Quantity=5 });
        await db.SaveChangesAsync();
        var total = await db.InventoryStocks.SumAsync(x => x.Quantity);
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://localhost") });
        await using var blocker = Context(database.ConnectionString);
        await using var transaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM dbo.WarehouseLocations WITH (UPDLOCK,HOLDLOCK) WHERE Id={destination.Id}");
        var move = Send(client, HttpMethod.Post, $"/api/putaway-tasks/{task.Id}/move", admin.Id, "Admin", "qa-location-move",
            JsonSerializer.Serialize(new { itemId=item.Id, destinationLocationId=destination.Id, quantity=5, unitCode=product.Unit.Code, rowVersion=Convert.ToBase64String(task.RowVersion) }));
        var deactivate = Send(client, HttpMethod.Put, $"/api/putaway-tasks/locations/{destination.Id}", admin.Id, "Admin", body:
            JsonSerializer.Serialize(new { name=destination.Name, isActive=false, isPickable=true, rowVersion=Convert.ToBase64String(destination.RowVersion) }));
        await Task.Delay(150);
        Assert.False(move.IsCompleted); Assert.False(deactivate.IsCompleted);
        await transaction.CommitAsync();
        using var moved = await move.WaitAsync(TimeSpan.FromSeconds(20));
        using var deactivated = await deactivate.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.True((moved.StatusCode == HttpStatusCode.OK && deactivated.StatusCode == HttpStatusCode.Conflict) ||
            (deactivated.StatusCode == HttpStatusCode.OK && moved.StatusCode == HttpStatusCode.BadRequest),
            $"Move={moved.StatusCode}; deactivate={deactivated.StatusCode}");
        db.ChangeTracker.Clear();
        var active = await db.WarehouseLocations.Where(x => x.Id == destination.Id).Select(x => x.IsActive).SingleAsync();
        var quantity = await db.InventoryStocks.Where(x => x.LocationId == destination.Id).SumAsync(x => x.Quantity);
        Assert.True(active || quantity == 0);
        Assert.Equal(total, await db.InventoryStocks.SumAsync(x => x.Quantity));
        var effects = moved.IsSuccessStatusCode ? 1 : 0;
        Assert.Equal(effects, await db.InventoryLocationMovements.CountAsync());
        Assert.Equal(effects, await db.AuditLogs.CountAsync(x => x.Action == "PutawayTask.Moved"));
        Assert.Equal(effects, await db.IdempotencyRecords.CountAsync());
    }

    [ApprovalSqlServerFact]
    public async Task Partner_assignment_isolates_foreign_receipt_before_state_or_partner_validation()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var manager = await db.Users.SingleAsync(u => u.Role.RoleName == "Manager");
        var warehouse = await db.Warehouses.SingleAsync();
        var import = new ImportReceipt { Code="QA-PARTNER-PRIVATE-IMPORT", WarehouseId=warehouse.Id, CreatedBy=manager.Id, Status=ReceiptStatus.Approved };
        var export = new ExportReceipt { Code="QA-PARTNER-PRIVATE-EXPORT", WarehouseId=warehouse.Id, CreatedBy=manager.Id, Status=ReceiptStatus.Approved };
        db.AddRange(import, export); await db.SaveChangesAsync();
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://localhost") });
        var auditCount = await db.AuditLogs.CountAsync();
        foreach (var path in new[] { $"/api/importreceipts/{import.Id}/supplier", $"/api/exportreceipts/{export.Id}/customer" })
            foreach (var partnerId in new int?[] { null, int.MaxValue })
            {
                using var denied = await Send(client, HttpMethod.Put, path, manager.Id, "Manager", key:Guid.NewGuid().ToString("N"), body:JsonSerializer.Serialize(new { partnerId }));
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
                var raw = await denied.Content.ReadAsStringAsync();
                Assert.DoesNotContain("QA-PARTNER-PRIVATE", raw);
                Assert.DoesNotContain("nháp", raw);
            }
        Assert.Equal(auditCount, await db.AuditLogs.CountAsync());
        Assert.Null((await db.ImportReceipts.AsNoTracking().SingleAsync(x => x.Id == import.Id)).SupplierId);
        Assert.Null((await db.ExportReceipts.AsNoTracking().SingleAsync(x => x.Id == export.Id)).CustomerId);
    }

    [ApprovalSqlServerFact]
    public async Task SessionOwnershipAndAdministrativeReplayUseDatabaseAuthority()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var admin = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var manager = await db.Users.SingleAsync(u => u.Role.RoleName == "Manager");
        UserSession Session(int owner) => new()
        {
            Id = Guid.NewGuid(), UserId = owner, RefreshTokenFamilyId = Guid.NewGuid(),
            RefreshTokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            AccessTokenJti = Guid.NewGuid().ToString("N"), CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        };
        var own = Session(manager.Id);
        var foreign = Session(admin.Id);
        db.UserSessions.AddRange(own, foreign); await db.SaveChangesAsync();
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using (var list = await Send(client, HttpMethod.Get, "/api/auth/sessions", manager.Id, "Admin"))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var raw = await list.Content.ReadAsStringAsync();
            Assert.Contains(own.Id.ToString(), raw);
            Assert.DoesNotContain(foreign.Id.ToString(), raw);
            Assert.DoesNotContain("refreshTokenHash", raw);
            Assert.DoesNotContain("accessTokenJti", raw);
        }
        foreach (var request in new[] { (manager.Id, foreign.Id), (admin.Id, own.Id), (manager.Id, Guid.NewGuid()) })
        {
            using var denied = await Send(client, HttpMethod.Delete, $"/api/auth/sessions/{request.Item2}", request.Item1, "Admin");
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            var raw = await denied.Content.ReadAsStringAsync();
            Assert.DoesNotContain(foreign.Id.ToString(), raw);
            Assert.DoesNotContain(own.Id.ToString(), raw);
            Assert.DoesNotContain("SqlException", raw);
        }
        Assert.False(await db.UserSessions.AnyAsync(s => s.RevokedAt != null));
        Assert.Equal(0, await db.AuditLogs.CountAsync(a => a.Action.StartsWith("Authentication.")));
        Assert.Equal(0, await db.IdempotencyRecords.CountAsync());
        using (var success = await Send(client, HttpMethod.Delete, $"/api/auth/sessions/{own.Id}", manager.Id, "Manager"))
            Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        Assert.True(await db.UserSessions.AnyAsync(s => s.Id == own.Id && s.RevokedAt != null));
        Assert.False(await db.UserSessions.AnyAsync(s => s.Id == foreign.Id && s.RevokedAt != null));

        var path = $"/api/users/{admin.Id}/security/revoke-sessions";
        using (var denied = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qa-session-denied"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, await db.IdempotencyRecords.CountAsync());
        var permissionId = await db.Permissions.Where(p => p.Code == "user.manage").Select(p => p.Id).SingleAsync();
        db.RolePermissions.Add(new RolePermission { RoleId = manager.RoleId, PermissionId = permissionId });
        await db.SaveChangesAsync();
        using var snapshotResponse = await Send(client, HttpMethod.Get, $"/api/users/{admin.Id}/security", manager.Id, "Manager");
        var snapshot = JsonDocument.Parse(await snapshotResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("rowVersion").GetString();
        foreach (var retry in Enumerable.Range(0, 2))
        {
            using var success = await Send(client, HttpMethod.Post, path, manager.Id, "Manager", "qa-session-success", JsonSerializer.Serialize(new { rowVersion = snapshot }));
            Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        }
        Assert.True(await db.UserSessions.AnyAsync(s => s.Id == foreign.Id && s.RevokedAt != null));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "Authentication.AdminRevokedUserSessions"));
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync(r => r.UserId == manager.Id));
        await db.RolePermissions.Where(g => g.RoleId == manager.RoleId && g.PermissionId == permissionId).ExecuteDeleteAsync();
        using (var revokedReplay = await Send(client, HttpMethod.Post, path, manager.Id, "Admin", "qa-session-success"))
            Assert.Equal(HttpStatusCode.Forbidden, revokedReplay.StatusCode);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "Authentication.AdminRevokedUserSessions"));
        Assert.Equal(1, await db.IdempotencyRecords.CountAsync(r => r.UserId == manager.Id));
    }

    [ApprovalSqlServerFact]
    public async Task MembershipTokensProtectEmptySetsStaleRequestsAndReauthorizedReplay()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var actor = await db.Users.SingleAsync(u => u.Role.RoleName == "Admin");
        var target = new User { Username = "QA_MEMBERSHIP_HTTP", PasswordHash = "QA_FIXTURE_NOT_A_LOGIN", RoleId = actor.RoleId };
        db.Users.Add(target); await db.SaveChangesAsync();
        var warehouse = await db.Warehouses.SingleAsync();
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var path = $"/api/users/{target.Id}/warehouse-access";
        async Task<string> ReadToken()
        {
            using var response = await Send(client, HttpMethod.Get, path, actor.Id, "Admin");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("rowVersion").GetString()!;
        }
        var baseline = await ReadToken();
        Assert.Equal(8, Convert.FromBase64String(baseline).Length);
        foreach (var invalid in new string?[] { null, "invalid", Convert.ToBase64String(new byte[7]) })
        {
            using var denied = await Send(client, HttpMethod.Post, path, actor.Id, "Admin", Guid.NewGuid().ToString(),
                JsonSerializer.Serialize(new { warehouseId = warehouse.Id, rowVersion = invalid }));
            Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        }
        Assert.Equal(0, await db.IdempotencyRecords.CountAsync(r => r.UserId == actor.Id));
        var body = JsonSerializer.Serialize(new { warehouseId = warehouse.Id, rowVersion = baseline });
        var keys = new[] { "qa-membership-race-a", "qa-membership-race-b" };
        await using var blocker = Context(database.ConnectionString);
        await using var blockingTransaction = await blocker.Database.BeginTransactionAsync();
        await ERP.Infrastructure.Services.PermissionAdministrationGuard.LockAsync(blocker, default);
        var first = Send(client, HttpMethod.Post, path, actor.Id, "Admin", keys[0], body);
        var second = Send(client, HttpMethod.Post, path, actor.Id, "Admin", keys[1], body);
        await Task.Delay(100);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        await blockingTransaction.CommitAsync();
        var responses = await Task.WhenAll(first, second);
        var successKey = keys[Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.NoContent)];
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var response in responses) response.Dispose();
        using (var replay = await Send(client, HttpMethod.Post, path, actor.Id, "Admin", successKey, body))
            Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        using (var mismatch = await Send(client, HttpMethod.Post, path, actor.Id, "Admin", successKey,
            JsonSerializer.Serialize(new { warehouseId = warehouse.Id, rowVersion = await ReadToken() })))
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        using (var stale = await Send(client, HttpMethod.Delete, $"{path}/{warehouse.Id}", actor.Id, "Admin", "qa-membership-stale",
            JsonSerializer.Serialize(new { rowVersion = baseline })))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(1, await db.UserWarehouses.CountAsync(w => w.UserId == target.Id));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.EntityName == "UserWarehouse" && a.EntityId == target.Id));
        using (var success = await Send(client, HttpMethod.Delete, $"{path}/{warehouse.Id}", actor.Id, "Admin", "qa-membership-revoke",
            JsonSerializer.Serialize(new { rowVersion = await ReadToken() })))
            Assert.Equal(HttpStatusCode.NoContent, success.StatusCode);
        await db.RolePermissions.Where(g => g.RoleId == actor.RoleId && g.Permission.Code == "user_warehouse.manage").ExecuteDeleteAsync();
        using (var revoked = await Send(client, HttpMethod.Post, path, actor.Id, "Admin", successKey, body))
            Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        Assert.Equal(2, await db.IdempotencyRecords.CountAsync(r => r.UserId == actor.Id));
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityName == "UserWarehouse" && a.EntityId == target.Id));
        Assert.False(await db.UserWarehouses.AnyAsync(w => w.UserId == target.Id));
        var scopedRole = new Role { RoleName = "QA_MEMBERSHIP_SCOPED" };
        foreach (var permission in await db.Permissions.Where(p => p.Code == "user_warehouse.read" || p.Code == "user_warehouse.manage").ToArrayAsync())
            scopedRole.Permissions.Add(new RolePermission { PermissionId = permission.Id });
        db.Roles.Add(scopedRole); await db.SaveChangesAsync();
        actor.RoleId = scopedRole.Id;
        db.UserWarehouses.Add(new UserWarehouse { UserId = actor.Id, WarehouseId = warehouse.Id, CreatedBy = actor.Id });
        await db.SaveChangesAsync();
        await db.UserWarehouses.Where(w => w.UserId == actor.Id).ExecuteDeleteAsync();
        using (var revokedScopeReplay = await Send(client, HttpMethod.Post, path, actor.Id, "Admin", successKey, body))
            Assert.Equal(HttpStatusCode.NotFound, revokedScopeReplay.StatusCode);
        using (var deniedScope = await Send(client, HttpMethod.Post, path, actor.Id, "Admin", "qa-membership-outside", body))
            Assert.Equal(HttpStatusCode.NotFound, deniedScope.StatusCode);
        var viewerRole = await db.Roles.SingleAsync(r => r.RoleName == "Viewer");
        viewerRole.Permissions.Add(new RolePermission { PermissionId = await db.Permissions.Where(p => p.Code == "user_warehouse.read").Select(p => p.Id).SingleAsync() });
        actor.RoleId = viewerRole.Id; await db.SaveChangesAsync();
        using (var readOnly = await Send(client, HttpMethod.Get, path, actor.Id, "Admin"))
        {
            Assert.Equal(HttpStatusCode.OK, readOnly.StatusCode);
            using var json = JsonDocument.Parse(await readOnly.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.TryGetProperty("rowVersion", out _));
        }
        actor.IsActive = false; await db.SaveChangesAsync();
        using (var inactive = await Send(client, HttpMethod.Post, path, actor.Id, "Admin", "qa-membership-inactive", body))
            Assert.Equal(HttpStatusCode.Unauthorized, inactive.StatusCode);
        Assert.Equal(2, await db.IdempotencyRecords.CountAsync(r => r.UserId == actor.Id));
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityName == "UserWarehouse" && a.EntityId == target.Id));
    }

    [ApprovalSqlServerFact]
    public async Task MixedApprovalInboundRequiresIndependentReadAndRejectGrantsOnEveryRequest()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var users = await db.Users.OrderBy(u => u.Id).ToArrayAsync();
        var warehouse = await db.Warehouses.SingleAsync();
        db.UserWarehouses.Add(new UserWarehouse { UserId = users[1].Id, WarehouseId = warehouse.Id, CreatedBy = users[0].Id });
        var receipt = Receipt("QA-APPROVAL-GRANTS", warehouse.Id, users[0].Id, await db.Products.Include(p => p.Unit).SingleAsync(), ReceiptStatus.Received, 12m);
        db.ImportReceipts.Add(receipt);
        await db.SaveChangesAsync();
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var roleId = users[1].RoleId;
        var rejectId = await db.Permissions.Where(p => p.Code == "approval.reject").Select(p => p.Id).SingleAsync();
        var readId = await db.Permissions.Where(p => p.Code == "receipt.read").Select(p => p.Id).SingleAsync();
        await db.RolePermissions.Where(g => g.RoleId == roleId && g.PermissionId == rejectId).ExecuteDeleteAsync();
        var path = $"/api/approvals/ImportReceipt/{receipt.Id}/reject";
        using (var denied = await Send(client, HttpMethod.Post, path, users[1].Id, "Admin", "qa-denied-reject", "{\"reason\":\"Không đạt yêu cầu\"}"))
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await db.IdempotencyRecords.CountAsync(r => r.UserId == users[1].Id)).Should().Be(0);
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = rejectId });
        await db.SaveChangesAsync();
        await db.RolePermissions.Where(g => g.RoleId == roleId && g.PermissionId == readId).ExecuteDeleteAsync();
        using (var deniedRead = await Send(client, HttpMethod.Get, $"/api/approvals/ImportReceipt/{receipt.Id}", users[1].Id, "Admin"))
            deniedRead.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var queue = await Send(client, HttpMethod.Get, "/api/approvals/queue?documentType=ImportReceipt", users[1].Id, "Admin"))
        {
            queue.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await queue.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("totalRecords").GetInt32().Should().Be(0);
        }
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = readId });
        await db.SaveChangesAsync();
        using (var maker = await Send(client, HttpMethod.Post, path, users[0].Id, "Admin", "qa-maker-reject", "{\"reason\":\"Không đạt yêu cầu\"}"))
            maker.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var outside = new Warehouse { Code = "QA-APPROVAL-OUTSIDE", Name = "Kho ngoài phạm vi" };
        db.Warehouses.Add(outside); await db.SaveChangesAsync();
        var foreign = Receipt("QA-APPROVAL-FOREIGN", outside.Id, users[0].Id, await db.Products.Include(p => p.Unit).SingleAsync(), ReceiptStatus.Received, 987m);
        db.ImportReceipts.Add(foreign); await db.SaveChangesAsync();
        foreach (var suffix in new[] { "", "/history" })
        {
            using var hidden = await Send(client, HttpMethod.Get, $"/api/approvals/ImportReceipt/{foreign.Id}{suffix}", users[1].Id, "Admin");
            hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await hidden.Content.ReadAsStringAsync()).Should().NotContain(foreign.Code);
        }
        using (var hidden = await Send(client, HttpMethod.Post, $"/api/approvals/ImportReceipt/{foreign.Id}/reject", users[1].Id, "Admin", "qa-foreign-reject", "{\"reason\":\"Không đạt yêu cầu\"}"))
            hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using (var success = await Send(client, HttpMethod.Post, path, users[1].Id, "Admin", "qa-success-reject", "{\"reason\":\"Không đạt yêu cầu\"}"))
            success.StatusCode.Should().Be(HttpStatusCode.OK, await success.Content.ReadAsStringAsync());
        using (var replay = await Send(client, HttpMethod.Post, path, users[1].Id, "Admin", "qa-success-reject", "{\"reason\":\"Không đạt yêu cầu\"}"))
            replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await db.AuditLogs.CountAsync(a => a.EntityName == "ImportReceipt" && a.EntityId == receipt.Id && a.Action == "ApprovalRejected")).Should().Be(1);
        using (var mismatch = await Send(client, HttpMethod.Post, path, users[1].Id, "Admin", "qa-success-reject", "{\"reason\":\"Lý do khác\"}"))
            mismatch.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await db.UserWarehouses.Where(w => w.UserId == users[1].Id).ExecuteDeleteAsync();
        using (var revokedScopeReplay = await Send(client, HttpMethod.Post, path, users[1].Id, "Admin", "qa-success-reject", "{\"reason\":\"Không đạt yêu cầu\"}"))
            revokedScopeReplay.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await db.RolePermissions.Where(g => g.RoleId == roleId && g.PermissionId == rejectId).ExecuteDeleteAsync();
        using (var revokedReplay = await Send(client, HttpMethod.Post, path, users[1].Id, "Admin", "qa-success-reject", "{\"reason\":\"Không đạt yêu cầu\"}"))
            revokedReplay.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [ApprovalSqlServerFact]
    public async Task ViewerResponses_FilterCostAtTheHttpBoundary()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var users = await db.Users.OrderBy(x => x.Id).ToArrayAsync();
        var warehouse = await db.Warehouses.SingleAsync();
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = users[0].Id, WarehouseId = warehouse.Id, CreatedBy = users[0].Id },
            new UserWarehouse { UserId = users[1].Id, WarehouseId = warehouse.Id, CreatedBy = users[0].Id });
        var receipt = Receipt("HTTP-COST", warehouse.Id, users[0].Id, await db.Products.Include(x => x.Unit).SingleAsync(), ReceiptStatus.Draft, 123.45m);
        db.ImportReceipts.Add(receipt);
        await db.SaveChangesAsync();

        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using (var admin = await Send(client, HttpMethod.Get, $"/api/importreceipts/{receipt.Id}", users[0].Id, "Admin"))
        {
            admin.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await admin.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("details")[0].GetProperty("unitPrice").GetDecimal().Should().Be(123.45m);
        }
        using (var manager = await Send(client, HttpMethod.Get, $"/api/importreceipts/{receipt.Id}", users[1].Id, "Manager"))
        {
            manager.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await manager.Content.ReadAsStringAsync());
            json.RootElement.GetProperty("details")[0].GetProperty("unitPrice").GetDecimal().Should().Be(123.45m);
        }
        // Classification follows the current database role, not the test JWT/header role.
        users[1].RoleId = await db.Roles.Where(r => r.RoleName == "Viewer").Select(r => r.Id).SingleAsync();
        await db.SaveChangesAsync();
        using (var list = await Send(client, HttpMethod.Get, "/api/importreceipts", users[1].Id, "Viewer"))
        {
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            var raw = await list.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(raw);
            PropertyNames(json.RootElement).Should().NotContain(x => x == "unitprice" || x == "cost" || x == "value");
        }
        using (var detail = await Send(client, HttpMethod.Get, $"/api/importreceipts/{receipt.Id}", users[1].Id, "Viewer"))
        {
            detail.StatusCode.Should().Be(HttpStatusCode.OK);
            var raw = await detail.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(raw);
            json.RootElement.GetProperty("details")[0].GetProperty("unitPrice").ValueKind.Should().Be(JsonValueKind.Null,
                "the current JSON contract retains the field but filters its value server-side");
            raw.Should().NotContain("123.45");
            PropertyNames(json.RootElement).Should().NotContain(x => x == "cost" || x == "value");
        }
        using (var error = await Send(client, HttpMethod.Get, "/api/importreceipts/2147483647", users[1].Id, "Viewer"))
        {
            error.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var raw = await error.Content.ReadAsStringAsync();
            raw.ToLowerInvariant().Should().NotContain("123.45").And.NotContain("unitprice")
                .And.NotContain("HTTP-COST");
        }
    }

    [ApprovalSqlServerFact]
    public async Task WarehouseScopedHttpUser_CannotReadMutateOrReplayAnotherWarehouseReceipt()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var users = await db.Users.OrderBy(x => x.Id).ToArrayAsync();
        var warehouseA = await db.Warehouses.SingleAsync();
        var warehouseB = new Warehouse { Code = "HTTP-WH-B", Name = "HTTP Warehouse B" };
        db.Warehouses.Add(warehouseB);
        await db.SaveChangesAsync();
        db.UserWarehouses.Add(new UserWarehouse { UserId = users[1].Id, WarehouseId = warehouseA.Id, CreatedBy = users[0].Id });
        var product = await db.Products.Include(x => x.Unit).SingleAsync();
        var draft = Receipt("HTTP-SCOPE-DRAFT", warehouseB.Id, users[0].Id, product, ReceiptStatus.Draft, 777.77m);
        var received = Receipt("HTTP-SCOPE-RECEIVED", warehouseB.Id, users[0].Id, product, ReceiptStatus.Received, 777.77m);
        var ready = Receipt("HTTP-SCOPE-READY", warehouseB.Id, users[0].Id, product, ReceiptStatus.ReadyToPost, 777.77m);
        db.ImportReceipts.AddRange(draft, received, ready);
        await db.SaveChangesAsync();
        var before = await Snapshot(db, warehouseB.Id);

        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using (var list = await Send(client, HttpMethod.Get, "/api/importreceipts", users[1].Id, "Manager"))
        {
            list.StatusCode.Should().Be(HttpStatusCode.OK);
            var raw = await list.Content.ReadAsStringAsync();
            raw.Should().NotContain("HTTP-SCOPE-").And.NotContain("777.77");
        }
        using (var detail = await Send(client, HttpMethod.Get, $"/api/importreceipts/{draft.Id}", users[1].Id, "Manager"))
        {
            detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await detail.Content.ReadAsStringAsync()).Should().NotContain("HTTP-SCOPE-").And.NotContain("777.77");
        }

        var receiveBody = JsonSerializer.Serialize(new { lines = new[] { new { lineId = draft.Details.Single().Id, receivedQuantity = 5m, acceptedQuantity = 5m, damagedQuantity = 0m, rejectedQuantity = 0m } } });
        await Denied(client, HttpMethod.Post, $"/api/importreceipts/{draft.Id}/receive", users[1].Id, "Manager", "scope-receive", receiveBody);
        await Denied(client, HttpMethod.Post, $"/api/importreceipts/{received.Id}/approve", users[1].Id, "Manager", "scope-approve");
        await Denied(client, HttpMethod.Post, $"/api/importreceipts/{ready.Id}/post", users[1].Id, "Manager", "scope-post");
        await Denied(client, HttpMethod.Post, $"/api/importreceipts/{ready.Id}/post", users[1].Id, "Manager", "scope-post");

        db.ChangeTracker.Clear();
        (await db.ImportReceipts.AsNoTracking().SingleAsync(x => x.Id == draft.Id)).Status.Should().Be(ReceiptStatus.Draft);
        (await db.ImportReceipts.AsNoTracking().SingleAsync(x => x.Id == received.Id)).Status.Should().Be(ReceiptStatus.Received);
        (await db.ImportReceipts.AsNoTracking().SingleAsync(x => x.Id == ready.Id)).Status.Should().Be(ReceiptStatus.ReadyToPost);
        (await Snapshot(db, warehouseB.Id)).Should().Be(before);
        (await db.IdempotencyRecords.AsNoTracking().CountAsync(x => x.UserId == users[1].Id)).Should().Be(0,
            "authorization failure rolls back the idempotency claim, so replay cannot expose a cached response");
    }

    private static ImportReceipt Receipt(string code, int warehouseId, int makerId, Product product, ReceiptStatus status, decimal unitPrice)
    {
        var unit = product.Unit;
        return new ImportReceipt
        {
            Code = code, WarehouseId = warehouseId, CreatedBy = makerId, Status = status,
            SupplierCodeSnapshot = "SECRET-SUPPLIER", SupplierNameSnapshot = "Secret Supplier",
            Details = [new ImportReceiptDetail
            {
                ProductId = product.Id, Quantity = 5m, ExpectedQuantity = 5m,
                ReceivedQuantity = status == ReceiptStatus.Draft ? 0m : 5m,
                AcceptedQuantity = status == ReceiptStatus.Draft ? 0m : 5m,
                BaseExpectedQuantity = 5m, BaseReceivedQuantity = status == ReceiptStatus.Draft ? 0m : 5m,
                BaseAcceptedQuantity = status == ReceiptStatus.Draft ? 0m : 5m,
                OperationUnitId = product.UnitId, OperationUnitCodeSnapshot = unit.Code, OperationUnitDecimalPlaces = unit.DecimalPlaces,
                BaseUnitId = product.UnitId, BaseUnitCodeSnapshot = unit.Code, BaseUnitDecimalPlaces = unit.DecimalPlaces,
                ConversionFactor = 1m, ConversionVersion = 1, UnitPrice = unitPrice
            }]
        };
    }

    private static async Task Denied(HttpClient client, HttpMethod method, string path, int actor, string role, string key, string? body = null)
    {
        using var response = await Send(client, method, path, actor, role, key, body);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("HTTP-SCOPE-").And.NotContain("777.77").And.NotContain("SECRET-SUPPLIER");
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, int actor, string role, string? key = null, string? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Actor", actor.ToString());
        request.Headers.Add("X-Test-Role", role);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (body is not null) request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return await client.SendAsync(request);
    }

    private static async Task<string> Snapshot(ErpKhoDbContext db, int warehouseId) => JsonSerializer.Serialize(new
    {
        Stock = await db.InventoryStocks.AsNoTracking().Where(x => x.WarehouseId == warehouseId).OrderBy(x => x.Id).ToListAsync(),
        Ledger = await db.InventoryTransactions.AsNoTracking().Where(x => x.WarehouseId == warehouseId).OrderBy(x => x.Id).ToListAsync(),
        Audits = await db.AuditLogs.AsNoTracking().Where(x => x.WarehouseId == warehouseId && x.Action == "ImportReceipt.Posted").CountAsync()
    });

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name.ToLowerInvariant();
                foreach (var nested in PropertyNames(property.Value)) yield return nested;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) foreach (var nested in PropertyNames(item)) yield return nested;
    }

    private static ErpKhoDbContext Context(string connection) => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(connection).Options);
    private static WebApplicationFactory<Program> Factory(string connection) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["JwtSettings:Secret"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ErpKhoDbContext>>();
            services.AddDbContext<ErpKhoDbContext>(options => options.UseSqlServer(connection));
            services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "OwnedHttpTest"; options.DefaultChallengeScheme = "OwnedHttpTest"; })
                .AddScheme<AuthenticationSchemeOptions, ApprovalHttpIntegrationTests.OwnedHttpAuthentication>("OwnedHttpTest", _ => { });
        });
    });
}
