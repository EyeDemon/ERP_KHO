using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
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
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace ERP.Application.Tests
{
    public class SqliteExportReceiptConflictClassifier : IExportReceiptConflictClassifier
    {
        public bool IsCodeUniqueConflict(DbUpdateException ex, string code)
        {
            if (ex == null) return false;

            bool hasMatchingEntity = ex.Entries != null && ex.Entries.Any(entry =>
                entry.Entity is ExportReceipt receipt &&
                string.Equals(receipt.Code, code, StringComparison.OrdinalIgnoreCase));

            if (!hasMatchingEntity) return false;

            if (ex.InnerException is SqliteException sqliteEx)
            {
                if (sqliteEx.SqliteExtendedErrorCode == 2067 || sqliteEx.SqliteErrorCode == 19)
                {
                    var msg = sqliteEx.Message ?? string.Empty;
                    return msg.Contains("UNIQUE constraint failed: ExportReceipts.Code", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }
    }

    #region Status Concurrency Configuration Tests

    public class ExportReceiptStatusConcurrencyConfigurationTests
    {
        [Fact]
        public void RuntimeModel_MarksExportReceiptStatus_AsConcurrencyToken()
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var ctx = new ErpKhoDbContext(options);
            var entityType = ctx.Model.FindEntityType(typeof(ExportReceipt));
            var property = entityType!.FindProperty(nameof(ExportReceipt.Status));
            property.Should().NotBeNull();
            property!.IsConcurrencyToken.Should().BeTrue("ExportReceipt.Status must be configured as a concurrency token in the runtime model");
        }

        [Fact]
        public async Task StaleStatusSave_ThrowsDbUpdateConcurrencyException_InSqliteContext()
        {
            var dbName = Guid.NewGuid().ToString("N");
            var connStr = $"DataSource=file:{dbName}?mode=memory&cache=shared;Default Timeout=5";
            using var anchorConn = new SqliteConnection(connStr);
            anchorConn.Open();

            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseSqlite(anchorConn)
                .Options;

            using (var setupCtx = new TestDbContext(options))
            {
                setupCtx.Database.EnsureCreated();
                var user = new User { Username = "conc_user", PasswordHash = "hash", FullName = "Conc User", Role = new Role { RoleName = "Admin" }, IsActive = true };
                var warehouse = new Warehouse { Code = "WH-CONC", Name = "Kho Conc", IsActive = true };
                setupCtx.Users.Add(user);
                setupCtx.Warehouses.Add(warehouse);
                await setupCtx.SaveChangesAsync();

                var receipt = new ExportReceipt
                {
                    Code = "EX-CONC-001",
                    WarehouseId = warehouse.Id,
                    Status = ReceiptStatus.Draft,
                    CreatedBy = user.Id,
                    CreatedAt = DateTime.UtcNow
                };
                setupCtx.ExportReceipts.Add(receipt);
                await setupCtx.SaveChangesAsync();
            }

            using (var ctx1 = new TestDbContext(options))
            using (var ctx2 = new TestDbContext(options))
            {
                var receipt1 = await ctx1.ExportReceipts.FirstAsync(r => r.Code == "EX-CONC-001");
                var receipt2 = await ctx2.ExportReceipts.FirstAsync(r => r.Code == "EX-CONC-001");

                receipt1.Status = ReceiptStatus.Approved;
                await ctx1.SaveChangesAsync();

                receipt2.Status = ReceiptStatus.Cancelled;

                Func<Task> act = async () => await ctx2.SaveChangesAsync();
                await act.Should().ThrowAsync<DbUpdateConcurrencyException>("saving a stale ExportReceipt status update must throw DbUpdateConcurrencyException");
            }
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
    }

    #endregion

    #region Production Decision Helper Tests

    public class ExportReceiptConflictDecisionHelperTests
    {
        private const string ExactCodeMsg =
            "Cannot insert duplicate key row in object 'dbo.ExportReceipts' with unique index 'IX_ExportReceipts_Code'. The duplicate key value is (EX-001).";

        [Fact]
        public void IsDuplicateKeyWithExactIndex_2601_ExactToken_ReturnsTrue()
        {
            ExportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2601, ExactCodeMsg, "IX_ExportReceipts_Code").Should().BeTrue();
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_2627_ExactToken_ReturnsTrue()
        {
            ExportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2627, ExactCodeMsg, "IX_ExportReceipts_Code").Should().BeTrue();
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_WrongNumber_ExactToken_ReturnsFalse()
        {
            ExportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                547, ExactCodeMsg, "IX_ExportReceipts_Code").Should().BeFalse("wrong SQL Server error number with exact token must return false");
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_ValidNumber_AnotherToken_ReturnsFalse()
        {
            var msg = "Violation of UNIQUE KEY constraint 'IX_ExportReceipts_Note'. Cannot insert duplicate key in object 'dbo.ExportReceipts'.";
            ExportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2601, msg, "IX_ExportReceipts_Code").Should().BeFalse("valid number but another constraint must return false");
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_ValidNumber_MalformedMessage_ReturnsFalse()
        {
            var msg = "Database execution error occurred without quoted tokens.";
            ExportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2627, msg, "IX_ExportReceipts_Code").Should().BeFalse("valid number but malformed message must return false");
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_ValidNumber_EmptyMessage_ReturnsFalse()
        {
            ExportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2601, "", "IX_ExportReceipts_Code").Should().BeFalse("valid number but empty message must return false");
        }

        [Fact]
        public void IsCodeUniqueConflict_MismatchedEntityCode_ReturnsFalse()
        {
            var classifier = new ExportReceiptConflictClassifier();
            var receipt = new ExportReceipt { Code = "EX-OTHER" };

            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var ctx = new ErpKhoDbContext(options);
            var entry = ctx.Entry(receipt);

            var dbEx = new DbUpdateException("Error", new Exception("Generic error"), new List<EntityEntry> { entry });
            classifier.IsCodeUniqueConflict(dbEx, "EX-TARGET").Should().BeFalse("mismatched entity code must return false");
        }
    }

    #endregion

    #region SQLite Classifier Tests

    public class SqliteExportReceiptConflictClassifierTests
    {
        private (ErpKhoDbContext ctx, EntityEntry entry) CreateEntryForReceipt(string code)
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var ctx = new ErpKhoDbContext(options);
            var receipt = new ExportReceipt { Code = code };
            var entry = ctx.Entry(receipt);
            return (ctx, entry);
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteUniqueConstraintOnCode_ReturnsTrue()
        {
            var classifier = new SqliteExportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("EX-DUP");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: ExportReceipts.Code'.", 19, 2067);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "EX-DUP").Should().BeTrue();
            }
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteForeignKeyConstraint_ReturnsFalse()
        {
            var classifier = new SqliteExportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("EX-FK");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "EX-FK").Should().BeFalse("FK constraint must not be classified as code conflict");
            }
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteCheckConstraint_ReturnsFalse()
        {
            var classifier = new SqliteExportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("EX-CHECK");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 19: 'CHECK constraint failed: CK_ExportReceipts_Quantity'.", 19, 275);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "EX-CHECK").Should().BeFalse("CHECK constraint must not be classified as code conflict");
            }
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteDatabaseLocked_ReturnsFalse()
        {
            var classifier = new SqliteExportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("EX-LOCK");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 5: 'database is locked'.", 5);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "EX-LOCK").Should().BeFalse("database locked must not be classified as code conflict");
            }
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteAnotherUniqueIndex_ReturnsFalse()
        {
            var classifier = new SqliteExportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("EX-OTHER-UNIQ");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: ExportReceipts.Note'.", 19, 2067);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "EX-OTHER-UNIQ").Should().BeFalse("another unique index on ExportReceipts must not be classified as code conflict");
            }
        }
    }

    #endregion

    #region Service & Repository Boundary Tests

    public class ExportReceiptServiceDuplicateCodeConcurrencyTests : IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _anchorConnection;

        public ExportReceiptServiceDuplicateCodeConcurrencyTests()
        {
            var dbName = Guid.NewGuid().ToString("N");
            _connectionString = $"DataSource=file:{dbName}?mode=memory&cache=shared;Default Timeout=5";
            _anchorConnection = new SqliteConnection(_connectionString);
            _anchorConnection.Open();

            using var setupConn = CreateIndependentConnection();
            using var setupCtx = CreateContext(setupConn);
            setupCtx.Database.EnsureCreated();
        }

        private SqliteConnection CreateIndependentConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        private TestDbContext CreateContext(SqliteConnection connection)
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseSqlite(connection)
                .Options;
            return new TestDbContext(options);
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

        private async Task<(int warehouseId, int productId, int userId)> SeedTestDataAsync()
        {
            using var conn = CreateIndependentConnection();
            using var ctx = CreateContext(conn);
            var role = new Role { RoleName = "Admin" };
            ctx.Roles.Add(role);

            var user = new User { Username = "exp_dup_user", PasswordHash = "hash", FullName = "Exp Dup User", Role = role, IsActive = true };
            ctx.Users.Add(user);

            var unit = new Unit { Code = "CAI", Name = "Cái" };
            ctx.Units.Add(unit);

            var warehouse = new Warehouse { Code = "WH-EXP-DUP", Name = "Kho Exp Dup", IsActive = true };
            ctx.Warehouses.Add(warehouse);

            var product = new Product { Code = "SKU-EXP-DUP", Name = "SP Exp Dup", Unit = unit, IsActive = true };
            ctx.Products.Add(product);

            var stock = new InventoryStock { Product = product, Warehouse = warehouse, Quantity = 100, LastUpdated = DateTime.UtcNow };
            ctx.InventoryStocks.Add(stock);

            await ctx.SaveChangesAsync();

            return (warehouse.Id, product.Id, user.Id);
        }

        private class BarrierExistsByCodeExportReceiptRepo : IExportReceiptRepository
        {
            private readonly IExportReceiptRepository _inner;
            private readonly System.Threading.Barrier _barrier;
            private int _callCount;

            public BarrierExistsByCodeExportReceiptRepo(IExportReceiptRepository inner, System.Threading.Barrier barrier)
            {
                _inner = inner;
                _barrier = barrier;
            }

            public async Task<bool> ExistsByCodeAsync(string code, int? excludeId = null)
            {
                var result = await _inner.ExistsByCodeAsync(code, excludeId);
                if (System.Threading.Interlocked.Increment(ref _callCount) == 1)
                {
                    _barrier.SignalAndWait(TimeSpan.FromSeconds(5));
                }
                return result;
            }

            public Task<ExportReceipt?> GetByIdWithDetailsAsync(int id) => _inner.GetByIdWithDetailsAsync(id);
            public Task<ExportReceipt?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);
            public Task<IEnumerable<ExportReceipt>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);
            public Task<IEnumerable<ExportReceipt>> GetListAsync() => _inner.GetListAsync();
            public Task<IEnumerable<ExportReceipt>> FindAsync(Expression<Func<ExportReceipt, bool>> predicate, CancellationToken cancellationToken = default) => _inner.FindAsync(predicate, cancellationToken);
            public Task<ExportReceipt> AddAsync(ExportReceipt entity, CancellationToken cancellationToken = default) => _inner.AddAsync(entity, cancellationToken);
            public Task UpdateAsync(ExportReceipt entity, CancellationToken cancellationToken = default) => _inner.UpdateAsync(entity, cancellationToken);
            public Task DeleteAsync(ExportReceipt entity, CancellationToken cancellationToken = default) => _inner.DeleteAsync(entity, cancellationToken);
        }

        [Fact]
        public async Task CreateAsync_ParallelExecution_SameCode_PreventsDuplicateReceiptAndThrowsBusinessRuleException()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var conn1 = CreateIndependentConnection();
            using var conn2 = CreateIndependentConnection();

            using var ctx1 = CreateContext(conn1);
            using var ctx2 = CreateContext(conn2);

            using var existsBarrier = new System.Threading.Barrier(2);

            var testClassifier = new SqliteExportReceiptConflictClassifier();

            var repo1 = new BarrierExistsByCodeExportReceiptRepo(new ExportReceiptRepository(ctx1, testClassifier), existsBarrier);
            var repo2 = new BarrierExistsByCodeExportReceiptRepo(new ExportReceiptRepository(ctx2, testClassifier), existsBarrier);

            var service1 = new ExportReceiptService(
                repo1, new InventoryStockRepository(ctx1), new InventoryTransactionRepository(ctx1),
                new UnitOfWork(ctx1), new AuditLogRepository(ctx1));

            var service2 = new ExportReceiptService(
                repo2, new InventoryStockRepository(ctx2), new InventoryTransactionRepository(ctx2),
                new UnitOfWork(ctx2), new AuditLogRepository(ctx2));

            var dto1 = new CreateExportReceiptDto
            {
                Code = "EX-RACE-001",
                WarehouseId = warehouseId,
                Details = new List<CreateExportReceiptDetailDto>
                {
                    new CreateExportReceiptDetailDto { ProductId = productId, Quantity = 10, UnitPrice = 100 }
                }
            };

            var dto2 = new CreateExportReceiptDto
            {
                Code = "EX-RACE-001",
                WarehouseId = warehouseId,
                Details = new List<CreateExportReceiptDetailDto>
                {
                    new CreateExportReceiptDetailDto { ProductId = productId, Quantity = 10, UnitPrice = 100 }
                }
            };

            int successCount = 0;
            int errorCount = 0;
            Exception? errorException = null;

            var task1 = Task.Run(async () =>
            {
                try
                {
                    await service1.CreateAsync(dto1, userId);
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
                    await service2.CreateAsync(dto2, userId);
                    System.Threading.Interlocked.Increment(ref successCount);
                }
                catch (Exception ex)
                {
                    errorException = ex;
                    System.Threading.Interlocked.Increment(ref errorCount);
                }
            });

            await Task.WhenAll(task1, task2);

            successCount.Should().Be(1, "exactly one export create call must succeed");
            errorCount.Should().Be(1, "losing duplicate code export create call must fail with BusinessRuleException");
            errorException.Should().BeOfType<BusinessRuleException>("losing call must receive mapped duplicate-code BusinessRuleException");
            errorException!.Message.Should().Contain("EX-RACE-001", "error message must state that code already exists");

            using var freshConn = CreateIndependentConnection();
            using var freshCtx = CreateContext(freshConn);
            var receipts = await freshCtx.ExportReceipts.Where(r => r.Code == "EX-RACE-001").ToListAsync();
            receipts.Should().HaveCount(1, "database must contain exactly one Draft export receipt");
            receipts.First().Status.Should().Be(ReceiptStatus.Draft);

            var details = await freshCtx.ExportReceiptDetails.Where(d => d.ExportReceipt.Code == "EX-RACE-001").ToListAsync();
            details.Should().HaveCount(1, "exactly one detail row must exist for the receipt");

            var audits = await freshCtx.AuditLogs.Where(a => a.Action == "ExportReceipt.Created" && a.EntityId == receipts.First().Id).ToListAsync();
            audits.Should().HaveCount(1, "exactly one audit log row must exist");

            var stock = await freshCtx.InventoryStocks.FirstAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId);
            stock.Quantity.Should().Be(100, "Create must not mutate stock rows");

            var txs = await freshCtx.InventoryTransactions.ToListAsync();
            txs.Should().BeEmpty("Create must not create inventory transaction rows");
        }

        [Fact]
        public async Task CreateAsync_SequentialDuplicateCode_ThrowsBusinessRuleException()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var conn = CreateIndependentConnection();
            using var ctx = CreateContext(conn);
            var testClassifier = new SqliteExportReceiptConflictClassifier();
            var service = new ExportReceiptService(
                new ExportReceiptRepository(ctx, testClassifier),
                new InventoryStockRepository(ctx),
                new InventoryTransactionRepository(ctx),
                new UnitOfWork(ctx),
                new AuditLogRepository(ctx));

            var dto = new CreateExportReceiptDto
            {
                Code = "EX-SEQ-DUP",
                WarehouseId = warehouseId,
                Details = new List<CreateExportReceiptDetailDto>
                {
                    new CreateExportReceiptDetailDto { ProductId = productId, Quantity = 5, UnitPrice = 50 }
                }
            };

            await service.CreateAsync(dto, userId);

            Func<Task> act = async () => await service.CreateAsync(dto, userId);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*EX-SEQ-DUP*đã tồn tại*");
        }

        [Fact]
        public async Task AddAsync_DirectRepositoryBoundary_UnrelatedDbUpdateException_RethrowsWithoutMapping()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var conn = CreateIndependentConnection();
            using var ctx = CreateContext(conn);

            var testClassifier = new SqliteExportReceiptConflictClassifier();
            var repo = new ExportReceiptRepository(ctx, testClassifier);

            var receipt = new ExportReceipt
            {
                Code = "EX-REPO-FK-FAIL",
                WarehouseId = 999999,
                Status = ReceiptStatus.Draft,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            Func<Task> act = async () => await repo.AddAsync(receipt);

            await act.Should().ThrowAsync<DbUpdateException>(
                "direct ExportReceiptRepository.AddAsync with invalid FK must rethrow original DbUpdateException without mapping to BusinessRuleException");
        }

        public void Dispose()
        {
            _anchorConnection.Close();
            _anchorConnection.Dispose();
        }
    }

    #endregion
}
