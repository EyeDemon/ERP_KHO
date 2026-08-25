using System.Security.Claims;
using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests
{
    public class ExportReceiptsControllerTests
    {
        private readonly Mock<IExportReceiptService> _mockService;
        private readonly ExportReceiptsController _controller;

        public ExportReceiptsControllerTests()
        {
            _mockService = new Mock<IExportReceiptService>();
            _controller = new ExportReceiptsController(_mockService.Object);
        }

        private void SetupUserClaims(string? userIdClaim)
        {
            var claims = new List<Claim>();
            if (userIdClaim != null)
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userIdClaim));
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
        public async Task Approve_MissingUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            SetupUserClaims(null);

            // Act
            var result = await _controller.Approve(1);

            // Assert
            result.Should().BeOfType<UnauthorizedResult>();
            _mockService.Verify(s => s.ApproveAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Approve_InvalidUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            SetupUserClaims("invalid_int");

            // Act
            var result = await _controller.Approve(1);

            // Assert
            result.Should().BeOfType<UnauthorizedResult>();
            _mockService.Verify(s => s.ApproveAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Approve_ValidUserIdClaim_CallsServiceAndReturnsOk()
        {
            // Arrange
            SetupUserClaims("99");
            _mockService.Setup(s => s.ApproveAsync(1, 99)).Returns(Task.CompletedTask);

            // Act
            var result = await _controller.Approve(1) as OkObjectResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            _mockService.Verify(s => s.ApproveAsync(1, 99), Times.Once);
        }

        [Fact]
        public async Task Create_MissingUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            SetupUserClaims(null);

            // Act
            var result = await _controller.Create(new CreateExportReceiptDto());

            // Assert
            var unauthorizedResult = result as UnauthorizedObjectResult;
            unauthorizedResult.Should().NotBeNull();
            unauthorizedResult!.Value.Should().BeEquivalentTo(new { message = "Không xác định được danh tính người dùng" });
            _mockService.Verify(s => s.CreateAsync(It.IsAny<CreateExportReceiptDto>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Create_InvalidUserIdClaim_ReturnsUnauthorized()
        {
            // Arrange
            SetupUserClaims("invalid");

            // Act
            var result = await _controller.Create(new CreateExportReceiptDto());

            // Assert
            var unauthorizedResult = result as UnauthorizedObjectResult;
            unauthorizedResult.Should().NotBeNull();
            unauthorizedResult!.Value.Should().BeEquivalentTo(new { message = "Không xác định được danh tính người dùng" });
            _mockService.Verify(s => s.CreateAsync(It.IsAny<CreateExportReceiptDto>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Create_ValidUserIdClaim_CallsServiceAndReturnsCreatedAtAction()
        {
            // Arrange
            SetupUserClaims("99");
            var dto = new CreateExportReceiptDto { Code = "EX001" };
            var returnedDto = new ExportReceiptDto { Id = 1, Code = "EX001" };
            
            _mockService.Setup(s => s.CreateAsync(dto, 99)).ReturnsAsync(returnedDto);

            // Act
            var result = await _controller.Create(dto) as CreatedAtActionResult;

            // Assert
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(201);
            result.Value.Should().BeEquivalentTo(returnedDto);
            _mockService.Verify(s => s.CreateAsync(dto, 99), Times.Once);
        }

        [Fact]
        public async Task Create_BusinessRuleException_ReturnsBadRequest()
        {
            // Arrange
            SetupUserClaims("99");
            var dto = new CreateExportReceiptDto { Code = "EX001" };
            _mockService.Setup(s => s.CreateAsync(dto, 99))
                .ThrowsAsync(new ERP.Application.Exceptions.BusinessRuleException("Mã phiếu xuất không được để trống"));

            // Act
            var result = await _controller.Create(dto);

            // Assert
            var badRequestResult = result as BadRequestObjectResult;
            badRequestResult.Should().NotBeNull();
            badRequestResult!.StatusCode.Should().Be(400);
            badRequestResult.Value.Should().BeEquivalentTo(new { message = "Mã phiếu xuất không được để trống" });
            _mockService.Verify(s => s.CreateAsync(dto, 99), Times.Once);
        }
        [Fact]
        public async Task GetAll_ReturnsOkResult_WithReceipts()
        {
            var receipts = new List<ExportReceiptDto> { new ExportReceiptDto { Id = 1 } };
            _mockService.Setup(s => s.GetAllAsync()).ReturnsAsync(receipts);
            var result = await _controller.GetAll() as OkObjectResult;
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            result.Value.Should().BeEquivalentTo(receipts);
        }

        [Fact]
        public async Task GetById_ReturnsOkResult_WithReceipt()
        {
            var receipt = new ExportReceiptDto { Id = 1 };
            _mockService.Setup(s => s.GetByIdAsync(1)).ReturnsAsync(receipt);
            var result = await _controller.GetById(1) as OkObjectResult;
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            result.Value.Should().BeEquivalentTo(receipt);
        }

        [Fact]
        public async Task Cancel_MissingUserIdClaim_ReturnsUnauthorized()
        {
            SetupUserClaims(null);
            var result = await _controller.Cancel(1);
            var unauthorizedResult = result as UnauthorizedObjectResult;
            unauthorizedResult.Should().NotBeNull();
            unauthorizedResult!.Value.Should().BeEquivalentTo(new { message = "Không xác định được danh tính người dùng" });
            _mockService.Verify(s => s.CancelAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Cancel_InvalidUserIdClaim_ReturnsUnauthorized()
        {
            SetupUserClaims("invalid");
            var result = await _controller.Cancel(1);
            var unauthorizedResult = result as UnauthorizedObjectResult;
            unauthorizedResult.Should().NotBeNull();
            unauthorizedResult!.Value.Should().BeEquivalentTo(new { message = "Không xác định được danh tính người dùng" });
            _mockService.Verify(s => s.CancelAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Cancel_ValidUserIdClaim_CallsServiceAndReturnsOk()
        {
            SetupUserClaims("99");
            _mockService.Setup(s => s.CancelAsync(1, 99)).Returns(Task.CompletedTask);
            var result = await _controller.Cancel(1) as OkObjectResult;
            result.Should().NotBeNull();
            result!.StatusCode.Should().Be(200);
            _mockService.Verify(s => s.CancelAsync(1, 99), Times.Once);
        }
    }
}
