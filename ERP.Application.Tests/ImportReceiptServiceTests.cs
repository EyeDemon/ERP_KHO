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
    public class ImportReceiptServiceTests
    {
        private readonly Mock<IImportReceiptRepository> _mockImportRepo;
        private readonly Mock<IInventoryStockRepository> _mockStockRepo;
        private readonly Mock<IInventoryTransactionRepository> _mockTransactionRepo;
        private readonly Mock<IWarehouseRepository> _mockWarehouseRepo;
        private readonly Mock<IProductRepository> _mockProductRepo;
        private readonly Mock<IUnitOfWork> _mockUnitOfWork;
        private readonly Mock<IAuditLogRepository> _mockAuditRepo;
        private readonly ImportReceiptService _service;

        public ImportReceiptServiceTests()
        {
            _mockImportRepo = new Mock<IImportReceiptRepository>();
            _mockStockRepo = new Mock<IInventoryStockRepository>();
            _mockTransactionRepo = new Mock<IInventoryTransactionRepository>();
            _mockWarehouseRepo = new Mock<IWarehouseRepository>();
            _mockProductRepo = new Mock<IProductRepository>();
            _mockUnitOfWork = new Mock<IUnitOfWork>();
            _mockAuditRepo = new Mock<IAuditLogRepository>();

            _service = new ImportReceiptService(
                _mockImportRepo.Object,
                _mockStockRepo.Object,
                _mockTransactionRepo.Object,
                _mockWarehouseRepo.Object,
                _mockProductRepo.Object,
                _mockUnitOfWork.Object,
                _mockAuditRepo.Object);
        }

        [Fact]
        public async Task CreateAsync_ValidDto_ReturnsDto_WithoutStockOrTxUpdate()
        {
            // Arrange
            var dto = new CreateImportReceiptDto
            {
                Code = "IM001",
                WarehouseId = 1,
                Note = "Import note",
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = 1, Quantity = 10, UnitPrice = 100, Note = "Detail note" }
                }
            };
            
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1, Name = "WH1" });
            _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(ProductWithBaseUnit(1, "P1"));
            _mockImportRepo.Setup(x => x.ExistsByCodeAsync("IM001", null)).ReturnsAsync(false);
            
            var savedReceipt = new ImportReceipt { Id = 1, Code = "IM001", Status = ReceiptStatus.Draft };
            _mockImportRepo.Setup(x => x.AddAsync(It.IsAny<ImportReceipt>(), It.IsAny<CancellationToken>())).Callback<ImportReceipt, CancellationToken>((r, ct) => r.Id = 1).ReturnsAsync((ImportReceipt r, CancellationToken ct) => r);
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(savedReceipt);

            // Act
            var result = await _service.CreateAsync(dto, 99);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(1);
            result.Code.Should().Be("IM001");
            
            _mockImportRepo.Verify(x => x.AddAsync(It.Is<ImportReceipt>(r => 
                r.Status == ReceiptStatus.Draft &&
                r.CreatedBy == 99 &&
                r.CreatedAt >= DateTime.UtcNow.AddSeconds(-5) &&
                r.Code == "IM001" &&
                r.WarehouseId == 1 &&
                r.Note == "Import note" &&
                r.Details.Count == 1 &&
                r.Details.First().ProductId == 1 &&
                r.Details.First().Quantity == 10 &&
                r.Details.First().UnitPrice == 100 &&
                r.Details.First().Note == "Detail note"
            )), Times.Once);
            
            // Should not modify stock or transactions during Create (Draft mode)
            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
            
            // Should write audit log
            var expectedTime = DateTime.UtcNow;
            _mockAuditRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => 
                a.UserId == 99 &&
                a.Action == "ImportReceipt.Created" &&
                a.EntityName == "ImportReceipt" &&
                a.EntityId == 1 &&
                a.Timestamp >= expectedTime.AddSeconds(-2) && a.Timestamp <= expectedTime.AddSeconds(2)
            )), Times.Once);
            
            // Should persist in transaction
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_OperationUom_SnapshotsConversionAndBaseExpectedQuantity()
        {
            var each = new Unit { Id = 1, Code = "EA", Name = "Each", DecimalPlaces = 0 };
            var box = new Unit { Id = 2, Code = "BOX", Name = "Box", DecimalPlaces = 0 };
            var product = new Product { Id = 1, UnitId = 1, Unit = each, IsActive = true, Uoms = [new ProductUom { UnitId = 2, Unit = box, ConversionFactor = 24, Version = 4, EffectiveFromUtc = DateTime.UtcNow.AddDays(1), IsActive = true }, new ProductUom { UnitId = 2, Unit = box, ConversionFactor = 12, Version = 3, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1), IsActive = true }] };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(product);
            _mockImportRepo.Setup(x => x.ExistsByCodeAsync("IM-UOM", null)).ReturnsAsync(false);
            _mockImportRepo.Setup(x => x.AddAsync(It.IsAny<ImportReceipt>(), It.IsAny<CancellationToken>())).ReturnsAsync((ImportReceipt r, CancellationToken _) => r);
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<int>())).ReturnsAsync(new ImportReceipt { Code = "IM-UOM" });

            await _service.CreateAsync(new CreateImportReceiptDto { Code = "IM-UOM", WarehouseId = 1, Details = [new() { ProductId = 1, OperationUnitId = 2, ExpectedQuantity = 2 }] }, 99);

            _mockImportRepo.Verify(x => x.AddAsync(It.Is<ImportReceipt>(r => r.Details.Single().ConversionFactor == 12 && r.Details.Single().ConversionVersion == 3 && r.Details.Single().BaseExpectedQuantity == 24 && r.Details.Single().OperationUnitCodeSnapshot == "BOX" && r.Details.Single().BaseUnitCodeSnapshot == "EA"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ReceiveAsync_NoQc_SnapshotsBaseAcceptedAndRejectsDamage()
        {
            var detail = new ImportReceiptDetail { Id = 7, ExpectedQuantity = 2, ConversionFactor = 12, OperationUnitDecimalPlaces = 0, BaseUnitDecimalPlaces = 0 };
            var receipt = new ImportReceipt { Id = 1, Status = ReceiptStatus.Draft, Details = [detail] };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            await _service.ReceiveAsync(1, new ReceiveImportReceiptDto { Lines = [new() { LineId = 7, ReceivedQuantity = 2, AcceptedQuantity = 2 }] }, 99);

            detail.BaseReceivedQuantity.Should().Be(24);
            detail.BaseAcceptedQuantity.Should().Be(24);
            receipt.Status.Should().Be(ReceiptStatus.Received);

            receipt.Status = ReceiptStatus.Draft;
            var damaged = () => _service.ReceiveAsync(1, new ReceiveImportReceiptDto { Lines = [new() { LineId = 7, ReceivedQuantity = 2, AcceptedQuantity = 1, DamagedQuantity = 1 }] }, 99);
            await damaged.Should().ThrowAsync<BusinessRuleException>().WithMessage("*no-QC*");
        }

        [Fact]
        public async Task ReceiveAsync_ConversionWithTrailingZeroScale_AcceptsWholeBaseQuantity()
        {
            var detail = new ImportReceiptDetail { Id = 8, ExpectedQuantity = 100, ConversionFactor = 1.00000000m, OperationUnitDecimalPlaces = 4, BaseUnitDecimalPlaces = 4 };
            var receipt = new ImportReceipt { Id = 2, Status = ReceiptStatus.Draft, Details = [detail] };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(2)).ReturnsAsync(receipt);

            await _service.ReceiveAsync(2, new ReceiveImportReceiptDto { Lines = [new() { LineId = 8, ReceivedQuantity = 100, AcceptedQuantity = 100 }] }, 99);

            detail.BaseAcceptedQuantity.Should().Be(100);
        }

        [Fact]
        public async Task CreateAsync_ProductSupplierQcPolicy_WinsAndIsSnapshotted()
        {
            var product = ProductWithBaseUnit(1, "P1");
            product.QcPolicies =
            [
                new QcPolicy { Id = 10, ProductId = 1, Version = 2, RequiresQc = true, IsActive = true, EffectiveFromUtc = DateTime.UtcNow.AddDays(-2), Rule = "product" },
                new QcPolicy { Id = 11, ProductId = 1, SupplierId = 7, Version = 3, RequiresQc = true, IsActive = true, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1), Rule = "supplier" }
            ];
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(product);
            _mockImportRepo.Setup(x => x.ExistsByCodeAsync("QC-1", null)).ReturnsAsync(false);
            _mockImportRepo.Setup(x => x.AddAsync(It.IsAny<ImportReceipt>(), It.IsAny<CancellationToken>())).ReturnsAsync((ImportReceipt x, CancellationToken _) => x);
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<int>())).ReturnsAsync(new ImportReceipt { Code = "QC-1" });

            await _service.CreateAsync(new CreateImportReceiptDto { Code = "QC-1", WarehouseId = 1, SupplierId = 7, Details = [new() { ProductId = 1, ExpectedQuantity = 2 }] }, 99);

            _mockImportRepo.Verify(x => x.AddAsync(It.Is<ImportReceipt>(r => r.Details.Single().RequiresQc && r.Details.Single().QcPolicyId == 11 && r.Details.Single().QcPolicyVersion == 3 && r.Details.Single().QcPolicySourceSnapshot == "ProductSupplier" && r.Details.Single().QcRuleSnapshot == "supplier"), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_ProductSupplierNoQcPolicy_OverridesProductQcPolicy()
        {
            var product = ProductWithBaseUnit(1, "P1");
            product.QcPolicies =
            [
                new QcPolicy { Id = 10, ProductId = 1, Version = 5, RequiresQc = true, IsActive = true, EffectiveFromUtc = DateTime.UtcNow.AddDays(-2) },
                new QcPolicy { Id = 11, ProductId = 1, SupplierId = 7, Version = 1, RequiresQc = false, IsActive = true, EffectiveFromUtc = DateTime.UtcNow.AddDays(-1) }
            ];
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(product);
            _mockImportRepo.Setup(x => x.ExistsByCodeAsync("QC-OVERRIDE", null)).ReturnsAsync(false);
            _mockImportRepo.Setup(x => x.AddAsync(It.IsAny<ImportReceipt>(), It.IsAny<CancellationToken>())).ReturnsAsync((ImportReceipt x, CancellationToken _) => x);
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(It.IsAny<int>())).ReturnsAsync(new ImportReceipt { Code = "QC-OVERRIDE" });

            await _service.CreateAsync(new CreateImportReceiptDto { Code = "QC-OVERRIDE", WarehouseId = 1, SupplierId = 7, Details = [new() { ProductId = 1, ExpectedQuantity = 2 }] }, 99);

            _mockImportRepo.Verify(x => x.AddAsync(It.Is<ImportReceipt>(r => !r.Details.Single().RequiresQc && r.Details.Single().QcPolicyId == 11 && r.Details.Single().QcState == ReceiptLineQcState.NoQcRequired), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task QcMixedReceipt_DispositionCompletesThenPostSplitsInventoryStatuses()
        {
            var noQc = new ImportReceiptDetail { Id = 1, ProductId = 1, ReceivedQuantity = 5, AcceptedQuantity = 5, BaseReceivedQuantity = 5, BaseAcceptedQuantity = 5 };
            var qc = new ImportReceiptDetail { Id = 2, ProductId = 2, RequiresQc = true, QcState = ReceiptLineQcState.QcPending, ReceivedQuantity = 10, BaseReceivedQuantity = 10, ConversionFactor = 1, OperationUnitDecimalPlaces = 0, BaseUnitDecimalPlaces = 0 };
            var receipt = new ImportReceipt { Id = 9, WarehouseId = 4, Status = ReceiptStatus.QcPending, Details = [noQc, qc] };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(9)).ReturnsAsync(receipt);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(It.IsAny<int>(), 4)).ReturnsAsync((InventoryStock?)null);
            _mockStockRepo.Setup(x => x.GetByProductWarehouseAndStatusAsync(It.IsAny<int>(), 4, It.IsAny<InventoryStatus>())).ReturnsAsync((InventoryStock?)null);

            await _service.RecordQcDispositionAsync(9, new RecordQcDispositionDto { Lines = [new() { LineId = 2, AcceptedQuantity = 6, DamagedQuantity = 2, RejectedQuantity = 2, ReasonCode = "QC-DISPOSITION" }] }, 33);
            receipt.Status.Should().Be(ReceiptStatus.QcCompleted);
            await _service.ApproveImportReceiptAsync(9, 34);
            await _service.PostAsync(9, 35);

            receipt.Status.Should().Be(ReceiptStatus.Posted);
            _mockStockRepo.Verify(x => x.AddAsync(It.Is<InventoryStock>(s => s.ProductId == 2 && s.Status == InventoryStatus.Available && s.Quantity == 6), It.IsAny<CancellationToken>()), Times.Once);
            _mockStockRepo.Verify(x => x.AddAsync(It.Is<InventoryStock>(s => s.ProductId == 2 && s.Status == InventoryStatus.Damaged && s.Quantity == 2), It.IsAny<CancellationToken>()), Times.Once);
            _mockStockRepo.Verify(x => x.AddAsync(It.Is<InventoryStock>(s => s.ProductId == 2 && s.Status == InventoryStatus.Rejected && s.Quantity == 2), It.IsAny<CancellationToken>()), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t => t.ProductId == 2 && t.InventoryStatus != InventoryStatus.Available && t.Quantity == 2)), Times.Exactly(2));
        }

        [Fact]
        public async Task PostAsync_QcPending_ReturnsConflictWithoutInventoryEffect()
        {
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(3)).ReturnsAsync(new ImportReceipt { Id = 3, Status = ReceiptStatus.QcPending });
            var act = () => _service.PostAsync(3, 99);
            var error = await act.Should().ThrowAsync<BusinessRuleException>();
            error.Which.Data["HttpStatusCode"].Should().Be(409);
            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_AuditLogFails_RollsBackTransactionAndThrows()
        {
            var dto = new CreateImportReceiptDto
            {
                Code = "IM001",
                WarehouseId = 1,
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = 1, Quantity = 10, UnitPrice = 100 }
                }
            };

            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(ProductWithBaseUnit());
            _mockImportRepo.Setup(x => x.ExistsByCodeAsync("IM001", null)).ReturnsAsync(false);
            _mockImportRepo.Setup(x => x.AddAsync(It.IsAny<ImportReceipt>(), It.IsAny<CancellationToken>()))
                .Callback<ImportReceipt, CancellationToken>((r, ct) => r.Id = 1)
                .ReturnsAsync((ImportReceipt r, CancellationToken ct) => r);

            _mockAuditRepo.Setup(x => x.AddAsync(It.IsAny<AuditLog>())).ThrowsAsync(new Exception("Audit write failed"));

            Func<Task> act = async () => await _service.CreateAsync(dto, 99);

            await act.Should().ThrowAsync<Exception>().WithMessage("Audit write failed");
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public async Task CreateAsync_EmptyCode_ThrowsBusinessRuleException(string code)
        {
            var dto = new CreateImportReceiptDto { Code = code, WarehouseId = 1, Details = new List<CreateImportReceiptDetailDto> { new CreateImportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Mã phiếu nhập không được để trống");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_EmptyDetails_ThrowsBusinessRuleException()
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = 1, Details = new List<CreateImportReceiptDetailDto>() };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Phiếu nhập phải có ít nhất 1 sản phẩm");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_NullDetails_ThrowsBusinessRuleException()
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = 1, Details = null! };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Phiếu nhập phải có ít nhất 1 sản phẩm");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public async Task CreateAsync_InvalidQuantity_ThrowsBusinessRuleException(decimal quantity)
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = 1, Details = new List<CreateImportReceiptDetailDto> { new CreateImportReceiptDetailDto { ProductId = 1, Quantity = quantity } } };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(ProductWithBaseUnit());
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Số lượng dự kiến của sản phẩm ID 1 phải lớn hơn 0");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }
        
        [Fact]
        public async Task CreateAsync_DuplicateCode_ThrowsBusinessRuleException()
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = 1, Details = new List<CreateImportReceiptDetailDto> { new CreateImportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            _mockImportRepo.Setup(x => x.ExistsByCodeAsync("IM001", null)).ReturnsAsync(true);
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Mã phiếu nhập 'IM001' đã tồn tại");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_InvalidWarehouse_ThrowsBusinessRuleException()
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = 99, Details = new List<CreateImportReceiptDetailDto> { new CreateImportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(99)).ReturnsAsync((Warehouse?)null);
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Không tìm thấy kho nhập với ID 99");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_InvalidProduct_ThrowsBusinessRuleException()
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = 1, Details = new List<CreateImportReceiptDetailDto> { new CreateImportReceiptDetailDto { ProductId = 99, Quantity = 10 } } };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            _mockProductRepo.Setup(x => x.GetByIdAsync(99)).ReturnsAsync((Product?)null);
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Không tìm thấy sản phẩm với ID 99");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_InvalidUnitPrice_ThrowsBusinessRuleException()
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = 1, Details = new List<CreateImportReceiptDetailDto> { new CreateImportReceiptDetailDto { ProductId = 1, Quantity = 10, UnitPrice = -5 } } };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            _mockProductRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(ProductWithBaseUnit());
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Đơn giá của sản phẩm ID 1 không được âm");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task CreateAsync_InvalidWarehouseId_ThrowsBusinessRuleException(int warehouseId)
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = warehouseId, Details = new List<CreateImportReceiptDetailDto> { new CreateImportReceiptDetailDto { ProductId = 1, Quantity = 10 } } };
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Kho nhập không hợp lệ");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task CreateAsync_InvalidProductId_ThrowsBusinessRuleException(int productId)
        {
            var dto = new CreateImportReceiptDto { Code = "IM001", WarehouseId = 1, Details = new List<CreateImportReceiptDetailDto> { new CreateImportReceiptDetailDto { ProductId = productId, Quantity = 10 } } };
            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(new Warehouse { Id = 1 });
            Func<Task> act = async () => await _service.CreateAsync(dto, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Sản phẩm không hợp lệ");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }
        [Fact]
        public async Task ApproveImportReceiptAsync_NotFound_RollsBackAndThrowsNotFoundException()
        {
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync((ImportReceipt?)null);
            Func<Task> act = async () => await _service.ApproveImportReceiptAsync(1, 1);
            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Không tìm thấy phiếu nhập id 1");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task ApproveImportReceiptAsync_NotDraft_RollsBackAndThrowsBusinessRuleException()
        {
            var receipt = new ImportReceipt { Id = 1, Status = ReceiptStatus.Approved };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            Func<Task> act = async () => await _service.ApproveImportReceiptAsync(1, 1);
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Chỉ có thể duyệt phiếu đã hoàn tất nhận hàng");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task ApproveImportReceiptAsync_Received_CommitsWithoutChangingInventory()
        {
            var receipt = new ImportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.Received,
                Details = new List<ImportReceiptDetail> { new ImportReceiptDetail { ProductId = 1, Quantity = 10, AcceptedQuantity = 10, BaseAcceptedQuantity = 10 } }
            };
            var stock = new InventoryStock { ProductId = 1, WarehouseId = 1, Quantity = 5 };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1)).ReturnsAsync(stock);

            await _service.ApproveImportReceiptAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.ReadyToPost);
            receipt.ApprovedBy.Should().Be(99);
            stock.Quantity.Should().Be(5);
            
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
            _mockImportRepo.Verify(x => x.UpdateAsync(receipt), Times.Once);
            
            var expectedTime = DateTime.UtcNow;
            _mockAuditRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => 
                a.UserId == 99 &&
                a.Action == "ImportReceipt.Approved" &&
                a.EntityName == "ImportReceipt" &&
                a.EntityId == 1 &&
                a.Timestamp >= expectedTime.AddSeconds(-2) && a.Timestamp <= expectedTime.AddSeconds(2)
            )), Times.Once);
            
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task PostAsync_ReadyToPostWithoutStock_CommitsAndCreatesStockAndImportTransaction()
        {
            var receipt = new ImportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.ReadyToPost,
                Details = new List<ImportReceiptDetail> { new ImportReceiptDetail { ProductId = 1, Quantity = 10, AcceptedQuantity = 10, BaseAcceptedQuantity = 10 } }
            };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1)).ReturnsAsync((InventoryStock?)null);

            await _service.PostAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Posted);
            
            _mockStockRepo.Verify(x => x.AddAsync(It.Is<InventoryStock>(s => s.Quantity == 10 && s.ProductId == 1 && s.WarehouseId == 1)), Times.Once);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.Is<InventoryTransaction>(t => 
                t.TransactionType == TransactionType.Import && t.Quantity == 10 && t.ReferenceId == 1 && t.CreatedBy == 99)), Times.Once);
            _mockImportRepo.Verify(x => x.UpdateAsync(receipt), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task PostAsync_WhenStockReadFails_RollsBackAndThrows()
        {
            var receipt = new ImportReceipt
            {
                Id = 1,
                WarehouseId = 1,
                Status = ReceiptStatus.ReadyToPost,
                Details = new List<ImportReceiptDetail> { new ImportReceiptDetail { ProductId = 1, Quantity = 10, AcceptedQuantity = 10, BaseAcceptedQuantity = 10 } }
            };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockStockRepo.Setup(x => x.GetByProductAndWarehouseAsync(1, 1)).ThrowsAsync(new Exception("DB Error"));

            Func<Task> act = async () => await _service.PostAsync(1, 99);
            await act.Should().ThrowAsync<Exception>().WithMessage("DB Error");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task PostAsync_ByReceiptCreator_IsRejectedBeforeStockMutation()
        {
            var receipt = new ImportReceipt { Id = 1, WarehouseId = 1, CreatedBy = 99, Status = ReceiptStatus.ReadyToPost };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            var act = () => _service.PostAsync(1, 99);

            await act.Should().ThrowAsync<ForbiddenException>();
            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>(), It.IsAny<CancellationToken>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task CancelAsync_ValidDraftReceipt_UpdatesStatusToCancelledAndWritesAuditLog()
        {
            var receipt = new ImportReceipt { Id = 1, Status = ReceiptStatus.Draft, Supplier = new BusinessPartner { Code = "SUP-1", Name = "Nhà cung cấp" } };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            await _service.CancelAsync(1, 99);

            receipt.Status.Should().Be(ReceiptStatus.Cancelled);
            receipt.SupplierCodeSnapshot.Should().Be("SUP-1");
            receipt.SupplierNameSnapshot.Should().Be("Nhà cung cấp");
            _mockImportRepo.Verify(x => x.UpdateAsync(receipt), Times.Once);

            var expectedTime = DateTime.UtcNow;
            _mockAuditRepo.Verify(x => x.AddAsync(It.Is<AuditLog>(a => 
                a.UserId == 99 &&
                a.Action == "ImportReceipt.Cancelled" &&
                a.EntityName == "ImportReceipt" &&
                a.EntityId == 1 &&
                a.Timestamp >= expectedTime.AddSeconds(-2) && a.Timestamp <= expectedTime.AddSeconds(2)
            )), Times.Once);

            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task CancelAsync_AuditLogFails_RollsBackTransactionAndThrows()
        {
            var receipt = new ImportReceipt { Id = 1, Status = ReceiptStatus.Draft };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockAuditRepo.Setup(x => x.AddAsync(It.IsAny<AuditLog>())).ThrowsAsync(new Exception("Audit write failed"));

            Func<Task> act = async () => await _service.CancelAsync(1, 99);

            await act.Should().ThrowAsync<Exception>().WithMessage("Audit write failed");
            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
        }

        [Fact]
        public async Task CancelAsync_ReceiptNotFound_ThrowsNotFoundException()
        {
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync((ImportReceipt?)null);

            Func<Task> act = async () => await _service.CancelAsync(1, 99);

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Không tìm thấy phiếu nhập id 1");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Theory]
        [InlineData(ReceiptStatus.Approved)]
        [InlineData(ReceiptStatus.Cancelled)]
        public async Task CancelAsync_NonDraftStatus_ThrowsBusinessRuleException(ReceiptStatus status)
        {
            var receipt = new ImportReceipt { Id = 1, Status = status };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            Func<Task> act = async () => await _service.CancelAsync(1, 99);

            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Chỉ có thể hủy phiếu nhập ở trạng thái nháp");
            _mockAuditRepo.Verify(x => x.AddAsync(It.IsAny<AuditLog>()), Times.Never);
        }

        [Fact]
        public async Task CancelAsync_DoesNotMutateInventoryStockOrTransactions()
        {
            var receipt = new ImportReceipt { Id = 1, Status = ReceiptStatus.Draft };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);

            await _service.CancelAsync(1, 99);

            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockStockRepo.Verify(x => x.UpdateAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_AuditLogFails_RollsBackTransactionAndLeavesNoAuditLog()
        {
            var warehouse = new Warehouse { Id = 1, IsActive = true };
            var product = ProductWithBaseUnit(10);

            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(warehouse);
            _mockProductRepo.Setup(x => x.GetByIdAsync(10)).ReturnsAsync(product);

            _mockAuditRepo.Setup(x => x.AddAsync(It.IsAny<AuditLog>()))
                .ThrowsAsync(new InvalidOperationException("Audit DB failure"));

            var dto = new CreateImportReceiptDto
            {
                Code = "IM-ATOM-FAIL",
                WarehouseId = 1,
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = 10, Quantity = 5, UnitPrice = 100 }
                }
            };

            Func<Task> act = async () => await _service.CreateAsync(dto, 99);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Audit DB failure");

            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_CommitTransactionFails_RollsBackTransactionAndThrows()
        {
            var warehouse = new Warehouse { Id = 1, IsActive = true };
            var product = ProductWithBaseUnit(10);

            _mockWarehouseRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(warehouse);
            _mockProductRepo.Setup(x => x.GetByIdAsync(10)).ReturnsAsync(product);

            _mockUnitOfWork.Setup(x => x.CommitTransactionAsync())
                .ThrowsAsync(new InvalidOperationException("Commit DB failure"));

            var dto = new CreateImportReceiptDto
            {
                Code = "IM-ATOM-COMMIT-FAIL",
                WarehouseId = 1,
                Details = new List<CreateImportReceiptDetailDto>
                {
                    new CreateImportReceiptDetailDto { ProductId = 10, Quantity = 5, UnitPrice = 100 }
                }
            };

            Func<Task> act = async () => await _service.CreateAsync(dto, 99);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Commit DB failure");

            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        [Fact]
        public async Task CancelAsync_AuditLogFails_RollsBackTransactionAndLeavesNoAuditLog()
        {
            var receipt = new ImportReceipt { Id = 1, Status = ReceiptStatus.Draft };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockAuditRepo.Setup(x => x.AddAsync(It.IsAny<AuditLog>()))
                .ThrowsAsync(new InvalidOperationException("Audit DB failure"));

            Func<Task> act = async () => await _service.CancelAsync(1, 99);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Audit DB failure");

            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.CommitTransactionAsync(), Times.Never);
            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }

        private static Product ProductWithBaseUnit(int id = 1, string name = "P1") => new()
        {
            Id = id, Name = name, IsActive = true, UnitId = 1,
            Unit = new Unit { Id = 1, Code = "EA", Name = "Each", DecimalPlaces = 4 }
        };

        [Fact]
        public async Task CancelAsync_CommitTransactionFails_RollsBackTransactionAndThrows()
        {
            var receipt = new ImportReceipt { Id = 1, Status = ReceiptStatus.Draft };
            _mockImportRepo.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(receipt);
            _mockUnitOfWork.Setup(x => x.CommitTransactionAsync())
                .ThrowsAsync(new InvalidOperationException("Commit DB failure"));

            Func<Task> act = async () => await _service.CancelAsync(1, 99);

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Commit DB failure");

            _mockUnitOfWork.Verify(x => x.BeginTransactionAsync(), Times.Once);
            _mockUnitOfWork.Verify(x => x.RollbackTransactionAsync(), Times.Once);
            _mockStockRepo.Verify(x => x.AddAsync(It.IsAny<InventoryStock>()), Times.Never);
            _mockTransactionRepo.Verify(x => x.AddAsync(It.IsAny<InventoryTransaction>()), Times.Never);
        }
    }
}


