using ERP.Application.Exceptions;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Domain.Models;
using FluentAssertions;
using Moq;
using Xunit;

namespace ERP.Application.Tests
{
    public class ReportServiceTests
    {
        private readonly Mock<IReportRepository> _mockRepo;
        private readonly ReportService _service;

        public ReportServiceTests()
        {
            _mockRepo = new Mock<IReportRepository>();
            _service = new ReportService(_mockRepo.Object);
        }

        [Fact]
        public async Task GetInventoryInOutReportAsync_FromDateGreaterThanToDate_ThrowsBusinessRuleException()
        {
            var fromDate = new DateTime(2023, 1, 10);
            var toDate = new DateTime(2023, 1, 1);

            var act = async () => await _service.GetInventoryInOutReportAsync(fromDate, toDate, null, null);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Từ ngày không được lớn hơn đến ngày.");
        }

        [Fact]
        public async Task GetInventoryInOutReportAsync_ValidDates_ReturnsDtoAndCalculatesClosingQuantity()
        {
            var fromDate = new DateTime(2023, 1, 1);
            var toDate = new DateTime(2023, 1, 31);
            var mockModels = new List<InventoryInOutReportModel>
            {
                new InventoryInOutReportModel
                {
                    ProductId = 1, ProductCode = "P1", ProductName = "Prod1", UnitName = "Cai",
                    WarehouseId = 1, WarehouseName = "W1",
                    OpeningQuantity = 10.1234m,
                    InQuantity = 5.2222m,
                    OutQuantity = 2.1111m,
                    ClosingQuantity = 13.2345m // 10.1234 + 5.2222 - 2.1111
                }
            };
            
            _mockRepo.Setup(x => x.GetInventoryInOutReportAsync(fromDate, toDate, 1, 1))
                     .ReturnsAsync(mockModels);

            var result = await _service.GetInventoryInOutReportAsync(fromDate, toDate, 1, 1);

            var list = result.ToList();
            list.Should().HaveCount(1);
            list[0].ClosingQuantity.Should().Be(13.2345m);
            list[0].OpeningQuantity.Should().Be(10.1234m);
        }

        [Fact]
        public async Task GetInventoryReportAsync_CurrentMode_CallsGetCurrentStocksAsyncWithExactFiltersAndMapsFieldsAndReportDate()
        {
            var warehouseId = 10;
            var productId = 20;
            var unit = new Unit { Id = 1, Name = "Hộp" };
            var product = new Product { Id = productId, Code = "P20", Name = "Sản phẩm 20", Unit = unit };
            var warehouse = new Warehouse { Id = warehouseId, Name = "Kho Tổng" };

            var mockStocks = new List<InventoryStock>
            {
                new InventoryStock
                {
                    ProductId = productId,
                    Product = product,
                    WarehouseId = warehouseId,
                    Warehouse = warehouse,
                    Quantity = 150.5m,
                    LastUpdated = DateTime.UtcNow
                }
            };

            _mockRepo.Setup(x => x.GetCurrentStocksAsync(warehouseId, productId))
                     .ReturnsAsync(mockStocks);

            var startTime = DateTime.UtcNow;
            var result = await _service.GetInventoryReportAsync(null, warehouseId, productId);
            var endTime = DateTime.UtcNow;

            _mockRepo.Verify(x => x.GetCurrentStocksAsync(warehouseId, productId), Times.Once);

            var list = result.ToList();
            list.Should().HaveCount(1);
            var item = list[0];
            item.ProductId.Should().Be(productId);
            item.ProductCode.Should().Be("P20");
            item.ProductName.Should().Be("Sản phẩm 20");
            item.UnitName.Should().Be("Hộp");
            item.WarehouseId.Should().Be(warehouseId);
            item.WarehouseName.Should().Be("Kho Tổng");
            item.Quantity.Should().Be(150.5m);
            item.ReportDate.Should().BeOnOrAfter(startTime).And.BeOnOrBefore(endTime);
            item.LastUpdated.Should().Be(mockStocks[0].LastUpdated);
        }

