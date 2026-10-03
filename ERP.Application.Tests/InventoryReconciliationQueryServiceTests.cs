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
    public class InventoryReconciliationQueryServiceTests
    {
        private async Task<ErpKhoDbContext> GetDbContextAsync()
        {
            var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new ErpKhoDbContext(options);

            var product1 = new Product { Id = 1, Code = "P1", Name = "Product Alpha" };
            var product2 = new Product { Id = 2, Code = "P2", Name = "Product Beta" };
            var product3 = new Product { Id = 3, Code = "P3", Name = "Product Gamma" };
            var warehouse1 = new Warehouse { Id = 1, Code = "W1", Name = "Warehouse 1" };
            var warehouse2 = new Warehouse { Id = 2, Code = "W2", Name = "Warehouse 2" };

            context.Products.AddRange(product1, product2, product3);
            context.Warehouses.AddRange(warehouse1, warehouse2);

            // Case 1: matches: Import 10 + AdjInc 5 + TransferIn 4 - Export 2 - TransferOut 4 = 13.
            context.InventoryStocks.Add(new InventoryStock { ProductId = 1, WarehouseId = 1, Quantity = 13 });
            context.InventoryTransactions.AddRange(
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.Import, Quantity = 10, TransactionDate = DateTime.Now },
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.AdjustmentIncrease, Quantity = 5, TransactionDate = DateTime.Now },
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.TransferIn, Quantity = 4, TransactionDate = DateTime.Now },
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.Export, Quantity = 2, TransactionDate = DateTime.Now },
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.TransferOut, Quantity = 4, TransactionDate = DateTime.Now }
            );

            // Case 2: Stock for P2-W1: mismatch (Stock 20, Expected 25)
            context.InventoryStocks.Add(new InventoryStock { ProductId = 2, WarehouseId = 1, Quantity = 20 });
            context.InventoryTransactions.Add(
                new InventoryTransaction { ProductId = 2, WarehouseId = 1, TransactionType = TransactionType.Import, Quantity = 25, TransactionDate = DateTime.Now }
            );

            // Case 3: Stock for P1-W2: mismatch (Stock 0, Expected -5)
            context.InventoryStocks.Add(new InventoryStock { ProductId = 1, WarehouseId = 2, Quantity = 0 });
            context.InventoryTransactions.Add(
                new InventoryTransaction { ProductId = 1, WarehouseId = 2, TransactionType = TransactionType.Export, Quantity = 5, TransactionDate = DateTime.Now }
            );

            // Case 4: Stock for P2-W2: Stock exists (10), no transactions (Expected 0) -> Difference 10
            context.InventoryStocks.Add(new InventoryStock { ProductId = 2, WarehouseId = 2, Quantity = 10 });
            
            // Case 5: Transaction for P3-W1: No stock (0), transaction exists (Expected 5) -> Difference -5
            context.InventoryTransactions.Add(
                new InventoryTransaction { ProductId = 3, WarehouseId = 1, TransactionType = TransactionType.Import, Quantity = 5, TransactionDate = DateTime.Now }
            );

            await context.SaveChangesAsync();
            return context;
        }

        [Fact]
        public async Task GetReconciliationsAsync_CalculatesCorrectExpectedQuantity()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryReconciliationQueryService(context);

            var result = await service.GetReconciliationsAsync(null, null, null);

            result.TotalRecords.Should().Be(5); // 5 distinct pairs

            var matchP1W1 = result.Items.Single(x => x.ProductId == 1 && x.WarehouseId == 1);
            matchP1W1.CurrentQuantity.Should().Be(13);
            matchP1W1.ExpectedQuantity.Should().Be(13);
            matchP1W1.Difference.Should().Be(0);
            matchP1W1.Status.Should().Be("Match");
            matchP1W1.ImportQuantity.Should().Be(10);
            matchP1W1.TransferInQuantity.Should().Be(4);
            matchP1W1.TransferOutQuantity.Should().Be(4);
            matchP1W1.AdjustmentIncreaseQuantity.Should().Be(5);
            matchP1W1.ExportQuantity.Should().Be(2);

            var mismatchP2W1 = result.Items.Single(x => x.ProductId == 2 && x.WarehouseId == 1);
            mismatchP2W1.CurrentQuantity.Should().Be(20);
            mismatchP2W1.ExpectedQuantity.Should().Be(25);
            mismatchP2W1.Difference.Should().Be(-5);
            mismatchP2W1.Status.Should().Be("Mismatch");

            var mismatchP1W2 = result.Items.Single(x => x.ProductId == 1 && x.WarehouseId == 2);
            mismatchP1W2.CurrentQuantity.Should().Be(0);
            mismatchP1W2.ExpectedQuantity.Should().Be(-5);
            mismatchP1W2.Difference.Should().Be(5);
            mismatchP1W2.Status.Should().Be("Mismatch");

            var mismatchP2W2 = result.Items.Single(x => x.ProductId == 2 && x.WarehouseId == 2);
            mismatchP2W2.CurrentQuantity.Should().Be(10);
            mismatchP2W2.ExpectedQuantity.Should().Be(0);
            mismatchP2W2.Difference.Should().Be(10);
            mismatchP2W2.Status.Should().Be("Mismatch");

            var mismatchP3W1 = result.Items.Single(x => x.ProductId == 3 && x.WarehouseId == 1);
            mismatchP3W1.CurrentQuantity.Should().Be(0);
            mismatchP3W1.ExpectedQuantity.Should().Be(5);
            mismatchP3W1.Difference.Should().Be(-5);
            mismatchP3W1.Status.Should().Be("Mismatch");
        }

        [Fact]
        public async Task GetReconciliationsAsync_FiltersByWarehouseAndProductCorrectly()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryReconciliationQueryService(context);

            var result = await service.GetReconciliationsAsync(1, 2, null);

            result.TotalRecords.Should().Be(1);
            result.Items[0].ProductId.Should().Be(2);
            result.Items[0].WarehouseId.Should().Be(1);
        }

        [Fact]
        public async Task GetReconciliationsAsync_FiltersByKeywordCorrectly_ProductName()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryReconciliationQueryService(context);

            // "Beta" is only Product 2
            var result = await service.GetReconciliationsAsync(null, null, "beta");

            result.TotalRecords.Should().Be(2); // P2-W1 and P2-W2
            result.Items.All(x => x.ProductId == 2).Should().BeTrue();
        }

        [Fact]
        public async Task GetReconciliationsAsync_FiltersByKeywordCorrectly_ProductCode()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryReconciliationQueryService(context);

            // "P3" is only Product 3 (Transaction only case)
            var result = await service.GetReconciliationsAsync(null, null, "p3");

            result.TotalRecords.Should().Be(1);
            result.Items[0].ProductId.Should().Be(3);
            result.Items[0].WarehouseId.Should().Be(1);
        }
    }
}
