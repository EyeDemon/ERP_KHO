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
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Xunit;

namespace ERP.Application.Tests
{
    public class SqliteImportReceiptConflictClassifier : IImportReceiptConflictClassifier
    {
        public bool IsCodeUniqueConflict(DbUpdateException ex, string code)
        {
            if (ex == null) return false;

            bool hasMatchingEntity = ex.Entries != null && ex.Entries.Any(entry =>
                entry.Entity is ImportReceipt receipt &&
                string.Equals(receipt.Code, code, StringComparison.OrdinalIgnoreCase));

            if (!hasMatchingEntity) return false;

            if (ex.InnerException is SqliteException sqliteEx)
            {
                if (sqliteEx.SqliteExtendedErrorCode == 2067 || sqliteEx.SqliteErrorCode == 19)
                {
                    var msg = sqliteEx.Message ?? string.Empty;
                    return msg.Contains("UNIQUE constraint failed: ImportReceipts.Code", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }
    }

    #region Production Decision Helper Tests

    public class ImportReceiptConflictDecisionHelperTests
    {
        private const string ExactCodeMsg =
            "Cannot insert duplicate key row in object 'dbo.ImportReceipts' with unique index 'IX_ImportReceipts_Code'. The duplicate key value is (IM-001).";

        [Fact]
        public void IsDuplicateKeyWithExactIndex_2601_ExactToken_ReturnsTrue()
        {
            ImportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2601, ExactCodeMsg, "IX_ImportReceipts_Code").Should().BeTrue();
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_2627_ExactToken_ReturnsTrue()
        {
            ImportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2627, ExactCodeMsg, "IX_ImportReceipts_Code").Should().BeTrue();
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_WrongNumber_ExactToken_ReturnsFalse()
        {
            ImportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                547, ExactCodeMsg, "IX_ImportReceipts_Code").Should().BeFalse("wrong SQL Server error number with exact token must return false");
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_ValidNumber_AnotherToken_ReturnsFalse()
        {
            var msg = "Violation of UNIQUE KEY constraint 'IX_ImportReceipts_Note'. Cannot insert duplicate key in object 'dbo.ImportReceipts'.";
            ImportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2601, msg, "IX_ImportReceipts_Code").Should().BeFalse("valid number but another constraint must return false");
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_ValidNumber_MalformedMessage_ReturnsFalse()
        {
            var msg = "Database execution error occurred without quoted tokens.";
            ImportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2627, msg, "IX_ImportReceipts_Code").Should().BeFalse("valid number but malformed message must return false");
        }

        [Fact]
        public void IsDuplicateKeyWithExactIndex_ValidNumber_EmptyMessage_ReturnsFalse()
        {
            ImportReceiptConflictClassifier.IsDuplicateKeyWithExactIndex(
                2601, "", "IX_ImportReceipts_Code").Should().BeFalse("valid number but empty message must return false");
        }

        [Fact]
        public void ContainsExactConstraintToken_ExactMatch_ReturnsTrue()
        {
            ImportReceiptConflictClassifier.ContainsExactConstraintToken(ExactCodeMsg, "IX_ImportReceipts_Code").Should().BeTrue();
        }

        [Fact]
        public void ContainsExactConstraintToken_AnotherConstraint_ReturnsFalse()
        {
            var msg = "Violation of UNIQUE KEY constraint 'IX_ImportReceipts_Note'. Cannot insert duplicate key in object 'dbo.ImportReceipts'.";
            ImportReceiptConflictClassifier.ContainsExactConstraintToken(msg, "IX_ImportReceipts_Code").Should().BeFalse();
        }

        [Fact]
        public void ContainsExactConstraintToken_MalformedMessage_ReturnsFalse()
        {
            ImportReceiptConflictClassifier.ContainsExactConstraintToken(
                "Database execution error occurred without quoted tokens.", "IX_ImportReceipts_Code").Should().BeFalse();
        }

        [Fact]
        public void IsCodeUniqueConflict_MismatchedEntityCode_ReturnsFalse()
        {
            var classifier = new ImportReceiptConflictClassifier();
            var receipt = new ImportReceipt { Code = "IM-OTHER" };

            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var ctx = new ErpKhoDbContext(options);
            var entry = ctx.Entry(receipt);

            var dbEx = new DbUpdateException("Error", new Exception("Generic error"), new List<EntityEntry> { entry });
            classifier.IsCodeUniqueConflict(dbEx, "IM-TARGET").Should().BeFalse("mismatched entity code must return false");
        }
    }

    #endregion

    #region SQLite Classifier Tests

    public class SqliteImportReceiptConflictClassifierTests
    {
        private (ErpKhoDbContext ctx, EntityEntry entry) CreateEntryForReceipt(string code)
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var ctx = new ErpKhoDbContext(options);
            var receipt = new ImportReceipt { Code = code };
            var entry = ctx.Entry(receipt);
            return (ctx, entry);
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteUniqueConstraintOnCode_ReturnsTrue()
        {
            var classifier = new SqliteImportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("IM-DUP");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: ImportReceipts.Code'.", 19, 2067);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "IM-DUP").Should().BeTrue();
            }
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteForeignKeyConstraint_ReturnsFalse()
        {
            var classifier = new SqliteImportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("IM-FK");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 19: 'FOREIGN KEY constraint failed'.", 19, 787);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "IM-FK").Should().BeFalse("FK constraint must not be classified as code conflict");
            }
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteCheckConstraint_ReturnsFalse()
        {
            var classifier = new SqliteImportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("IM-CHECK");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 19: 'CHECK constraint failed: CK_ImportReceipts_Quantity'.", 19, 275);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "IM-CHECK").Should().BeFalse("CHECK constraint must not be classified as code conflict");
            }
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteDatabaseLocked_ReturnsFalse()
        {
            var classifier = new SqliteImportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("IM-LOCK");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 5: 'database is locked'.", 5);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "IM-LOCK").Should().BeFalse("database locked must not be classified as code conflict");
            }
        }

        [Fact]
        public void IsCodeUniqueConflict_SqliteAnotherUniqueIndex_ReturnsFalse()
        {
            var classifier = new SqliteImportReceiptConflictClassifier();
            var (ctx, entry) = CreateEntryForReceipt("IM-OTHER-UNIQ");
            using (ctx)
            {
                var sqliteEx = new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: ImportReceipts.Note'.", 19, 2067);
                var dbEx = new DbUpdateException("Error", sqliteEx, new List<EntityEntry> { entry });
                classifier.IsCodeUniqueConflict(dbEx, "IM-OTHER-UNIQ").Should().BeFalse("another unique index on ImportReceipts must not be classified as code conflict");
            }
        }
    }

