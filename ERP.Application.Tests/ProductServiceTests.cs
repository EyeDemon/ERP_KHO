using System.Collections.Generic;
using System.Linq;
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
    public class ProductServiceTests
    {
        private readonly Mock<IProductRepository> _mockRepo;
        private readonly ProductService _service;

        public ProductServiceTests()
        {
            _mockRepo = new Mock<IProductRepository>();
            _service = new ProductService(_mockRepo.Object);
        }

        [Fact]
        public async Task CreateProduct_WithVietnameseText_PreservesUnicodeAndTrimsCode()
        {
            // Arrange
            var dto = new CreateProductDto 
            { 
                Code = "  SP01  ", 
                Name = "Cà phê Việt Nam",
                Description = "Sản phẩm chất lượng cao có dấu tiếng Việt"
            };
            
            _mockRepo.Setup(r => r.ExistsByCodeAsync("SP01", null)).ReturnsAsync(false);
            
            Product? capturedProduct = null;
            _mockRepo.Setup(r => r.AddAsync(It.IsAny<Product>(), It.IsAny<CancellationToken>()))
                .Callback<Product, CancellationToken>((p, ct) => 
                {
                    p.Id = 1;
                    capturedProduct = p;
                })
                .ReturnsAsync((Product p, CancellationToken ct) => p);

            _mockRepo.Setup(r => r.GetByIdAsync(1))
                .ReturnsAsync(() => capturedProduct);

            // Act
            var result = await _service.CreateProductAsync(dto, "testuser");

            // Assert
            capturedProduct.Should().NotBeNull();
            capturedProduct.Code.Should().Be("SP01");
            capturedProduct.Name.Should().Be("Cà phê Việt Nam");
            capturedProduct.Description.Should().Be("Sản phẩm chất lượng cao có dấu tiếng Việt");

            result.Code.Should().Be("SP01");
            result.Name.Should().Be("Cà phê Việt Nam");
            _mockRepo.Verify(r => r.ExistsByCodeAsync("SP01", null), Times.Once);
        }

        [Fact]
        public async Task CreateProduct_WithDuplicateCode_ThrowsBusinessRuleException()
        {
            // Arrange
            var dto = new CreateProductDto { Code = "DUP01" };
            _mockRepo.Setup(r => r.ExistsByCodeAsync("DUP01", null)).ReturnsAsync(true);

            // Act
            var act = async () => await _service.CreateProductAsync(dto, "testuser");

            // Assert
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*tồn tại*");
            _mockRepo.Verify(r => r.AddAsync(It.IsAny<Product>()), Times.Never);
        }

        [Fact]
        public async Task DeleteProduct_HasTransactions_ThrowsBusinessRuleException_DoesNotDelete()
        {
            // Arrange
            var product = new Product { Id = 1, Code = "P1" };
            _mockRepo.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(product);
            _mockRepo.Setup(r => r.HasTransactionsAsync(1)).ReturnsAsync(true);

            // Act
            var act = async () => await _service.DeleteProductAsync(1);

            // Assert
            await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*giao dịch kho*");
            _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<Product>()), Times.Never);
        }

        [Fact]
        public async Task DeleteProduct_Unreferenced_DeletesExactlyOnce()
        {
            // Arrange
            var product = new Product { Id = 2, Code = "P2" };
            _mockRepo.Setup(r => r.GetByIdAsync(2)).ReturnsAsync(product);
            _mockRepo.Setup(r => r.HasTransactionsAsync(2)).ReturnsAsync(false);

            // Act
            await _service.DeleteProductAsync(2);

            // Assert
            _mockRepo.Verify(r => r.DeleteAsync(product), Times.Once);
        }

        [Fact]
        public async Task DeleteProduct_MissingId_ThrowsNotFoundException()
        {
            // Arrange
            _mockRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Product?)null);

            // Act
            var act = async () => await _service.DeleteProductAsync(99);

            // Assert
            await act.Should().ThrowAsync<NotFoundException>();
            _mockRepo.Verify(r => r.DeleteAsync(It.IsAny<Product>()), Times.Never);
        }

        [Fact]
        public async Task GetPagedProductsAsync_ReturnsPagedResultWithCorrectMapping()
        {
            // Arrange
            var products = new List<Product>
            {
                new Product { Id = 1, Code = "SP01", Name = "Sản phẩm 1", Unit = new Unit { Name = "Cái" } },
                new Product { Id = 2, Code = "SP02", Name = "Sản phẩm 2", Unit = new Unit { Name = "Hộp" } }
            };

            _mockRepo.Setup(r => r.GetPagedAsync(1, 20, "SP"))
                .ReturnsAsync(((IReadOnlyList<Product>)products, 2));

            // Act
            var result = await _service.GetPagedProductsAsync(1, 20, "SP");

            // Assert
            result.Should().NotBeNull();
            result.TotalRecords.Should().Be(2);
            result.PageIndex.Should().Be(1);
            result.PageSize.Should().Be(20);
            result.TotalPages.Should().Be(1);
            result.Items.Should().HaveCount(2);
            result.Items[0].Code.Should().Be("SP01");
            result.Items[0].UnitName.Should().Be("Cái");
            result.Items[1].Code.Should().Be("SP02");
            result.Items[1].UnitName.Should().Be("Hộp");
        }

        [Theory]
        [InlineData(0, 10, 1, 10)]
        [InlineData(2, 0, 2, 20)]
        [InlineData(1, 200, 1, 100)]
        public async Task GetPagedProductsAsync_NormalizesPagingParameters(int inputPage, int inputSize, int expectedPage, int expectedSize)
        {
            // Arrange
            _mockRepo.Setup(r => r.GetPagedAsync(expectedPage, expectedSize, null))
                .ReturnsAsync(((IReadOnlyList<Product>)new List<Product>(), 0));

            // Act
            var result = await _service.GetPagedProductsAsync(inputPage, inputSize, null);

            // Assert
            result.PageIndex.Should().Be(expectedPage);
            result.PageSize.Should().Be(expectedSize);
            _mockRepo.Verify(r => r.GetPagedAsync(expectedPage, expectedSize, null), Times.Once);
        }
    }
}
