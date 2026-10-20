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

        [Fact]
        public async Task Wave2_Rem07_Controllers_HandleNullRequestInEfCoreQueries_AndEnforceContracts()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var exportController = new ExportReceiptsController(new Mock<IExportReceiptService>().Object, db);
            var cutoffRes = await exportController.VerifyDispatchCutoff(null);
            cutoffRes.Should().BeOfType<OkObjectResult>();

            var huController = new HandlingUnitsController(new Mock<IPackingService>().Object, db);
            var sealRes = await huController.VerifyInTransitSeal(null);
            sealRes.Should().BeOfType<OkObjectResult>();

            var importController = new ImportReceiptsController(new Mock<IImportReceiptService>().Object, null, db);
            var rmaRes = await importController.RmaIntakeCheck(null);
            var rmaOk = rmaRes.Should().BeOfType<OkObjectResult>().Subject;
            System.Text.Json.JsonSerializer.Serialize(rmaOk.Value)
                .Should().Contain("rmaInspectionCode").And.Contain("RMA-INSP-");

            var rtvRes = await importController.ExecuteRtvRefurbish(null);
            var rtvOk = rtvRes.Should().BeOfType<OkObjectResult>().Subject;
            System.Text.Json.JsonSerializer.Serialize(rtvOk.Value)
                .Should().Contain("DISP-RTV-");

            var reconQuery = new Mock<IInventoryReconciliationQueryService>();
            reconQuery.Setup(x => x.GetReconciliationsAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(new ERP.Application.Common.PagedResult<InventoryReconciliationDto> { Items = Array.Empty<InventoryReconciliationDto>(), TotalRecords = 0 });
            var reconController = new InventoryReconciliationController(reconQuery.Object, db);
            var glRes = await reconController.VerifySiteReadiness(null, null);
            var glOk = glRes.Should().BeOfType<OkObjectResult>().Subject;
            System.Text.Json.JsonSerializer.Serialize(glOk.Value)
                .Should().Contain("GL-JRN-").And.Contain("OUTBOX_IDEMPOTENT_SYNCED").And.Contain("SUBLEDGER_TO_GL_BALANCED");

            var stocktakeController = new StocktakesController(
                new Mock<IStocktakeService>().Object,
                new Mock<IStocktakeQueryService>().Object,
                db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                    {
                        User = new System.Security.Claims.ClaimsPrincipal(
                            new System.Security.Claims.ClaimsIdentity(
                                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, ERP.Api.Authorization.AppRoles.Viewer) },
                                "Test"))
                    }
                }
            };
            var viewerProposalRes = await stocktakeController.CreateAdjustmentProposal(new StocktakesController.CreateAdjustmentProposalRequest(1));
            viewerProposalRes.Should().BeOfType<ForbidResult>();
        }

        [Fact]
        public async Task Wave3_Rem11_MultiWarehouseScoping_AccountSecurity_ViewerGuard_AndAnalyticsParity()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            // Seed 2 warehouses: WH 1 (accessible) and WH 2 (restricted)
            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock { Id = 1, WarehouseId = 1, ProductId = 10, Quantity = 50m, ReservedQuantity = 0m, Status = ERP.Domain.Enums.InventoryStatus.Available },
                new ERP.Domain.Entities.InventoryStock { Id = 2, WarehouseId = 2, ProductId = 10, Quantity = 80m, ReservedQuantity = 0m, Status = ERP.Domain.Enums.InventoryStatus.Available });

            var role = new ERP.Domain.Entities.Role { Id = 1, RoleName = "Manager" };
            var user = new ERP.Domain.Entities.User
            {
                Id = 77,
                Username = "manager77",
                FullName = "Manager 77",
                Email = "m77@test.local",
                PasswordHash = "old-hash",
                RoleId = 1,
                Role = role,
                IsActive = true,
                FailedLoginCount = 3,
                SecurityRevision = 2
            };
            db.Roles.Add(role);
            db.Users.Add(user);
            db.UserSessions.Add(new ERP.Domain.Entities.UserSession
            {
                Id = Guid.NewGuid(),
                UserId = 77,
                RefreshTokenHash = "tok-1",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                ExpiresAt = DateTime.UtcNow.AddHours(2)
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new int[] { 1 });

            // 1. W3-CTL-01: InventoryControlController scopes to accessibleIds when warehouseId is null
            var invControl = new InventoryControlController(
                new Mock<IInventoryLockService>().Object,
                new Mock<IInventoryMovementService>().Object,
                db,
                warehouseAuth.Object);
            var evalRes = await invControl.EvaluateDecisionPolicyGovernance(null);
            var evalJson = System.Text.Json.JsonSerializer.Serialize(evalRes.Should().BeOfType<OkObjectResult>().Subject.Value);
            evalJson.Should().Contain("\"evaluatedStockBucketsCount\":1");

            // 2. W3-CTL-02 & W3-CTL-03: AccountSecurityController Lock & ResetPassword
            var secController = new AccountSecurityController(
                new Mock<IAccountAdminService>().Object,
                new Mock<IUserSessionService>().Object,
                db);
            var lockRes = await secController.Lock(77, null);
            lockRes.Should().BeOfType<OkObjectResult>();
            var lockedUser = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Users, u => u.Id == 77);
            lockedUser.LockoutEnd.Should().NotBeNull();
            lockedUser.SecurityRevision.Should().Be(3);
            var revokedSession = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.UserSessions, s => s.UserId == 77);
            revokedSession.RevokedAt.Should().NotBeNull();

            var shortPwAct = async () => await secController.ResetPassword(77, new ResetPasswordAdminRequest("short"));
            await shortPwAct.Should().ThrowAsync<ERP.Application.Exceptions.BusinessRuleException>();

            var resetRes = await secController.ResetPassword(77, new ResetPasswordAdminRequest("NewSecurePass#2026", "Admin reset"));
            resetRes.Should().BeOfType<OkObjectResult>();
            lockedUser.PasswordHash.Should().NotBe("old-hash");
            lockedUser.FailedLoginCount.Should().Be(0);
            lockedUser.LockoutEnd.Should().BeNull();
            lockedUser.SecurityRevision.Should().Be(4);

            // 3. W3-FE-02: VerifySodIsolation includes status, sodConflictCount, multiWarehouseIsolationEnforced
            var uwController = new UserWarehouseAccessController(new Mock<IUserWarehouseAccessService>().Object, db, warehouseAuth.Object);
            var sodRes = await uwController.VerifySodIsolation(null);
            var sodJson = System.Text.Json.JsonSerializer.Serialize(sodRes.Should().BeOfType<OkObjectResult>().Subject.Value);
            sodJson.Should().Contain("ZERO_PRIVILEGE_ESCALATION_VERIFIED")
                .And.Contain("sodConflictCount")
                .And.Contain("multiWarehouseIsolationEnforced");

            // 4. W3-CTL-05: Viewer role receives ForbidResult on command-named POST endpoints
            var viewerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                            new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, ERP.Api.Authorization.AppRoles.Viewer) },
                            "Test"))
                }
            };
            var importController = new ImportReceiptsController(new Mock<IImportReceiptService>().Object, null, db, warehouseAuth.Object)
            {
                ControllerContext = viewerContext
            };
            (await importController.RmaIntakeCheck(null)).Should().BeOfType<ForbidResult>();
            (await importController.ExecuteRtvRefurbish(null)).Should().BeOfType<ForbidResult>();
        }

        [Fact]
        public async Task Wave14_GetInventoryInOutStock_AfterPutawayMove_ExcludesIntraWarehouseMoveFromInOutTotals()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var unit = new ERP.Domain.Entities.Unit { Id = 1, Code = "PCS", Name = "Cái", IsActive = true };
            var warehouse = new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
            var receivingBin = new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Warehouse = warehouse,
                Code = "RECEIVING",
                Name = "Vị trí nhận hàng",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Receiving,
                IsActive = true,
                IsBlocked = false,
                IsReceivable = true,
                IsPickable = false,
                IsSystemManaged = true,
                RowVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 1 }
            };
            var storageBin = new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 20,
                WarehouseId = 1,
                Warehouse = warehouse,
                Code = "A-01-01-01-01",
                Name = "Kệ lưu trữ A01",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsBlocked = false,
                IsReceivable = false,
                IsPickable = true,
                IsSystemManaged = false,
                RowVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 2 }
            };
            var product = new ERP.Domain.Entities.Product
            {
                Id = 100,
                Code = "SKU-PUTAWAY-10",
                Name = "Sản phẩm kiểm thử Putaway",
                UnitId = 1,
                Unit = unit,
                IsActive = true
            };

            var receipt = new ERP.Domain.Entities.ImportReceipt
            {
                Id = 500,
                Code = "PNK-PUTAWAY-001",
                WarehouseId = 1,
                Warehouse = warehouse,
                Status = ERP.Domain.Enums.ReceiptStatus.Posted,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5)
            };
            var receiptLine = new ERP.Domain.Entities.ImportReceiptDetail
            {
                Id = 501,
                ImportReceiptId = 500,
                ImportReceipt = receipt,
                ProductId = 100,
                Product = product,
                Quantity = 10m,
                AcceptedQuantity = 10m,
                BaseAcceptedQuantity = 10m,
                OperationUnitId = 1,
                OperationUnitCodeSnapshot = "PCS",
                BaseUnitId = 1,
                BaseUnitCodeSnapshot = "PCS",
                ConversionFactor = 1m,
                ConversionVersion = 1,
                BaseUnitDecimalPlaces = 0
            };
            receipt.Details.Add(receiptLine);

            // Step 1: Inbound 10 units into RECEIVING
            db.Units.Add(unit);
            db.Warehouses.Add(warehouse);
            db.WarehouseLocations.AddRange(receivingBin, storageBin);
            db.Products.Add(product);
            db.ImportReceipts.Add(receipt);
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                Warehouse = warehouse,
                LocationId = receivingBin.Id,
                Location = receivingBin,
                ProductId = 100,
                Product = product,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 10m,
                ReservedQuantity = 0m,
                LastUpdated = DateTime.UtcNow.AddMinutes(-5)
            });
            db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
            {
                ProductId = 100,
                Product = product,
                WarehouseId = 1,
                Warehouse = warehouse,
                LocationId = receivingBin.Id,
                InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                TransactionType = ERP.Domain.Enums.TransactionType.Import,
                Quantity = 10m,
                ReferenceId = receipt.Id,
                ReferenceType = "ImportReceipt",
                TransactionDate = DateTime.UtcNow.AddMinutes(-5),
                CreatedBy = 1
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new int[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(1);
            currentUser.SetupGet(x => x.Role).Returns("Admin");
            currentUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);

            // Step 2: Create PutawayTask and execute PutawayService.MoveAsync(10 units RECEIVING -> Storage Bin)
            var putawayService = new ERP.Infrastructure.Services.PutawayService(db, warehouseAuth.Object, currentUser.Object);
            await putawayService.CreateForPostedReceiptAsync(receipt, receivingBin.Id, 1);

            var taskEntity = await db.PutawayTasks.Include(t => t.Items).SingleAsync();
            taskEntity.RowVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 10 };
            taskEntity.Status = ERP.Domain.Enums.PutawayTaskStatus.Assigned;
            taskEntity.AssignedUserId = 1;
            foreach (var item in taskEntity.Items)
            {
                item.RowVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 11 };
            }
            await db.SaveChangesAsync();

            var movedTask = await putawayService.MoveAsync(
                taskEntity.Id,
                new MovePutawayItemDto
                {
                    ItemId = taskEntity.Items.Single().Id,
                    DestinationLocationId = storageBin.Id,
                    DestinationLocationBarcode = storageBin.Code,
                    ProductBarcode = product.Code,
                    Quantity = 10m,
                    UnitCode = "PCS",
                    RowVersion = Convert.ToBase64String(taskEntity.RowVersion)
                });
            movedTask.Status.Should().Be("Completed");

            // Confirm TransactionType.Move was written to InventoryTransactions
            var txTypes = await db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == 1 && t.ProductId == 100)
                .Select(t => t.TransactionType)
                .ToListAsync();
            txTypes.Should().BeEquivalentTo(new[]
            {
                ERP.Domain.Enums.TransactionType.Import,
                ERP.Domain.Enums.TransactionType.Move
            });

            // Step 3: Verify ReportsController.GetInventoryInOutStock & GetInventoryInOutReport report TotalIn = 10, TotalOut = 0, ClosingStock = 10
            var controllerWithDb = new ReportsController(_mockService.Object, db, warehouseAuth.Object);

            var stockRes = await controllerWithDb.GetInventoryInOutStock(null, null, 1, 100);
            var stockOk = stockRes.Should().BeOfType<OkObjectResult>().Subject;
            var rows = stockOk.Value.Should().BeAssignableTo<IReadOnlyList<InventoryInOutReportDto>>().Subject;
            rows.Should().ContainSingle();
            var row = rows[0];
            row.ImportQuantity.Should().Be(10m);
            row.InQuantity.Should().Be(10m);      // TotalIn = 10 (NOT 20)
            row.ExportQuantity.Should().Be(0m);
            row.OutQuantity.Should().Be(0m);      // TotalOut = 0 (NOT 10)
            row.ClosingQuantity.Should().Be(10m); // ClosingStock = 10

            var reportRes = await controllerWithDb.GetInventoryInOutReport(null, null, 1, 100);
            var reportRows = reportRes.Should().BeOfType<OkObjectResult>().Subject
                .Value.Should().BeAssignableTo<IReadOnlyList<InventoryInOutReportDto>>().Subject;
            reportRows.Should().ContainSingle();
            reportRows[0].ImportQuantity.Should().Be(10m);
            reportRows[0].InQuantity.Should().Be(10m);
            reportRows[0].ExportQuantity.Should().Be(0m);
            reportRows[0].OutQuantity.Should().Be(0m);
            reportRows[0].ClosingQuantity.Should().Be(10m);

            // Step 3b: Verify ExportInventoryInOutReport (Excel export) writes InQuantity = 10, OutQuantity = 0, ClosingQuantity = 10
            var exportRes = await controllerWithDb.ExportInventoryInOutReport(null, null, 1, 100);
            var exportFile = exportRes.Should().BeOfType<FileContentResult>().Subject;
            exportFile.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            using (var exportStream = new System.IO.MemoryStream(exportFile.FileContents))
            using (var exportWorkbook = new XLWorkbook(exportStream))
            {
                var sheet = exportWorkbook.Worksheet("Báo cáo Xuất nhập tồn");
                sheet.Should().NotBeNull();
                sheet.Cell(2, 1).GetString().Should().Be("SKU-PUTAWAY-10");
                sheet.Cell(2, 5).GetValue<decimal>().Should().Be(0m);  // OpeningQuantity
                sheet.Cell(2, 6).GetValue<decimal>().Should().Be(10m); // ImportQuantity
                sheet.Cell(2, 9).GetValue<decimal>().Should().Be(10m); // InQuantity (Tổng tăng)
                sheet.Cell(2, 10).GetValue<decimal>().Should().Be(0m); // ExportQuantity
                sheet.Cell(2, 13).GetValue<decimal>().Should().Be(0m); // OutQuantity (Tổng giảm)
                sheet.Cell(2, 14).GetValue<decimal>().Should().Be(10m); // ClosingQuantity (Tồn cuối kỳ)
            }

            // Step 3c: Verify GetAnalyticsSummary excludes TransactionType.Move from periodInByProduct and issuedByProduct
            var analyticsRes = await controllerWithDb.GetAnalyticsSummary(db, warehouseAuth.Object, 1, 30);
            var analyticsOk = analyticsRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var analyticsDoc = System.Text.Json.JsonSerializer.SerializeToDocument(analyticsOk.Value))
            {
                var turnoverItems = analyticsDoc.RootElement.GetProperty("turnoverItems");
                turnoverItems.GetArrayLength().Should().Be(1);
                var item = turnoverItems[0];
                item.GetProperty("productId").GetInt32().Should().Be(100);
                item.GetProperty("productCode").GetString().Should().Be("SKU-PUTAWAY-10");
                item.GetProperty("currentOnHand").GetDecimal().Should().Be(10m);
                item.GetProperty("periodInQuantity").GetDecimal().Should().Be(10m);
                item.GetProperty("issuedLastPeriod").GetDecimal().Should().Be(0m);
                item.GetProperty("turnoverRatio").GetDecimal().Should().Be(0m);
                item.GetProperty("movementClass").GetString().Should().Be("NON_MOVING");
            }

            // Step 4: Verify InventoryReconciliationQueryService also excludes TransactionType.Move
            var reconService = new ERP.Infrastructure.Queries.InventoryReconciliationQueryService(db, warehouseAuth.Object);
            var reconPage = await reconService.GetReconciliationsAsync(1, 100, null);
            reconPage.Items.Should().ContainSingle();
            reconPage.Items[0].ImportQuantity.Should().Be(10m);
            reconPage.Items[0].ExportQuantity.Should().Be(0m);
            reconPage.Items[0].CurrentQuantity.Should().Be(10m);
            reconPage.Items[0].ExpectedQuantity.Should().Be(10m);
            reconPage.Items[0].Difference.Should().Be(0m);
            reconPage.Items[0].Status.Should().Be("Match");
        }

        [Fact]
        public async Task Wave15_ExportInventoryInOutReport_And_GetAnalyticsSummary_ExcludeIntraWarehouseMovesAcrossJsonExcelAndTurnover()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var unit = new ERP.Domain.Entities.Unit { Id = 1, Code = "PCS", Name = "Cái", IsActive = true };
            var warehouse = new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH-01", Name = "Kho Chính", IsActive = true };
            var receivingBin = new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Warehouse = warehouse,
                Code = "RECEIVING",
                Name = "Vị trí nhận hàng",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Receiving,
                IsActive = true,
                IsBlocked = false,
                IsReceivable = true,
                IsPickable = false,
                IsSystemManaged = true,
                RowVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 1 }
            };
            var storageBin = new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 20,
                WarehouseId = 1,
                Warehouse = warehouse,
                Code = "A-01-01-01-01",
                Name = "Kệ lưu trữ A01",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsBlocked = false,
                IsReceivable = false,
                IsPickable = true,
                IsSystemManaged = false,
                RowVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 2 }
            };
            var product = new ERP.Domain.Entities.Product
            {
                Id = 100,
                Code = "SKU-W15-10",
                Name = "Sản phẩm kiểm thử Wave 15",
                UnitId = 1,
                Unit = unit,
                IsActive = true
            };

            var receipt = new ERP.Domain.Entities.ImportReceipt
            {
                Id = 600,
                Code = "PNK-W15-001",
                WarehouseId = 1,
                Warehouse = warehouse,
                Status = ERP.Domain.Enums.ReceiptStatus.Posted,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow.AddMinutes(-10)
            };
            receipt.Details.Add(new ERP.Domain.Entities.ImportReceiptDetail
            {
                Id = 601,
                ImportReceiptId = 600,
                ImportReceipt = receipt,
                ProductId = 100,
                Product = product,
                Quantity = 10m,
                AcceptedQuantity = 10m,
                BaseAcceptedQuantity = 10m,
                OperationUnitId = 1,
                OperationUnitCodeSnapshot = "PCS",
                BaseUnitId = 1,
                BaseUnitCodeSnapshot = "PCS",
                ConversionFactor = 1m,
                ConversionVersion = 1,
                BaseUnitDecimalPlaces = 0
            });

            db.Units.Add(unit);
            db.Warehouses.Add(warehouse);
            db.WarehouseLocations.AddRange(receivingBin, storageBin);
            db.Products.Add(product);
            db.ImportReceipts.Add(receipt);
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                Warehouse = warehouse,
                LocationId = receivingBin.Id,
                Location = receivingBin,
                ProductId = 100,
                Product = product,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 10m,
                ReservedQuantity = 0m,
                LastUpdated = DateTime.UtcNow.AddMinutes(-10)
            });
            db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
            {
                ProductId = 100,
                Product = product,
                WarehouseId = 1,
                Warehouse = warehouse,
                LocationId = receivingBin.Id,
                InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                TransactionType = ERP.Domain.Enums.TransactionType.Import,
                Quantity = 10m,
                ReferenceId = receipt.Id,
                ReferenceType = "ImportReceipt",
                TransactionDate = DateTime.UtcNow.AddMinutes(-10),
                CreatedBy = 1
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new int[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(1);
            currentUser.SetupGet(x => x.Role).Returns("Admin");
            currentUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);

            var putawayService = new ERP.Infrastructure.Services.PutawayService(db, warehouseAuth.Object, currentUser.Object);
            await putawayService.CreateForPostedReceiptAsync(receipt, receivingBin.Id, 1);

            var taskEntity = await db.PutawayTasks.Include(t => t.Items).SingleAsync();
            taskEntity.RowVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 20 };
            taskEntity.Status = ERP.Domain.Enums.PutawayTaskStatus.Assigned;
            taskEntity.AssignedUserId = 1;
            foreach (var item in taskEntity.Items)
            {
                item.RowVersion = new byte[] { 1, 0, 0, 0, 0, 0, 0, 21 };
            }
            await db.SaveChangesAsync();

            await putawayService.MoveAsync(
                taskEntity.Id,
                new MovePutawayItemDto
                {
                    ItemId = taskEntity.Items.Single().Id,
                    DestinationLocationId = storageBin.Id,
                    DestinationLocationBarcode = storageBin.Code,
                    ProductBarcode = product.Code,
                    Quantity = 10m,
                    UnitCode = "PCS",
                    RowVersion = Convert.ToBase64String(taskEntity.RowVersion)
                });

            var controller = new ReportsController(_mockService.Object, db, warehouseAuth.Object);

            // 1. JSON payload: GetInventoryInOutReport
            var jsonResult = await controller.GetInventoryInOutReport(null, null, 1, 100);
            var jsonRows = jsonResult.Should().BeOfType<OkObjectResult>().Subject
                .Value.Should().BeAssignableTo<IReadOnlyList<InventoryInOutReportDto>>().Subject;
            jsonRows.Should().ContainSingle();
            jsonRows[0].InQuantity.Should().Be(10m);
            jsonRows[0].OutQuantity.Should().Be(0m);
            jsonRows[0].ClosingQuantity.Should().Be(10m);

            // 2. Excel export: ExportInventoryInOutReport
            var excelResult = await controller.ExportInventoryInOutReport(null, null, 1, 100);
            var excelFile = excelResult.Should().BeOfType<FileContentResult>().Subject;
            using (var stream = new System.IO.MemoryStream(excelFile.FileContents))
            using (var workbook = new XLWorkbook(stream))
            {
                var worksheet = workbook.Worksheet("Báo cáo Xuất nhập tồn");
                worksheet.Cell(2, 1).GetString().Should().Be("SKU-W15-10");
                worksheet.Cell(2, 6).GetValue<decimal>().Should().Be(10m);  // Nhập kho
                worksheet.Cell(2, 9).GetValue<decimal>().Should().Be(10m);  // Tổng tăng (InQuantity)
                worksheet.Cell(2, 10).GetValue<decimal>().Should().Be(0m);  // Xuất kho
                worksheet.Cell(2, 13).GetValue<decimal>().Should().Be(0m);  // Tổng giảm (OutQuantity)
                worksheet.Cell(2, 14).GetValue<decimal>().Should().Be(10m); // Tồn cuối kỳ (ClosingQuantity)
            }

            // 3. Dashboard Analytics: GetAnalyticsSummary
            var analyticsResult = await controller.GetAnalyticsSummary(db, warehouseAuth.Object, 1, 30);
            var analyticsOk = analyticsResult.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(analyticsOk.Value))
            {
                var root = doc.RootElement;
                root.GetProperty("inboundPostedCount").GetInt32().Should().Be(1);
                root.GetProperty("putawayCompletedCount").GetInt32().Should().Be(1);

                var turnover = root.GetProperty("turnoverItems");
                turnover.GetArrayLength().Should().Be(1);
                var row = turnover[0];
                row.GetProperty("productId").GetInt32().Should().Be(100);
                row.GetProperty("currentOnHand").GetDecimal().Should().Be(10m);
                row.GetProperty("periodInQuantity").GetDecimal().Should().Be(10m);
                row.GetProperty("issuedLastPeriod").GetDecimal().Should().Be(0m);
                row.GetProperty("turnoverRatio").GetDecimal().Should().Be(0m);
                row.GetProperty("daysOnHand").GetDecimal().Should().Be(999m);
                row.GetProperty("movementClass").GetString().Should().Be("NON_MOVING");
            }
        }

        [Fact]
        public async Task Wave28_OperationalDashboard_FiltersSessionRefreshNoise_AndTranslatesVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH-01", Name = "Kho trung tâm", IsActive = true });
            var now = DateTime.UtcNow;

            for (var i = 0; i < 9; i++)
            {
                db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    EntityName = "UserSession",
                    Action = "Auth.Refresh",
                    Result = "Success",
                    Timestamp = now.AddMinutes(-i)
                });
            }

            db.AuditLogs.AddRange(
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "ImportReceipt", Action = "ImportReceipt.Posted", Result = "Success", Timestamp = now.AddMinutes(-10) },
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "PickingTask", Action = "PickingTask.Completed", Result = "Success", Timestamp = now.AddMinutes(-11) },
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "PackingSession", Action = "Packing.Completed", Result = "Success", Timestamp = now.AddMinutes(-12) },
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "Shipment", Action = "Shipment.Dispatched", Result = "Success", Timestamp = now.AddMinutes(-13) },
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "PutawayTask", Action = "Putaway.Moved", Result = "Success", Timestamp = now.AddMinutes(-14) },
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "InventoryLock", Action = "InventoryLock.Created", Result = "Success", Timestamp = now.AddMinutes(-15) },
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "StocktakeAdjustmentProposal", Action = "COUNT_VARIANCE", Result = "Approved", Timestamp = now.AddMinutes(-16) },
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "RtvRefurbish", Action = "ImportReceipt.ExecuteRtvRefurbish", Result = "Success", Timestamp = now.AddMinutes(-17) },
                new ERP.Domain.Entities.AuditLog { WarehouseId = 1, EntityName = "CrossdockDispatch", Action = "DockYard.DispatchCrossdock", Result = "CROSSDOCK_ROUTED", Timestamp = now.AddMinutes(-18) }
            );
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });

            var controller = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var response = await controller.GetOperationalDashboard(db, warehouseAuth.Object);
            var ok = response.Should().BeOfType<OkObjectResult>().Subject;

            using var doc = System.Text.Json.JsonSerializer.SerializeToDocument(ok.Value);
            var activities = doc.RootElement.GetProperty("recentActivities");
            activities.GetArrayLength().Should().Be(9);

            var actions = new List<string>();
            for (var i = 0; i < activities.GetArrayLength(); i++)
            {
                actions.Add(activities[i].GetProperty("Action").GetString()!);
            }

            actions.Should().NotContain("Làm mới phiên đăng nhập");
            actions.Should().Contain(new[]
            {
                "Ghi sổ phiếu nhập",
                "Hoàn tất lấy hàng",
                "Đóng gói kiện hàng",
                "Xuất giao chuyến hàng",
                "Di chuyển vị trí nội bộ",
                "Phong tỏa lô hàng",
                "Phê duyệt điều chỉnh kiểm kê",
                "Xử lý hàng hỏng / trả NCC",
                "Điều phối trung chuyển thẳng"
            });
        }

        [Fact]
        public async Task Wave28_StocktakeAdjustmentProposal_Approve_MutatesStockAndAppendsLedgerAndAuditLog()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH-01", Name = "Kho 1", IsActive = true });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation { Id = 10, WarehouseId = 1, Code = "A-01-01-01-01", Name = "Kệ A1", IsActive = true, LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage });
            db.Products.Add(new ERP.Domain.Entities.Product { Id = 100, Code = "SKU-ADJ-01", Name = "Sản phẩm điều chỉnh", UnitId = 1 });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 500,
                WarehouseId = 1,
                ProductId = 100,
                LocationId = 10,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 20m,
                ReservedQuantity = 2m
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });

            var stocktakeService = new Mock<IStocktakeService>();
            var stocktakeQuery = new Mock<IStocktakeQueryService>();
            var controller = new StocktakesController(stocktakeService.Object, stocktakeQuery.Object, db, warehouseAuth.Object);

            var createRes = await controller.CreateAdjustmentProposal(new StocktakesController.CreateAdjustmentProposalRequest(
                WarehouseId: 1,
                ProductCode: "SKU-ADJ-01",
                DeltaQuantity: -5m,
                ReasonCode: "COUNT_VARIANCE",
                Note: "Kiểm đếm thiếu 5 thùng",
                ProductId: 100,
                LocationId: 10));
            var createOk = createRes.Should().BeOfType<OkObjectResult>().Subject;
            using var createDoc = System.Text.Json.JsonSerializer.SerializeToDocument(createOk.Value);
            var proposalAuditId = createDoc.RootElement.GetProperty("proposalAuditId").GetInt64();

            var approveRes = await controller.ApproveAdjustmentProposal(proposalAuditId, new StocktakesController.ApproveAdjustmentProposalRequest(LocationId: 10));
            var approveOk = approveRes.Should().BeOfType<OkObjectResult>().Subject;
            using var approveDoc = System.Text.Json.JsonSerializer.SerializeToDocument(approveOk.Value);
            approveDoc.RootElement.GetProperty("status").GetString().Should().Be("Đã phê duyệt & Ghi sổ");
            approveDoc.RootElement.GetProperty("resultingQuantity").GetDecimal().Should().Be(15m);

            var updatedStock = await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.Id == 500);
            updatedStock.Quantity.Should().Be(15m);

            var ledgerRow = await db.InventoryTransactions.AsNoTracking().SingleAsync();
            ledgerRow.TransactionType.Should().Be(ERP.Domain.Enums.TransactionType.AdjustmentDecrease);
            ledgerRow.Quantity.Should().Be(5m);
            ledgerRow.ReferenceType.Should().Be("StocktakeAdjustment");

            var listRes = await controller.GetReasonCodesAndAdjustments(1);
            var listOk = listRes.Should().BeOfType<OkObjectResult>().Subject;
            using var listDoc = System.Text.Json.JsonSerializer.SerializeToDocument(listOk.Value);
            var proposals = listDoc.RootElement.GetProperty("recentProposals");
            proposals.GetArrayLength().Should().Be(1);
            proposals[0].GetProperty("status").GetString().Should().Be("Đã phê duyệt & Ghi sổ");
        }

        [Fact]
        public async Task Wave28_ImportReceipts_ExecuteRtvRefurbish_MutatesBucketsAndAppendsLedger()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH-01", Name = "Kho 1", IsActive = true });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 600,
                WarehouseId = 1,
                ProductId = 200,
                LocationId = 20,
                Status = ERP.Domain.Enums.InventoryStatus.Damaged,
                Quantity = 8m,
                ReservedQuantity = 0m
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            var importService = new Mock<IImportReceiptService>();
            var controller = new ImportReceiptsController(importService.Object, null, db, warehouseAuth.Object);

            // 1. RTV write-off 3 units from Damaged bucket
            var rtvRes = await controller.ExecuteRtvRefurbish(new ImportReceiptsController.ExecuteRtvRefurbishRequest(
                WarehouseId: 1,
                DispositionType: "RTV",
                ProductId: 200,
                LocationId: 20,
                ExecuteStockMutation: true,
                ActionType: "RTV",
                Quantity: 3m));
            var rtvOk = rtvRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(rtvOk.Value))
            {
                doc.RootElement.GetProperty("status").GetString().Should().Be("Đã thực thi & Ghi sổ kiểm toán");
                doc.RootElement.GetProperty("ledgerUpdated").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("resultingStockQuantity").GetDecimal().Should().Be(5m);
            }

            // 2. Refurbish remaining 5 units to Available
            var refRes = await controller.ExecuteRtvRefurbish(new ImportReceiptsController.ExecuteRtvRefurbishRequest(
                WarehouseId: 1,
                DispositionType: "REFURBISH",
                ProductId: 200,
                LocationId: 20,
                ExecuteStockMutation: true,
                ActionType: "REFURBISH",
                Quantity: 5m));
            var refOk = refRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(refOk.Value))
            {
                doc.RootElement.GetProperty("status").GetString().Should().Be("Đã thực thi & Ghi sổ kiểm toán");
                doc.RootElement.GetProperty("ledgerUpdated").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("resultingStatus").GetString().Should().Be("Available");
                doc.RootElement.GetProperty("resultingStockQuantity").GetDecimal().Should().Be(5m);
            }

            var updatedBucket = await db.InventoryStocks.AsNoTracking().SingleAsync(s => s.Id == 600);
            updatedBucket.Status.Should().Be(ERP.Domain.Enums.InventoryStatus.Available);
            updatedBucket.Quantity.Should().Be(5m);
        }

        [Fact]
        public async Task Wave28_DockYard_CrossdockCandidatesAndDispatch_PairsOutboundOrdersWithVietnameseStatus()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.ImportReceipts.Add(new ERP.Domain.Entities.ImportReceipt { Id = 1, WarehouseId = 1, Code = "PNK-XD-01", Status = ERP.Domain.Enums.ReceiptStatus.Draft });
            db.SalesOrders.Add(new ERP.Domain.Entities.SalesOrder { Id = 1, WarehouseId = 1, OrderCode = "SO-XD-99", CustomerId = 1, Status = ERP.Domain.Enums.SalesOrderStatus.Released, Priority = 1 });
            await db.SaveChangesAsync();

            var dockService = new Mock<IDockYardService>();
            dockService.Setup(x => x.GetAppointmentsAsync(It.IsAny<DockAppointmentQueryDto>(), default))
                .ReturnsAsync(Array.Empty<DockAppointmentDto>());
            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });

            var controller = new DockYardController(dockService.Object, db, warehouseAuth.Object);

            var candRes = await controller.GetCrossDockCandidates(1, default);
            var candOk = candRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(candOk.Value))
            {
                doc.RootElement.GetProperty("inboundAppointmentCode").GetString().Should().Be("PNK-XD-01");
                doc.RootElement.GetProperty("outboundReferenceCode").GetString().Should().Be("SO-XD-99");
                doc.RootElement.GetProperty("dispatchStatus").GetString().Should().Be("Đã phát lệnh điều phối trung chuyển thẳng");
                doc.RootElement.GetProperty("priorityLabel").GetString().Should().Be("Ưu tiên cao");
            }

            var dispRes = await controller.DispatchCrossdock(new DispatchCrossdockRequest(WarehouseId: 1, DockDoorCode: "DOCK-XD-02"), default);
            var dispOk = dispRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(dispOk.Value))
            {
                doc.RootElement.GetProperty("inboundAppointmentCode").GetString().Should().Be("PNK-XD-01");
                doc.RootElement.GetProperty("outboundReferenceCode").GetString().Should().Be("SO-XD-99");
                doc.RootElement.GetProperty("dispatchStatus").GetString().Should().Be("Đã phát lệnh điều phối trung chuyển thẳng");
                doc.RootElement.GetProperty("priorityLabel").GetString().Should().Be("Ưu tiên cao");
            }
        }

        [Fact]
        public async Task Wave29_OperationalDashboard_EnrichesWarehouseNameAndReferenceCodes_AndNullsEntityIdForFormattedSubtitle()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true });
            var now = DateTime.UtcNow;

            db.AuditLogs.AddRange(
                new ERP.Domain.Entities.AuditLog
                {
                    WarehouseId = 1,
                    EntityName = "ImportReceipt",
                    EntityId = 1,
                    Action = "ImportReceipt.Posted",
                    Reason = "IR-QA-001 — Ghi sổ phiếu nhập kho lô hàng QA",
                    Result = "Success",
                    Timestamp = now.AddMinutes(-1)
                },
                new ERP.Domain.Entities.AuditLog
                {
                    WarehouseId = 1,
                    EntityName = "StocktakeSession",
                    EntityId = 1,
                    Action = "Stocktake.Approve",
                    Reason = "Phê duyệt phiên kiểm kê định kỳ",
                    Result = "Success",
                    Timestamp = now.AddMinutes(-2)
                }
            );
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });

            var controller = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var response = await controller.GetOperationalDashboard(db, warehouseAuth.Object);
            var ok = response.Should().BeOfType<OkObjectResult>().Subject;

            using var doc = System.Text.Json.JsonSerializer.SerializeToDocument(ok.Value);
            var activities = doc.RootElement.GetProperty("recentActivities");
            activities.GetArrayLength().Should().Be(2);

            var first = activities[0];
            first.GetProperty("WarehouseName").GetString().Should().Be("WH01 — Kho Chính TP.HCM");
            first.GetProperty("ReferenceCode").GetString().Should().Be("IR-QA-001");
            first.GetProperty("EntityName").GetString().Should().Be("Phiếu nhập kho (IR-QA-001)");
            first.GetProperty("EntityId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
            first.GetProperty("OriginalEntityId").GetInt32().Should().Be(1);

            var second = activities[1];
            second.GetProperty("WarehouseName").GetString().Should().Be("WH01 — Kho Chính TP.HCM");
            second.GetProperty("ReferenceCode").GetString().Should().Be("STK-QA-001");
            second.GetProperty("EntityName").GetString().Should().Be("Phiếu kiểm kê (STK-QA-001)");
            second.GetProperty("EntityId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        }

        [Fact]
        public async Task Wave29_MasterDataVerificationEndpoints_ReturnRealDatabaseMetrics_AndVietnameseStatusLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Chính TP.HCM", IsActive = true });
            db.WarehouseLocations.AddRange(
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 1,
                    WarehouseId = 1,
                    Code = "RECEIVING",
                    Name = "Khu tiếp nhận",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Receiving,
                    IsActive = true,
                    IsBlocked = false,
                    MaxWeightKg = 2000m,
                    MaxVolumeM3 = 15m,
                    MaxPalletEquivalent = 10m
                },
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 2,
                    WarehouseId = 1,
                    Code = "A-01-01-01-01",
                    Name = "Kệ lưu trữ A01",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                    IsActive = true,
                    IsBlocked = false,
                    MaxWeightKg = 1000m,
                    MaxVolumeM3 = 5m,
                    MaxPalletEquivalent = 4m
                }
            );
            db.WarehouseShifts.Add(new ERP.Domain.Entities.WarehouseShift
            {
                Id = 1,
                WarehouseId = 1,
                Code = "SHIFT-MORNING",
                Name = "Ca sáng",
                PlannedHeadcount = 18,
                InboundPalletsPerHour = 45m,
                OutboundOrdersPerHour = 120m,
                DockSlots = 6,
                IsActive = true
            });
            db.Units.Add(new ERP.Domain.Entities.Unit { Id = 1, Code = "PCS", Name = "Cái", IsActive = true });
            db.Products.Add(new ERP.Domain.Entities.Product { Id = 10, Code = "SKU-01", Name = "Sản phẩm 01", UnitId = 1, IsActive = true });
            db.ProductUoms.Add(new ERP.Domain.Entities.ProductUom
            {
                Id = 1,
                ProductId = 10,
                UnitId = 1,
                ConversionFactor = 24m
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var whService = new Mock<IWarehouseService>();
            var whController = new WarehousesController(whService.Object, db, warehouseAuth.Object);

            var topRes = await whController.VerifyTopologyCapacity(new VerifyTopologyCapacityRequest(1), default);
            var topOk = topRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(topOk.Value))
            {
                doc.RootElement.GetProperty("activeBinCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("storageBinCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("totalMaxWeightKg").GetDecimal().Should().Be(3000m);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực cấu trúc 5 cấp & sức chứa vị trí");
            }

            var vlocRes = await whController.VerifyVirtualLocations(new VerifyVirtualLocationsRequest(1), default);
            var vlocOk = vlocRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(vlocOk.Value))
            {
                doc.RootElement.GetProperty("configuredVirtualBins").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("receivingLocationsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực đầy đủ các vị trí chức năng & khu vực xử lý");
            }

            var calService = new Mock<IWarehouseCalendarService>();
            var calController = new WarehouseCalendarsController(calService.Object, db, warehouseAuth.Object);
            var shiftRes = await calController.VerifyShiftCoverage(1, default);
            var shiftOk = shiftRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(shiftOk.Value))
            {
                doc.RootElement.GetProperty("configuredShiftsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("totalPlannedHeadcount").GetInt32().Should().Be(18);
                doc.RootElement.GetProperty("totalDockSlots").GetInt32().Should().Be(6);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực độ phủ ca làm việc & khung giờ chốt chuyến");
            }

            var unitService = new Mock<IUnitService>();
            var unitController = new UnitsController(unitService.Object, db);
            var uomRes = await unitController.VerifyUomIntegrity(default);
            var uomOk = uomRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(uomOk.Value))
            {
                doc.RootElement.GetProperty("activeUnitsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("productsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("conversionCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực toàn vẹn danh mục ĐVT & hệ số quy đổi");
            }
        }
    }
}