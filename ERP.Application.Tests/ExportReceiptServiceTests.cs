using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Services;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Interfaces;
using ERP.Application.Options;
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
        private readonly Mock<IWarehouseAuthorizationService> _mockWarehouseAuthorization;
        private readonly Mock<ICurrentUser> _mockCurrentUser;
        private readonly Mock<IStockReservationService> _mockReservationService;
        private readonly ExportReceiptService _service;
        private readonly ExportReceiptService _workflowService;

        public ExportReceiptServiceTests()
        {
            _mockExportRepo = new Mock<IExportReceiptRepository>();
            _mockStockRepo = new Mock<IInventoryStockRepository>();
            _mockTransactionRepo = new Mock<IInventoryTransactionRepository>();
            _mockUnitOfWork = new Mock<IUnitOfWork>();
            _mockAuditRepo = new Mock<IAuditLogRepository>();
            _mockWarehouseAuthorization = new Mock<IWarehouseAuthorizationService>();
            _mockCurrentUser = new Mock<ICurrentUser>();
            _mockReservationService = new Mock<IStockReservationService>();
            _mockCurrentUser.SetupGet(x => x.UserId).Returns(99);
            _mockCurrentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            _mockCurrentUser.SetupGet(x => x.IsGlobalAdmin).Returns(false);
            _mockCurrentUser.SetupGet(x => x.Role).Returns("Manager");
            _mockWarehouseAuthorization.Setup(x => x.EnsureWarehouseAccessAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockReservationService.Setup(x => x.ReserveForExportAsync(
                    It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new StockReservation { Id = 1, SourceType = "ExportReceipt" });
            _mockReservationService.Setup(x => x.ConsumeAsync(
                    It.IsAny<StockReservation>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InventoryStockConsumption[]
                {
                    new(101, 10m)
                });
            _mockStockRepo
                .Setup(x => x.GetAvailableQuantityAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(20m);
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
            _workflowService = new ExportReceiptService(
                _mockExportRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditRepo.Object,
                _mockWarehouseAuthorization.Object,
                _mockCurrentUser.Object,
                _mockReservationService.Object,
                new ExportReceiptOptions { RequireDifferentDispatcher = true });
        }

        [Theory]
        [InlineData("approve-reserve")]
        [InlineData("approve-dispatch")]
        [InlineData("dispatch")]
        public async Task ExportMutation_WhenWriteDisabled_ReturnsBeforeTransaction(string action)
        {
            var service = new ExportReceiptService(
                _mockExportRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditRepo.Object,
                new ExportReceiptOptions { WriteEnabled = false });

            Func<Task> act = action switch
            {
                "approve-reserve" => () => service.ApproveAndReserveAsync(1, 1),
                "approve-dispatch" => () => service.ApproveAndDispatchAsync(1, 1),
                _ => () => service.DispatchAsync(1, 1)
            };

            await act.Should().ThrowAsync<ServiceUnavailableException>()
                .WithMessage("*tạm dừng để bảo trì*");
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Never);
            _mockExportRepo.Verify(x => x.UpdateAsync(It.IsAny<ExportReceipt>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task CancelApproved_WhenWriteDisabled_DoesNotStartTransactionOrMutate()
        {
            _mockExportRepo.Setup(x => x.GetByIdAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ExportReceipt { Id = 1, Status = ReceiptStatus.Approved });
            var service = new ExportReceiptService(
                _mockExportRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditRepo.Object,
                new ExportReceiptOptions { WriteEnabled = false });

            Func<Task> act = () => service.CancelAsync(1, 1);

            await act.Should().ThrowAsync<ServiceUnavailableException>();
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Never);
            _mockExportRepo.Verify(x => x.UpdateAsync(It.IsAny<ExportReceipt>(), It.IsAny<CancellationToken>()), Times.Never);
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
                .ReturnsAsync(new InventoryStock
                {
                    Quantity = 20,
                    Product = new Product
                    {
                        Id = 1,
                        IsActive = true,
                        Unit = new Unit { Id = 7, Code = "EA", Name = "Cái", DecimalPlaces = 0, IsActive = true }
                    }
                });
            
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
                r.Details.First().Note == "Detail Note" &&
                r.Details.First().BaseUomIdSnapshot == 7 &&
                r.Details.First().BaseUomCodeSnapshot == "EA" &&
                r.Details.First().BaseUomNameSnapshot == "Cái" &&
                r.Details.First().BaseUomDecimalPlacesSnapshot == 0
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
        public async Task CreateAsync_AggregatedEligibleAvailability_AllowsQuantityAboveSingleLocationBalance()
        {
            var dto = new CreateExportReceiptDto
            {
                Code = "EX-MULTI-LOC",
                WarehouseId = 1,
                Details = new List<CreateExportReceiptDetailDto> { new() { ProductId = 1, Quantity = 40, UnitPrice = 10 } }
            };
            _mockExportRepo.Setup(x => x.ExistsByCodeAsync("EX-MULTI-LOC", null)).ReturnsAsync(false);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1)).ReturnsAsync(new InventoryStock
            {
                Quantity = 20,
                Product = new Product
                {
                    Id = 1,
                    IsActive = true,
                    Unit = new Unit { Id = 7, Code = "EA", Name = "Cái", DecimalPlaces = 0, IsActive = true }
                }
            });
            _mockStockRepo.Setup(x => x.GetAvailableQuantityAsync(1, 1, It.IsAny<CancellationToken>())).ReturnsAsync(50m);
            _mockExportRepo.Setup(x => x.AddAsync(It.IsAny<ExportReceipt>(), It.IsAny<CancellationToken>()))
                .Callback<ExportReceipt, CancellationToken>((entity, _) => entity.Id = 42)
                .ReturnsAsync((ExportReceipt entity, CancellationToken _) => entity);
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(42)).ReturnsAsync(new ExportReceipt
            {
                Id = 42,
                Code = "EX-MULTI-LOC",
                WarehouseId = 1,
                Details = new List<ExportReceiptDetail>()
            });

            var result = await _service.CreateAsync(dto, 1);

            result.Id.Should().Be(42);
            _mockStockRepo.Verify(x => x.GetAvailableQuantityAsync(1, 1, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_QuantityGreaterThanStock_ThrowsBusinessRuleException()
        {
            var dto = new CreateExportReceiptDto { Code = "EX001", WarehouseId = 1, Details = new List<CreateExportReceiptDetailDto> { new CreateExportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1)).ReturnsAsync(new InventoryStock
                {
                    Quantity = 5,
                    Product = new Product
                    {
                        Id = 1,
                        IsActive = true,
                        Unit = new Unit { Id = 7, Code = "EA", Name = "Cái", DecimalPlaces = 0, IsActive = true }
                    }
                });
            _mockStockRepo.Setup(x => x.GetAvailableQuantityAsync(1, 1, It.IsAny<CancellationToken>())).ReturnsAsync(5m);
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
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Không tìm thấy thông tin tồn kho khả dụng cho sản phẩm ID 1 tại kho ID 1");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_QuantityExceedsBaseUomPrecision_ThrowsBusinessRuleException()
        {
            var dto = new CreateExportReceiptDto
            {
                Code = "EX-PRECISION",
                WarehouseId = 1,
                Details = new List<CreateExportReceiptDetailDto> { new() { ProductId = 1, Quantity = 1.25m, UnitPrice = 1 } }
            };
            _mockExportRepo.Setup(x => x.ExistsByCodeAsync("EX-PRECISION", null)).ReturnsAsync(false);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1)).ReturnsAsync(new InventoryStock
            {
                Quantity = 10,
                Product = new Product
                {
                    Id = 1,
                    IsActive = true,
                    Unit = new Unit { Id = 7, Code = "EA", Name = "Cái", DecimalPlaces = 0, IsActive = true }
                }
            });

            Func<Task> act = () => _service.CreateAsync(dto, 1);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*độ chính xác Base UOM*");
            _mockExportRepo.Verify(x => x.AddAsync(It.IsAny<ExportReceipt>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_ReceiptNotFound_ThrowsNotFoundExceptionAndRollbacks()
        {
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync((ExportReceipt?)null);
            
            Func<Task> act = async () => await _workflowService.ApproveAsync(1, 99);
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

            Func<Task> act = async () => await _workflowService.ApproveAsync(1, 99);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Phiếu xuất phải có ít nhất 1 sản phẩm để duyệt");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_ValidReceipt_ReservesWithoutPhysicalStockMovement()
        {
            var receipt = new ExportReceipt
            {
                Id = 1,
                Code = "EX-1",
                CreatedBy = 10,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail> { new ExportReceiptDetail { ProductId = 1, Quantity = 10 } }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            await _workflowService.ApproveAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Approved);
            receipt.DispatchMode.Should().Be(ExportDispatchMode.RequireSeparateDispatch);
            receipt.ApprovedBy.Should().Be(99);
            _mockReservationService.Verify(x => x.ReserveForExportAsync(1, "EX-1", 1, 1, 10, 99, It.IsAny<CancellationToken>()), Times.Once);
            _mockStockRepo.Verify(x => x.TryDecreaseStockAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()), Times.Never);

            var expectedTime = DateTime.UtcNow;
            _mockAuditRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a =>
                a.UserId == 99 &&
                a.Action == "ExportReceipt.ApprovedAndReserved" &&
                a.EntityName == "ExportReceipt" &&
                a.EntityId == 1 &&
                a.Timestamp >= expectedTime.AddSeconds(-2) && a.Timestamp <= expectedTime.AddSeconds(2)
            )), Times.Once);

            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveAndDispatchAsync_ExplicitCompatibility_ReservesConsumesAndDispatches()
        {
            var reservation = new StockReservation { Id = 11, SourceType = "ExportReceipt" };
            _mockReservationService.Setup(x => x.ReserveForExportAsync(1, "EX-1", 1, 1, 10, 99, It.IsAny<CancellationToken>()))
                .ReturnsAsync(reservation);
            var receipt = new ExportReceipt
            {
                Id = 1,
                Code = "EX-1",
                CreatedBy = 10,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail> { new() { ProductId = 1, Quantity = 10 } }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            await _workflowService.ApproveAndDispatchAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Dispatched);
            receipt.DispatchMode.Should().Be(ExportDispatchMode.DispatchOnApproval);
            receipt.ApprovedBy.Should().Be(99);
            receipt.DispatchedBy.Should().Be(99);
            _mockReservationService.Verify(x => x.ConsumeAsync(reservation, 99, It.IsAny<CancellationToken>()), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t =>
                t.TransactionType == TransactionType.Export && t.ReferenceId == 1 && t.ProductId == 1 && t.LocationId == 101 && t.Quantity == 10),
                It.IsAny<CancellationToken>()), Times.Once);
            _mockAuditRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => a.Action == "ExportReceipt.ApprovedAndDispatched")), Times.Once);
        }

        [Fact]
        public async Task ApproveAsync_MultipleDetails_ReservesEveryLineWithoutLedger()
        {
            var receipt = new ExportReceipt
            {
                Id = 1,
                Code = "EX-1",
                CreatedBy = 10,
                WarehouseId = 1,
                Status = ReceiptStatus.Draft,
                Details = new List<ExportReceiptDetail>
                {
                    new ExportReceiptDetail { ProductId = 1, Quantity = 10 },
                    new ExportReceiptDetail { ProductId = 2, Quantity = 5 }
                }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            await _workflowService.ApproveAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Approved);
            _mockReservationService.Verify(x => x.ReserveForExportAsync(1, "EX-1", 1, 1, 10, 99, It.IsAny<CancellationToken>()), Times.Once);
            _mockReservationService.Verify(x => x.ReserveForExportAsync(1, "EX-1", 1, 2, 5, 99, It.IsAny<CancellationToken>()), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task ApproveAsync_NotDraft_ThrowsConcurrencyExceptionAndRollbacks()
        {
            var receipt = new ExportReceipt { Id = 1, Status = ReceiptStatus.Approved };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            Func<Task> act = async () => await _workflowService.ApproveAsync(1, 99);
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
            _mockReservationService.Setup(x => x.ReserveForExportAsync(1, It.IsAny<string>(), 1, 1, 10, 99, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ERP.Domain.Exceptions.ConcurrencyException("Không đủ tồn kho khả dụng để giữ hàng."));

            Func<Task> act = async () => await _workflowService.ApproveAsync(1, 99);
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
            _mockReservationService.Setup(x => x.ReserveForExportAsync(1, It.IsAny<string>(), 1, 1, 10, 99, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ERP.Domain.Exceptions.ConcurrencyException("Không đủ tồn kho khả dụng để giữ hàng."));

            Func<Task> act = async () => await _workflowService.ApproveAsync(1, 99);
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
            _mockReservationService.Setup(x => x.ReserveForExportAsync(1, It.IsAny<string>(), 1, 1, 10, 99, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ERP.Domain.Exceptions.ConcurrencyException("EF Core concurrency exception"));

            Func<Task> act = async () => await _workflowService.ApproveAsync(1, 99);
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
            _mockReservationService
                .SetupSequence(x => x.ReserveForExportAsync(1, It.IsAny<string>(), 1, 1, 10, 99, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ERP.Domain.Exceptions.DeadlockException("deadlock", new Exception()))
                .ReturnsAsync(new StockReservation { Id = 1, SourceType = "ExportReceipt" });

            await _workflowService.ApproveAsync(1, 99);

            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Exactly(2));
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
            _mockTransactionRepo.Verify(
                x => x.AddAsync(It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task ApproveAsync_ParallelExecution_ReservationPreventsOversell()
        {
            decimal sharedAvailable = 20;
            var stockLock = new object();
            var successCount = 0;
            var conflictCount = 0;

            _mockReservationService
                .Setup(x => x.ReserveForExportAsync(It.IsAny<int>(), It.IsAny<string>(), 1, 1, 10, 99, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    lock (stockLock)
                    {
                        if (sharedAvailable < 10)
                            throw new ERP.Domain.Exceptions.ConcurrencyException("Không đủ tồn kho khả dụng để giữ hàng.");
                        sharedAvailable -= 10;
                        return new StockReservation { Id = 1, SourceType = "ExportReceipt" };
                    }
                });

            var tasks = Enumerable.Range(1, 5).Select(async i =>
            {
                var receipt = new ExportReceipt
                {
                    Id = i,
                    Code = $"EX-{i}",
                    CreatedBy = i,
                    WarehouseId = 1,
                    Status = ReceiptStatus.Draft,
                    Details = new List<ExportReceiptDetail> { new ExportReceiptDetail { ProductId = 1, Quantity = 10 } }
                };
                _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(i)).ReturnsAsync(receipt);
                try
                {
                    await _workflowService.ApproveAsync(i, 99);
                    Interlocked.Increment(ref successCount);
                }
                catch (ERP.Domain.Exceptions.ConcurrencyException)
                {
                    Interlocked.Increment(ref conflictCount);
                }
            });

            await Task.WhenAll(tasks);

            successCount.Should().Be(2);
            sharedAvailable.Should().Be(0);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
            (successCount + conflictCount).Should().Be(5);
        }

        [Fact]
        public async Task CancelAsync_ValidReceipt_UpdatesStatusAndWritesAuditLog()
        {
            var receipt = new ExportReceipt { Id = 1, Status = ReceiptStatus.Draft, Customer = new BusinessPartner { Code = "CUS-1", Name = "Khách hàng" } };
            _mockExportRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(receipt);

            await _service.CancelAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Cancelled);
            receipt.CustomerCodeSnapshot.Should().Be("CUS-1");
            receipt.CustomerNameSnapshot.Should().Be("Khách hàng");
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
        public async Task DispatchAsync_ApprovedReceipt_ConsumesReservationAndWritesLedger()
        {
            var reservation = new StockReservation { Id = 77, SourceType = "ExportReceipt" };
            var receipt = new ExportReceipt
            {
                Id = 1,
                Code = "EX-1",
                WarehouseId = 1,
                Status = ReceiptStatus.Approved,
                DispatchMode = ExportDispatchMode.RequireSeparateDispatch,
                ApprovedBy = 88,
                Details = new List<ExportReceiptDetail> { new() { ProductId = 1, Quantity = 10 } }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockReservationService.Setup(x => x.GetExportReservationAsync(1, 1, 1, It.IsAny<CancellationToken>())).ReturnsAsync(reservation);

            await _workflowService.DispatchAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Dispatched);
            receipt.DispatchedBy.Should().Be(99);
            _mockReservationService.Verify(x => x.ConsumeAsync(reservation, 99, It.IsAny<CancellationToken>()), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t =>
                t.TransactionType == TransactionType.Export && t.ReferenceId == 1 && t.ProductId == 1 && t.LocationId == 101 && t.Quantity == 10 && t.CreatedBy == 99),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DispatchAsync_WhenCanonicalShipmentExistsButNotLoaded_RollsBackBeforeInventoryMutation()
        {
            var readiness = new Mock<IShipmentDispatchReadiness>();
            readiness.Setup(x => x.EnsureSourceReadyAsync("ExportReceipt", 1, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ERP.Domain.Exceptions.ConcurrencyException("Chứng từ đã đi vào canonical Shipment workflow. Hãy xác nhận xuất kho tại Shipment thay vì ExportReceipt."));
            var service = new ExportReceiptService(
                _mockExportRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditRepo.Object,
                _mockWarehouseAuthorization.Object,
                _mockCurrentUser.Object,
                _mockReservationService.Object,
                new ExportReceiptOptions { RequireDifferentDispatcher = true },
                shipmentDispatchReadiness: readiness.Object);

            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(new ExportReceipt
            {
                Id = 1,
                Code = "EX-1",
                WarehouseId = 1,
                Status = ReceiptStatus.Approved,
                DispatchMode = ExportDispatchMode.RequireSeparateDispatch,
                ApprovedBy = 88,
                Details = new List<ExportReceiptDetail> { new() { ProductId = 1, Quantity = 10 } }
            });

            Func<Task> act = () => service.DispatchAsync(1, 99);

            await act.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>()
                .WithMessage("*canonical Shipment workflow*");
            readiness.Verify(x => x.EnsureSourceReadyAsync("ExportReceipt", 1, It.IsAny<CancellationToken>()), Times.Once);
            _mockReservationService.Verify(x => x.GetExportReservationAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockReservationService.Verify(x => x.ConsumeAsync(
                It.IsAny<StockReservation>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(
                It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task DispatchAsync_ConsumptionBreakdownDoesNotMatchLine_RollsBackWithoutLedger()
        {
            var reservation = new StockReservation { Id = 77, SourceType = "ExportReceipt" };
            var receipt = new ExportReceipt
            {
                Id = 1,
                Code = "EX-1",
                WarehouseId = 1,
                Status = ReceiptStatus.Approved,
                DispatchMode = ExportDispatchMode.RequireSeparateDispatch,
                ApprovedBy = 88,
                Details = new List<ExportReceiptDetail> { new() { ProductId = 1, Quantity = 10 } }
            };
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockReservationService.Setup(x => x.GetExportReservationAsync(1, 1, 1, It.IsAny<CancellationToken>())).ReturnsAsync(reservation);
            _mockReservationService.Setup(x => x.ConsumeAsync(reservation, 99, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InventoryStockConsumption[] { new(101, 9m) });

            Func<Task> act = () => _workflowService.DispatchAsync(1, 99);

            await act.Should().ThrowAsync<ERP.Domain.Exceptions.ConcurrencyException>().WithMessage("*đối soát vị trí tồn kho*");
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task DispatchAsync_SameCheckerAndDispatcher_IsRejectedEvenForGlobalAdmin()
        {
            var admin = new Mock<ICurrentUser>();
            admin.SetupGet(x => x.UserId).Returns(99);
            admin.SetupGet(x => x.IsAuthenticated).Returns(true);
            admin.SetupGet(x => x.IsGlobalAdmin).Returns(true);
            admin.SetupGet(x => x.Role).Returns("Admin");
            var service = new ExportReceiptService(
                _mockExportRepo.Object, _mockStockRepo.Object, _mockTransactionRepo.Object, _mockUnitOfWork.Object,
                _mockAuditRepo.Object, _mockWarehouseAuthorization.Object, admin.Object, _mockReservationService.Object,
                new ExportReceiptOptions { RequireDifferentDispatcher = true });
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(new ExportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Approved,
                DispatchMode = ExportDispatchMode.RequireSeparateDispatch,
                ApprovedBy = 99,
                Details = new List<ExportReceiptDetail> { new() { ProductId = 1, Quantity = 10 } }
            });

            Func<Task> act = () => service.DispatchAsync(1, 99);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*khác người xác nhận xuất*");
            _mockReservationService.Verify(x => x.ConsumeAsync(It.IsAny<StockReservation>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetByIdAsync_Viewer_RedactsSensitiveValueAndPrivateNotes()
        {
            var viewer = new Mock<ICurrentUser>();
            viewer.SetupGet(x => x.UserId).Returns(55);
            viewer.SetupGet(x => x.IsAuthenticated).Returns(true);
            viewer.SetupGet(x => x.Role).Returns("Viewer");
            var service = new ExportReceiptService(
                _mockExportRepo.Object, _mockStockRepo.Object, _mockTransactionRepo.Object, _mockUnitOfWork.Object,
                _mockAuditRepo.Object, _mockWarehouseAuthorization.Object, viewer.Object, _mockReservationService.Object);
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(new ExportReceipt
            {
                Id = 1,
                Code = "EX-1",
                WarehouseId = 1,
                Note = "private receipt note",
                Details = new List<ExportReceiptDetail>
                {
                    new() { Id = 1, ProductId = 1, Quantity = 2, UnitPrice = 123, Note = "private line note" }
                }
            });

            var result = await service.GetByIdAsync(1);

            result.Note.Should().BeNull();
            result.Details.Single().UnitPrice.Should().BeNull();
            result.Details.Single().Note.Should().BeNull();
        }

        [Fact]
        public async Task GetByIdAsync_ExposesWarehouseStaffDirectDispatchOption()
        {
            _mockExportRepo.Setup(x => x.GetByIdWithDetailsAsync(1))
                .ReturnsAsync(new ExportReceipt { Id = 1, Details = new List<ExportReceiptDetail>() });
            var service = new ExportReceiptService(
                _mockExportRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditRepo.Object,
                new ExportReceiptOptions { AllowWarehouseStaffDirectDispatch = true });

            var result = await service.GetByIdAsync(1);

            result.AllowWarehouseStaffDirectDispatch.Should().BeTrue();
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


