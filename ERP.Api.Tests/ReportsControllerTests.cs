using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using ClosedXML.Excel;

namespace ERP.Api.Tests
{
    public class ReportsControllerTests
    {
        private readonly Mock<IReportService> _mockService;
        private readonly ReportsController _controller;

        public ReportsControllerTests()
        {
            _mockService = new Mock<IReportService>();
            _controller = new ReportsController(_mockService.Object);
        }

        [Fact]
        public async Task ExportInventoryInOutReport_WritesTransferBreakdownAndFourDecimalFormat()
        {
            _mockService.Setup(x => x.GetInventoryInOutReportAsync(null, null, 1, 2, default))
                .ReturnsAsync(new[]
                {
                    new InventoryInOutReportDto
                    {
                        ProductCode = "P1", ProductName = "Product", UnitName = "Cái", WarehouseName = "Kho A",
                        OpeningQuantity = 100, ImportQuantity = 50, TransferInQuantity = 20,
                        AdjustmentIncreaseQuantity = 5, InQuantity = 75, ExportQuantity = 30,
                        TransferOutQuantity = 25, AdjustmentDecreaseQuantity = 10, OutQuantity = 65,
                        ClosingQuantity = 110
                    }
                });

            var result = await _controller.ExportInventoryInOutReport(null, null, 1, 2);

            var file = result.Should().BeOfType<FileContentResult>().Subject;
            using var stream = new MemoryStream(file.FileContents);
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet("Báo cáo Xuất nhập tồn");
            sheet.Cell("G1").GetString().Should().Be("Nhận điều chuyển");
            sheet.Cell("K1").GetString().Should().Be("Xuất điều chuyển");
            sheet.Cell("G2").GetValue<decimal>().Should().Be(20);
            sheet.Cell("K2").GetValue<decimal>().Should().Be(25);
            sheet.Cell("N2").GetValue<decimal>().Should().Be(110);
            sheet.Cell("N2").Style.NumberFormat.Format.Should().Be("#,##0.0000");
        }

        [Fact]
        public async Task GetInventoryInOutReport_ValidRequest_ReturnsOkWithData()
        {
            var mockData = new List<InventoryInOutReportDto>
            {
                new InventoryInOutReportDto { ProductId = 1, ClosingQuantity = 10 }
            };
            
            _mockService.Setup(x => x.GetInventoryInOutReportAsync(null, null, null, null))
                        .ReturnsAsync(mockData);

            var result = await _controller.GetInventoryInOutReport(null, null, null, null);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            okResult.Value.Should().BeEquivalentTo(mockData);
        }

        [Fact]
        public async Task GetInventoryInOutReport_ServiceThrowsBusinessRuleException_ReturnsBadRequest()
        {
            _mockService.Setup(x => x.GetInventoryInOutReportAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null))
                        .ThrowsAsync(new BusinessRuleException("Từ ngày không được lớn hơn đến ngày."));

            var result = await _controller.GetInventoryInOutReport(new DateTime(2023, 1, 10), new DateTime(2023, 1, 1), null, null);

            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.Value.Should().BeEquivalentTo(new { message = "Từ ngày không được lớn hơn đến ngày." });
        }

