using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using Microsoft.AspNetCore.Authorization;
using ERP.Api.Authorization;

namespace ERP.Api.Tests
{
    public class StocktakesControllerQueryTests
    {
        [Fact]
        public void Controller_HasAdminManagerOrStaffAuthorization()
        {
            var type = typeof(StocktakesController);
            var authAttribute = type.GetCustomAttributes(typeof(AuthorizeAttribute), true).FirstOrDefault() as AuthorizeAttribute;
            Assert.NotNull(authAttribute);
            Assert.Equal(AppRoles.AdminManagerOrStaff, authAttribute.Roles);
        }

        [Fact]
        public async Task GetAll_ReturnsOkWithStocktakes()
        {
            var mockService = new Mock<IStocktakeService>();
            var mockQueryService = new Mock<IStocktakeQueryService>();

            var mockData = new List<StocktakeSummaryDto>
            {
                new StocktakeSummaryDto { Id = 1, Code = "ST001" },
                new StocktakeSummaryDto { Id = 2, Code = "ST002" }
            };

            mockQueryService.Setup(s => s.GetStocktakesAsync()).ReturnsAsync(mockData);

            var controller = new StocktakesController(mockService.Object, mockQueryService.Object);

            var result = await controller.GetAll();
            
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedData = Assert.IsAssignableFrom<IEnumerable<StocktakeSummaryDto>>(okResult.Value);
            Assert.Equal(2, returnedData.Count());
        }

        [Fact]
        public async Task GetById_ReturnsOkWithStocktake()
        {
            var mockService = new Mock<IStocktakeService>();
            var mockQueryService = new Mock<IStocktakeQueryService>();

            var mockData = new StocktakeResponseDto 
            { 
                Id = 1, 
                Code = "ST001",
                Details = new List<StocktakeDetailRowDto>
                {
                    new StocktakeDetailRowDto { Id = 1, ProductCode = "P1" }
                }
            };

            mockQueryService.Setup(s => s.GetStocktakeByIdAsync(1)).ReturnsAsync(mockData);

            var controller = new StocktakesController(mockService.Object, mockQueryService.Object);

            var result = await controller.GetById(1);
            
            var okResult = Assert.IsType<OkObjectResult>(result);
            var returnedData = Assert.IsType<StocktakeResponseDto>(okResult.Value);
            Assert.Equal(1, returnedData.Id);
            Assert.Single(returnedData.Details);
        }
    }
}
