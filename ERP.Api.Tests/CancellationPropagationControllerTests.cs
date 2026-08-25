using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests
{
    public class CancellationPropagationControllerTests
    {
        [Fact]
        public async Task ProductsController_PropagatesCancellationToken_ToService()
        {
            // Arrange
            var mockService = new Mock<IProductService>();
            var controller = new ProductsController(mockService.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            mockService.Setup(s => s.GetAllProductsAsync(token))
                .ReturnsAsync(new List<ProductDto>());
            mockService.Setup(s => s.GetPagedProductsAsync(1, 20, "kw", token))
                .ReturnsAsync(new PagedResult<ProductDto>());
            mockService.Setup(s => s.GetProductByIdAsync(1, token))
                .ReturnsAsync(new ProductDto { Id = 1, Code = "P1" });
            mockService.Setup(s => s.CreateProductAsync(It.IsAny<CreateProductDto>(), It.IsAny<string>(), token))
                .ReturnsAsync(new ProductDto { Id = 1, Code = "P1" });
            mockService.Setup(s => s.UpdateProductAsync(1, It.IsAny<UpdateProductDto>(), It.IsAny<string>(), token))
                .Returns(Task.CompletedTask);
            mockService.Setup(s => s.DeleteProductAsync(1, token))
                .Returns(Task.CompletedTask);

            // Act & Assert
            await controller.GetAll(token);
            mockService.Verify(s => s.GetAllProductsAsync(token), Times.Once);

            await controller.GetPaged(1, 20, "kw", token);
            mockService.Verify(s => s.GetPagedProductsAsync(1, 20, "kw", token), Times.Once);

            await controller.GetById(1, token);
            mockService.Verify(s => s.GetProductByIdAsync(1, token), Times.Once);

            await controller.Create(new CreateProductDto { Code = "P1", Name = "N1", UnitId = 1 }, token);
            mockService.Verify(s => s.CreateProductAsync(It.IsAny<CreateProductDto>(), It.IsAny<string>(), token), Times.Once);

            await controller.Update(1, new UpdateProductDto { Name = "N1", UnitId = 1 }, token);
            mockService.Verify(s => s.UpdateProductAsync(1, It.IsAny<UpdateProductDto>(), It.IsAny<string>(), token), Times.Once);

            await controller.Delete(1, token);
            mockService.Verify(s => s.DeleteProductAsync(1, token), Times.Once);
        }

        [Fact]
        public async Task ReportsController_PropagatesCancellationToken_ToService()
        {
            // Arrange
            var mockService = new Mock<IReportService>();
            var controller = new ReportsController(mockService.Object);
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            mockService.Setup(s => s.GetInventoryReportAsync(null, 1, 2, token))
                .ReturnsAsync(new List<InventoryReportDto>());
            mockService.Setup(s => s.GetInventoryInOutReportAsync(null, null, 1, 2, token))
                .ReturnsAsync(new List<InventoryInOutReportDto>());

            // Act & Assert
            await controller.GetInventoryReport(null, 1, 2, token);
            mockService.Verify(s => s.GetInventoryReportAsync(null, 1, 2, token), Times.Once);

            await controller.ExportInventoryReport(null, 1, 2, token);
            mockService.Verify(s => s.GetInventoryReportAsync(null, 1, 2, token), Times.Exactly(2));

            await controller.GetInventoryInOutReport(null, null, 1, 2, token);
            mockService.Verify(s => s.GetInventoryInOutReportAsync(null, null, 1, 2, token), Times.Once);

            await controller.ExportInventoryInOutReport(null, null, 1, 2, token);
            mockService.Verify(s => s.GetInventoryInOutReportAsync(null, null, 1, 2, token), Times.Exactly(2));
        }

        [Fact]
        public async Task AuthController_PropagatesCancellationToken_ToService()
        {
            // Arrange
            var mockService = new Mock<IAuthService>();
            var controller = new AuthController(mockService.Object);
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

            var dto = new LoginRequestDto { Username = "admin", Password = "password" };
            mockService.Setup(s => s.LoginAsync(dto, token))
                .ReturnsAsync(new LoginResponseDto { Token = "jwt", Username = "admin", Role = "Admin" });

            // Act
            var res = await controller.Login(dto, token);

            // Assert
            var okResult = res.Should().BeOfType<OkObjectResult>().Subject;
            mockService.Verify(s => s.LoginAsync(dto, token), Times.Once);
        }
    }
}
