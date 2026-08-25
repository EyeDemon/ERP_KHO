using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using FluentAssertions;
using Moq;

namespace ERP.Application.Tests
{
    public class ExportReceiptServiceTests
    {
        private readonly Mock<IExportReceiptRepository> _mockExportRepo;
        private readonly Mock<IInventoryStockRepository> _mockStockRepo;
        private readonly Mock<IInventoryTransactionRepository> _mockTransactionRepo;
        private readonly Mock<IUnitOfWork> _mockUnitOfWork;
        private readonly Mock<IAuditLogRepository> _mockAuditRepo;
        private readonly ExportReceiptService _service;

        public ExportReceiptServiceTests()
        {
            _mockExportRepo = new Mock<IExportReceiptRepository>();
            _mockStockRepo = new Mock<IInventoryStockRepository>();
            _mockTransactionRepo = new Mock<IInventoryTransactionRepository>();
            _mockUnitOfWork = new Mock<IUnitOfWork>();
            _mockAuditRepo = new Mock<IAuditLogRepository>();
            _mockStockRepo
                .Setup(x => x.TryDecreaseStockAsync(
                    It.IsAny<int>(),
                    It.IsAny<int>(),
                    It.IsAny<decimal>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            _service = new ExportReceiptService(
                _mockExportRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditRepo.Object);
        }

        [Fact]
        public async Task CreateAsync_ValidDto_ReturnsDto()
        {
            // Arrange
            var dto = new CreateExportReceiptDto
            {
                Code = "EX001",
                WarehouseId = 1,
                Note = "Test Note",
                Details = new List<CreateExportReceiptDetailDto>
                {
                    new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10, UnitPrice = 100, Note = "Detail Note" }
                }
            };
            
            _mockExportRepo.Setup(x => x.ExistsByCodeAsync("EX001", null)).ReturnsAsync(false);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1))
                .ReturnsAsync(new InventoryStock { Quantity = 20 });
            
            var savedReceipt = new ExportReceipt { Id = 1, Code = "EX001", Status = ReceiptStatus.Draft };
            _mockExportRepo.Setup(x => x.AddAsync(It.IsAny<ExportReceipt>(), It.IsAny<CancellationToken>())).Callback<ExportReceipt, CancellationToken>((r, ct) => r.Id = 1).ReturnsAsync((ExportReceipt r, CancellationToken ct) => r);
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(savedReceipt);

            // Act
            var result = await _service.CreateAsync(dto, 1);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(1);
            result.Code.Should().Be("EX001");
            
            _mockExportRepo.Verify(x => x.AddAsync(It.Is<ExportReceipt>(r => 
                r.Code == "EX001" && 
                r.WarehouseId == 1 && 
                r.Status == ReceiptStatus.Draft && 
                r.CreatedBy == 1 && 
                r.CreatedAt != default(DateTime) &&
                r.Note == "Test Note" &&
                r.Details.Count == 1 && 
                r.Details.First().ProductId == 1 && 
                r.Details.First().Quantity == 10 && 
                r.Details.First().UnitPrice == 100 &&
                r.Details.First().Note == "Detail Note"
            )), Times.Once);
            
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
            
