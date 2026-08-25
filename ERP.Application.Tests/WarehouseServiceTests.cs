using System.Threading.Tasks;
using Xunit;
using Moq;
using FluentAssertions;
using ERP.Application.Services;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Interfaces;

namespace ERP.Application.Tests
{
    public class WarehouseServiceTests
    {
        private readonly Mock<IWarehouseRepository> _mockRepo;
        private readonly WarehouseService _service;

        public WarehouseServiceTests()
        {
            _mockRepo = new Mock<IWarehouseRepository>();
            _service = new WarehouseService(_mockRepo.Object);
        }

        [Fact]
        public async Task CreateWarehouse_WithVietnameseText_PreservesUnicodeAndTrimsCode()
        {
            // Arrange
            var dto = new CreateWarehouseDto 
            { 
                Code = "  KHO01  ", 
                Name = "Kho Tổng Hồ Chí Minh",
                Address = "Số 1, Đường Lê Duẩn, Quận 1, TP. HCM"
            };
            
            _mockRepo.Setup(r => r.ExistsByCodeAsync("KHO01", null)).ReturnsAsync(false);
            
            Warehouse? capturedWarehouse = null;
            _mockRepo.Setup(r => r.AddAsync(It.IsAny<Warehouse>(), It.IsAny<CancellationToken>()))
                .Callback<Warehouse, CancellationToken>((w, ct) => 
                {
                    w.Id = 1;
                    capturedWarehouse = w;
                })
                .ReturnsAsync((Warehouse w, CancellationToken ct) => w);

            _mockRepo.Setup(r => r.GetByIdAsync(1))
                .ReturnsAsync(() => capturedWarehouse);

            // Act
            var result = await _service.CreateWarehouseAsync(dto, "testuser");

            // Assert
            capturedWarehouse.Should().NotBeNull();
            capturedWarehouse.Code.Should().Be("KHO01");
            capturedWarehouse.Name.Should().Be("Kho Tổng Hồ Chí Minh");
            capturedWarehouse.Address.Should().Be("Số 1, Đường Lê Duẩn, Quận 1, TP. HCM");

            result.Code.Should().Be("KHO01");
            result.Name.Should().Be("Kho Tổng Hồ Chí Minh");
            _mockRepo.Verify(r => r.ExistsByCodeAsync("KHO01", null), Times.Once);
        }

        [Fact]
        public async Task CreateWarehouse_WithDuplicateCode_ThrowsBusinessRuleException()
        {
            // Arrange
            var dto = new CreateWarehouseDto { Code = "DUP01" };
            _mockRepo.Setup(r => r.ExistsByCodeAsync("DUP01", null)).ReturnsAsync(true);

            // Act
            var act = async () => await _service.CreateWarehouseAsync(dto, "testuser");

            // Assert
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*tồn tại*");
            _mockRepo.Verify(r => r.AddAsync(It.IsAny<Warehouse>()), Times.Never);
        }

        [Fact]
        public async Task DeleteWarehouse_HasTransactions_ThrowsBusinessRuleException_DoesNotDelete()
        {
            // Arrange
            var warehouse = new Warehouse { Id = 1, Code = "W1" };
            _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(warehouse);
            _mockRepo.Setup(r => r.HasTransactionsAsync(1)).ReturnsAsync(true);

            // Act
            var act = async () => await _service.DeleteWarehouseAsync(1);

            // Assert
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*giao dịch*");
            _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<Warehouse>()), Times.Never);
        }

        [Fact]
        public async Task DeleteWarehouse_Unreferenced_DeletesExactlyOnce()
        {
            // Arrange
            var warehouse = new Warehouse { Id = 2, Code = "W2" };
            _mockRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(warehouse);
            _mockRepo.Setup(r => r.HasTransactionsAsync(2)).ReturnsAsync(false);

            // Act
            await _service.DeleteWarehouseAsync(2);

            // Assert
            _mockRepo.Verify(r => r.DeleteAsync(warehouse), Times.Once);
        }

        [Fact]
        public async Task DeleteWarehouse_MissingId_ThrowsNotFoundException()
        {
            // Arrange
            _mockRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Warehouse?)null);

            // Act
            var act = async () => await _service.DeleteWarehouseAsync(99);

            // Assert
            await act.Should().ThrowAsync<NotFoundException>();
            _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<Warehouse>()), Times.Never);
        }
    }
}