        [Fact]
        public async Task GetInventoryReportAsync_HistoricalMode_CallsGetTransactionsUpToDateAsyncWithExactDateAndFilters()
        {
            var asOfDate = new DateTime(2026, 6, 30, 23, 59, 59);
            var warehouseId = 5;
            var productId = 15;

            var unit = new Unit { Id = 1, Name = "Cái" };
            var product = new Product { Id = productId, Code = "P15", Name = "SP 15", Unit = unit };
            var warehouse = new Warehouse { Id = warehouseId, Name = "Kho Phụ" };
            var txDate = new DateTime(2026, 6, 1);

            var mockTransactions = new List<InventoryTransaction>
            {
                new InventoryTransaction
                {
                    ProductId = productId,
                    Product = product,
                    WarehouseId = warehouseId,
                    Warehouse = warehouse,
                    Quantity = 50m,
                    TransactionType = TransactionType.Import,
                    TransactionDate = txDate
                }
            };

            _mockRepo.Setup(x => x.GetTransactionsUpToDateAsync(asOfDate, warehouseId, productId))
                     .ReturnsAsync(mockTransactions);

            var result = await _service.GetInventoryReportAsync(asOfDate, warehouseId, productId);

            _mockRepo.Verify(x => x.GetTransactionsUpToDateAsync(asOfDate, warehouseId, productId), Times.Once);

            var list = result.ToList();
            list.Should().HaveCount(1);
            list[0].ReportDate.Should().Be(asOfDate);
            list[0].LastUpdated.Should().Be(txDate);
            list[0].LastUpdated.Should().BeOnOrBefore(asOfDate);
            list[0].Quantity.Should().Be(50m);
        }

        [Fact]
        public async Task GetInventoryReportAsync_HistoricalMode_CalculatesNetQuantityForTransactionTypes_SeparatesByProductWarehouse_ExcludesZeroNetRows()
        {
            var asOfDate = new DateTime(2026, 7, 15);

            var unit = new Unit { Id = 1, Name = "Kg" };
            var p1 = new Product { Id = 1, Code = "P001", Name = "Sản phẩm 1", Unit = unit };
            var p2 = new Product { Id = 2, Code = "P002", Name = "Sản phẩm 2", Unit = unit };

            var w1 = new Warehouse { Id = 101, Name = "Kho A" };
            var w2 = new Warehouse { Id = 102, Name = "Kho B" };

            // P1/W1: Import(10) + AdjustmentIncrease(5) - Export(3) - AdjustmentDecrease(2) = 10
            // P1/W2: Import(20) - Export(20) = 0 (Must be EXCLUDED)
            // P2/W1: Import(100) - Export(25) = 75
            var mockTransactions = new List<InventoryTransaction>
            {
                // P1 / W1
                new InventoryTransaction { ProductId = 1, Product = p1, WarehouseId = 101, Warehouse = w1, Quantity = 10m, TransactionType = TransactionType.Import, TransactionDate = new DateTime(2026, 7, 1) },
                new InventoryTransaction { ProductId = 1, Product = p1, WarehouseId = 101, Warehouse = w1, Quantity = 5m, TransactionType = TransactionType.AdjustmentIncrease, TransactionDate = new DateTime(2026, 7, 5) },
                new InventoryTransaction { ProductId = 1, Product = p1, WarehouseId = 101, Warehouse = w1, Quantity = 3m, TransactionType = TransactionType.Export, TransactionDate = new DateTime(2026, 7, 8) },
                new InventoryTransaction { ProductId = 1, Product = p1, WarehouseId = 101, Warehouse = w1, Quantity = 2m, TransactionType = TransactionType.AdjustmentDecrease, TransactionDate = new DateTime(2026, 7, 10) },

                // P1 / W2 (Net 0 -> Excluded)
                new InventoryTransaction { ProductId = 1, Product = p1, WarehouseId = 102, Warehouse = w2, Quantity = 20m, TransactionType = TransactionType.Import, TransactionDate = new DateTime(2026, 7, 2) },
                new InventoryTransaction { ProductId = 1, Product = p1, WarehouseId = 102, Warehouse = w2, Quantity = 20m, TransactionType = TransactionType.Export, TransactionDate = new DateTime(2026, 7, 4) },

                // P2 / W1
                new InventoryTransaction { ProductId = 2, Product = p2, WarehouseId = 101, Warehouse = w1, Quantity = 100m, TransactionType = TransactionType.Import, TransactionDate = new DateTime(2026, 7, 3) },
                new InventoryTransaction { ProductId = 2, Product = p2, WarehouseId = 101, Warehouse = w1, Quantity = 25m, TransactionType = TransactionType.Export, TransactionDate = new DateTime(2026, 7, 12) }
            };

            _mockRepo.Setup(x => x.GetTransactionsUpToDateAsync(asOfDate, null, null))
                     .ReturnsAsync(mockTransactions);

            var result = (await _service.GetInventoryReportAsync(asOfDate, null, null)).ToList();

            // P1/W2 is zero net, so list count is 2 (P1/W1 and P2/W1)
            result.Should().HaveCount(2);

            var p1w1 = result.FirstOrDefault(x => x.ProductId == 1 && x.WarehouseId == 101);
            p1w1.Should().NotBeNull();
            p1w1!.Quantity.Should().Be(10m);
            p1w1.ProductCode.Should().Be("P001");
            p1w1.WarehouseName.Should().Be("Kho A");
            p1w1.ReportDate.Should().Be(asOfDate);
            p1w1.LastUpdated.Should().Be(new DateTime(2026, 7, 10));
            p1w1.LastUpdated.Should().BeOnOrBefore(asOfDate);

            var p1w2 = result.FirstOrDefault(x => x.ProductId == 1 && x.WarehouseId == 102);
            p1w2.Should().BeNull();

            var p2w1 = result.FirstOrDefault(x => x.ProductId == 2 && x.WarehouseId == 101);
            p2w1.Should().NotBeNull();
            p2w1!.Quantity.Should().Be(75m);
            p2w1.LastUpdated.Should().Be(new DateTime(2026, 7, 12));
            p2w1.LastUpdated.Should().BeOnOrBefore(asOfDate);
        }

