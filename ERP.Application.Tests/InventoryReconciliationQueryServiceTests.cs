using System;
using System.Linq;
using System.Threading.Tasks;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using ERP.Application.Interfaces;
using ERP.Application.Exceptions;
using ERP.Domain.Exceptions;
using Moq;
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

            // Case 1: matches: Import 10 + AdjInc 5 + TransferIn 4 - Export 2 - Ship 1 - TransferOut 4 = 12.
            context.InventoryStocks.Add(new InventoryStock { ProductId = 1, WarehouseId = 1, Quantity = 12 });
            context.InventoryTransactions.AddRange(
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.Import, Quantity = 10, TransactionDate = DateTime.Now },
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.AdjustmentIncrease, Quantity = 5, TransactionDate = DateTime.Now },
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.TransferIn, Quantity = 4, TransactionDate = DateTime.Now },
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.Export, Quantity = 2, TransactionDate = DateTime.Now },
                new InventoryTransaction { ProductId = 1, WarehouseId = 1, TransactionType = TransactionType.Ship, Quantity = 1, TransactionDate = DateTime.Now },
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


        [Theory]
        [InlineData(0, null)]
        [InlineData(-2, null)]
        [InlineData(null, 0)]
        [InlineData(null, -3)]
        public async Task InvalidWarehouseOrProductId_IsRejectedBeforeAuthorization(
            int? warehouseId, int? productId)
        {
            using var context = await GetDbContextAsync();
            var authorization = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            var service = new InventoryReconciliationQueryService(context, authorization.Object);
            var act = () => service.GetReconciliationsAsync(warehouseId, productId, null);
            await act.Should().ThrowAsync<BusinessRuleException>()
                .WithMessage("*ID*");
            authorization.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AssignedWarehouseOnly_IsAppliedToPairCountAndSecondPhaseData()
        {
            using var context = await GetDbContextAsync();
            var authorization = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            authorization.Setup(x => x.GetAccessibleWarehouseIdsAsync(default))
                .ReturnsAsync(new[] { 1 });
            var service = new InventoryReconciliationQueryService(context, authorization.Object);

            var result = await service.GetReconciliationsAsync(null, null, null);
            result.Items.Should().OnlyContain(x => x.WarehouseId == 1);
            result.TotalRecords.Should().Be(3);
            result.Items.Single(x => x.ProductId == 1).CurrentQuantity.Should().Be(12);
            result.Items.Single(x => x.ProductId == 1).ExpectedQuantity.Should().Be(12);
            authorization.Verify(x => x.GetAccessibleWarehouseIdsAsync(default), Times.Once);
            authorization.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task ExplicitUnassignedWarehouse_FailsClosedAsNotFound()
        {
            using var context = await GetDbContextAsync();
            var authorization = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            authorization.Setup(x => x.EnsureWarehouseAccessAsync(2, default))
                .ThrowsAsync(new NotFoundException("Không tìm thấy tài nguyên."));
            var service = new InventoryReconciliationQueryService(context, authorization.Object);

            var act = () => service.GetReconciliationsAsync(2, 1, null);
            await act.Should().ThrowAsync<NotFoundException>();
            authorization.Verify(x => x.EnsureWarehouseAccessAsync(2, default), Times.Once);
            authorization.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task AuthorizedWarehouseSelector_ReturnsOnlyAssignedWarehouse()
        {
            using var context = await GetDbContextAsync();
            var authorization = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            authorization.Setup(x => x.GetAccessibleWarehouseIdsAsync(default))
                .ReturnsAsync(new[] { 1 });
            var service = new InventoryReconciliationQueryService(context, authorization.Object);

            var options = await service.GetAccessibleWarehousesAsync();
            options.Should().ContainSingle();
            options[0].Id.Should().Be(1);
            options[0].Code.Should().Be("W1");
            options[0].Name.Should().Be("Warehouse 1");
            authorization.Verify(x => x.GetAccessibleWarehouseIdsAsync(default), Times.Once);
        }

        [Fact]
        public async Task ExplicitAssignedWarehouse_ScopesBeforeReadingPairsAndTotals()
        {
            using var context = await GetDbContextAsync();
            var authorization = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            authorization.Setup(x => x.EnsureWarehouseAccessAsync(1, default))
                .Returns(Task.CompletedTask);
            var service = new InventoryReconciliationQueryService(context, authorization.Object);

            var data = await service.GetReconciliationsAsync(1, 1, null);
            data.Items.Should().ContainSingle();
            data.Items[0].WarehouseId.Should().Be(1);
            data.Items[0].ExpectedQuantity.Should().Be(12);
            authorization.Verify(x => x.EnsureWarehouseAccessAsync(1, default), Times.Once);
            authorization.VerifyNoOtherCalls();
        }


        [Theory]
        [InlineData(0, 1, null, 50)]
        [InlineData(1, 0, null, 50)]
        [InlineData(1, 1, -1, 50)]
        [InlineData(1, 1, null, 0)]
        [InlineData(1, 1, null, 101)]
        public async Task Investigation_InvalidInputs_FailBeforeAuthorization(
            int warehouseId, int productId, int? anchor, int limit)
        {
            using var db = await GetDbContextAsync();
            var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            var service = new InventoryReconciliationQueryService(db, auth.Object);
            var call = () => service.GetInvestigationAsync(warehouseId, productId, anchor, limit);
            await call.Should().ThrowAsync<BusinessRuleException>();
            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Investigation_DeniedWarehouse_DoesNotReadEvidence()
        {
            using var db = await GetDbContextAsync();
            var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            auth.Setup(x => x.EnsureWarehouseAccessAsync(2, default))
                .ThrowsAsync(new NotFoundException("Không tìm thấy tài nguyên."));
            var service = new InventoryReconciliationQueryService(db, auth.Object);
            var call = () => service.GetInvestigationAsync(2, 1);
            await call.Should().ThrowAsync<NotFoundException>();
            auth.Verify(x => x.EnsureWarehouseAccessAsync(2, default), Times.Once);
            auth.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Investigation_SignedLedgerAndCurrentStock_AreDistinctAndAnchored()
        {
            using var db = await GetDbContextAsync();
            var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            auth.Setup(x => x.EnsureWarehouseAccessAsync(1, default))
                .Returns(Task.CompletedTask);
            var service = new InventoryReconciliationQueryService(db, auth.Object);

            var result = await service.GetInvestigationAsync(1, 1, limit: 2);
            result.IsReadOnly.Should().BeTrue();
            result.ProductCode.Should().Be("P1");
            result.WarehouseId.Should().Be(1);
            result.CurrentQuantity.Should().Be(12m);
            result.ExpectedQuantity.Should().Be(12m);
            result.Difference.Should().Be(0m);
            result.BucketCount.Should().Be(1);
            result.Buckets.Should().ContainSingle();
            result.Buckets[0].Quantity.Should().Be(12m);
            result.EventCount.Should().Be(6);
            result.Events.Should().HaveCount(2);
            result.EventsTruncated.Should().BeTrue();
            result.Events.Select(x => x.TransactionId).Should().BeInDescendingOrder();
            result.EventAnchorId.Should().BeGreaterThan(0);

            db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = 1, WarehouseId = 1,
                TransactionType = TransactionType.AdjustmentIncrease,
                Quantity = 2m, InventoryStatus = InventoryStatus.Available,
                TransactionDate = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var anchored = await service.GetInvestigationAsync(1, 1, result.EventAnchorId, limit: 2);
            anchored.EventCount.Should().Be(6);
            anchored.ExpectedQuantity.Should().Be(12m);
            anchored.Events.Select(x => x.TransactionId)
                .Should().Equal(result.Events.Select(x => x.TransactionId));

            var fresh = await service.GetInvestigationAsync(1, 1, limit: 2);
            fresh.EventCount.Should().Be(7);
            fresh.ExpectedQuantity.Should().Be(14m);
            fresh.Difference.Should().Be(-2m);
            fresh.EventAnchorId.Should().BeGreaterThan(result.EventAnchorId);
            auth.Verify(x => x.EnsureWarehouseAccessAsync(1, default), Times.Exactly(3));
        }

        [Fact]
        public async Task Investigation_EmptyPairOrMissingProduct_FailsClosed()
        {
            using var db = await GetDbContextAsync();
            var auth = new Mock<IWarehouseAuthorizationService>(MockBehavior.Strict);
            auth.Setup(x => x.EnsureWarehouseAccessAsync(1, default))
                .Returns(Task.CompletedTask);
            var service = new InventoryReconciliationQueryService(db, auth.Object);
            var missingPair = () => service.GetInvestigationAsync(1, 3, eventAnchorId: 0);
            await missingPair.Should().ThrowAsync<NotFoundException>();
            var missingProduct = () => service.GetInvestigationAsync(1, int.MaxValue);
            await missingProduct.Should().ThrowAsync<NotFoundException>();
        }

        [Fact]
        public async Task GetReconciliationsAsync_CalculatesCorrectExpectedQuantity()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryReconciliationQueryService(context);

            var result = await service.GetReconciliationsAsync(null, null, null);

            result.TotalRecords.Should().Be(5); // 5 distinct pairs

            var matchP1W1 = result.Items.Single(x => x.ProductId == 1 && x.WarehouseId == 1);
            matchP1W1.CurrentQuantity.Should().Be(12);
            matchP1W1.ExpectedQuantity.Should().Be(12);
            matchP1W1.Difference.Should().Be(0);
            matchP1W1.Status.Should().Be("Match");
            matchP1W1.ImportQuantity.Should().Be(10);
            matchP1W1.TransferInQuantity.Should().Be(4);
            matchP1W1.TransferOutQuantity.Should().Be(4);
            matchP1W1.AdjustmentIncreaseQuantity.Should().Be(5);
            matchP1W1.ExportQuantity.Should().Be(3);

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
        public async Task GetReconciliationsAsync_ClampsInvalidPagingAndPreventsOffsetOverflow()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryReconciliationQueryService(context);

            var farPage = await service.GetReconciliationsAsync(
                null, null, null, pageIndex: int.MaxValue, pageSize: int.MaxValue);
            farPage.TotalRecords.Should().Be(5);
            farPage.Items.Should().BeEmpty();
            farPage.PageIndex.Should().Be(int.MaxValue);
            farPage.PageSize.Should().Be(100);

            var invalidPage = await service.GetReconciliationsAsync(
                null, null, null, pageIndex: -200, pageSize: 0);
            invalidPage.TotalRecords.Should().Be(5);
            invalidPage.Items.Should().ContainSingle();
            invalidPage.PageIndex.Should().Be(1);
            invalidPage.PageSize.Should().Be(1);
        }

        [Fact]
        public async Task GetReconciliationsAsync_TrimsKeywordBeforeQuery()
        {
            using var context = await GetDbContextAsync();
            var service = new InventoryReconciliationQueryService(context);

            var result = await service.GetReconciliationsAsync(null, null, "  BeTa  ");

            result.TotalRecords.Should().Be(2);
            result.Items.Should().OnlyContain(row => row.ProductId == 2);
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