    #endregion

    #region Service & Repository Boundary Tests

    public class ImportReceiptServiceDuplicateCodeConcurrencyTests : IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _anchorConnection;

        public ImportReceiptServiceDuplicateCodeConcurrencyTests()
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

            var user = new User { Username = "dup_test_user", PasswordHash = "hash", FullName = "Dup Test User", Role = role, IsActive = true };
            ctx.Users.Add(user);

            var unit = new Unit { Code = "CAI", Name = "Cái" };
            ctx.Units.Add(unit);

            var warehouse = new Warehouse { Code = "WH-DUP", Name = "Kho Dup", IsActive = true };
            ctx.Warehouses.Add(warehouse);

            var product = new Product { Code = "SKU-DUP", Name = "SP Dup", Unit = unit, IsActive = true };
            ctx.Products.Add(product);

            await ctx.SaveChangesAsync();

            return (warehouse.Id, product.Id, user.Id);
        }

        private class BarrierExistsByCodeImportReceiptRepo : IImportReceiptRepository
        {
            private readonly IImportReceiptRepository _inner;
            private readonly System.Threading.Barrier _barrier;
            private int _callCount;

            public BarrierExistsByCodeImportReceiptRepo(IImportReceiptRepository inner, System.Threading.Barrier barrier)
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

            public Task<ImportReceipt?> GetByIdWithDetailsAsync(int id) => _inner.GetByIdWithDetailsAsync(id);
            public Task<ImportReceipt?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => _inner.GetByIdAsync(id, cancellationToken);
            public Task<IEnumerable<ImportReceipt>> GetAllAsync(CancellationToken cancellationToken = default) => _inner.GetAllAsync(cancellationToken);
            public Task<IEnumerable<ImportReceipt>> FindAsync(Expression<Func<ImportReceipt, bool>> predicate, CancellationToken cancellationToken = default) => _inner.FindAsync(predicate, cancellationToken);
            public Task<ImportReceipt> AddAsync(ImportReceipt entity, CancellationToken cancellationToken = default) => _inner.AddAsync(entity, cancellationToken);
            public Task UpdateAsync(ImportReceipt entity, CancellationToken cancellationToken = default) => _inner.UpdateAsync(entity, cancellationToken);
            public Task DeleteAsync(ImportReceipt entity, CancellationToken cancellationToken = default) => _inner.DeleteAsync(entity, cancellationToken);
            public Task<IEnumerable<ImportReceipt>> GetAllWithDetailsAsync(ReceiptStatus? status) => _inner.GetAllWithDetailsAsync(status);
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

            var testClassifier = new SqliteImportReceiptConflictClassifier();

