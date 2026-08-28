using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using ERP.Application.Options;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerExportStockCollection
{
    public const string Name = "SQL Server export stock concurrency";
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "ERP_KHO_SQLSERVER_TEST_CONNECTION";

    public SqlServerFactAttribute()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Skip = $"Set {ConnectionVariable} to run SQL Server integration tests.";
            return;
        }

        var database = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        if (!string.Equals(database, "ERP_KHO", StringComparison.OrdinalIgnoreCase))
        {
            Skip = $"{ConnectionVariable} must target the ERP_KHO database.";
        }
    }
}

[Collection(SqlServerExportStockCollection.Name)]
public class SqlServerExportStockConcurrencyTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;

    [SqlServerFact]
    public async Task CompetingReceipts_OnlyOneSucceeds_StockAndHistoryStayConsistent()
    {
        await using var scenario = await SqlServerScenario.CreateAsync(ConnectionString, [100m], [[(0, 80m)], [(0, 80m)]]);

        var outcomes = await ApproveConcurrentlyAsync(scenario.ReceiptIds[0], scenario.ReceiptIds[1], scenario.UserId);

        outcomes.Count(error => error is null).Should().Be(1);
        outcomes.Count(error => error is ConcurrencyException).Should().Be(1);
        await scenario.AssertStateAsync([20m], 80m, dispatchedReceiptCount: 1);
    }

    [SqlServerFact]
    public async Task ConcurrentValidReceipts_BothSucceed_StockReachesZero()
    {
        await using var scenario = await SqlServerScenario.CreateAsync(ConnectionString, [100m], [[(0, 40m)], [(0, 60m)]]);

        var outcomes = await ApproveConcurrentlyAsync(scenario.ReceiptIds[0], scenario.ReceiptIds[1], scenario.UserId);

        outcomes.Should().AllSatisfy(error => error.Should().BeNull());
        await scenario.AssertStateAsync([0m], 100m, dispatchedReceiptCount: 2);
    }

    [SqlServerFact]
    public async Task InsufficientStock_RollsBackReceiptStockAndHistory()
    {
        await using var scenario = await SqlServerScenario.CreateAsync(ConnectionString, [50m], [[(0, 60m)]]);

        var error = await ApproveAsync(scenario.ReceiptIds[0], scenario.UserId);

        error.Should().BeOfType<ConcurrencyException>();
        await scenario.AssertStateAsync([50m], 0m, dispatchedReceiptCount: 0);
    }

    [SqlServerFact]
    public async Task SameReceiptApprovedConcurrently_DeductsAndWritesHistoryOnce()
    {
        await using var scenario = await SqlServerScenario.CreateAsync(ConnectionString, [100m], [[(0, 40m)]]);

        var outcomes = await ApproveConcurrentlyAsync(scenario.ReceiptIds[0], scenario.ReceiptIds[0], scenario.UserId);

        outcomes.Count(error => error is null).Should().Be(1);
        outcomes.Count(error => error is ConcurrencyException).Should().Be(1);
        await scenario.AssertStateAsync([60m], 40m, dispatchedReceiptCount: 1);
    }

    [SqlServerFact]
    public async Task MultiLineReceipt_WhenSecondLineIsInsufficient_RollsBackEveryLine()
    {
        await using var scenario = await SqlServerScenario.CreateAsync(ConnectionString, [100m, 10m], [[(0, 40m), (1, 20m)]]);

        var error = await ApproveAsync(scenario.ReceiptIds[0], scenario.UserId);

        error.Should().BeOfType<ConcurrencyException>();
        await scenario.AssertStateAsync([100m, 10m], 0m, dispatchedReceiptCount: 0);
    }

    [SqlServerFact]
    public async Task WriteDisabled_RejectsBeforeStockReceiptTransactionOrAuditMutation()
    {
        await using var scenario = await SqlServerScenario.CreateAsync(ConnectionString, [100m], [[(0, 40m)]]);

        var error = await ApproveAsync(
            scenario.ReceiptIds[0],
            scenario.UserId,
            options: new ExportReceiptOptions { WriteEnabled = false });

        error.Should().BeOfType<ERP.Application.Exceptions.ServiceUnavailableException>();
        await scenario.AssertStateAsync([100m], 0m, dispatchedReceiptCount: 0);
        await scenario.AssertWriteDisabledStateAsync();
    }

    private static async Task<Exception?[]> ApproveConcurrentlyAsync(int firstReceiptId, int secondReceiptId, int userId)
    {
        using var barrier = new Barrier(2);
        var firstTask = Task.Run(() => ApproveAsync(firstReceiptId, userId, barrier));
        var secondTask = Task.Run(() => ApproveAsync(secondReceiptId, userId, barrier));
        return await Task.WhenAll(firstTask, secondTask);
    }

    private static async Task<Exception?> ApproveAsync(int receiptId, int userId, Barrier? barrier = null, ExportReceiptOptions? options = null)
    {
        await using var context = CreateContext(ConnectionString);
        IExportReceiptRepository receiptRepository = new ExportReceiptRepository(context);
        if (barrier != null)
        {
            receiptRepository = new BarrierExportReceiptRepository(receiptRepository, barrier);
        }

        var service = new ExportReceiptService(
            receiptRepository,
            new InventoryStockRepository(context),
            new InventoryTransactionRepository(context),
            new UnitOfWork(context),
            new AuditLogRepository(context),
            options);

        try
        {
            await service.ApproveAsync(receiptId, userId);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static ErpKhoDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseSqlServer(connectionString, sql => sql.CommandTimeout(30))
            .Options;
        return new ErpKhoDbContext(options);
    }

    private sealed class BarrierExportReceiptRepository : IExportReceiptRepository
    {
        private readonly IExportReceiptRepository _inner;
        private readonly Barrier _barrier;

        public BarrierExportReceiptRepository(IExportReceiptRepository inner, Barrier barrier)
        {
            _inner = inner;
            _barrier = barrier;
        }

        public async Task<ExportReceipt?> GetByIdWithDetailsAsync(int id)
        {
            var receipt = await _inner.GetByIdWithDetailsAsync(id);
            if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(20)))
            {
                throw new TimeoutException("Concurrent approval barrier timed out.");
            }

            return receipt;
        }

        public Task<IEnumerable<ExportReceipt>> GetListAsync() => _inner.GetListAsync();
        public Task<bool> ExistsByCodeAsync(string code, int? excludeId = null) => _inner.ExistsByCodeAsync(code, excludeId);
        public Task<ExportReceipt?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);
        public Task<IEnumerable<ExportReceipt>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);
        public Task<IEnumerable<ExportReceipt>> FindAsync(System.Linq.Expressions.Expression<Func<ExportReceipt, bool>> predicate, CancellationToken cancellationToken = default) => _inner.FindAsync(predicate, cancellationToken);
        public Task<ExportReceipt> AddAsync(ExportReceipt entity, CancellationToken cancellationToken = default) => _inner.AddAsync(entity, cancellationToken);
        public Task UpdateAsync(ExportReceipt entity, CancellationToken cancellationToken = default) => _inner.UpdateAsync(entity, cancellationToken);
        public Task DeleteAsync(ExportReceipt entity, CancellationToken cancellationToken = default) => _inner.DeleteAsync(entity, cancellationToken);
    }

    private sealed class SqlServerScenario : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly string _prefix;

        public int UserId { get; private set; }
        public int WarehouseId { get; private set; }
        public int[] ProductIds { get; private set; } = [];
        public int[] ReceiptIds { get; private set; } = [];

        private SqlServerScenario(string connectionString, string prefix)
        {
            _connectionString = connectionString;
            _prefix = prefix;
        }

        public static async Task<SqlServerScenario> CreateAsync(
            string connectionString,
            decimal[] initialStocks,
            (int ProductIndex, decimal Quantity)[][] receiptLines)
        {
            var prefix = $"QAC{Guid.NewGuid():N}"[..15];
            var scenario = new SqlServerScenario(connectionString, prefix);
            await using var context = CreateContext(connectionString);

            (await context.Database.CanConnectAsync()).Should().BeTrue();
            context.Database.GetDbConnection().Database.Should().Be("ERP_KHO");

            var role = new Role { RoleName = $"{prefix}R", Description = "SQL concurrency test" };
            var unit = new Unit { Code = $"{prefix}U", Name = "SQL concurrency unit", IsActive = true };
            var warehouse = new Warehouse { Code = $"{prefix}W", Name = "SQL concurrency warehouse", IsActive = true };
            context.AddRange(role, unit, warehouse);
            await context.SaveChangesAsync();

            var user = new User
            {
                Username = $"{prefix}USR",
                PasswordHash = "integration-test-only",
                FullName = "SQL concurrency user",
                RoleId = role.Id,
                IsActive = true
            };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var products = initialStocks.Select((_, index) => new Product
            {
                Code = $"{prefix}P{index}",
                Name = $"SQL concurrency product {index}",
                UnitId = unit.Id,
                IsActive = true
            }).ToArray();
            context.Products.AddRange(products);
            await context.SaveChangesAsync();

            context.InventoryStocks.AddRange(products.Select((product, index) => new InventoryStock
            {
                ProductId = product.Id,
                WarehouseId = warehouse.Id,
                Quantity = initialStocks[index]
            }));

            var receipts = receiptLines.Select((lines, receiptIndex) => new ExportReceipt
            {
                Code = $"{prefix}E{receiptIndex}",
                WarehouseId = warehouse.Id,
                Status = ReceiptStatus.Draft,
                CreatedBy = user.Id,
                Details = lines.Select(line => new ExportReceiptDetail
                {
                    ProductId = products[line.ProductIndex].Id,
                    Quantity = line.Quantity,
                    UnitPrice = 1m
                }).ToList()
            }).ToArray();
            context.ExportReceipts.AddRange(receipts);
            await context.SaveChangesAsync();

            scenario.UserId = user.Id;
            scenario.WarehouseId = warehouse.Id;
            scenario.ProductIds = products.Select(product => product.Id).ToArray();
            scenario.ReceiptIds = receipts.Select(receipt => receipt.Id).ToArray();
            return scenario;
        }

        public async Task AssertStateAsync(decimal[] expectedStocks, decimal expectedExportTotal, int dispatchedReceiptCount)
        {
            await using var context = CreateContext(_connectionString);
            var stocks = await context.InventoryStocks
                .Where(stock => ProductIds.Contains(stock.ProductId) && stock.WarehouseId == WarehouseId)
                .OrderBy(stock => stock.ProductId)
                .Select(stock => stock.Quantity)
                .ToArrayAsync();
            stocks.Should().Equal(expectedStocks);

            var transactions = await context.InventoryTransactions
                .Where(transaction => transaction.ReferenceType == "ExportReceipt" && ReceiptIds.Contains(transaction.ReferenceId!.Value))
                .ToListAsync();
            transactions.Sum(transaction => transaction.Quantity).Should().Be(expectedExportTotal);
            transactions.Should().OnlyContain(transaction => transaction.TransactionType == TransactionType.Export);

            var dispatched = await context.ExportReceipts
                .CountAsync(receipt => ReceiptIds.Contains(receipt.Id) && receipt.Status == ReceiptStatus.Dispatched);
            dispatched.Should().Be(dispatchedReceiptCount);
        }

        public async Task AssertWriteDisabledStateAsync()
        {
            await using var context = CreateContext(_connectionString);
            (await context.InventoryStocks
                .Where(stock => ProductIds.Contains(stock.ProductId) && stock.WarehouseId == WarehouseId)
                .SumAsync(stock => stock.ReservedQuantity)).Should().Be(0m);
            (await context.ExportReceipts
                .Where(receipt => ReceiptIds.Contains(receipt.Id))
                .AllAsync(receipt => receipt.Status == ReceiptStatus.Draft)).Should().BeTrue();
            (await context.StockReservations
                .CountAsync(reservation => reservation.SourceType == "ExportReceipt" && reservation.SourceId.HasValue && ReceiptIds.Contains(reservation.SourceId.Value)))
                .Should().Be(0);
            (await context.AuditLogs
                .CountAsync(log => log.EntityName == "ExportReceipt" && log.EntityId.HasValue && ReceiptIds.Contains(log.EntityId.Value)))
                .Should().Be(0);
        }

        public async ValueTask DisposeAsync()
        {
            await using var context = CreateContext(_connectionString);
            await context.InventoryTransactions
                .Where(transaction => transaction.ReferenceType == "ExportReceipt" && transaction.ReferenceId.HasValue && ReceiptIds.Contains(transaction.ReferenceId.Value))
                .ExecuteDeleteAsync();
            await context.AuditLogs
                .Where(log => log.EntityName == "ExportReceipt" && log.EntityId.HasValue && ReceiptIds.Contains(log.EntityId.Value))
                .ExecuteDeleteAsync();
            await context.ExportReceiptDetails
                .Where(detail => ReceiptIds.Contains(detail.ExportReceiptId))
                .ExecuteDeleteAsync();
            await context.ExportReceipts
                .Where(receipt => ReceiptIds.Contains(receipt.Id))
                .ExecuteDeleteAsync();
            await context.InventoryStocks
                .Where(stock => ProductIds.Contains(stock.ProductId) && stock.WarehouseId == WarehouseId)
                .ExecuteDeleteAsync();
            await context.Products.Where(product => product.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
            await context.Users.Where(user => user.Username.StartsWith(_prefix)).ExecuteDeleteAsync();
            await context.Warehouses.Where(warehouse => warehouse.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
            await context.Units.Where(unit => unit.Code.StartsWith(_prefix)).ExecuteDeleteAsync();
            await context.Roles.Where(role => role.RoleName.StartsWith(_prefix)).ExecuteDeleteAsync();
        }
    }
}
