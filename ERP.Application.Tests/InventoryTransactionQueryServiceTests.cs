using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ERP.Application.Tests
{
    public class InventoryTransactionQueryServiceTests
    {
        private async Task<ErpKhoDbContext> GetDbContextAsync()
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new ErpKhoDbContext(options);

            var user = new User { Id = 1, Username = "testuser", FullName = "Test User" };
            var unit = new Unit { Id = 1, Name = "Cai" };
            
            var product1 = new Product { Id = 1, Code = "P1", Name = "Product 1", UnitId = 1 };
            var product2 = new Product { Id = 2, Code = "P2", Name = "Product 2", UnitId = 1 };
            var warehouse1 = new Warehouse { Id = 1, Code = "W1", Name = "Warehouse 1" };
            var warehouse2 = new Warehouse { Id = 2, Code = "W2", Name = "Warehouse 2" };

            context.Users.Add(user);
            context.Units.Add(unit);
            context.Products.AddRange(product1, product2);
            context.Warehouses.AddRange(warehouse1, warehouse2);

            var t1 = new InventoryTransaction 
            { 
                Id = 1, ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.Import, Quantity = 10,
                TransactionDate = new DateTime(2023, 1, 1), CreatedBy = 1, Note = "Tx 1"
            };
            var t2 = new InventoryTransaction 
            { 
                Id = 2, ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.Export, Quantity = 5,
                TransactionDate = new DateTime(2023, 1, 5), CreatedBy = 1, Note = "Tx 2"
            };
            var t3 = new InventoryTransaction 
            { 
                Id = 3, ProductId = 2, WarehouseId = 2, TransactionType = TransactionType.Import, Quantity = 20,
                TransactionDate = new DateTime(2023, 1, 10), CreatedBy = 1, Note = "Tx 3"
            };
            var t4 = new InventoryTransaction 
            { 
                Id = 4, ProductId = 1, WarehouseId = 2, TransactionType = TransactionType.AdjustmentIncrease, Quantity = 0,
                TransactionDate = new DateTime(2023, 1, 15), CreatedBy = 1, Note = "Tx 4"
            };
            var t5 = new InventoryTransaction 
            { 
                Id = 5, ProductId = 2, WarehouseId = 1, TransactionType = TransactionType.Import, Quantity = 50,
                TransactionDate = new DateTime(2023, 1, 20), CreatedBy = 1, Note = "Tx 5"
            };

            context.InventoryTransactions.AddRange(t1, t2, t3, t4, t5);

            await context.SaveChangesAsync();
            return context;
        }

        [Fact]
        public async Task GetHistoryAsync_DateRangeFilter_ReturnsCorrectRecords()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryTransactionQueryService(context);

            var result = await service.GetHistoryAsync(
                new DateTime(2023, 1, 4), new DateTime(2023, 1, 12), null, null, null, null, null, null);

            result.TotalRecords.Should().Be(2); // t2, t3
            result.Items.Should().Contain(t => t.Id == 2);
            result.Items.Should().Contain(t => t.Id == 3);
        }

        [Fact]
        public async Task GetHistoryAsync_TransactionTypeFilter_ReturnsCorrectRecords()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryTransactionQueryService(context);

            var result = await service.GetHistoryAsync(
                null, null, TransactionType.Import, null, null, null, null, null);

            result.TotalRecords.Should().Be(3); // t1, t3, t5
            result.Items.All(t => t.TransactionType == "Import").Should().BeTrue();
        }

        [Fact]
        public async Task GetHistoryAsync_WarehouseAndProductFilter_ReturnsCorrectRecords()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryTransactionQueryService(context);

            var result = await service.GetHistoryAsync(
                null, null, null, 1, 1, null, null, null); // Warehouse 1, Product 1

            result.TotalRecords.Should().Be(2); // t1, t2
            result.Items.Should().OnlyContain(t => t.WarehouseId == 1 && t.ProductId == 1);
        }

        [Fact]
        public async Task GetHistoryAsync_DefaultSort_ReturnsNewestFirst()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryTransactionQueryService(context);

            var result = await service.GetHistoryAsync(
                null, null, null, null, null, null, null, null);

            result.TotalRecords.Should().Be(5);
            // Newest first: t5(Jan 20), t4(Jan 15), t3(Jan 10), t2(Jan 5), t1(Jan 1)
            result.Items[0].Id.Should().Be(5);
            result.Items[1].Id.Should().Be(4);
            result.Items[2].Id.Should().Be(3);
            result.Items[3].Id.Should().Be(2);
            result.Items[4].Id.Should().Be(1);
        }

        [Fact]
        public async Task GetHistoryAsync_Paging_ReturnsCorrectPage()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryTransactionQueryService(context);

            var result = await service.GetHistoryAsync(
                null, null, null, null, null, null, null, null, pageIndex: 2, pageSize: 2);

            result.TotalRecords.Should().Be(5);
            result.Items.Should().HaveCount(2);
            // Page 1: t5, t4. Page 2: t3, t2
            result.Items[0].Id.Should().Be(3);
            result.Items[1].Id.Should().Be(2);
            result.PageIndex.Should().Be(2);
            result.PageSize.Should().Be(2);
        }
    }
}
