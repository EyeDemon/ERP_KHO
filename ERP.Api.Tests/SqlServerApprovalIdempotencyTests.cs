using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.SqlIntegrationHarness;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Moq;
using System.Security.Claims;

namespace ERP.Api.Tests;

public sealed class ApprovalSqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "ERP_KHO_APPROVAL_SQLSERVER_ADMIN_CONNECTION";
    public ApprovalSqlServerFactAttribute()
    {
        var value = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Approval SQL test requires an isolated database source; missing configuration must not skip the test.");
    }
}

public sealed class SqlServerApprovalIdempotencyTests
{
    [ApprovalSqlServerFact]
    public async Task UniqueScopeRaceAndAuditFailureRollback_AreEnforcedBySqlServer()
    {
        await using var database = await ApprovalSafetyDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(ApprovalSqlServerFactAttribute.ConnectionVariable)!);
        await database.MigrateAndSeedAsync();

        var rawKey = "never-store-this-raw-key";
        var keyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey)));
        var userIds = await database.UserIdsAsync();
        var firstClaim = database.InsertRecordAsync(userIds[0], "ImportReceipt.Approve", keyHash, "A");
        var secondClaim = database.InsertRecordAsync(userIds[0], "ImportReceipt.Approve", keyHash, "A");
        var outcomes = await Task.WhenAll(Capture(firstClaim), Capture(secondClaim));
        outcomes.Count(x => x is null).Should().Be(1);
        outcomes.Count(x => x is DbUpdateException).Should().Be(1);

        await database.InsertRecordAsync(userIds[1], "ImportReceipt.Approve", keyHash, "A");
        await database.InsertRecordAsync(userIds[0], "Stocktake.Approve", keyHash, "A");
        (await database.StoredValuesAsync()).Should().NotContain(value => value.Contains(rawKey, StringComparison.Ordinal));

        var before = await database.CountsAsync();
        await database.AttemptAtomicFailureAsync(userIds[0]);
        (await database.CountsAsync()).Should().Be(before, "business, required audit and idempotency must roll back together");

        var filterBefore = await database.CountsAsync();
        var executions = 0;
        async Task<IActionResult> ExecuteBusiness(ErpKhoDbContext context)
        {
            Interlocked.Increment(ref executions);
            var stock = await context.InventoryStocks.SingleAsync();
            stock.Quantity -= 10m;
            context.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = stock.ProductId, WarehouseId = stock.WarehouseId,
                TransactionType = TransactionType.Export, Quantity = 10m,
                ReferenceId = 17, ReferenceType = "IdempotencyTest",
                TransactionDate = DateTime.UtcNow, CreatedBy = userIds[0]
            });
            context.AuditLogs.Add(new AuditLog
            {
                UserId = userIds[0], Action = "Test.Dispatched", EntityName = "InventoryStock", EntityId = stock.Id,
                WarehouseId = stock.WarehouseId, OldValues = "Quantity: 100", NewValues = "Quantity: 90", Result = "Success",
                Timestamp = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
            return new OkObjectResult(new { id = 17, status = "Approved" });
        }
        var firstRequest = database.ExecuteFilterAsync(userIds[0], "concurrent-filter-key", 17, ExecuteBusiness);
        var secondRequest = database.ExecuteFilterAsync(userIds[0], "concurrent-filter-key", 17, ExecuteBusiness);
        var concurrentResults = await Task.WhenAll(firstRequest, secondRequest);
        var replay = await database.ExecuteFilterAsync(userIds[0], "concurrent-filter-key", 17,
            _ => throw new InvalidOperationException("Completed replay must not execute the command."));
        var conflict = await database.ExecuteFilterAsync(userIds[0], "concurrent-filter-key", 18,
            _ => throw new InvalidOperationException("Fingerprint conflict must not execute the command."));

        executions.Should().Be(1);
        concurrentResults.Count(x => x is OkObjectResult).Should().Be(1);
        concurrentResults.Count(x => x is ContentResult { StatusCode: 200 }).Should().Be(1);
        replay.Should().BeOfType<ContentResult>().Which.Content.Should().Contain("\"status\":\"Approved\"");
        conflict.Should().BeOfType<ConflictObjectResult>();
        var filterAfter = await database.CountsAsync();
        filterAfter.Should().Be((filterBefore.Units, filterBefore.Audits + 1, filterBefore.Records + 1));
        (await database.InventoryEvidenceAsync()).Should().Be((90m, 1));
    }

    private static async Task<Exception?> Capture(Task action)
    {
        try { await action; return null; }
        catch (Exception ex) { return ex; }
    }

    internal sealed class ApprovalSafetyDatabase : IAsyncDisposable
    {
        private readonly string _master;
        private readonly string _database;
        private readonly string _runId;
        private bool _disposed;
        public string ConnectionString { get; }

        private ApprovalSafetyDatabase(string master, string database, string runId, string connectionString) =>
            (_master, _database, _runId, ConnectionString) = (master, database, runId, connectionString);

        public static async Task<ApprovalSafetyDatabase> CreateAsync(string source)
        {
            var runId = OwnedDatabasePolicy.NewRunId();
            var database = OwnedDatabasePolicy.NewDatabaseName(DateTime.UtcNow, runId);
            var masterBuilder = OwnedDatabasePolicy.MasterConnection(source);
            var sourceBuilder = OwnedDatabasePolicy.DatabaseConnection(source, database);
            Console.WriteLine($"Owned approval target: {database}; RunId={runId}.");
            await using var connection = new SqlConnection(masterBuilder.ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {OwnedDatabasePolicy.QuoteIdentifier(database)}";
            await command.ExecuteNonQueryAsync();
            var owned = new ApprovalSafetyDatabase(masterBuilder.ConnectionString, database, runId, sourceBuilder.ConnectionString);
            try
            {
                await owned.CreateMarkerAsync();
                return owned;
            }
            catch
            {
                await owned.DisposeAsync();
                throw;
            }
        }

        private async Task CreateMarkerAsync()
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                IF DB_NAME() <> @database THROW 51020, 'SQL integration DB_NAME mismatch.', 1;
                CREATE TABLE dbo.{OwnedDatabasePolicy.MarkerTable}
                (
                    MarkerType nvarchar(64) NOT NULL, RunId char(32) NOT NULL,
                    CreatedAtUtc datetime2 NOT NULL, HarnessVersion nvarchar(32) NOT NULL,
                    HeadReference char(40) NULL, DatabaseName sysname NOT NULL,
                    CONSTRAINT PK_{OwnedDatabasePolicy.MarkerTable} PRIMARY KEY (MarkerType, RunId)
                );
                INSERT dbo.{OwnedDatabasePolicy.MarkerTable}
                    (MarkerType,RunId,CreatedAtUtc,HarnessVersion,HeadReference,DatabaseName)
                VALUES (@marker,@runId,SYSUTCDATETIME(),N'1.0',NULL,@database);
                """;
            command.Parameters.AddWithValue("@marker", OwnedDatabasePolicy.MarkerType);
            command.Parameters.AddWithValue("@runId", _runId);
            command.Parameters.AddWithValue("@database", _database);
            await command.ExecuteNonQueryAsync();
        }

        private ErpKhoDbContext Context() => new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options);

        private ErpKhoDbContext Context(IRequestMetadata metadata) =>
            new(new DbContextOptionsBuilder<ErpKhoDbContext>().UseSqlServer(ConnectionString).Options, metadata);

        public async Task MigrateAndSeedAsync()
        {
            await using var context = Context();
            await context.Database.MigrateAsync();
            var role = new Role { RoleName = "ApprovalTestRole" };
            context.Roles.Add(role);
            context.Users.AddRange(
                new User { Username = "approval_test_user_1", FullName = "Test One", PasswordHash = "TEST_HASH_ONLY", Role = role },
                new User { Username = "approval_test_user_2", FullName = "Test Two", PasswordHash = "TEST_HASH_ONLY", Role = role });
            var unit = new Unit { Code = "IDEM_UNIT", Name = "Idempotency unit" };
            var product = new Product { Code = "IDEM_PRODUCT", Name = "Idempotency product", Unit = unit };
            var warehouse = new Warehouse { Code = "IDEM_WH", Name = "Idempotency warehouse" };
            context.InventoryStocks.Add(new InventoryStock
            {
                Product = product, Warehouse = warehouse, Quantity = 100m,
                ReservedQuantity = 0m, LastUpdated = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        public async Task<int[]> UserIdsAsync()
        {
            await using var context = Context();
            return await context.Users.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        }

        public async Task InsertRecordAsync(int userId, string scope, string keyHash, string fingerprint)
        {
            await using var context = Context();
            context.IdempotencyRecords.Add(new IdempotencyRecord
            {
                UserId = userId, CommandScope = scope, KeyHash = keyHash,
                RequestFingerprint = fingerprint.PadRight(64, '0'), CorrelationId = Guid.NewGuid().ToString("N"),
                Status = IdempotencyStatus.Completed, ResponseStatusCode = 200, CreatedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
            });
            await context.SaveChangesAsync();
        }

        public async Task<string[]> StoredValuesAsync()
        {
            await using var context = Context();
            return await context.IdempotencyRecords.Select(x => x.KeyHash + x.RequestFingerprint).ToArrayAsync();
        }

        public async Task<(int Units, int Audits, int Records)> CountsAsync()
        {
            await using var context = Context();
            return (await context.Units.CountAsync(), await context.AuditLogs.CountAsync(), await context.IdempotencyRecords.CountAsync());
        }

        public async Task<(decimal Quantity, int Transactions)> InventoryEvidenceAsync()
        {
            await using var context = Context();
            return (await context.InventoryStocks.Select(x => x.Quantity).SingleAsync(),
                await context.InventoryTransactions.CountAsync());
        }

        public async Task AttemptAtomicFailureAsync(int userId)
        {
            await using var context = Context();
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.Units.Add(new Unit { Code = "ROLLBACK", Name = "Rollback proof" });
            context.IdempotencyRecords.Add(new IdempotencyRecord
            {
                UserId = userId, CommandScope = "Atomic.Failure", KeyHash = new string('A', 64), RequestFingerprint = new string('B', 64),
                CorrelationId = Guid.NewGuid().ToString("N"), Status = IdempotencyStatus.Processing,
                CreatedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
            });
            context.AuditLogs.Add(new AuditLog { UserId = userId, Action = new string('X', 101), EntityName = "Unit", Timestamp = DateTime.UtcNow });
            var action = async () => await context.SaveChangesAsync();
            await action.Should().ThrowAsync<DbUpdateException>();
            await transaction.RollbackAsync();
        }

        public async Task<IActionResult?> ExecuteFilterAsync(
            int userId, string key, int documentId, Func<ErpKhoDbContext, Task<IActionResult>> operation)
        {
            var metadata = new RequestMetadata { CorrelationId = Guid.NewGuid().ToString("N") };
            await using var context = Context(metadata);
            var warehouseAuthorization = new Mock<IWarehouseAuthorizationService>();
            warehouseAuthorization
                .Setup(x => x.EnsureWarehouseAccessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var filter = new IdempotentCommandFilter("StockTransfer.Approve", context, metadata, warehouseAuthorization.Object);
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
            };
            http.Request.Headers[IdempotentCommandFilter.HeaderName] = key;
            var actionContext = new ActionContext(
                http, new RouteData(), new ActionDescriptor(),
                new Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary());
            var executing = new ActionExecutingContext(
                actionContext, [], new Dictionary<string, object?> { ["id"] = documentId }, new object());
            IActionResult? executedResult = null;
            ActionExecutionDelegate next = async () => new ActionExecutedContext(actionContext, [], new object())
            {
                Result = executedResult = await operation(context)
            };

            await filter.OnActionExecutionAsync(executing, next);
            return executing.Result ?? executedResult;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            OwnedDatabasePolicy.EnsureAllowedName(_database);
            await using (var database = new SqlConnection(ConnectionString))
            {
                await database.OpenAsync();
                await using var verify = database.CreateCommand();
                verify.CommandText = $"""
                    SELECT COUNT(*) FROM dbo.{OwnedDatabasePolicy.MarkerTable}
                    WHERE MarkerType=@marker AND RunId=@runId AND DatabaseName=@database AND DB_NAME()=@database
                    """;
                verify.Parameters.AddWithValue("@marker", OwnedDatabasePolicy.MarkerType);
                verify.Parameters.AddWithValue("@runId", _runId);
                verify.Parameters.AddWithValue("@database", _database);
                var markerCount = Convert.ToInt32(await verify.ExecuteScalarAsync());
                OwnedDatabasePolicy.EnsureOwnership(_database, _runId, database.Database, OwnedDatabasePolicy.MarkerType, _runId, _database, markerCount);
            }
            await using var connection = new SqlConnection(_master);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            var quoted = OwnedDatabasePolicy.QuoteIdentifier(_database);
            command.CommandText = $"ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted};";
            await command.ExecuteNonQueryAsync();
            await using var absent = connection.CreateCommand();
            absent.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@database";
            absent.Parameters.AddWithValue("@database", _database);
            if (Convert.ToInt32(await absent.ExecuteScalarAsync()) != 0)
                throw new InvalidOperationException("Approval SQL test database cleanup verification failed.");
            _disposed = true;
            Console.WriteLine($"Owned approval target cleaned: {_database}; RunId={_runId}.");
        }
    }
}