            var repo1 = new BarrierExistsByCodeImportReceiptRepo(new ImportReceiptRepository(ctx1, testClassifier), existsBarrier);
            var repo2 = new BarrierExistsByCodeImportReceiptRepo(new ImportReceiptRepository(ctx2, testClassifier), existsBarrier);

            var service1 = new ImportReceiptService(
                repo1, new InventoryStockRepository(ctx1), new InventoryTransactionRepository(ctx1),
                new WarehouseRepository(ctx1), new ProductRepository(ctx1), new UnitOfWork(ctx1), new AuditLogRepository(ctx1));

            var service2 = new ImportReceiptService(
                repo2, new InventoryStockRepository(ctx2), new InventoryTransactionRepository(ctx2),
                new WarehouseRepository(ctx2), new ProductRepository(ctx2), new UnitOfWork(ctx2), new AuditLogRepository(ctx2));

            var dto1 = new CreateImportReceiptDto
            {
                Code = "IM-RACE-001",
                WarehouseId = warehouseId,
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = productId, Quantity = 10, UnitPrice = 100 }
                }
            };

            var dto2 = new CreateImportReceiptDto
            {
                Code = "IM-RACE-001",
                WarehouseId = warehouseId,
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = productId, Quantity = 10, UnitPrice = 100 }
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

            successCount.Should().Be(1, "exactly one create call must succeed");
            errorCount.Should().Be(1, "losing duplicate code create call must fail with BusinessRuleException");
            errorException.Should().BeOfType<BusinessRuleException>("losing call must receive mapped duplicate-code BusinessRuleException");
            errorException!.Message.Should().Contain("IM-RACE-001", "error message must state that code already exists");

            using var freshConn = CreateIndependentConnection();
            using var freshCtx = CreateContext(freshConn);
            var receipts = await freshCtx.ImportReceipts.Where(r => r.Code == "IM-RACE-001").ToListAsync();
            receipts.Should().HaveCount(1, "database must contain exactly one Draft receipt");
            receipts.First().Status.Should().Be(ReceiptStatus.Draft);

            var details = await freshCtx.ImportReceiptDetails.Where(d => d.ImportReceipt.Code == "IM-RACE-001").ToListAsync();
            details.Should().HaveCount(1, "exactly one detail row must exist for the receipt");

            var audits = await freshCtx.AuditLogs.Where(a => a.Action == "ImportReceipt.Created" && a.EntityId == receipts.First().Id).ToListAsync();
            audits.Should().HaveCount(1, "exactly one audit log row must exist");

            var stocks = await freshCtx.InventoryStocks.ToListAsync();
            stocks.Should().BeEmpty("Create must not create or mutate stock rows");

            var txs = await freshCtx.InventoryTransactions.ToListAsync();
            txs.Should().BeEmpty("Create must not create inventory transaction rows");
        }

        [Fact]
        public async Task CreateAsync_SequentialDuplicateCode_ThrowsBusinessRuleException()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var conn = CreateIndependentConnection();
            using var ctx = CreateContext(conn);
            var testClassifier = new SqliteImportReceiptConflictClassifier();
            var service = new ImportReceiptService(
                new ImportReceiptRepository(ctx, testClassifier),
                new InventoryStockRepository(ctx),
                new InventoryTransactionRepository(ctx),
                new WarehouseRepository(ctx),
                new ProductRepository(ctx),
                new UnitOfWork(ctx),
                new AuditLogRepository(ctx));

            var dto = new CreateImportReceiptDto
            {
                Code = "IM-SEQ-DUP",
                WarehouseId = warehouseId,
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = productId, Quantity = 5, UnitPrice = 50 }
                }
            };

            await service.CreateAsync(dto, userId);

            Func<Task> act = async () => await service.CreateAsync(dto, userId);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*IM-SEQ-DUP*đã tồn tại*");
        }

        [Fact]
        public async Task AddAsync_DirectRepositoryBoundary_UnrelatedDbUpdateException_RethrowsWithoutMapping()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var conn = CreateIndependentConnection();
            using var ctx = CreateContext(conn);

            var testClassifier = new SqliteImportReceiptConflictClassifier();
            var repo = new ImportReceiptRepository(ctx, testClassifier);

            var receipt = new ImportReceipt
            {
                Code = "IM-REPO-FK-FAIL",
                WarehouseId = 999999,
                Status = ReceiptStatus.Draft,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };

            Func<Task> act = async () => await repo.AddAsync(receipt);

            await act.Should().ThrowAsync<DbUpdateException>(
                "direct ImportReceiptRepository.AddAsync with invalid FK must rethrow original DbUpdateException without mapping to BusinessRuleException");
        }

        public void Dispose()
        {
            _anchorConnection.Close();
            _anchorConnection.Dispose();
        }
    }

    #endregion
}
