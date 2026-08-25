using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Application.DTOs;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Repositories;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP.Application.Tests
{
    public class ImportReceiptServicePersistenceAtomicityTests : IDisposable
    {
        private readonly string _connectionString;
        private readonly SqliteConnection _connection;

        public ImportReceiptServicePersistenceAtomicityTests()
        {
            var dbName = Guid.NewGuid().ToString("N");
            _connectionString = $"DataSource=file:{dbName}?mode=memory&cache=shared;Default Timeout=5";
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            using var setupCtx = CreateContext();
            setupCtx.Database.EnsureCreated();
        }

        private TestDbContext CreateContext()
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseSqlite(_connection)
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
            using var ctx = CreateContext();
            var role = new Role { RoleName = "Admin" };
            ctx.Roles.Add(role);

            var user = new User { Username = "admin", PasswordHash = "hash", FullName = "Admin", Role = role, IsActive = true };
            ctx.Users.Add(user);

            var unit = new Unit { Code = "CAI", Name = "Cái" };
            ctx.Units.Add(unit);

            var warehouse = new Warehouse { Code = "WH-ATOM", Name = "Kho Atom", IsActive = true };
            ctx.Warehouses.Add(warehouse);

            var product = new Product { Code = "SKU-ATOM", Name = "SP Atom", Unit = unit, IsActive = true };
            ctx.Products.Add(product);

            await ctx.SaveChangesAsync();

            return (warehouse.Id, product.Id, user.Id);
        }

        private class ThrowingAuditLogRepo : IAuditLogRepository
        {
            private readonly IAuditLogRepository _inner;
            public ThrowingAuditLogRepo(IAuditLogRepository inner) => _inner = inner;

            public Task AddAsync(AuditLog entity)
            {
                throw new InvalidOperationException("Injected AuditLog DB write failure");
            }
        }

        private class ThrowingCommitUnitOfWork : IUnitOfWork
        {
            private readonly IUnitOfWork _inner;
            public ThrowingCommitUnitOfWork(IUnitOfWork inner) => _inner = inner;

            public Task BeginTransactionAsync() => _inner.BeginTransactionAsync();

            public Task CommitTransactionAsync()
            {
                throw new InvalidOperationException("Injected UnitOfWork Commit failure");
            }

            public Task RollbackTransactionAsync() => _inner.RollbackTransactionAsync();
            public Task<int> SaveChangesAsync() => _inner.SaveChangesAsync();
        }

        [Fact]
        public async Task CreateAsync_Success_PersistsDraftReceiptDetailsAndCreatedAuditInFreshContext()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var ctx = CreateContext();
            var service = new ImportReceiptService(
                new ImportReceiptRepository(ctx),
                new InventoryStockRepository(ctx),
                new InventoryTransactionRepository(ctx),
                new WarehouseRepository(ctx),
                new ProductRepository(ctx),
                new UnitOfWork(ctx),
                new AuditLogRepository(ctx));

            var dto = new CreateImportReceiptDto
            {
                Code = "IM-PERSIST-01",
                WarehouseId = warehouseId,
                Note = "Fresh Context Persistence Note",
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = productId, Quantity = 15, UnitPrice = 200, Note = "D1" }
                }
            };

            var result = await service.CreateAsync(dto, userId);

            result.Should().NotBeNull();
            result.Code.Should().Be("IM-PERSIST-01");
            result.Status.Should().Be("Draft");

            using var freshCtx = CreateContext();
            var receipt = await freshCtx.ImportReceipts.Include(r => r.Details).FirstOrDefaultAsync(r => r.Code == "IM-PERSIST-01");
            receipt.Should().NotBeNull();
            receipt!.Details.Should().HaveCount(1);
            receipt.Details.First().Quantity.Should().Be(15);

            var audit = await freshCtx.AuditLogs.FirstOrDefaultAsync(a => a.Action == "ImportReceipt.Created" && a.EntityId == receipt.Id);
            audit.Should().NotBeNull();
            audit!.UserId.Should().Be(userId);

            var stocks = await freshCtx.InventoryStocks.ToListAsync();
            stocks.Should().BeEmpty("Create must not mutate inventory stock rows");

            var txs = await freshCtx.InventoryTransactions.ToListAsync();
            txs.Should().BeEmpty("Create must not create inventory transaction rows");
        }

        [Fact]
        public async Task CreateAsync_AuditLogFailure_RollsBackAllReceiptHeaderDetailsAndAuditRowsInFreshContext()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var ctx = CreateContext();
            var service = new ImportReceiptService(
                new ImportReceiptRepository(ctx),
                new InventoryStockRepository(ctx),
                new InventoryTransactionRepository(ctx),
                new WarehouseRepository(ctx),
                new ProductRepository(ctx),
                new UnitOfWork(ctx),
                new ThrowingAuditLogRepo(new AuditLogRepository(ctx)));

            var dto = new CreateImportReceiptDto
            {
                Code = "IM-PERSIST-FAIL-AUDIT",
                WarehouseId = warehouseId,
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = productId, Quantity = 15, UnitPrice = 200 }
                }
            };

            Func<Task> act = async () => await service.CreateAsync(dto, userId);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected AuditLog DB write failure");

            using var freshCtx = CreateContext();
            var receipts = await freshCtx.ImportReceipts.Where(r => r.Code == "IM-PERSIST-FAIL-AUDIT").ToListAsync();
            receipts.Should().BeEmpty("Audit log failure must roll back receipt header creation");

            var details = await freshCtx.ImportReceiptDetails.Where(d => d.ImportReceipt.Code == "IM-PERSIST-FAIL-AUDIT").ToListAsync();
            details.Should().BeEmpty("Audit log failure must roll back receipt details creation");

            var audits = await freshCtx.AuditLogs.Where(a => a.Action == "ImportReceipt.Created" && a.NewValues != null && a.NewValues.Contains("IM-PERSIST-FAIL-AUDIT")).ToListAsync();
            audits.Should().BeEmpty("Audit log failure must leave zero audit records in database");
        }

        [Fact]
        public async Task CreateAsync_CommitFailure_RollsBackAllReceiptHeaderDetailsAndAuditRowsInFreshContext()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var ctx = CreateContext();
            var service = new ImportReceiptService(
                new ImportReceiptRepository(ctx),
                new InventoryStockRepository(ctx),
                new InventoryTransactionRepository(ctx),
                new WarehouseRepository(ctx),
                new ProductRepository(ctx),
                new ThrowingCommitUnitOfWork(new UnitOfWork(ctx)),
                new AuditLogRepository(ctx));

            var dto = new CreateImportReceiptDto
            {
                Code = "IM-PERSIST-FAIL-COMMIT",
                WarehouseId = warehouseId,
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = productId, Quantity = 15, UnitPrice = 200 }
                }
            };

            Func<Task> act = async () => await service.CreateAsync(dto, userId);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected UnitOfWork Commit failure");

            using var freshCtx = CreateContext();
            var receipts = await freshCtx.ImportReceipts.Where(r => r.Code == "IM-PERSIST-FAIL-COMMIT").ToListAsync();
            receipts.Should().BeEmpty("Commit failure must roll back receipt header creation");

            var details = await freshCtx.ImportReceiptDetails.Where(d => d.ImportReceipt.Code == "IM-PERSIST-FAIL-COMMIT").ToListAsync();
            details.Should().BeEmpty("Commit failure must roll back receipt details creation");

            var audits = await freshCtx.AuditLogs.Where(a => a.Action == "ImportReceipt.Created" && a.NewValues != null && a.NewValues.Contains("IM-PERSIST-FAIL-COMMIT")).ToListAsync();
            audits.Should().BeEmpty("Commit failure must leave zero audit records in database");
        }

        [Fact]
        public async Task CancelAsync_Success_PersistsCancelledStatusAndCancelledAuditInFreshContext()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var seedCtx = CreateContext();
            var receipt = new ImportReceipt
            {
                Code = "IM-CANCEL-PERSIST-01",
                WarehouseId = warehouseId,
                Status = ReceiptStatus.Draft,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            seedCtx.ImportReceipts.Add(receipt);
            await seedCtx.SaveChangesAsync();

            using var cancelCtx = CreateContext();
            var service = new ImportReceiptService(
                new ImportReceiptRepository(cancelCtx),
                new InventoryStockRepository(cancelCtx),
                new InventoryTransactionRepository(cancelCtx),
                new WarehouseRepository(cancelCtx),
                new ProductRepository(cancelCtx),
                new UnitOfWork(cancelCtx),
                new AuditLogRepository(cancelCtx));

            await service.CancelAsync(receipt.Id, userId);

            using var freshCtx = CreateContext();
            var freshReceipt = await freshCtx.ImportReceipts.FindAsync(receipt.Id);
            freshReceipt!.Status.Should().Be(ReceiptStatus.Cancelled);

            var audit = await freshCtx.AuditLogs.FirstOrDefaultAsync(a => a.Action == "ImportReceipt.Cancelled" && a.EntityId == receipt.Id);
            audit.Should().NotBeNull();
            audit!.UserId.Should().Be(userId);

            var stocks = await freshCtx.InventoryStocks.ToListAsync();
            stocks.Should().BeEmpty("Cancel operation must not mutate stock rows");

            var txs = await freshCtx.InventoryTransactions.ToListAsync();
            txs.Should().BeEmpty("Cancel operation must not create transaction rows");
        }

        [Fact]
        public async Task CancelAsync_AuditLogFailure_RollsBackCancelledStatusAndLeavesNoAuditLogInFreshContext()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var seedCtx = CreateContext();
            var receipt = new ImportReceipt
            {
                Code = "IM-CANCEL-FAIL-AUDIT-PERSIST",
                WarehouseId = warehouseId,
                Status = ReceiptStatus.Draft,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            seedCtx.ImportReceipts.Add(receipt);
            await seedCtx.SaveChangesAsync();

            using var cancelCtx = CreateContext();
            var service = new ImportReceiptService(
                new ImportReceiptRepository(cancelCtx),
                new InventoryStockRepository(cancelCtx),
                new InventoryTransactionRepository(cancelCtx),
                new WarehouseRepository(cancelCtx),
                new ProductRepository(cancelCtx),
                new UnitOfWork(cancelCtx),
                new ThrowingAuditLogRepo(new AuditLogRepository(cancelCtx)));

            Func<Task> act = async () => await service.CancelAsync(receipt.Id, userId);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected AuditLog DB write failure");

            using var freshCtx = CreateContext();
            var freshReceipt = await freshCtx.ImportReceipts.FindAsync(receipt.Id);
            freshReceipt!.Status.Should().Be(ReceiptStatus.Draft, "Audit log failure must restore Draft status in fresh context");

            var audits = await freshCtx.AuditLogs.Where(a => a.Action == "ImportReceipt.Cancelled" && a.EntityId == receipt.Id).ToListAsync();
            audits.Should().BeEmpty("Audit log failure must leave zero cancellation audit records");
        }

        [Fact]
        public async Task CancelAsync_CommitFailure_RollsBackCancelledStatusAndLeavesNoAuditLogInFreshContext()
        {
            var (warehouseId, productId, userId) = await SeedTestDataAsync();

            using var seedCtx = CreateContext();
            var receipt = new ImportReceipt
            {
                Code = "IM-CANCEL-FAIL-COMMIT-PERSIST",
                WarehouseId = warehouseId,
                Status = ReceiptStatus.Draft,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            seedCtx.ImportReceipts.Add(receipt);
            await seedCtx.SaveChangesAsync();

            using var cancelCtx = CreateContext();
            var service = new ImportReceiptService(
                new ImportReceiptRepository(cancelCtx),
                new InventoryStockRepository(cancelCtx),
                new InventoryTransactionRepository(cancelCtx),
                new WarehouseRepository(cancelCtx),
                new ProductRepository(cancelCtx),
                new ThrowingCommitUnitOfWork(new UnitOfWork(cancelCtx)),
                new AuditLogRepository(cancelCtx));

            Func<Task> act = async () => await service.CancelAsync(receipt.Id, userId);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Injected UnitOfWork Commit failure");

            using var freshCtx = CreateContext();
            var freshReceipt = await freshCtx.ImportReceipts.FindAsync(receipt.Id);
            freshReceipt!.Status.Should().Be(ReceiptStatus.Draft, "Commit failure must restore Draft status in fresh context");

            var audits = await freshCtx.AuditLogs.Where(a => a.Action == "ImportReceipt.Cancelled" && a.EntityId == receipt.Id).ToListAsync();
            audits.Should().BeEmpty("Commit failure must leave zero cancellation audit records");
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();
        }
    }
}
