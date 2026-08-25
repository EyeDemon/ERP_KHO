using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests
{
    public class InventoryTransactionsControllerTests
    {
        private readonly Mock<IInventoryTransactionQueryService> _mockQueryService;
        private readonly InventoryTransactionsController _controller;

        public InventoryTransactionsControllerTests()
        {
            _mockQueryService = new Mock<IInventoryTransactionQueryService>();
            _controller = new InventoryTransactionsController(_mockQueryService.Object);
        }

        [Fact]
        public async Task GetHistory_ReturnsOkResult_WithPagedResult()
        {
            // Arrange
            var expectedResult = new PagedResult<InventoryTransactionHistoryDto>
            {
                Items = new List<InventoryTransactionHistoryDto>
                {
                    new InventoryTransactionHistoryDto { Id = 1, TransactionType = "Import", Quantity = 10 }
                },
                TotalRecords = 1,
                PageIndex = 1,
                PageSize = 20
            };

            _mockQueryService.Setup(s => s.GetHistoryAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<TransactionType?>(), 
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), 
                It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(expectedResult);

            // Act
            var result = await _controller.GetHistory(null, null, null, null, null, null, null, null, 1, 20);

            // Assert
            var okResult = result as OkObjectResult;
            okResult.Should().NotBeNull();
            okResult!.StatusCode.Should().Be(200);
            okResult.Value.Should().BeEquivalentTo(expectedResult);
        }

        [Fact]
        public async Task GetHistory_PassesCorrectFilters()
        {
            // Arrange
            var fromDate = new DateTime(2023, 1, 1);
            var toDate = new DateTime(2023, 12, 31);
            
            _mockQueryService.Setup(s => s.GetHistoryAsync(
                fromDate, toDate, TransactionType.Export, 1, 2, 3, "ExportReceipt", "test", 2, 10))
                .ReturnsAsync(new PagedResult<InventoryTransactionHistoryDto>());

            // Act
            await _controller.GetHistory(fromDate, toDate, TransactionType.Export, 1, 2, 3, "ExportReceipt", "test", 2, 10);

            // Assert
            _mockQueryService.Verify(s => s.GetHistoryAsync(
                fromDate, toDate, TransactionType.Export, 1, 2, 3, "ExportReceipt", "test", 2, 10), Times.Once);
        }
    }
}