        [Fact]
        public async Task ExportInventoryReport_ValidRequest_ReturnsFileContentResult()
        {
            var lastUpdatedDate = new DateTime(2023, 9, 25, 14, 30, 0);
            var mockData = new List<InventoryReportDto>
            {
                new InventoryReportDto
                {
                    ProductId = 1,
                    ProductCode = "P1",
                    ProductName = "Prod1",
                    UnitName = "Cai",
                    WarehouseId = 10,
                    WarehouseName = "Kho 10",
                    Quantity = 5.5m,
                    ReportDate = new DateTime(2023, 10, 1),
                    LastUpdated = lastUpdatedDate
                }
            };

            var testAsOfDate = new DateTime(2023, 10, 1);
            int testWarehouseId = 2;
            int testProductId = 3;

            _mockService.Setup(x => x.GetInventoryReportAsync(testAsOfDate, testWarehouseId, testProductId))
                        .ReturnsAsync(mockData);

            var result = await _controller.ExportInventoryReport(testAsOfDate, testWarehouseId, testProductId);

            _mockService.Verify(x => x.GetInventoryReportAsync(testAsOfDate, testWarehouseId, testProductId), Times.Once);

            var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
            fileResult.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            fileResult.FileDownloadName.Should().EndWith(".xlsx");
            fileResult.FileContents.Should().NotBeEmpty();

            // Reopen ClosedXML workbook from memory stream to assert cell content
            using var stream = new System.IO.MemoryStream(fileResult.FileContents);
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var worksheet = workbook.Worksheet("Báo cáo Tồn kho");
            worksheet.Should().NotBeNull();

            // Assert headers
            worksheet.Cell(1, 1).GetString().Should().Be("Mã SP");
            worksheet.Cell(1, 2).GetString().Should().Be("Tên SP");
            worksheet.Cell(1, 3).GetString().Should().Be("ĐVT");
            worksheet.Cell(1, 4).GetString().Should().Be("Kho");
            worksheet.Cell(1, 5).GetString().Should().Be("Số lượng tồn");
            worksheet.Cell(1, 6).GetString().Should().Be("Cập nhật lần cuối");
            worksheet.Cell(1, 7).GetString().Should().Be("Ngày báo cáo");

            // Assert data row
            worksheet.Cell(2, 1).GetString().Should().Be("P1");
            worksheet.Cell(2, 2).GetString().Should().Be("Prod1");
            worksheet.Cell(2, 3).GetString().Should().Be("Cai");
            worksheet.Cell(2, 4).GetString().Should().Be("Kho 10");
            worksheet.Cell(2, 5).GetDouble().Should().Be(5.5);
            worksheet.Cell(2, 6).GetString().Should().Be("2023-09-25 14:30:00");
            worksheet.Cell(2, 7).GetString().Should().Be("2023-10-01 00:00:00");
        }

