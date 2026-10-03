using ERP.Application.Exceptions;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace ERP.Application.Tests
{
    public class StocktakeServiceTests
    {
        private readonly Mock<IStocktakeRepository> _mockStocktakeRepo;
        private readonly Mock<IInventoryStockRepository> _mockStockRepo;
        private readonly Mock<IInventoryTransactionRepository> _mockTransactionRepo;
        private readonly Mock<IUnitOfWork> _mockUnitOfWork;
        private readonly Mock<IWarehouseRepository> _mockWarehouseRepo;
        private readonly Mock<IAuditLogRepository> _mockAuditLogRepo;
        private readonly StocktakeService _service;

        public StocktakeServiceTests()
        {
            _mockStocktakeRepo = new Mock<IStocktakeRepository>();
            _mockStockRepo = new Mock<IInventoryStockRepository>();
            _mockTransactionRepo = new Mock<IInventoryTransactionRepository>();
            _mockUnitOfWork = new Mock<IUnitOfWork>();
            _mockWarehouseRepo = new Mock<IWarehouseRepository>();
            _mockAuditLogRepo = new Mock<IAuditLogRepository>();

            _service = new StocktakeService(
                _mockStocktakeRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockWarehouseRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditLogRepo.Object);
        }

        [Fact]
        public async Task ApproveStocktakeAsync_NotFound_ThrowsNotFoundExceptionAndRollbacks()
        {
            _mockStocktakeRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync((Stocktake?)null);

            var act = async () => await _service.ApproveStocktakeAsync(1, 99);

            await act.Should().ThrowAsync<NotFoundException>();
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockAuditLogRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task ApproveStocktakeAsync_NotDraft_ThrowsBusinessRuleExceptionAndRollbacks()
        {
            var stocktake = new Stocktake { Id = 1, Status = ReceiptStatus.Approved };
            _mockStocktakeRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(stocktake);

            var act = async () => await _service.ApproveStocktakeAsync(1, 99);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Chỉ có thể duyệt phiếu ở trạng thái nháp");
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockAuditLogRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task ApproveStocktakeAsync_ActualQuantityNull_ThrowsBusinessRuleExceptionAndRollbacks()
        {
            var stocktake = new Stocktake 
            { 
                Id = 1, 
                Status = ReceiptStatus.Draft,
                Details = new List<StocktakeDetail> { new StocktakeDetail { ProductId = 1, ActualQuantity = null } }
            };
            _mockStocktakeRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(stocktake);

            var act = async () => await _service.ApproveStocktakeAsync(1, 99);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Chưa nhập số lượng thực tế cho sản phẩm id 1");
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockAuditLogRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task ApproveStocktakeAsync_DiffGreaterThanZero_CreatesIncreaseTransactionAndUpdatesStock()
        {
            var stocktake = new Stocktake 
            { 
                Id = 1, 
                WarehouseId = 2,
                Status = ReceiptStatus.Draft,
                Details = new List<StocktakeDetail> { new StocktakeDetail { ProductId = 1, SystemQuantity = 10, ActualQuantity = 15 } }
            };
            var existingStock = new InventoryStock { ProductId = 1, WarehouseId = 2, Quantity = 10 };
            
            _mockStocktakeRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(stocktake);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 2)).ReturnsAsync(existingStock);

            await _service.ApproveStocktakeAsync(1, 99);

            stocktake.Status.Should().Be(ReceiptStatus.Approved);
            stocktake.ApprovedBy.Should().Be(99);
            stocktake.Details.First().DifferenceQuantity.Should().Be(5);
            existingStock.Quantity.Should().Be(15);
            
            _mockStockRepo.Verify(x => x.UpdateAsync(existingStock), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t => 
                t.TransactionType == TransactionType.AdjustmentIncrease && 
                t.Quantity == 5 && 
                t.CreatedBy == 99
            )), Times.Once);
            _mockAuditLogRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => a.Action == "Stocktake.Approved" && a.UserId == 99)), Times.Once);
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveStocktakeAsync_DiffLessThanZero_CreatesDecreaseTransactionAndUpdatesStock()
        {
            var stocktake = new Stocktake 
            { 
                Id = 1, 
                WarehouseId = 2,
                Status = ReceiptStatus.Draft,
                Details = new List<StocktakeDetail> { new StocktakeDetail { ProductId = 1, SystemQuantity = 10, ActualQuantity = 5 } }
            };
            var existingStock = new InventoryStock { ProductId = 1, WarehouseId = 2, Quantity = 10 };
            
            _mockStocktakeRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(stocktake);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 2)).ReturnsAsync(existingStock);

            await _service.ApproveStocktakeAsync(1, 99);

            stocktake.Status.Should().Be(ReceiptStatus.Approved);
            stocktake.ApprovedBy.Should().Be(99);
            stocktake.Details.First().DifferenceQuantity.Should().Be(-5);
            existingStock.Quantity.Should().Be(5);
            
            _mockStockRepo.Verify(x => x.UpdateAsync(existingStock), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t => 
                t.TransactionType == TransactionType.AdjustmentDecrease && 
                t.Quantity == 5 && 
                t.CreatedBy == 99
            )), Times.Once);
            _mockAuditLogRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => a.Action == "Stocktake.Approved" && a.UserId == 99)), Times.Once);
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveStocktakeAsync_DiffZero_NoTransactionNoUpdate()
        {
            var stocktake = new Stocktake 
            { 
                Id = 1, 
                WarehouseId = 2,
                Status = ReceiptStatus.Draft,
                Details = new List<StocktakeDetail> { new StocktakeDetail { ProductId = 1, SystemQuantity = 10, ActualQuantity = 10 } }
            };
            
            _mockStocktakeRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(stocktake);

            await _service.ApproveStocktakeAsync(1, 99);

            stocktake.Status.Should().Be(ReceiptStatus.Approved);
            stocktake.Details.First().DifferenceQuantity.Should().Be(0);
            
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
            _mockAuditLogRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => a.Action == "Stocktake.Approved" && a.UserId == 99)), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveStocktakeAsync_ConcurrencyException_RollbacksAndThrowsBusinessRuleException()
        {
            var stocktake = new Stocktake 
            { 
                Id = 1, 
                WarehouseId = 2,
                Status = ReceiptStatus.Draft,
                Details = new List<StocktakeDetail> { new StocktakeDetail { ProductId = 1, SystemQuantity = 10, ActualQuantity = 15 } }
            };
            var existingStock = new InventoryStock { ProductId = 1, WarehouseId = 2, Quantity = 10 };
            
            _mockStocktakeRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(stocktake);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 2)).ReturnsAsync(existingStock);
            
            _mockUnitOfWork.Setup(x => x.CommitTransactionAsync()).ThrowsAsync(new ConcurrencyException("db error"));

            var act = async () => await _service.ApproveStocktakeAsync(1, 99);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Dữ liệu đã bị thay đổi bởi người khác, vui lòng thử lại.");
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task CreateStocktakeAsync_ValidWarehouse_AssignsNonBlankCodeStartingWithKKAndSnapshotsStock()
        {
            var dto = new DTOs.CreateStocktakeDto { WarehouseId = 1, Note = "Kiểm kê tháng 7" };
            var warehouse = new Warehouse { Id = 1, Name = "Kho Tổng" };
            var stocks = new List<InventoryStock>
            {
                new InventoryStock { ProductId = 10, WarehouseId = 1, Quantity = 100m },
                new InventoryStock { ProductId = 20, WarehouseId = 1, Quantity = 50m }
            };

            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(warehouse);
            _mockStockRepo.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<InventoryStock, bool>>>()))
                           .ReturnsAsync(stocks);

            Stocktake? capturedStocktake = null;
            _mockStocktakeRepo.Setup(x => x.AddAsync(It.IsAny<Stocktake>(), It.IsAny<CancellationToken>()))
                              .Callback<Stocktake, CancellationToken>((s, ct) => capturedStocktake = s)
                              .ReturnsAsync((Stocktake s, CancellationToken ct) => s);

            var id = await _service.CreateStocktakeAsync(dto, 99);

            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
            _mockStocktakeRepo.Verify(x => x.AddAsync(It.IsAny<Stocktake>(), It.IsAny<CancellationToken>()), Times.Once);

            capturedStocktake.Should().NotBeNull();
            capturedStocktake!.Code.Should().NotBeNullOrWhiteSpace();
            capturedStocktake.Code.Should().StartWith("KK-");
            capturedStocktake.Code.Should().MatchRegex(@"^KK-\d{14}-[0-9A-F]{16}$");
            capturedStocktake.Code.Length.Should().Be(34);
            capturedStocktake.Code.Length.Should().BeLessThanOrEqualTo(50);
            capturedStocktake.WarehouseId.Should().Be(1);
            capturedStocktake.Status.Should().Be(ReceiptStatus.Draft);
            capturedStocktake.CreatedBy.Should().Be(99);
            capturedStocktake.Details.Should().HaveCount(2);

            var detailsList = capturedStocktake.Details.ToList();
            detailsList[0].SystemQuantity.Should().Be(100m);
            detailsList[0].ActualQuantity.Should().BeNull();
            detailsList[0].DifferenceQuantity.Should().Be(0);
        }

        [Fact]
        public async Task CreateStocktakeAsync_MultipleCalls_GeneratesDistinctCodes()
        {
            var dto = new DTOs.CreateStocktakeDto { WarehouseId = 1 };
            var warehouse = new Warehouse { Id = 1, Name = "Kho Tổng" };

            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(warehouse);
            _mockStockRepo.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<InventoryStock, bool>>>(), It.IsAny<CancellationToken>()))
                           .ReturnsAsync(new List<InventoryStock>());

            var capturedCodes = new List<string>();
            _mockStocktakeRepo.Setup(x => x.AddAsync(It.IsAny<Stocktake>(), It.IsAny<CancellationToken>()))
                              .Callback<Stocktake, CancellationToken>((s, ct) => capturedCodes.Add(s.Code))
                              .ReturnsAsync((Stocktake s, CancellationToken ct) => s);

            await _service.CreateStocktakeAsync(dto, 99);
            await _service.CreateStocktakeAsync(dto, 99);

            capturedCodes.Should().HaveCount(2);
            capturedCodes[0].Should().NotBeNullOrWhiteSpace().And.StartWith("KK-");
            capturedCodes[1].Should().NotBeNullOrWhiteSpace().And.StartWith("KK-");
            capturedCodes[0].Should().NotBe(capturedCodes[1]);
        }
    }
}