            var expectedTime = DateTime.UtcNow;
            _mockAuditRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => 
                a.UserId == 1 &&
                a.Action == "ExportReceipt.Created" &&
                a.EntityName == "ExportReceipt" &&
                a.EntityId == 1 &&
                a.Timestamp >= expectedTime.AddSeconds(-2) && a.Timestamp <= expectedTime.AddSeconds(2)
            )), Times.Once);
            
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_DuplicateCode_ThrowsBusinessRuleException()
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            _mockExportRepo.Setup(x => x.ExistsByCodeAsync("EX001", null)).ReturnsAsync(true);
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Mã phiếu xuất 'EX001' đã tồn tại");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task CreateAsync_InvalidWarehouseId_ThrowsBusinessRuleException(int warehouseId)
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = warehouseId, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Kho xuất không hợp lệ");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public async Task CreateAsync_EmptyCode_ThrowsBusinessRuleException(string code)
        {
            var dto = new CreateExportReceiptDto { Code = code, WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Mã phiếu xuất không được để trống");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_NullCode_ThrowsBusinessRuleException()
        {
            var dto = new CreateExportReceiptDto { Code = null!, WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Mã phiếu xuất không được để trống");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_EmptyDetails_ThrowsBusinessRuleException()
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto>() };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Phiếu xuất phải có ít nhất 1 sản phẩm");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_NullDetails_ThrowsBusinessRuleException()
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = null! };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Phiếu xuất phải có ít nhất 1 sản phẩm");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task CreateAsync_InvalidProductId_ThrowsBusinessRuleException(int productId)
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = productId, Quantity = 10 } } };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Sản phẩm không hợp lệ");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task CreateAsync_InvalidQuantity_ThrowsBusinessRuleException(decimal quantity)
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = quantity } } };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Số lượng xuất của sản phẩm ID 1 phải lớn hơn 0");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        public async Task CreateAsync_InvalidUnitPrice_ThrowsBusinessRuleException(decimal unitPrice)
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10, UnitPrice = unitPrice } } };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Đơn giá của sản phẩm ID 1 không được âm");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_QuantityGreaterThanStock_ThrowsBusinessRuleException()
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1)).ReturnsAsync(new InventoryStock { Quantity = 5 });
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Sản phẩm có ID 1 không đủ tồn kho*");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_StockNotFound_ThrowsBusinessRuleException()
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1)).ReturnsAsync((InventoryStock?)null);
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Không tìm thấy thông tin tồn kho cho sản phẩm ID 1 tại kho ID 1");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_ReceiptNotFound_ThrowsNotFoundExceptionAndRollbacks()
        {
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync((ExportReceipt?)null);
            
            Func<Task> act = async () => await _service.ApproveAsync(1, 99);
            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Không tìm thấy phiếu xuất id 1");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_NoDetails_ThrowsBusinessRuleExceptionAndRollbacks()
        {
            var receipt = new ExportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail>()
            };
            
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            Func<Task> act = async () => await _service.ApproveAsync(1, 99);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Phiếu xuất phải có ít nhất 1 sản phẩm để duyệt");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_ValidReceipt_UpdatesStatusAndStockAndCreatesTransaction()

        {
            var receipt = new ExportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail> { new ExportReceiptDetail { ProductId = 1, Quantity = 10 } }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            await _service.ApproveAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Dispatched);
            receipt.ApprovedBy.Should().Be(99);
            _mockStockRepo.Verify(x => x.TryDecreaseStockAsync(1, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t => t.TransactionType == TransactionType.Export && t.Quantity == 10 && t.CreatedBy == 99)), Times.Once);
            
            var expectedTime = DateTime.UtcNow;
            _mockAuditRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => 
                a.UserId == 99 &&
                a.Action == "ExportReceipt.ApprovedAndDispatched" &&
                a.EntityName == "ExportReceipt" &&
                a.EntityId == 1 &&
                a.Timestamp >= expectedTime.AddSeconds(-2) && a.Timestamp <= expectedTime.AddSeconds(2)
            )), Times.Once);
            
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveAsync_MultipleDetails_ValidReceipt_UpdatesStatusAndStockAndCreatesMultipleTransactions()
        {
            var receipt = new ExportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail> 
                { 
                    new ExportReceiptDetail { ProductId = 1, Quantity = 10 },
                    new ExportReceiptDetail { ProductId = 2, Quantity = 5 }
                }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            await _service.ApproveAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Dispatched);
            receipt.ApprovedBy.Should().Be(99);
            
            _mockStockRepo.Verify(x => x.TryDecreaseStockAsync(1, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
            _mockStockRepo.Verify(x => x.TryDecreaseStockAsync(2, 1, 5, It.IsAny<CancellationToken>()), Times.Once);
            
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t => t.TransactionType == TransactionType.Export && t.ProductId == 1 && t.Quantity == 10 && t.CreatedBy == 99)), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t => t.TransactionType == TransactionType.Export && t.ProductId == 2 && t.Quantity == 5 && t.CreatedBy == 99)), Times.Once);
            
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveAsync_NotDraft_ThrowsConcurrencyExceptionAndRollbacks()
        {
            var receipt = new ExportReceipt { Id = 1, Status = ReceiptStatus.Approved };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            Func<Task> act = async () => await _service.ApproveAsync(1, 99);
            await act.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>()
                .WithMessage("Phiếu xuất đã được xử lý hoặc đang được xử lý bởi yêu cầu khác.");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_QuantityGreaterThanStock_ThrowsBusinessRuleExceptionAndRollbacks()
        {
            var receipt = new ExportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail> { new ExportReceiptDetail { ProductId = 1, Quantity = 10 } }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockStockRepo.Setup(x => x.TryDecreaseStockAsync(1, 1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            Func<Task> act = async () => await _service.ApproveAsync(1, 99);
            await act.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>().WithMessage("Không đủ tồn kho*");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_StockNotFound_ThrowsBusinessRuleExceptionAndRollbacks()
        {
            var receipt = new ExportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail> { new ExportReceiptDetail { ProductId = 1, Quantity = 10 } }
            };

            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockStockRepo.Setup(x => x.TryDecreaseStockAsync(1, 1, 10, It.IsAny<CancellationToken>())).ReturnsAsync(false);

            Func<Task> act = async () => await _service.ApproveAsync(1, 99);
            await act.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>().WithMessage("Không đủ tồn kho*");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_ConcurrencyException_RollbacksAndThrows()
        {
            var receipt = new ExportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail> { new ExportReceiptDetail { ProductId = 1, Quantity = 10 } }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockStockRepo.Setup(x => x.TryDecreaseStockAsync(1, 1, 10, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ERP.Domain.Exceptions.ConcurrencyException("EF Core concurrency exception"));

            Func<Task> act = async () => await _service.ApproveAsync(1, 99);
            await act.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>().WithMessage("EF Core concurrency exception");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_Deadlock_RetriesWholeTransactionOnce()
        {
            ExportReceipt NewReceipt() => new()
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail>
                {
                    new() { ProductId = 1, Quantity = 10 }
                }
            };

            _mockExportRepo.SetupSequence(x => x.GetByIdWithDetailsAsync(1))
                .ReturnsAsync(NewReceipt())
                .ReturnsAsync(NewReceipt());
            _mockStockRepo
                .SetupSequence(x => x.TryDecreaseStockAsync(1, 1, 10, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ERP.Domain.Exceptions.DeadlockException("deadlock", new Exception()))
                .ReturnsAsync(true);

            await _service.ApproveAsync(1, 99);

            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Exactly(2));
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
            _mockTransactionRepo.Verify(
                x => x.AddAsync(It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task ApproveAsync_ParallelExecution_PreventsRaceCondition()
        {
            decimal sharedQuantity = 20;
            var stockLock = new object();
            var successCount = 0;
            var conflictCount = 0;

            var mockExportRepo = new Mock<IExportReceiptRepository>();
            var mockStockRepo = new Mock<IInventoryStockRepository>();
            var mockTxRepo = new Mock<IInventoryTransactionRepository>();
            var mockUnitOfWork = new Mock<IUnitOfWork>();
            var mockAuditRepo = new Mock<IAuditLogRepository>();

            mockStockRepo
                .Setup(x => x.TryDecreaseStockAsync(1, 1, 10, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    lock (stockLock)
                    {
                        if (sharedQuantity < 10)
                        {
                            return false;
                        }

                        sharedQuantity -= 10;
                        return true;
                    }
                });

            var service = new ExportReceiptService(mockExportRepo.Object, mockStockRepo.Object, mockTxRepo.Object, mockUnitOfWork.Object, mockAuditRepo.Object);

            var tasks = Enumerable.Range(1, 5).Select(async i =>
            {
                var receipt = new ExportReceipt
                {
                    Id = i,
                    WarehouseId = 1,
                    Status = ReceiptStatus.Draft,
                    Details = new List<ExportReceiptDetail> { new ExportReceiptDetail { ProductId = 1, Quantity = 10 } }
                };

                mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(i)).ReturnsAsync(receipt);

                try
                {
                    await service.ApproveAsync(i, 99);
                    Interlocked.Increment(ref successCount);
                }
                catch (BusinessRuleException ex) when (ex.Message.Contains("thao tác bởi một người khác"))
                {
                    Interlocked.Increment(ref conflictCount);
                }
                catch (ERP.Domain.Exceptions.ConcurrencyException)
                {
                    Interlocked.Increment(ref conflictCount);
                }
            });

            await Task.WhenAll(tasks);

            successCount.Should().Be(2);
            sharedQuantity.Should().Be(0);
            mockTxRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Exactly(successCount));
            mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Exactly(successCount));
            (successCount + conflictCount).Should().Be(5);
        }

        [Fact]
        public async Task CancelAsync_ValidReceipt_UpdatesStatusAndWritesAuditLog()
        {
            var receipt = new ExportReceipt { Id = 1, Status = ReceiptStatus.Draft };
            _mockExportRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(receipt);

            await _service.CancelAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Cancelled);
            _mockExportRepo.Verify(x => x.UpdateAsync(receipt), Times.Once);
            var expectedTime = DateTime.UtcNow;
            _mockAuditRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => 
                a.UserId == 99 &&
                a.Action == "ExportReceipt.Cancelled" &&
                a.EntityName == "ExportReceipt" &&
                a.EntityId == 1 &&
                a.Timestamp >= expectedTime.AddSeconds(-2) && a.Timestamp <= expectedTime.AddSeconds(2)
            )), Times.Once);
            
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task CancelAsync_ReceiptNotFound_ThrowsNotFoundException()
        {
            _mockExportRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync((ExportReceipt?)null);
            Func<Task> act = async () => await _service.CancelAsync(1, 99);
            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Không tìm thấy phiếu xuất id 1");
        }

        [Fact]
        public async Task CancelAsync_WhenStatusIsApproved_CancelsReceipt()
        {
            var receipt = new ExportReceipt { Id = 1, Status = ReceiptStatus.Approved };
            _mockExportRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(receipt);
            await _service.CancelAsync(1, 99);
            receipt.Status.Should().Be(ReceiptStatus.Cancelled);
        }

        [Fact]
        public async Task CancelAsync_WhenStatusIsCancelled_ThrowsBusinessRuleException()
        {
            var receipt = new ExportReceipt { Id = 1, Status = ReceiptStatus.Cancelled };
            _mockExportRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(receipt);
            Func<Task> act = async () => await _service.CancelAsync(1, 99);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Không thể hủy phiếu xuất đã xuất kho hoặc đã bị hủy.");
        }
    }
}