        [Fact]
        public async Task ExportInventoryInOutReport_ValidRequest_ReturnsFileContentResult()
        {
            var mockData = new List<InventoryInOutReportDto>
            {
                new InventoryInOutReportDto
                {
                    ProductId = 1,
                    ProductCode = "P1",
                    ProductName = "Prod1",
                    UnitName = "Cai",
                    WarehouseId = 4,
                    WarehouseName = "Kho Test",
                    OpeningQuantity = 100m,
                    ImportQuantity = 30m,
                    TransferInQuantity = 15m,
                    AdjustmentIncreaseQuantity = 5m,
                    InQuantity = 50m,
                    ExportQuantity = 10m,
                    TransferOutQuantity = 7m,
                    AdjustmentDecreaseQuantity = 3m,
                    OutQuantity = 20m,
                    ClosingQuantity = 130m
                }
            };

            var testFromDate = new DateTime(2023, 1, 1);
            var testToDate = new DateTime(2023, 1, 31);
            int testWarehouseId = 4;
            int testProductId = 5;

            _mockService.Setup(x => x.GetInventoryInOutReportAsync(testFromDate, testToDate, testWarehouseId, testProductId))
                        .ReturnsAsync(mockData);

            var result = await _controller.ExportInventoryInOutReport(testFromDate, testToDate, testWarehouseId, testProductId);

            _mockService.Verify(x => x.GetInventoryInOutReportAsync(testFromDate, testToDate, testWarehouseId, testProductId), Times.Once);

            var fileResult = result.Should().BeOfType<FileContentResult>().Subject;
            fileResult.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            fileResult.FileDownloadName.Should().EndWith(".xlsx");
            fileResult.FileContents.Should().NotBeEmpty();

            // Reopen ClosedXML workbook from memory stream to assert cell content
            using var stream = new System.IO.MemoryStream(fileResult.FileContents);
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var worksheet = workbook.Worksheet("Báo cáo Xuất nhập tồn");
            worksheet.Should().NotBeNull();

            // Assert headers
            worksheet.Cell(1, 1).GetString().Should().Be("Mã SP");
            worksheet.Cell(1, 2).GetString().Should().Be("Tên SP");
            worksheet.Cell(1, 3).GetString().Should().Be("ĐVT");
            worksheet.Cell(1, 4).GetString().Should().Be("Kho");
            worksheet.Cell(1, 5).GetString().Should().Be("Tồn đầu kỳ");
            worksheet.Cell(1, 6).GetString().Should().Be("Nhập kho");
            worksheet.Cell(1, 7).GetString().Should().Be("Nhận điều chuyển");
            worksheet.Cell(1, 8).GetString().Should().Be("Điều chỉnh tăng");
            worksheet.Cell(1, 9).GetString().Should().Be("Tổng tăng");
            worksheet.Cell(1, 10).GetString().Should().Be("Xuất kho");
            worksheet.Cell(1, 11).GetString().Should().Be("Xuất điều chuyển");
            worksheet.Cell(1, 12).GetString().Should().Be("Điều chỉnh giảm");
            worksheet.Cell(1, 13).GetString().Should().Be("Tổng giảm");
            worksheet.Cell(1, 14).GetString().Should().Be("Tồn cuối kỳ");

            // Assert data row
            worksheet.Cell(2, 1).GetString().Should().Be("P1");
            worksheet.Cell(2, 2).GetString().Should().Be("Prod1");
            worksheet.Cell(2, 3).GetString().Should().Be("Cai");
            worksheet.Cell(2, 4).GetString().Should().Be("Kho Test");
            worksheet.Cell(2, 5).GetDouble().Should().Be(100.0);
            worksheet.Cell(2, 6).GetDouble().Should().Be(30.0);
            worksheet.Cell(2, 7).GetDouble().Should().Be(15.0);
            worksheet.Cell(2, 8).GetDouble().Should().Be(5.0);
            worksheet.Cell(2, 9).GetDouble().Should().Be(50.0);
            worksheet.Cell(2, 10).GetDouble().Should().Be(10.0);
            worksheet.Cell(2, 11).GetDouble().Should().Be(7.0);
            worksheet.Cell(2, 12).GetDouble().Should().Be(3.0);
            worksheet.Cell(2, 13).GetDouble().Should().Be(20.0);
            worksheet.Cell(2, 14).GetDouble().Should().Be(130.0);
        }

        [Fact]
        public async Task ExportInventoryInOutReport_ServiceThrowsBusinessRuleException_ReturnsBadRequest()
        {
            _mockService.Setup(x => x.GetInventoryInOutReportAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null))
                        .ThrowsAsync(new BusinessRuleException("Từ ngày lỗi."));

            var result = await _controller.ExportInventoryInOutReport(new DateTime(2023, 1, 10), new DateTime(2023, 1, 1), null, null);

