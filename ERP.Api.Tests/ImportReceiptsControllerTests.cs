using System.Security.Claims;
using ERP.Api.Controllers;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests
{
    public class ImportReceiptsControllerTests
    {
        private readonly Mock<IImportReceiptService> _mockService;
        private readonly ImportReceiptsController _controller;

        public ImportReceiptsControllerTests()
        {
            _mockService = new Mock<IImportReceiptService>();
            _controller = new ImportReceiptsController(_mockService.Object);
        }

        private void SetUserClaims(params Claim[] claims)
        {
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims))
                }
            };
        }

        [Fact]
        public async Task Approve_MissingUserIdClaim_ReturnsUnauthorized()
        {
            SetUserClaims(); // Empty claims
            var result = await _controller.Approve(1);
            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauthorizedResult.Value.Should().BeEquivalentTo(new { message = "Không xác định được danh tính người dùng" });
            _mockService.Verify(x => x.ApproveImportReceiptAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Approve_InvalidUserIdClaim_ReturnsUnauthorized()
        {
            SetUserClaims(new Claim("Id", "abc"));
            var result = await _controller.Approve(1);
            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauthorizedResult.Value.Should().BeEquivalentTo(new { message = "Không xác định được danh tính người dùng" });
            _mockService.Verify(x => x.ApproveImportReceiptAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Approve_ValidUserIdClaim_ReturnsOkAndCallsService()
        {
            SetUserClaims(new Claim("Id", "99"));
            var result = await _controller.Approve(1);
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().BeEquivalentTo(new { message = "Duyệt phiếu nhập thành công" });
            _mockService.Verify(x => x.ApproveImportReceiptAsync(1, 99), Times.Once);
        }

        [Fact]
        public async Task Cancel_ValidNameIdentifierClaim_ReturnsOkAndCallsService()
        {
            SetUserClaims(new Claim(ClaimTypes.NameIdentifier, "45"));
            var result = await _controller.Cancel(1);
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().BeEquivalentTo(new { message = "Hủy phiếu nhập thành công" });
            _mockService.Verify(x => x.CancelAsync(1, 45), Times.Once);
        }

        [Fact]
        public async Task Cancel_ValidIdClaim_ReturnsOkAndCallsService()
        {
            SetUserClaims(new Claim("Id", "42"));
            var result = await _controller.Cancel(1);
            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().BeEquivalentTo(new { message = "Hủy phiếu nhập thành công" });
            _mockService.Verify(x => x.CancelAsync(1, 42), Times.Once);
        }

        [Fact]
        public async Task Cancel_MissingUserIdClaim_ReturnsUnauthorized()
        {
            SetUserClaims(); // Empty claims
            var result = await _controller.Cancel(1);
            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauthorizedResult.Value.Should().BeEquivalentTo(new { message = "Không xác định được danh tính người dùng" });
            _mockService.Verify(x => x.CancelAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task Cancel_InvalidUserIdClaim_ReturnsUnauthorized()
        {
            SetUserClaims(new Claim(ClaimTypes.NameIdentifier, "xyz"));
            var result = await _controller.Cancel(1);
            var unauthorizedResult = result.Should().BeOfType<UnauthorizedObjectResult>().Subject;
            unauthorizedResult.Value.Should().BeEquivalentTo(new { message = "Không xác định được danh tính người dùng" });
            _mockService.Verify(x => x.CancelAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void Controller_ClassHasAdminManagerOrViewerAuthorizeAttribute()
        {
            var authorizeAttr = typeof(ImportReceiptsController)
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .FirstOrDefault();

            authorizeAttr.Should().NotBeNull();
            authorizeAttr!.Roles.Should().Be(ERP.Api.Authorization.AppRoles.AdminManagerOrViewer);
        }

        [Theory]
        [InlineData(nameof(ImportReceiptsController.Create))]
        [InlineData(nameof(ImportReceiptsController.Approve))]
        [InlineData(nameof(ImportReceiptsController.Cancel))]
        public void MutationEndpoints_HaveAdminOrManagerAuthorizeAttribute(string methodName)
        {
            var method = typeof(ImportReceiptsController).GetMethod(methodName);
            method.Should().NotBeNull();

            var authorizeAttr = method!
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .FirstOrDefault();

            authorizeAttr.Should().NotBeNull();
            authorizeAttr!.Roles.Should().Be(ERP.Api.Authorization.AppRoles.AdminOrManager);
        }
    }
}
