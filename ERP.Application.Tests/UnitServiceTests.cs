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
    public class UnitServiceTests
    {
        private readonly Mock<IUnitRepository> _mockRepo;
        private readonly UnitService _service;

        public UnitServiceTests()
        {
            _mockRepo = new Mock<IUnitRepository>();
            _service = new UnitService(_mockRepo.Object);
        }

        [Fact]
        public async Task CreateUnit_WithVietnameseText_PreservesUnicodeAndTrimsCode()
        {
            // Arrange
            var dto = new CreateUnitDto 
            { 
                Code = "  KG  ", 
                Name = "Kilôgam"
            };
            
            _mockRepo.Setup(r => r.ExistsByCodeAsync("KG", null)).ReturnsAsync(false);
            
            Unit? capturedUnit = null;
            _mockRepo.Setup(r => r.AddAsync(It.IsAny<Unit>(), It.IsAny<CancellationToken>()))
                .Callback<Unit, CancellationToken>((u, ct) => 
                {
                    u.Id = 1;
                    capturedUnit = u;
                })
                .ReturnsAsync((Unit u, CancellationToken ct) => u);

            _mockRepo.Setup(r => r.GetByIdAsync(1))
                .ReturnsAsync(() => capturedUnit);

            // Act
            var result = await _service.CreateUnitAsync(dto, "testuser");

            // Assert
            capturedUnit.Should().NotBeNull();
            capturedUnit.Code.Should().Be("KG");
            capturedUnit.Name.Should().Be("Kilôgam");

            result.Code.Should().Be("KG");
            result.Name.Should().Be("Kilôgam");
            _mockRepo.Verify(r => r.ExistsByCodeAsync("KG", null), Times.Once);
        }

        [Fact]
        public async Task CreateUnit_WithDuplicateCode_ThrowsBusinessRuleException()
        {
            // Arrange
            var dto = new CreateUnitDto { Code = "DUP01" };
            _mockRepo.Setup(r => r.ExistsByCodeAsync("DUP01", null)).ReturnsAsync(true);

            // Act
            var act = async () => await _service.CreateUnitAsync(dto, "testuser");

            // Assert
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*tồn tại*");
            _mockRepo.Verify(r => r.AddAsync(It.IsAny<Unit>()), Times.Never);
        }

        [Fact]
        public async Task DeleteUnit_HasProducts_ThrowsBusinessRuleException_DoesNotDelete()
        {
            // Arrange
            var unit = new Unit { Id = 1, Code = "U1" };
            _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(unit);
            _mockRepo.Setup(r => r.HasProductsAsync(1)).ReturnsAsync(true);

            // Act
            var act = async () => await _service.DeleteUnitAsync(1);

            // Assert
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*được sử dụng cho sản phẩm*");
            _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<Unit>()), Times.Never);
        }

        [Fact]
        public async Task DeleteUnit_Unreferenced_DeletesExactlyOnce()
        {
            // Arrange
            var unit = new Unit { Id = 2, Code = "U2" };
            _mockRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(unit);
            _mockRepo.Setup(r => r.HasProductsAsync(2)).ReturnsAsync(false);

            // Act
            await _service.DeleteUnitAsync(2);

            // Assert
            _mockRepo.Verify(r => r.DeleteAsync(unit), Times.Once);
        }

        [Fact]
        public async Task DeleteUnit_MissingId_ThrowsNotFoundException()
        {
            // Arrange
            _mockRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Unit?)null);

            // Act
            var act = async () => await _service.DeleteUnitAsync(99);

            // Assert
            await act.Should().ThrowAsync<NotFoundException>();
            _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<Unit>()), Times.Never);
        }
    }
}
