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

public sealed class InboundHttpIntegrationTests
{
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