            var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
            badRequest.Value.Should().BeEquivalentTo(new { message = "Từ ngày lỗi." });
        }

        [Fact]
        public async Task AcknowledgeOperationalException_ForbidsViewerAndRecordsUserIdAndWarehouseIdForStaff()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new int[] { 7 });

            var viewerController = new ReportsController(_mockService.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                    {
                        User = new System.Security.Claims.ClaimsPrincipal(
                            new System.Security.Claims.ClaimsIdentity(
                                new[]
                                {
                                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "99"),
                                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, ERP.Api.Authorization.AppRoles.Viewer)
                                },
                                "Test"))
                    }
                }
            };

            var viewerRes = await viewerController.AcknowledgeOperationalException(
                new ReportsController.AcknowledgeOperationalExceptionDto("EX-001", null, "test"),
                db,
                warehouseAuth.Object);
            viewerRes.Should().BeOfType<ForbidResult>();

            var staffController = new ReportsController(_mockService.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                    {
                        User = new System.Security.Claims.ClaimsPrincipal(
                            new System.Security.Claims.ClaimsIdentity(
                                new[]
                                {
                                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "42"),
                                    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, ERP.Api.Authorization.AppRoles.WarehouseStaff)
                                },
                                "Test"))
                    }
                }
            };

            var wrongWhRes = await staffController.AcknowledgeOperationalException(
                new ReportsController.AcknowledgeOperationalExceptionDto("EX-001", 999, "test"),
                db,
                warehouseAuth.Object);
            wrongWhRes.Should().BeOfType<ForbidResult>();

            var okRes = await staffController.AcknowledgeOperationalException(
                new ReportsController.AcknowledgeOperationalExceptionDto("EX-001", null, "Handled"),
                db,
                warehouseAuth.Object);
            okRes.Should().BeOfType<OkObjectResult>();

            var audit = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.AuditLogs);
            audit.UserId.Should().Be(42);
            audit.WarehouseId.Should().Be(7);
            audit.OldValues.Should().Be("EX-001");
        }

        [Fact]
        public async Task ErpKhoDbContext_EnforcesAppendOnlyAuditLogs()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var log = new ERP.Domain.Entities.AuditLog
            {
                EntityName = "Test",
                EntityId = 1,
                Action = "Create",
                Timestamp = DateTime.UtcNow
            };
            db.AuditLogs.Add(log);
            await db.SaveChangesAsync();

            log.Action = "Tampered";
            var actModify = async () => await db.SaveChangesAsync();
            await actModify.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*AuditLog is append-only*");

            db.Entry(log).State = Microsoft.EntityFrameworkCore.EntityState.Unchanged;
            db.AuditLogs.Remove(log);
            var actDelete = async () => await db.SaveChangesAsync();
            await actDelete.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*AuditLog is append-only*");
        }

        [Fact]
        public async Task PermissionsController_ScopesAuditLogsByWarehouseForNonAdmin_AndReportsHonestTenancy()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);
            db.AuditLogs.AddRange(
                new ERP.Domain.Entities.AuditLog { EntityName = "A", EntityId = 1, WarehouseId = 1, Action = "Act1", Timestamp = DateTime.UtcNow },
                new ERP.Domain.Entities.AuditLog { EntityName = "B", EntityId = 2, WarehouseId = 2, Action = "Act2", Timestamp = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new int[] { 1 });

            var controller = new PermissionsController(db, warehouseAuth.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                    {
                        User = new System.Security.Claims.ClaimsPrincipal(
                            new System.Security.Claims.ClaimsIdentity(
                                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, ERP.Api.Authorization.AppRoles.Manager) },
                                "Test"))
                    }
                }
            };

            var logsRes = await controller.AuditLogs(CancellationToken.None);
            var okLogs = logsRes.Should().BeOfType<OkObjectResult>().Subject;
            var json = System.Text.Json.JsonSerializer.Serialize(okLogs.Value);
            json.Should().Contain("Act1").And.NotContain("Act2");

            var suiteRes = controller.EnterpriseGovernanceSuite().Should().BeOfType<OkObjectResult>().Subject;
            System.Text.Json.JsonSerializer.Serialize(suiteRes.Value)
                .Should().Contain("ROW_LEVEL_WAREHOUSE_SCOPE_AND_DEDICATED_DB_TENANCY");
        }

        [Fact]
        public async Task AuthController_Login_StripsRefreshTokenFromJsonResponseBody()
        {
            var authService = new Mock<IAuthService>();
            var sessionService = new Mock<IUserSessionService>();
            var env = new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            env.Setup(x => x.EnvironmentName).Returns("Testing");

            authService.Setup(x => x.LoginAsync(It.IsAny<LoginRequestDto>(), It.IsAny<SessionContextDto>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new LoginResponseDto
                {
                    Token = "access-jwt",
                    RefreshToken = "secret-refresh-token",
                    RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7),
                    Username = "staff",
                    Role = "WarehouseStaff"
                });

            var controller = new AuthController(authService.Object, sessionService.Object, env.Object, new ERP.Application.Common.SessionSecurityOptions())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
                }
            };

            var res = await controller.Login(new LoginRequestDto { Username = "staff", Password = "pw" });
            var ok = res.Should().BeOfType<OkObjectResult>().Subject;
            var dto = ok.Value.Should().BeOfType<LoginResponseDto>().Subject;
            dto.RefreshToken.Should().BeEmpty();
            controller.Response.Headers.SetCookie.ToString().Should().Contain("erp_refresh=secret-refresh-token");
        }
    }
}
