using System.Security.Claims;
using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ERP.Api.Tests
{
    public class StocktakesControllerTests
    {
        private readonly Mock<IStocktakeService> _mockService;
        private readonly Mock<IStocktakeQueryService> _mockQueryService;
        private readonly StocktakesController _controller;

        public StocktakesControllerTests()
        {
            _mockService = new Mock<IStocktakeService>();
            _mockQueryService = new Mock<IStocktakeQueryService>();
            _controller = new StocktakesController(_mockService.Object, _mockQueryService.Object);
        }

        private void SetupUserClaims(string? userIdClaim)
        {
            var claims = new List<Claim>();
            if (userIdClaim != null)
            {
                claims.Add(new Claim("Id", userIdClaim));
            }
            
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var claimsPrincipal = new ClaimsPrincipal(identity);

            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = claimsPrincipal
                }
            };
        }

        [Fact]
        public async Task Create_MissingUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            SetupUserClaims(null);
            var dto = new CreateStocktakeDto { WarehouseId = 1 };

            // Act
            var result = await _controller.Create(dto);

            // Assert
            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            _mockService.Verify(s => s.CreateStocktakeAsync(It.IsAny<CreateStocktakeDto>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Create_MalformedUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            SetupUserClaims("not-an-integer");
            var dto = new CreateStocktakeDto { WarehouseId = 1 };

            // Act
            var result = await _controller.Create(dto);

            // Assert
            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            _mockService.Verify(s => s.CreateStocktakeAsync(It.IsAny<CreateStocktakeDto>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Create_ValidUserIdClaim_CallsServiceAndReturnsOk()
        {
            // Arrange
            SetupUserClaims("123");
            var dto = new CreateStocktakeDto { WarehouseId = 1 };
            _mockService.Setup(s => s.CreateStocktakeAsync(dto, 123)).ReturnsAsync(10);

            // Act
            var result = await _controller.Create(dto);

            // Assert
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            _mockService.Verify(s => s.CreateStocktakeAsync(dto, 123), Times.Once);
        }
            [Fact]
        public async Task Approve_MissingUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            SetupUserClaims(null);

            // Act
            var result = await _controller.Approve(1);

            // Assert
            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            _mockService.Verify(s => s.ApproveStocktakeAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Approve_MalformedUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            SetupUserClaims("not-an-integer");

            // Act
            var result = await _controller.Approve(1);

            // Assert
            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            _mockService.Verify(s => s.ApproveStocktakeAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Approve_ValidUserIdClaim_CallsServiceAndReturnsOk()
        {
            // Arrange
            SetupUserClaims("123");
            _mockService.Setup(s => s.ApproveStocktakeAsync(1, 123)).Returns(Task.CompletedTask);

            // Act
            var result = await _controller.Approve(1);

            // Assert
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            _mockService.Verify(s => s.ApproveStocktakeAsync(1, 123), Times.Once);
        }
    }
}

