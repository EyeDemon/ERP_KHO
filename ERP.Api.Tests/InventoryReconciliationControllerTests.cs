using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests
{
    public class InventoryReconciliationControllerTests
    {
        [Fact]
        public async Task GetReconciliations_ReturnsOkResult_WithPagedResult()
        {
            var mockQueryService = new Mock<IInventoryReconciliationQueryService>();
            
            var expectedResult = new PagedResult<InventoryReconciliationDto>
            {
                Items = new List<InventoryReconciliationDto>
                {
                    new InventoryReconciliationDto { ProductId = 1, WarehouseId = 1, CurrentQuantity = 10, ExpectedQuantity = 10, Difference = 0, Status = "Match" }
                },
                TotalRecords = 1,
                PageIndex = 1,
                PageSize = 20
            };

            mockQueryService.Setup(s => s.GetReconciliationsAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(expectedResult);

            var controller = new InventoryReconciliationController(mockQueryService.Object);

            var result = await controller.GetReconciliations(null, null, null, 1, 20);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            var returnedResult = okResult.Value.Should().BeOfType<PagedResult<InventoryReconciliationDto>>().Subject;
            
            returnedResult.TotalRecords.Should().Be(1);
            returnedResult.Items[0].Status.Should().Be("Match");
        }
    }
}
