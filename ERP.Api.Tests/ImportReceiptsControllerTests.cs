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
            okResult.Value.Should().BeEquivalentTo(new { message = "Phiếu nhập đã sẵn sàng post" });
            _mockService.Verify(x => x.ApproveImportReceiptAsync(1, 99), Times.Once);
        }

        [Theory]
        [InlineData("receive")]
        [InlineData("post")]
        public async Task WorkflowCommand_ValidUserIdClaim_ReturnsOkAndCallsService(string command)
        {
            SetUserClaims(new Claim("Id", "99"));

            var receiveDto = new ERP.Application.DTOs.ReceiveImportReceiptDto();
            var result = command == "receive" ? await _controller.Receive(1, receiveDto) : await _controller.Post(1);

            result.Should().BeOfType<OkObjectResult>();
            if (command == "receive") _mockService.Verify(x => x.ReceiveAsync(1, receiveDto, 99), Times.Once);
            else _mockService.Verify(x => x.PostAsync(1, 99), Times.Once);
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
            authorizeAttr!.Roles.Should().BeNull();
        }

        [Theory]
        [InlineData(nameof(ImportReceiptsController.Create))]
        [InlineData(nameof(ImportReceiptsController.Cancel))]
        [InlineData(nameof(ImportReceiptsController.Receive))]
        public void MutationEndpoints_HaveAdminOrManagerAuthorizeAttribute(string methodName)
        {
            var method = typeof(ImportReceiptsController).GetMethod(methodName);
            method.Should().NotBeNull();

            var authorizeAttr = method!
                .GetCustomAttributes(typeof(ERP.Api.Authorization.PermissionAuthorizeAttribute), false)
                .Cast<ERP.Api.Authorization.PermissionAuthorizeAttribute>()
                .FirstOrDefault();

            authorizeAttr.Should().NotBeNull();
            authorizeAttr!.Permission.Should().Be(methodName switch
            {
                nameof(ImportReceiptsController.Create) => ERP.Api.Authorization.AppPermissions.ReceiptCreate,
                nameof(ImportReceiptsController.Cancel) => ERP.Api.Authorization.AppPermissions.ReceiptCancel,
                _ => ERP.Api.Authorization.AppPermissions.ReceiptReceive
            });
        }

        [Fact]
        public void ApproveEndpoint_UsesSharedCheckerPolicy()
        {
            var authorizeAttr = typeof(ImportReceiptsController).GetMethod(nameof(ImportReceiptsController.Approve))!
                .GetCustomAttributes(typeof(ERP.Api.Authorization.PermissionAuthorizeAttribute), false)
                .Cast<ERP.Api.Authorization.PermissionAuthorizeAttribute>()
                .Single();

            authorizeAttr.Permission.Should().Be(ERP.Api.Authorization.AppPermissions.ReceiptComplete);
        }

        [Fact]
        public void PostEndpoint_UsesSharedCheckerPolicy()
        {
            var authorizeAttr = typeof(ImportReceiptsController).GetMethod(nameof(ImportReceiptsController.Post))!
                .GetCustomAttributes(typeof(ERP.Api.Authorization.PermissionAuthorizeAttribute), false)
                .Cast<ERP.Api.Authorization.PermissionAuthorizeAttribute>()
                .Single();

            authorizeAttr.Permission.Should().Be(ERP.Api.Authorization.AppPermissions.ReceiptPost);
        }
    }
}
