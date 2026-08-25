using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests
{
    public class ProductsControllerTests
    {
        private readonly Mock<IProductService> _mockProductService;
        private readonly ProductsController _controller;

        public ProductsControllerTests()
        {
            _mockProductService = new Mock<IProductService>();
            _controller = new ProductsController(_mockProductService.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        }

        [Fact]
        public async Task GetPaged_ReturnsOkResult_WithPagedResult()
        {
            // Arrange
            var expectedResult = new PagedResult<ProductDto>
            {
                Items = new List<ProductDto>
                {
                    new ProductDto { Id = 1, Code = "SP01", Name = "Sản phẩm 1" },
                    new ProductDto { Id = 2, Code = "SP02", Name = "Sản phẩm 2" }
                },
                TotalRecords = 2,
                PageIndex = 1,
                PageSize = 20
            };

            _mockProductService.Setup(s => s.GetPagedProductsAsync(1, 20, "SP"))
                .ReturnsAsync(expectedResult);

            // Act
            var actionResult = await _controller.GetPaged(1, 20, "SP");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var result = Assert.IsType<PagedResult<ProductDto>>(okResult.Value);
            Assert.Equal(2, result.TotalRecords);
            Assert.Equal(2, result.Items.Count);
        }

        [Fact]
        public async Task GetAll_ReturnsOkResult_WithAllProducts()
        {
            // Arrange
            var products = new List<ProductDto>
            {
                new ProductDto { Id = 1, Code = "SP01", Name = "Sản phẩm 1" }
            };

            _mockProductService.Setup(s => s.GetAllProductsAsync())
                .ReturnsAsync(products);

            // Act
            var actionResult = await _controller.GetAll();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var result = Assert.IsAssignableFrom<IEnumerable<ProductDto>>(okResult.Value);
            Assert.Single(result);
        }

        [Fact]
        public async Task GetById_ReturnsOkResult_WithProduct()
        {
            // Arrange
            var product = new ProductDto { Id = 1, Code = "SP01", Name = "Sản phẩm 1" };
            _mockProductService.Setup(s => s.GetProductByIdAsync(1))
                .ReturnsAsync(product);

            // Act
            var actionResult = await _controller.GetById(1);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(actionResult);
            var result = Assert.IsType<ProductDto>(okResult.Value);
            Assert.Equal("SP01", result.Code);
        }

        [Fact]
        public async Task Create_ReturnsCreatedAtActionResult()
        {
            // Arrange
            var dto = new CreateProductDto { Code = "SP01", Name = "Sản phẩm 1", UnitId = 1 };
            var created = new ProductDto { Id = 1, Code = "SP01", Name = "Sản phẩm 1", UnitId = 1 };

            _mockProductService.Setup(s => s.CreateProductAsync(dto, It.IsAny<string>()))
                .ReturnsAsync(created);

            // Act
            var actionResult = await _controller.Create(dto);

            // Assert
            var createdAtResult = Assert.IsType<CreatedAtActionResult>(actionResult);
            Assert.Equal(1, createdAtResult.RouteValues?["id"]);
        }

        [Fact]
        public async Task Update_ReturnsNoContentResult()
        {
            // Arrange
            var dto = new UpdateProductDto { Name = "Cập nhật", UnitId = 1 };

            // Act
            var actionResult = await _controller.Update(1, dto);

            // Assert
            Assert.IsType<NoContentResult>(actionResult);
            _mockProductService.Verify(s => s.UpdateProductAsync(1, dto, It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Delete_ReturnsNoContentResult()
        {
            // Act
            var actionResult = await _controller.Delete(1);

            // Assert
            Assert.IsType<NoContentResult>(actionResult);
            _mockProductService.Verify(s => s.DeleteProductAsync(1), Times.Once);
        }
    }
}
