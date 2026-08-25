using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests
{
    public class InventoryStocksControllerTests
    {
        private readonly Mock<IInventoryQueryService> _mockInventoryQueryService;
        private readonly InventoryStocksController _controller;

        public InventoryStocksControllerTests()
        {
            _mockInventoryQueryService = new Mock<IInventoryQueryService>();
            _controller = new InventoryStocksController(_mockInventoryQueryService.Object);
        }

        [Fact]
        public async Task GetCurrentStock_ReturnsOkResult_WithStocks()
        {
            // Arrange
            var expectedStocks = new List<InventoryStockDto>
            {
                new InventoryStockDto { ProductId = 1, Quantity = 100 },
                new InventoryStockDto { ProductId = 2, Quantity = 50 }
            };

            _mockInventoryQueryService.Setup(s => s.GetCurrentStockAsync(
                It.IsAny<int?>(), 
                It.IsAny<int?>(), 
                It.IsAny<string?>(), 
                It.IsAny<decimal?>()))
                .ReturnsAsync(expectedStocks);

            // Act
            var result = await _controller.GetCurrentStock(null, null, null, null);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            var returnedStocks = Assert.IsAssignableFrom<IEnumerable<InventoryStockDto>>(okResult.Value);
            Assert.Equal(2, returnedStocks.Count());
        }

        [Fact]
        public async Task GetCurrentStock_PassesFiltersCorrectly()
        {
            // Arrange
            _mockInventoryQueryService.Setup(s => s.GetCurrentStockAsync(1, 2, "test", 10))
                .ReturnsAsync(new List<InventoryStockDto>());

            // Act
            await _controller.GetCurrentStock(1, 2, "test", 10);

            // Assert
            _mockInventoryQueryService.Verify(s => s.GetCurrentStockAsync(1, 2, "test", 10), Times.Once);
        }
    }
}