        [Theory]
        [InlineData(TransactionType.Import, 1)]
        [InlineData(TransactionType.AdjustmentIncrease, 1)]
        [InlineData(TransactionType.TransferIn, 1)]
        [InlineData(TransactionType.Export, -1)]
        [InlineData(TransactionType.AdjustmentDecrease, -1)]
        [InlineData(TransactionType.TransferOut, -1)]
        public void InventoryTransactionTypeMapping_RecognizedType_ReturnsExpectedSign(TransactionType type, int sign)
        {
            type.GetSign().Should().Be(sign);
        }

        [Fact]
        public void InventoryTransactionTypeMapping_UnclassifiedType_Throws()
        {
            var act = () => TransactionType.TransferAdjustment.GetSign();

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public async Task GetInventoryReportAsync_HistoricalMode_IncludesTransferDirections()
        {
            var asOfDate = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
            var product = new Product { Id = 1, Code = "P1", Name = "Product" };
            var warehouse = new Warehouse { Id = 1, Code = "W1", Name = "Warehouse" };
            var transactions = new[]
            {
                new InventoryTransaction { ProductId = 1, Product = product, WarehouseId = 1, Warehouse = warehouse, Quantity = 100, TransactionType = TransactionType.Import },
                new InventoryTransaction { ProductId = 1, Product = product, WarehouseId = 1, Warehouse = warehouse, Quantity = 50, TransactionType = TransactionType.Import },
                new InventoryTransaction { ProductId = 1, Product = product, WarehouseId = 1, Warehouse = warehouse, Quantity = 20, TransactionType = TransactionType.TransferIn },
                new InventoryTransaction { ProductId = 1, Product = product, WarehouseId = 1, Warehouse = warehouse, Quantity = 5, TransactionType = TransactionType.AdjustmentIncrease },
                new InventoryTransaction { ProductId = 1, Product = product, WarehouseId = 1, Warehouse = warehouse, Quantity = 30, TransactionType = TransactionType.Export },
                new InventoryTransaction { ProductId = 1, Product = product, WarehouseId = 1, Warehouse = warehouse, Quantity = 25, TransactionType = TransactionType.TransferOut },
                new InventoryTransaction { ProductId = 1, Product = product, WarehouseId = 1, Warehouse = warehouse, Quantity = 10, TransactionType = TransactionType.AdjustmentDecrease }
            };
            _mockRepo.Setup(x => x.GetTransactionsUpToDateAsync(asOfDate, null, null, default)).ReturnsAsync(transactions);

            var result = (await _service.GetInventoryReportAsync(asOfDate, null, null)).Single();

            result.Quantity.Should().Be(110);
        }
    }
}
