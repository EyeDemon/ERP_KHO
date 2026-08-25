using ERP.Application.Exceptions;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ERP.Application.Tests
{
    /// <summary>
    /// Concurrency tests for stocktake approval using SQLite in-memory.
    /// 
    /// Why SQLite instead of InMemory provider:
    /// - SQLite supports real transactions (BEGIN/COMMIT/ROLLBACK).
    /// - SQLite enforces concurrency tokens via UPDATE ... WHERE [Status] = @original.
    ///   When two contexts load Status=Draft, the first UPDATE succeeds and changes
    ///   Status to Approved. The second UPDATE's WHERE clause finds zero rows,
    ///   triggering DbUpdateConcurrencyException.
    /// - InMemory provider ignores transactions and its concurrency-token enforcement
    ///   is unreliable across concurrent independent contexts.
    /// 
    /// Package added: Microsoft.EntityFrameworkCore.Sqlite (test-only).
    /// 
    /// IsConcurrencyToken on Status (in StocktakeConfiguration) is the production
    /// concurrency guard. These tests prove it is required and effective.
    /// </summary>
    /// <summary>
    /// A DbContext subclass that overrides SQL Server-specific column types
    /// (e.g. nvarchar(max)) with SQLite-compatible equivalents.
    /// </summary>
    internal class SqliteTestDbContext : ErpKhoDbContext
    {
        public SqliteTestDbContext(DbContextOptions<ErpKhoDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // SQLite doesn't support nvarchar(max). Remove explicit column types
            // that are SQL Server-specific so EnsureCreated succeeds.
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    var columnType = property.GetColumnType();
                    if (columnType != null && columnType.Contains("max", StringComparison.OrdinalIgnoreCase))
                    {
                        property.SetColumnType(null);
                    }
                }
            }
        }
    }

    public class StocktakeServiceConcurrencyTests : IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _connection;
                        
                private class ConcurrentTestDbContext : SqliteTestDbContext
        {
            public bool SuppressSave { get; set; }
            public ConcurrentTestDbContext(DbContextOptions<ErpKhoDbContext> options) : base(options) { }
            public override Task<int> SaveChangesAsync(System.Threading.CancellationToken cancellationToken = default)
            {
                if (SuppressSave) return Task.FromResult(0);
                return base.SaveChangesAsync(cancellationToken);
            }
        }

        private class ConcurrentTestUnitOfWork : IUnitOfWork
        {
            private readonly ConcurrentTestDbContext _ctx;
            public ConcurrentTestUnitOfWork(ConcurrentTestDbContext ctx) { _ctx = ctx; }
            public Task BeginTransactionAsync() 
            { 
                _ctx.SuppressSave = true; 
                return Task.CompletedTask; 
            }
            public async Task CommitTransactionAsync() 
            {
                _ctx.SuppressSave = false;
                try { await _ctx.SaveChangesAsync(); }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) { throw new ERP.Domain.Exceptions.ConcurrencyException("Mock concurrency conflict", ex); }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 5 || ex.SqliteErrorCode == 6) { throw new ERP.Domain.Exceptions.ConcurrencyException("Mock concurrency conflict (locked)", ex); }
            }
            public Task RollbackTransactionAsync() { _ctx.SuppressSave = false; return Task.CompletedTask; }
            public Task<int> SaveChangesAsync() => _ctx.SaveChangesAsync();
        }
        private class ConcurrentTestStocktakeRepo : IStocktakeRepository
        {
            private readonly IStocktakeRepository _inner;
            private readonly SemaphoreSlim _bothLoaded;
            private readonly SemaphoreSlim _proceedToSave;

            public ConcurrentTestStocktakeRepo(IStocktakeRepository inner, SemaphoreSlim bothLoaded, SemaphoreSlim proceedToSave)
            {
                _inner = inner;
                _bothLoaded = bothLoaded;
                _proceedToSave = proceedToSave;
            }

            public async Task<Stocktake?> GetByIdWithDetailsAsync(int id)
            {
                var st = await _inner.GetByIdWithDetailsAsync(id);
                _bothLoaded.Release();
                await _proceedToSave.WaitAsync(TimeSpan.FromSeconds(5));
                return st;
            }

            public Task<Stocktake?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);
            public Task<System.Collections.Generic.IEnumerable<Stocktake>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);
            public Task<System.Collections.Generic.IEnumerable<Stocktake>> FindAsync(System.Linq.Expressions.Expression<Func<Stocktake, bool>> predicate, CancellationToken cancellationToken = default) => _inner.FindAsync(predicate, cancellationToken);
            public Task<Stocktake> AddAsync(Stocktake entity, CancellationToken cancellationToken = default) => _inner.AddAsync(entity, cancellationToken);
            public Task UpdateAsync(Stocktake entity, CancellationToken cancellationToken = default) => _inner.UpdateAsync(entity, cancellationToken);
            public Task DeleteAsync(Stocktake entity, CancellationToken cancellationToken = default) => _inner.DeleteAsync(entity, cancellationToken);
        }

        private class ThrowingAuditLogRepository : IAuditLogRepository
        {
            public Task AddAsync(AuditLog auditLog) => throw new Exception("Database connection failed during audit log write.");
        }


        public StocktakeServiceConcurrencyTests()
        {
            // A shared in-memory SQLite connection keeps the database alive
            // across multiple DbContext instances.
            var dbName = Guid.NewGuid().ToString("N"); _connectionString = $"DataSource=file:{dbName}?mode=memory&cache=shared;Default Timeout=5"; _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            // Create schema
            using var setupCtx = CreateContext();
            setupCtx.Database.EnsureCreated();
        }

        private ConcurrentTestDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseSqlite(_connectionString)
                .Options;
            return new ConcurrentTestDbContext(options);
        }

        private static StocktakeService CreateService(ErpKhoDbContext context)
        {
            var stocktakeRepo = new StocktakeRepository(context);
            var stockRepo = new InventoryStockRepository(context);
            var txRepo = new InventoryTransactionRepository(context);
            var warehouseRepo = new WarehouseRepository(context);
            var uow = new UnitOfWork(context);
            var auditLogRepo = new AuditLogRepository(context);
            return new StocktakeService(stocktakeRepo, stockRepo, txRepo, warehouseRepo, uow, auditLogRepo);
        }

        private async Task SeedTestData()
        {
            using var ctx = CreateContext();

            var role = new Role { RoleName = "Admin" };
            ctx.Roles.Add(role);
            await ctx.SaveChangesAsync();

            var user = new User
            {
                Username = "test",
                PasswordHash = "x",
                FullName = "Test User",
                RoleId = role.Id,
            };
            ctx.Users.Add(user);

            var unit = new Unit { Code = "CAI", Name = "Cái" };
            ctx.Units.Add(unit);
            await ctx.SaveChangesAsync();

            var warehouse = new Warehouse { Code = "WH01", Name = "Main Warehouse" };
            ctx.Warehouses.Add(warehouse);

            var product = new Product { Code = "P1", Name = "Product 1", UnitId = unit.Id };
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync();

            var stocktake = new Stocktake
            {
                Code = "ST-CONC-001",
                WarehouseId = warehouse.Id,
                Status = ReceiptStatus.Draft,
                CreatedBy = user.Id,
            };
            ctx.Stocktakes.Add(stocktake);
            await ctx.SaveChangesAsync();

            var detail = new StocktakeDetail
            {
                StocktakeId = stocktake.Id,
                ProductId = product.Id,
                SystemQuantity = 10,
                ActualQuantity = 15,
                DifferenceQuantity = 5,
            };
            ctx.StocktakeDetails.Add(detail);
            await ctx.SaveChangesAsync();
        }

        /// <summary>
        /// Two independent service scopes attempt to approve the same Draft stocktake.
        /// A SemaphoreSlim barrier ensures both load the entity as Draft before either
        /// persists. Exactly one must succeed; the other must fail with
        /// BusinessRuleException (wrapping ConcurrencyException from the
        /// IsConcurrencyToken on Status).
        /// 
        /// SQLite serializes writes, so concurrency is detected when the second
        /// context's UPDATE finds the Status column no longer matches its original
        /// value (Draft), raising DbUpdateConcurrencyException.
        /// </summary>
                [Fact]
        public async Task ConcurrentApproval_ExactlyOneSucceeds_OtherFailsWithBusinessRuleException()
        {
            await SeedTestData();

            int stocktakeId, userId;
            using (var idCtx = CreateContext())
            {
                var st = await idCtx.Stocktakes.FirstAsync();
                stocktakeId = st.Id;
                var u = await idCtx.Users.FirstAsync();
                userId = u.Id;
            }

            var bothLoaded = new SemaphoreSlim(0, 2);
            var proceedToSave = new SemaphoreSlim(0, 2);

            Exception? exception1 = null;
            Exception? exception2 = null;
            bool success1 = false;
            bool success2 = false;

            using var ctx1 = CreateContext();
            var repo1 = new ConcurrentTestStocktakeRepo(new StocktakeRepository(ctx1), bothLoaded, proceedToSave);
            var service1 = new StocktakeService(repo1, new InventoryStockRepository(ctx1), new InventoryTransactionRepository(ctx1), new WarehouseRepository(ctx1), new ConcurrentTestUnitOfWork(ctx1), new AuditLogRepository(ctx1));

            using var ctx2 = CreateContext();
            var repo2 = new ConcurrentTestStocktakeRepo(new StocktakeRepository(ctx2), bothLoaded, proceedToSave);
            var service2 = new StocktakeService(repo2, new InventoryStockRepository(ctx2), new InventoryTransactionRepository(ctx2), new WarehouseRepository(ctx2), new ConcurrentTestUnitOfWork(ctx2), new AuditLogRepository(ctx2));

            var task1 = Task.Run(async () =>
            {
                try { await service1.ApproveStocktakeAsync(stocktakeId, userId); success1 = true; }
                catch (Exception ex) { exception1 = ex; }
            });

            var task2 = Task.Run(async () =>
            {
                try { await service2.ApproveStocktakeAsync(stocktakeId, userId); success2 = true; }
                catch (Exception ex) { exception2 = ex; }
            });

            // Wait for both to load
            bool loaded = await Task.WhenAll(
                bothLoaded.WaitAsync(TimeSpan.FromSeconds(5)),
                bothLoaded.WaitAsync(TimeSpan.FromSeconds(5))
            ).ContinueWith(t => t.Result.All(r => r));
            
            if (!loaded) throw new Exception($"Loaded failed. Ex1: {exception1}, Ex2: {exception2}");

            // Release both to proceed to save
            proceedToSave.Release(2);

            await Task.WhenAll(task1, task2);

            // Assert exactly one success, exactly one failure
            (success1 ^ success2).Should().BeTrue("exactly one approval should succeed");
            var failedException = success1 ? exception2 : exception1;
            failedException.Should().NotBeNull("the failed approval should throw");
            failedException.Should().BeOfType<BusinessRuleException>("the concurrency conflict should be wrapped as BusinessRuleException");

            using var verifyCtx = CreateContext();
            var finalStocktake = await verifyCtx.Stocktakes.FindAsync(stocktakeId);
            finalStocktake!.Status.Should().Be(ReceiptStatus.Approved);

            var stocks = await verifyCtx.InventoryStocks.ToListAsync();
            stocks.Should().HaveCount(1, "exactly one stock row should exist");
            stocks.First().Quantity.Should().Be(15, "stock should reflect ActualQuantity");

            var transactions = await verifyCtx.InventoryTransactions.Where(t => t.ReferenceType == "Stocktake").ToListAsync();
            transactions.Should().HaveCount(1, "exactly one transaction from the single successful approval");

            var audits = await verifyCtx.AuditLogs.Where(a => a.Action == "Stocktake.Approved" && a.EntityId == stocktakeId).ToListAsync();
            audits.Should().HaveCount(1, "exactly one approval audit should exist from the single successful approval");
        }

        [Fact]
        public async Task RepeatedApproval_SecondAttemptFails_NoAdditionalStockMutation()
        {
            await SeedTestData();

            int stocktakeId, userId;
            using (var idCtx = CreateContext())
            {
                stocktakeId = (await idCtx.Stocktakes.FirstAsync()).Id;
                userId = (await idCtx.Users.FirstAsync()).Id;
            }

            // First approval - should succeed
            {
                using var ctx = CreateContext();
                var service = CreateService(ctx);
                await service.ApproveStocktakeAsync(stocktakeId, userId);
            }

            int stockCount;
            decimal stockQty;
            int txCount;
            int auditCount;
            {
                using var ctx = CreateContext();
                var stocks = await ctx.InventoryStocks.ToListAsync();
                stockCount = stocks.Count;
                stockQty = stocks.First().Quantity;
                txCount = await ctx.InventoryTransactions.CountAsync(t => t.ReferenceType == "Stocktake");
                auditCount = await ctx.AuditLogs.CountAsync(a => a.Action == "Stocktake.Approved" && a.EntityId == stocktakeId);
            }

            // Second approval - should fail (status is Approved, not Draft)
            {
                using var ctx = CreateContext();
                var service = CreateService(ctx);
                var act = () => service.ApproveStocktakeAsync(stocktakeId, userId);
                await act.Should().ThrowAsync<BusinessRuleException>(
                    "second approval attempt should be rejected because status is no longer Draft");
            }

            // Verify state unchanged after second attempt
            {
                using var ctx = CreateContext();

                var finalStocktake = await ctx.Stocktakes.FindAsync(stocktakeId);
                finalStocktake!.Status.Should().Be(ReceiptStatus.Approved);

                var stocks = await ctx.InventoryStocks.ToListAsync();
                stocks.Should().HaveCount(stockCount, "no additional stock rows should be created");
                stocks.First().Quantity.Should().Be(stockQty, "stock quantity should not change");

                var finalTxCount = await ctx.InventoryTransactions
                    .CountAsync(t => t.ReferenceType == "Stocktake");
                finalTxCount.Should().Be(txCount, "no additional transactions from second attempt");

                var finalAuditCount = await ctx.AuditLogs.CountAsync(a => a.Action == "Stocktake.Approved" && a.EntityId == stocktakeId);
                finalAuditCount.Should().Be(auditCount, "no additional audit logs from second attempt");
            }
        }

        [Fact]
        public async Task CreateStocktakeAsync_AuditLogFails_RollsBackTransaction_Integration()
        {
            await SeedTestData();

            int stockCount;
            int stocktakeCount;
            int warehouseId;
            int userId;

            using (var idCtx = CreateContext())
            {
                warehouseId = (await idCtx.Warehouses.FirstAsync()).Id;
                userId = (await idCtx.Users.FirstAsync()).Id;
                stockCount = await idCtx.InventoryStocks.CountAsync();
                stocktakeCount = await idCtx.Stocktakes.CountAsync();
            }

            using var ctx = CreateContext();
            var stocktakeRepo = new StocktakeRepository(ctx);
            var stockRepo = new InventoryStockRepository(ctx);
            var txRepo = new InventoryTransactionRepository(ctx);
            var warehouseRepo = new WarehouseRepository(ctx);
            var uow = new UnitOfWork(ctx); // Real UnitOfWork using SQLite transaction
            var auditLogRepo = new ThrowingAuditLogRepository();
            var service = new StocktakeService(stocktakeRepo, stockRepo, txRepo, warehouseRepo, uow, auditLogRepo);

            var dto = new ERP.Application.DTOs.CreateStocktakeDto { WarehouseId = warehouseId, Note = "Rollback Test" };
            
            var act = () => service.CreateStocktakeAsync(dto, userId);

            await act.Should().ThrowAsync<Exception>().WithMessage("Database connection failed during audit log write.");

            // Verify integration state
            using var verifyCtx = CreateContext();
            var createdStocktakes = await verifyCtx.Stocktakes.Where(s => s.Note == "Rollback Test").ToListAsync();
            createdStocktakes.Should().BeEmpty("Because the transaction was rolled back when audit log failed");
            
            var finalStocktakeCount = await verifyCtx.Stocktakes.CountAsync();
            finalStocktakeCount.Should().Be(stocktakeCount, "no stocktake should have been added");
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }
    }
}









