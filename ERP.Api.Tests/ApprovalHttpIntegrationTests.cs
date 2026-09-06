using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ERP.Api.Tests;

public sealed class ApprovalHttpIntegrationTests
{
    [ApprovalSqlServerFact]
    public async Task ConcurrentSameKeyHttpApprovals_OverlapAtIdempotencyBoundaryAndExecuteOnce()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var maker = await db.Users.OrderBy(x => x.Id).FirstAsync();
        var checker = await db.Users.OrderBy(x => x.Id).LastAsync();
        var source = await db.Warehouses.SingleAsync();
        var destination = new Warehouse { Code = "HTTP_RACE_DEST", Name = "HTTP race destination" };
        db.Warehouses.Add(destination);
        await db.SaveChangesAsync();
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = checker.Id, WarehouseId = source.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = checker.Id, WarehouseId = destination.Id, CreatedBy = maker.Id });
        var transfer = new StockTransfer
        {
            Code = "HTTP-RACE-" + Guid.NewGuid().ToString("N"),
            SourceWarehouseId = source.Id,
            DestinationWarehouseId = destination.Id,
            CreatedBy = maker.Id
        };
        db.StockTransfers.Add(transfer);
        await db.SaveChangesAsync();

        var inventoryBefore = await InventorySnapshot(db);
        var gate = new IdempotencyOverlapInterceptor();
        await using var factory = Factory(database.ConnectionString, gate);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var rawKey = "http-race-" + Guid.NewGuid().ToString("N");
        HttpRequestMessage Request(int documentId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/stock-transfers/{documentId}/approve");
            request.Headers.Add("X-Test-Actor", checker.Id.ToString());
            request.Headers.Add("X-Test-Role", "Manager");
            request.Headers.Add("Idempotency-Key", rawKey);
            return request;
        }

        using var firstRequest = Request(transfer.Id);
        using var secondRequest = Request(transfer.Id);
        var first = client.SendAsync(firstRequest);
        var second = client.SendAsync(secondRequest);
        await gate.BothArrived.WaitAsync(TimeSpan.FromSeconds(15));
        gate.Arrivals.Should().Be(2, "both HTTP requests must overlap before either idempotency claim is released");
        first.IsCompleted.Should().BeFalse();
        second.IsCompleted.Should().BeFalse();
        gate.Release();

        using var firstResponse = await first;
        using var secondResponse = await second;
        firstResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var secondBody = await secondResponse.Content.ReadAsStringAsync();
        firstBody.Should().Be(secondBody).And.NotContain(rawKey);
        secondBody.Should().NotContain(ERP.Api.Infrastructure.IdempotentCommandFilter.Hash(rawKey));

        using var replayRequest = Request(transfer.Id);
        using var replay = await client.SendAsync(replayRequest);
        replay.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await replay.Content.ReadAsStringAsync()).Should().Be(firstBody);

        var other = new StockTransfer
        {
            Code = "HTTP-RACE-OTHER-" + Guid.NewGuid().ToString("N"),
            SourceWarehouseId = source.Id,
            DestinationWarehouseId = destination.Id,
            CreatedBy = maker.Id
        };
        db.StockTransfers.Add(other);
        await db.SaveChangesAsync();
        using var conflictRequest = Request(other.Id);
        using var conflict = await client.SendAsync(conflictRequest);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await db.StockTransfers.AsNoTracking().SingleAsync(x => x.Id == transfer.Id)).Status.Should().Be(StockTransferStatus.Approved);
        (await db.StockTransfers.AsNoTracking().SingleAsync(x => x.Id == other.Id)).Status.Should().Be(StockTransferStatus.Draft);
        (await db.AuditLogs.CountAsync(x => x.EntityId == transfer.Id && x.Action == "StockTransfer.Approved")).Should().Be(1);
        var keyHash = ERP.Api.Infrastructure.IdempotentCommandFilter.Hash(rawKey);
        var records = await db.IdempotencyRecords.AsNoTracking()
            .Where(x => x.UserId == checker.Id && x.CommandScope == "StockTransfer.Approve" && x.KeyHash == keyHash)
            .ToListAsync();
        records.Should().ContainSingle().Which.Status.Should().Be(IdempotencyStatus.Completed);
        records[0].RequestFingerprint.Should().NotBeNullOrWhiteSpace().And.NotContain(rawKey);
        (await InventorySnapshot(db)).Should().Be(inventoryBefore);
    }

    [ApprovalSqlServerFact]
    public async Task HttpApprove_EnforcesScopeMakerPolicyReplayAndInventoryInvariants()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var maker = await db.Users.OrderBy(x => x.Id).FirstAsync();
        var checker = await db.Users.OrderBy(x => x.Id).LastAsync();
        var source = await db.Warehouses.SingleAsync();
        var destination = new Warehouse { Code = "HTTP_DEST", Name = "HTTP destination" };
        db.Warehouses.Add(destination); await db.SaveChangesAsync();
        db.UserWarehouses.AddRange(new UserWarehouse { UserId = maker.Id, WarehouseId = source.Id, CreatedBy = maker.Id },
            new UserWarehouse { UserId = maker.Id, WarehouseId = destination.Id, CreatedBy = maker.Id });
        await db.SaveChangesAsync();
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var before = await InventorySnapshot(db);
        async Task<StockTransfer> Draft()
        {
            var row = new StockTransfer { Code = "HTTP-" + Guid.NewGuid().ToString("N"), SourceWarehouseId = source.Id, DestinationWarehouseId = destination.Id, CreatedBy = maker.Id };
            db.StockTransfers.Add(row); await db.SaveChangesAsync(); return row;
        }
        async Task Scope(params int[] ids)
        {
            db.UserWarehouses.RemoveRange(await db.UserWarehouses.Where(x => x.UserId == checker.Id).ToListAsync());
            await db.SaveChangesAsync();
            db.UserWarehouses.AddRange(ids.Select(id => new UserWarehouse { UserId = checker.Id, WarehouseId = id, CreatedBy = maker.Id }));
            await db.SaveChangesAsync();
        }
        async Task<HttpResponseMessage> Approve(int id, int actor, string role, string key)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/stock-transfers/{id}/approve");
            request.Headers.Add("X-Test-Actor", actor.ToString()); request.Headers.Add("X-Test-Role", role);
            request.Headers.Add("Idempotency-Key", key);
            return await client.SendAsync(request);
        }
        async Task Denied(int actor, string role, HttpStatusCode expected)
        {
            var row = await Draft(); using var response = await Approve(row.Id, actor, role, Guid.NewGuid().ToString("N"));
            response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
            (await db.StockTransfers.AsNoTracking().SingleAsync(x => x.Id == row.Id)).Status.Should().Be(StockTransferStatus.Draft);
            (await db.AuditLogs.CountAsync(x => x.EntityName == "StockTransfer" && x.EntityId == row.Id && x.Action == "StockTransfer.Approved")).Should().Be(0);
            (await InventorySnapshot(db)).Should().Be(before);
        }
        await Scope(source.Id); await Denied(checker.Id, "Manager", HttpStatusCode.NotFound);
        await Scope(destination.Id); await Denied(checker.Id, "Manager", HttpStatusCode.NotFound);
        await Scope(); await Denied(checker.Id, "Manager", HttpStatusCode.NotFound);
        await Scope(source.Id, destination.Id);
        await Denied(maker.Id, "Manager", HttpStatusCode.Forbidden);
        await Denied(maker.Id, "Admin", HttpStatusCode.Forbidden);
        await Denied(checker.Id, "WarehouseStaff", HttpStatusCode.Forbidden);
        await Denied(checker.Id, "Viewer", HttpStatusCode.Forbidden);
        foreach (var role in new[] { "Manager", "Admin" })
        {
            var row = await Draft(); var key = Guid.NewGuid().ToString("N");
            using var result = await Approve(row.Id, checker.Id, role, key); result.StatusCode.Should().Be(HttpStatusCode.NoContent);
            using var replay = await Approve(row.Id, checker.Id, role, key); replay.StatusCode.Should().Be(result.StatusCode);
            using var repeat = await Approve(row.Id, checker.Id, role, Guid.NewGuid().ToString("N")); repeat.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var other = await Draft();
            using var conflict = await Approve(other.Id, checker.Id, role, key); conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await db.StockTransfers.AsNoTracking().SingleAsync(x => x.Id == other.Id)).Status.Should().Be(StockTransferStatus.Draft);
            (await db.StockTransfers.AsNoTracking().SingleAsync(x => x.Id == row.Id)).Status.Should().Be(StockTransferStatus.Approved);
            (await db.AuditLogs.CountAsync(x => x.EntityId == row.Id && x.Action == "StockTransfer.Approved")).Should().Be(1);
            var hash = ERP.Api.Infrastructure.IdempotentCommandFilter.Hash(key);
            (await db.IdempotencyRecords.AsNoTracking().SingleAsync(x => x.KeyHash == hash)).Status.Should().Be(IdempotencyStatus.Completed);
            (await InventorySnapshot(db)).Should().Be(before);
        }
    }

    [ApprovalSqlServerFact]
    public async Task SqlMaterializedQueueDetailHistory_EmitUtcAndRespectDateBoundariesOverHttp()
    {
        await using var database = await SqlServerApprovalIdempotencyTests.ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();
        await using var db = Context(database.ConnectionString);
        var users = await db.Users.OrderBy(x => x.Id).ToArrayAsync(); var warehouse = await db.Warehouses.SingleAsync();
        var instant = new DateTime(2026, 9, 5, 1, 2, 3, DateTimeKind.Utc);
        var receipt = new ImportReceipt { Code = "UTC-HTTP", CreatedBy = users[0].Id, WarehouseId = warehouse.Id, CreatedAt = instant };
        db.ImportReceipts.Add(receipt); await db.SaveChangesAsync();
        db.AuditLogs.Add(new AuditLog { UserId = users[1].Id, Action = "ApprovalRejected", EntityName = "ImportReceipt", EntityId = receipt.Id, WarehouseId = warehouse.Id, Timestamp = instant, OldValues = "Status: Draft", NewValues = "Status: Cancelled", Result = "Success", Reason = "Synthetic contract evidence" });
        await db.SaveChangesAsync();
        (await db.ImportReceipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id)).CreatedAt.Kind.Should().Be(DateTimeKind.Unspecified);
        await using var factory = Factory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Test-Actor", users[1].Id.ToString()); client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        foreach (var path in new[] { "/api/approvals/queue?keyword=UTC-HTTP", $"/api/approvals/ImportReceipt/{receipt.Id}", $"/api/approvals/ImportReceipt/{receipt.Id}/history", "/api/approvals/history?documentType=ImportReceipt" })
        {
            using var response = await client.GetAsync(path); response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var timestamps = Timestamps(json.RootElement).ToArray(); timestamps.Should().NotBeEmpty();
            timestamps.Should().OnlyContain(x => x == "2026-09-05T01:02:03Z");
        }
        using (var agingJson = JsonDocument.Parse(await client.GetStringAsync("/api/approvals/queue?keyword=UTC-HTTP")))
        {
            var item = agingJson.RootElement.GetProperty("items")[0];
            item.GetProperty("waitingMinutes").GetInt64().Should().BeGreaterThanOrEqualTo(0);
            item.GetProperty("slaStatus").GetString().Should().BeOneOf("Normal", "Warning", "Overdue");
        }
        foreach (var bounds in new[] { "fromUtc=2026-09-05T01:02:03Z&toUtc=2026-09-05T01:02:03Z", "fromUtc=2026-09-05T08:02:03%2B07:00&toUtc=2026-09-05T08:02:03%2B07:00" })
        {
            using var json = JsonDocument.Parse(await client.GetStringAsync("/api/approvals/queue?keyword=UTC-HTTP&" + bounds));
            json.RootElement.GetProperty("totalRecords").GetInt32().Should().Be(1);
        }
        using var excluded = JsonDocument.Parse(await client.GetStringAsync("/api/approvals/queue?keyword=UTC-HTTP&fromUtc=2026-09-05T01:02:04Z"));
        excluded.RootElement.GetProperty("totalRecords").GetInt32().Should().Be(0);
    }

    private static IEnumerable<string> Timestamps(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "requestedAtUtc" or "timestampUtc") yield return property.Value.GetString()!;
                else foreach (var value in Timestamps(property.Value)) yield return value;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) foreach (var value in Timestamps(item)) yield return value;
    }
    private static ErpKhoDbContext Context(string connection) => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(connection).Options);
    private static async Task<string> InventorySnapshot(ErpKhoDbContext db) => JsonSerializer.Serialize(new {
        Stocks = await db.InventoryStocks.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Reservations = await db.StockReservations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
        Ledger = await db.InventoryTransactions.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
    });
    private static WebApplicationFactory<Program> Factory(string connection, SaveChangesInterceptor? interceptor = null) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?> {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["JwtSettings:Secret"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48))
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ErpKhoDbContext>>();
            services.AddDbContext<ErpKhoDbContext>(options =>
            {
                options.UseSqlServer(connection);
                if (interceptor is not null) options.AddInterceptors(interceptor);
            });
            services.AddAuthentication(options => { options.DefaultAuthenticateScheme = "OwnedHttpTest"; options.DefaultChallengeScheme = "OwnedHttpTest"; })
                .AddScheme<AuthenticationSchemeOptions, OwnedHttpAuthentication>("OwnedHttpTest", _ => { });
        });
    });

    private sealed class IdempotencyOverlapInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public int Arrivals => Volatile.Read(ref _arrivals);
        public Task BothArrived => _bothArrived.Task;
        public void Release() => _release.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<IdempotencyRecord>()
                    .Any(x => x.State == EntityState.Added && x.Entity.CommandScope == "StockTransfer.Approve") == true)
            {
                if (Interlocked.Increment(ref _arrivals) == 2) _bothArrived.TrySetResult();
                await _release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
    public sealed class OwnedHttpAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Actor", out var actor)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, actor.ToString()), new Claim(ClaimTypes.Role, Request.Headers["X-Test-Role"].ToString()) }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
