using ERP.Application.DTOs;
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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace ERP.Application.Tests
{
    public class ImportReceiptServiceConcurrencyTests : IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _connection;

        public ImportReceiptServiceConcurrencyTests()
        {
            var dbName = Guid.NewGuid().ToString("N");
            _connectionString = $"DataSource=file:{dbName}?mode=memory&cache=shared;Default Timeout=5";
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            using var setupCmd = _connection.CreateCommand();
            setupCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            setupCmd.ExecuteNonQuery();

            using var setupCtx = CreateContext();
            setupCtx.Database.EnsureCreated();
        }

        private class TestDbContext : ErpKhoDbContext
        {
            public TestDbContext(DbContextOptions<ErpKhoDbContext> options) : base(options) { }

            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                base.OnModelCreating(modelBuilder);
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

        private TestDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseSqlite(_connectionString)
                .Options;
            
            return new TestDbContext(options);
        }

        private async Task<(int ReceiptId, int UserId, int ProductId, int WarehouseId)> SeedTestDataAsync()
        {
            using var ctx = CreateContext();

            var role = new Role { RoleName = "Admin" };
            ctx.Roles.Add(role);

            var maker = new User { Username = "maker", PasswordHash = "x", FullName = "Test Maker", Role = role };
            var checker = new User { Username = "checker", PasswordHash = "x", FullName = "Test Checker", Role = role };
            ctx.Users.AddRange(maker, checker);

            var unit = new Unit { Code = "CAI", Name = "Cái" };
            ctx.Units.Add(unit);

            var warehouse = new Warehouse { Code = "WH01", Name = "Main Warehouse" };
            ctx.Warehouses.Add(warehouse);

            var product = new Product { Code = "P1", Name = "Product 1", Unit = unit };
            ctx.Products.Add(product);
            await ctx.SaveChangesAsync();

            var stock = new InventoryStock { ProductId = product.Id, WarehouseId = warehouse.Id, Quantity = 50, LastUpdated = DateTime.UtcNow };
            ctx.InventoryStocks.Add(stock);

            var receipt = new ImportReceipt { Code = "IR-CONC-001", WarehouseId = warehouse.Id, Status = ReceiptStatus.Draft, CreatedBy = maker.Id };
            ctx.ImportReceipts.Add(receipt);
            await ctx.SaveChangesAsync();

            var detail = new ImportReceiptDetail { ImportReceiptId = receipt.Id, ProductId = product.Id, Quantity = 10, UnitPrice = 100 };
            ctx.ImportReceiptDetails.Add(detail);
            await ctx.SaveChangesAsync();

            return (receipt.Id, checker.Id, product.Id, warehouse.Id);
        }

        [Fact]
        public async Task Model_OptimisticConcurrency_ThrowsDbUpdateConcurrencyException()
        {
            var (receiptId, _, _, _) = await SeedTestDataAsync();

            using var ctx1 = CreateContext();
            using var ctx2 = CreateContext();

            // Track independently
            var r1 = await ctx1.ImportReceipts.FindAsync(receiptId);
            var r2 = await ctx2.ImportReceipts.FindAsync(receiptId);

            // Context 1 saves first
            r1!.Status = ReceiptStatus.Approved;
            await ctx1.SaveChangesAsync();

            // Context 2 saves stale
            r2!.Status = ReceiptStatus.Approved;
            var act = () => ctx2.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>("because the [ConcurrencyCheck] on Status ensures EF Core rejects stale updates");
        }

        [Fact]
        public async Task UnitOfWork_MapsDbUpdateConcurrencyException_ToDomainConcurrencyException()
        {
            var (receiptId, _, _, _) = await SeedTestDataAsync();

            using var ctx1 = CreateContext();
            using var ctx2 = CreateContext();

            var r1 = await ctx1.ImportReceipts.FindAsync(receiptId);
            var r2 = await ctx2.ImportReceipts.FindAsync(receiptId);

            r1!.Status = ReceiptStatus.Approved;
            await ctx1.SaveChangesAsync();

            r2!.Status = ReceiptStatus.Approved;
            var uow2 = new UnitOfWork(ctx2);
            var act = () => uow2.SaveChangesAsync();

            await act.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>("because UnitOfWork catches EF DbUpdateConcurrencyException and maps it to Domain ConcurrencyException");
        }

        private class ThrowingImportReceiptRepo : IImportReceiptRepository
        {
            private readonly IImportReceiptRepository _inner;
            public ThrowingImportReceiptRepo(IImportReceiptRepository inner) => _inner = inner;
            
            public Task UpdateAsync(ImportReceipt entity, CancellationToken cancellationToken = default) 
                => throw new ERP.Domain.Exceptions.ConcurrencyException("Mock DB conflict during save", new DbUpdateConcurrencyException("Mock"));

            public Task<ImportReceipt?> GetByIdWithDetailsAsync(int id) => _inner.GetByIdWithDetailsAsync(id);
            public Task<ImportReceipt?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);
            public Task<System.Collections.Generic.IEnumerable<ImportReceipt>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);
            public Task<System.Collections.Generic.IEnumerable<ImportReceipt>> FindAsync(System.Linq.Expressions.Expression<Func<ImportReceipt, bool>> predicate, CancellationToken cancellationToken = default) => _inner.FindAsync(predicate, cancellationToken);
            public Task<ImportReceipt> AddAsync(ImportReceipt entity, CancellationToken cancellationToken = default) => _inner.AddAsync(entity, cancellationToken);
            public Task DeleteAsync(ImportReceipt entity, CancellationToken cancellationToken = default) => _inner.DeleteAsync(entity, cancellationToken);
            public Task<System.Collections.Generic.IEnumerable<ImportReceipt>> GetAllWithDetailsAsync(ReceiptStatus? status) => _inner.GetAllWithDetailsAsync(status);
            public Task<bool> ExistsByCodeAsync(string code, int? excludeId = null) => _inner.ExistsByCodeAsync(code, excludeId);
        }

        [Fact]
        public async Task Service_CatchesDomainConcurrencyException_ThrowsBusinessRuleException_AndRollsBack()
        {
            var (receiptId, userId, productId, warehouseId) = await SeedTestDataAsync();

            using var ctx = CreateContext();
            var realRepo = new ImportReceiptRepository(ctx);
            var throwingRepo = new ThrowingImportReceiptRepo(realRepo);
            var stockRepo = new InventoryStockRepository(ctx);
            var txRepo = new InventoryTransactionRepository(ctx);
            var uow = new UnitOfWork(ctx);

            var service = new ImportReceiptService(
                throwingRepo, stockRepo, txRepo, 
                new WarehouseRepository(ctx), new ProductRepository(ctx), 
                uow, new AuditLogRepository(ctx));

            var act = () => service.ApproveImportReceiptAsync(receiptId, userId);

            await act.Should().ThrowAsync<BusinessRuleException>("because the service maps ConcurrencyException to a business error");

            // Verify rollback using a fresh context
            using var verifyCtx = CreateContext();
            
            var receipt = await verifyCtx.ImportReceipts.FindAsync(receiptId);
            receipt!.Status.Should().Be(ReceiptStatus.Draft, "rollback ensures receipt metadata is unchanged");

            var stock = await verifyCtx.InventoryStocks.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
            stock.Quantity.Should().Be(50, "rollback ensures inventory stock mutation is discarded");

            var txs = await verifyCtx.InventoryTransactions.ToListAsync();
            txs.Should().BeEmpty("rollback ensures inventory transactions are discarded");

            var audits = await verifyCtx.AuditLogs.ToListAsync();
            audits.Should().BeEmpty("rollback ensures audit logs are discarded");
        }

        [Fact]
        public async Task Service_SequentialApprovals_EnsureExactlyOnceState()
        {
            var (receiptId, userId, productId, warehouseId) = await SeedTestDataAsync();

            // First approval - should succeed
            {
                using var ctx = CreateContext();
                var service = new ImportReceiptService(
                    new ImportReceiptRepository(ctx), new InventoryStockRepository(ctx), new InventoryTransactionRepository(ctx),
                    new WarehouseRepository(ctx), new ProductRepository(ctx), new UnitOfWork(ctx), new AuditLogRepository(ctx));
                
                await service.ApproveImportReceiptAsync(receiptId, userId);
            }

            // Second approval - should fail
            {
                using var ctx = CreateContext();
                var service = new ImportReceiptService(
                    new ImportReceiptRepository(ctx), new InventoryStockRepository(ctx), new InventoryTransactionRepository(ctx),
                    new WarehouseRepository(ctx), new ProductRepository(ctx), new UnitOfWork(ctx), new AuditLogRepository(ctx));
                
                var act = () => service.ApproveImportReceiptAsync(receiptId, userId);
                await act.Should().ThrowAsync<BusinessRuleException>("second approval attempt should be rejected");
            }

            // Verify exactly-once state
            using var verifyCtx = CreateContext();
            
            var receipt = await verifyCtx.ImportReceipts.FindAsync(receiptId);
            receipt!.Status.Should().Be(ReceiptStatus.Approved);
            receipt.ApprovedBy.Should().Be(userId);
            receipt.ApprovedAt.Should().NotBeNull();
            receipt.ApprovedAt.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));

            var stocks = await verifyCtx.InventoryStocks.ToListAsync();
            stocks.Should().HaveCount(1, "exactly one stock row should exist");
            stocks.First().Quantity.Should().Be(60, "stock should reflect initial 50 + exactly 1 receipt of 10");

            var transactions = await verifyCtx.InventoryTransactions.Where(t => t.ReferenceType == "ImportReceipt").ToListAsync();
            transactions.Should().HaveCount(1, "exactly one transaction from the single successful approval");
            var tx = transactions.First();
            tx.ReferenceId.Should().Be(receiptId);
            tx.TransactionType.Should().Be(TransactionType.Import);
            tx.ProductId.Should().Be(productId);
            tx.WarehouseId.Should().Be(warehouseId);
            tx.Quantity.Should().Be(10);

            var audits = await verifyCtx.AuditLogs.Where(a => a.Action == "ImportReceipt.Approved" && a.EntityId == receiptId).ToListAsync();
            audits.Should().HaveCount(1, "exactly one approval audit should exist");
        }

        private class BarrierUnitOfWork : IUnitOfWork
        {
            private readonly IUnitOfWork _inner;
            private readonly System.Threading.Barrier _barrier;

            public BarrierUnitOfWork(IUnitOfWork inner, System.Threading.Barrier barrier)
            {
                _inner = inner;
                _barrier = barrier;
            }

            public async Task BeginTransactionAsync()
            {
                _barrier.SignalAndWait(TimeSpan.FromSeconds(5));
                try
                {
                    await _inner.BeginTransactionAsync();
                }
                catch (Exception ex) when (SqliteLockExceptionHelperTests.IsSqliteLockException(ex))
                {
                    throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
                }
            }

            public async Task CommitTransactionAsync()
            {
                try
                {
                    await _inner.CommitTransactionAsync();
                }
                catch (Exception ex) when (SqliteLockExceptionHelperTests.IsSqliteLockException(ex))
                {
                    throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
                }
            }

            public Task RollbackTransactionAsync() => _inner.RollbackTransactionAsync();

            public async Task<int> SaveChangesAsync()
            {
                try
                {
                    return await _inner.SaveChangesAsync();
                }
                catch (Exception ex) when (SqliteLockExceptionHelperTests.IsSqliteLockException(ex))
                {
                    throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã bị thay đổi bởi người khác.", ex);
                }
            }
        }

        private class BarrierImportReceiptRepo : IImportReceiptRepository
        {
            private readonly IImportReceiptRepository _inner;
            private readonly System.Threading.Barrier _barrier;

            public BarrierImportReceiptRepo(IImportReceiptRepository inner, System.Threading.Barrier barrier)
            {
                _inner = inner;
                _barrier = barrier;
            }

            public async Task<ImportReceipt?> GetByIdWithDetailsAsync(int id)
            {
                var receipt = await _inner.GetByIdWithDetailsAsync(id);
                _barrier.SignalAndWait(TimeSpan.FromSeconds(5));
                return receipt;
            }

            public Task<ImportReceipt?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);
            public Task<System.Collections.Generic.IEnumerable<ImportReceipt>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);
            public Task<System.Collections.Generic.IEnumerable<ImportReceipt>> FindAsync(System.Linq.Expressions.Expression<Func<ImportReceipt, bool>> predicate, CancellationToken cancellationToken = default) => _inner.FindAsync(predicate, cancellationToken);
            public Task<ImportReceipt> AddAsync(ImportReceipt entity, CancellationToken cancellationToken = default) => _inner.AddAsync(entity, cancellationToken);
            public Task UpdateAsync(ImportReceipt entity, CancellationToken cancellationToken = default) => _inner.UpdateAsync(entity, cancellationToken);
            public Task DeleteAsync(ImportReceipt entity, CancellationToken cancellationToken = default) => _inner.DeleteAsync(entity, cancellationToken);
            public Task<System.Collections.Generic.IEnumerable<ImportReceipt>> GetAllWithDetailsAsync(ReceiptStatus? status) => _inner.GetAllWithDetailsAsync(status);
            public Task<bool> ExistsByCodeAsync(string code, int? excludeId = null) => _inner.ExistsByCodeAsync(code, excludeId);
        }

        [Fact]
        public async Task ApproveAsync_ParallelExecution_PreventsRaceCondition()
        {
            var (receiptId, userId, productId, warehouseId) = await SeedTestDataAsync();

            using var ctx1 = CreateContext();
            using var ctx2 = CreateContext();

            using var beginBarrier = new System.Threading.Barrier(2);
            using var readBarrier = new System.Threading.Barrier(2);

            var uow1 = new BarrierUnitOfWork(new UnitOfWork(ctx1), beginBarrier);
            var uow2 = new BarrierUnitOfWork(new UnitOfWork(ctx2), beginBarrier);

            var repo1 = new BarrierImportReceiptRepo(new ImportReceiptRepository(ctx1), readBarrier);
            var repo2 = new BarrierImportReceiptRepo(new ImportReceiptRepository(ctx2), readBarrier);

            var service1 = new ImportReceiptService(
                repo1, new InventoryStockRepository(ctx1), new InventoryTransactionRepository(ctx1),
                new WarehouseRepository(ctx1), new ProductRepository(ctx1), uow1, new AuditLogRepository(ctx1));

            var service2 = new ImportReceiptService(
                repo2, new InventoryStockRepository(ctx2), new InventoryTransactionRepository(ctx2),
                new WarehouseRepository(ctx2), new ProductRepository(ctx2), uow2, new AuditLogRepository(ctx2));

            int successCount = 0;
            int errorCount = 0;
            Exception? errorException = null;

            var task1 = Task.Run(async () =>
            {
                try
                {
                    await service1.ApproveImportReceiptAsync(receiptId, userId);
                    System.Threading.Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    errorException = ex;
                    System.Threading.Interlocked.Increment(ref errorCount);
                }
            });

            var task2 = Task.Run(async () =>
            {
                try
                {
                    await service2.ApproveImportReceiptAsync(receiptId, userId);
                    System.Threading.Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    errorException = ex;
                    System.Threading.Interlocked.Increment(ref errorCount);
                }
            });

            await Task.WhenAll(task1, task2);

            successCount.Should().Be(1, "exactly one concurrent approval must succeed");
            errorCount.Should().Be(1, "losing concurrent approval must fail with BusinessRuleException");
            errorException.Should().BeOfType<BusinessRuleException>("losing concurrent approval must fail with mapped BusinessRuleException");

            using var verifyCtx = CreateContext();

            var receipt = await verifyCtx.ImportReceipts.FindAsync(receiptId);
            receipt!.Status.Should().Be(ReceiptStatus.Approved);
            receipt.ApprovedBy.Should().Be(userId);
            receipt.ApprovedAt.Should().NotBeNull();

            var stock = await verifyCtx.InventoryStocks.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
            stock.Quantity.Should().Be(60, "stock should reflect initial 50 + exactly 1 receipt of 10");

            var transactions = await verifyCtx.InventoryTransactions.Where(t => t.ReferenceType == "ImportReceipt" && t.ReferenceId == receiptId).ToListAsync();
            transactions.Should().HaveCount(1, "exactly one Import transaction from the single successful approval");
            var tx = transactions.First();
            tx.TransactionType.Should().Be(TransactionType.Import);
            tx.ProductId.Should().Be(productId);
            tx.WarehouseId.Should().Be(warehouseId);
            tx.Quantity.Should().Be(10);

            var audits = await verifyCtx.AuditLogs.Where(a => a.Action == "ImportReceipt.Approved" && a.EntityId == receiptId).ToListAsync();
            audits.Should().HaveCount(1, "exactly one approval audit log should exist");
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }
    }

    public class SqliteLockExceptionHelperTests
    {
        public static bool IsSqliteLockException(Exception ex)
        {
            for (Exception? current = ex; current != null; current = current.InnerException)
            {
                if (current is SqliteException sqliteEx && (sqliteEx.SqliteErrorCode == 5 || sqliteEx.SqliteErrorCode == 6))
                {
                    return true;
                }
            }
            return false;
        }

        [Fact]
        public void SqliteLockExceptionHelper_Code5And6_ReturnsTrue()
        {
            var ex5 = new SqliteException("busy", 5);
            var ex6 = new SqliteException("locked", 6);

            IsSqliteLockException(ex5).Should().BeTrue("SqliteErrorCode 5 (SQLITE_BUSY) is a lock conflict");
            IsSqliteLockException(ex6).Should().BeTrue("SqliteErrorCode 6 (SQLITE_LOCKED) is a lock conflict");
        }

        [Fact]
        public void SqliteLockExceptionHelper_Code19Constraint_ReturnsFalse()
        {
            var ex19 = new SqliteException("constraint", 19);

            IsSqliteLockException(ex19).Should().BeFalse("SqliteErrorCode 19 (SQLITE_CONSTRAINT) is not a lock conflict");
        }
    }
}
