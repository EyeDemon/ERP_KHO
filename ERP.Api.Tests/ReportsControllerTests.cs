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
            using (var glDoc = System.Text.Json.JsonSerializer.SerializeToDocument(glOk.Value))
            {
                glDoc.RootElement.GetProperty("journalBatchId").GetString().Should().StartWith("GL-JRN-");
                glDoc.RootElement.GetProperty("outboxSyncStatus").GetString().Should().Contain("Đã đồng bộ Outbox chống trùng lặp giao dịch");
                glDoc.RootElement.GetProperty("glReconciliationStatus").GetString().Should().Contain("Cân bằng 100% giữa Sổ cái Kho & Tồn vật lý");
            }

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

        [Fact]
        public async Task Wave30_InboundProcurementAndAsnStagingEndpoints_ComputeRealDatabaseMetrics_AndReturnVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 11,
                WarehouseId = 1,
                Code = "RCV-01",
                Name = "Khu tiếp nhận cổng 1",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Receiving,
                IsActive = true,
                IsBlocked = false
            });
            db.Products.AddRange(
                new ERP.Domain.Entities.Product { Id = 101, Code = "SKU-W30-01", Name = "Sản phẩm 01", UnitId = 1, IsActive = true },
                new ERP.Domain.Entities.Product { Id = 102, Code = "SKU-W30-02", Name = "Sản phẩm 02", UnitId = 1, IsActive = true }
            );
            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 1,
                    WarehouseId = 1,
                    ProductId = 101,
                    LocationId = 11,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 120m,
                    ReservedQuantity = 20m
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 2,
                    WarehouseId = 1,
                    ProductId = 102,
                    LocationId = 11,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 15m,
                    ReservedQuantity = 5m
                }
            );
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var inboundService = new Mock<IInboundPlanningService>();
            inboundService.Setup(x => x.ListPurchaseOrdersAsync(null, default)).ReturnsAsync(new List<PurchaseOrderListDto>
            {
                new()
                {
                    Id = 1,
                    Code = "PO-QA-001",
                    WarehouseId = 1,
                    WarehouseCode = "QA-HCM",
                    WarehouseName = "Kho Chính TP.HCM",
                    Status = "Open",
                    ExpectedDate = DateTime.UtcNow.AddDays(-2),
                    BaseOrderedQuantity = 50m
                },
                new()
                {
                    Id = 2,
                    Code = "PO-QA-002",
                    WarehouseId = 1,
                    WarehouseCode = "QA-HCM",
                    WarehouseName = "Kho Chính TP.HCM",
                    Status = "PartiallyReceived",
                    ExpectedDate = DateTime.UtcNow.AddDays(3),
                    BaseOrderedQuantity = 30m
                }
            });
            inboundService.Setup(x => x.ListAsnsAsync(null, default)).ReturnsAsync(new List<AsnListDto>
            {
                new()
                {
                    Id = 10,
                    Code = "ASN-QA-001",
                    WarehouseId = 1,
                    WarehouseCode = "QA-HCM",
                    WarehouseName = "Kho Chính TP.HCM",
                    Status = "InTransit",
                    BaseExpectedQuantity = 50m
                }
            });

            var poController = new PurchaseOrdersController(inboundService.Object, db, warehouseAuth.Object);

            var expRes = await poController.ExpediteOverdue(new PurchaseOrdersController.ExpediteOverduePurchaseOrdersRequest(WarehouseId: 1), default);
            var expOk = expRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(expOk.Value))
            {
                doc.RootElement.GetProperty("openPoCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("overdueCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("expeditedPoCodes")[0].GetString().Should().Be("PO-QA-001");
                doc.RootElement.GetProperty("vendorNotificationStatus").GetString().Should().Be("Đã gửi thông báo đôn đốc nhà cung cấp");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã phát lệnh đôn đốc đơn mua quá hạn & ưu tiên tiếp nhận");
            }

            var recRes = await poController.GenerateProcurementRecommendations(new PurchaseOrdersController.GenerateProcurementRecommendationsRequest(WarehouseId: 1), default);
            var recOk = recRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(recOk.Value))
            {
                doc.RootElement.GetProperty("evaluatedSkuCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("totalAvailableQuantity").GetDecimal().Should().Be(110m);
                doc.RootElement.GetProperty("recommendedOrderCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("status").GetString().Should().Be("Đã hoàn tất phân tích & đề xuất mua hàng bổ sung");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã hoàn tất phân tích & đề xuất mua hàng bổ sung");
            }

            var asnController = new AsnsController(inboundService.Object, db, warehouseAuth.Object);
            var stgRes = await asnController.PreAllocateStaging(new AsnsController.PreAllocateAsnStagingRequest(WarehouseId: 1), default);
            var stgOk = stgRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(stgOk.Value))
            {
                doc.RootElement.GetProperty("preAllocatedAsnCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("asnNumber").GetString().Should().Be("ASN-QA-001");
                doc.RootElement.GetProperty("stagingLaneCode").GetString().Should().Be("RCV-01");
                doc.RootElement.GetProperty("stagingLocationName").GetString().Should().Contain("RCV-01 — Khu tiếp nhận cổng 1");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã phân bổ trước khu vực tập kết hàng nhập");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result != null && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave30_OutboundAtpBackorderAndNetworkRebalancingEndpoints_ComputeRealDatabaseMetrics_AndReturnVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.AddRange(
                new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true },
                new ERP.Domain.Entities.Warehouse { Id = 2, Code = "QA-HN", Name = "Kho Chi Nhánh Hà Nội", IsActive = true }
            );
            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 1,
                    WarehouseId = 1,
                    ProductId = 101,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 200m,
                    ReservedQuantity = 40m
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 2,
                    WarehouseId = 2,
                    ProductId = 101,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 60m,
                    ReservedQuantity = 10m
                }
            );
            db.StockTransfers.Add(new ERP.Domain.Entities.StockTransfer
            {
                Id = 1,
                Code = "TRF-QA-001",
                SourceWarehouseId = 1,
                DestinationWarehouseId = 2,
                Status = ERP.Domain.Enums.StockTransferStatus.InTransit,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1, 2 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(It.IsAny<int>(), default)).Returns(Task.CompletedTask);

            var soService = new Mock<ISalesOrderBackorderService>();
            soService.Setup(x => x.ListSalesOrdersAsync(It.IsAny<int?>(), null, default)).ReturnsAsync(new List<SalesOrderListDto>
            {
                new()
                {
                    Id = 1,
                    OrderCode = "SO-QA-001",
                    WarehouseId = 1,
                    WarehouseCode = "QA-HCM",
                    WarehouseName = "Kho Chính TP.HCM",
                    Status = "Released",
                    OrderedQuantity = 100m,
                    OpenQuantity = 80m,
                    BackorderQuantity = 20m
                }
            });

            var soController = new SalesOrdersController(soService.Object, db, warehouseAuth.Object);

            var boRes = await soController.ExpediteBackorder(new ExpediteBackorderRequest(1, null), default);
            var boOk = boRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(boOk.Value))
            {
                doc.RootElement.GetProperty("orderNumber").GetString().Should().Be("SO-QA-001");
                doc.RootElement.GetProperty("backorderOrdersCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("totalBackorderQuantity").GetDecimal().Should().Be(20m);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã đưa đơn hàng thiếu tồn vào hàng đợi phân bổ ưu tiên");
            }

            var atpRes = await soController.EvaluateAtpRouting(new EvaluateAtpRoutingRequest(1), default);
            var atpOk = atpRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(atpOk.Value))
            {
                doc.RootElement.GetProperty("evaluatedOrdersCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("totalAtpQuantity").GetDecimal().Should().Be(160m);
                doc.RootElement.GetProperty("totalDemandQuantity").GetDecimal().Should().Be(80m);
                doc.RootElement.GetProperty("optimalWarehouseNode").GetString().Should().Be("QA-HCM — Kho Chính TP.HCM");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã hoàn tất đánh giá khả năng cung ứng (ATP) & định tuyến kho xuất");
            }

            var stService = new Mock<IStockTransferService>();
            var stController = new StockTransfersController(stService.Object, db, warehouseAuth.Object);
            var simRes = await stController.SimulateNetworkRebalancing(new StockTransfersController.SimulateNetworkRebalancingRequest(), default);
            var simOk = simRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(simOk.Value))
            {
                doc.RootElement.GetProperty("inTransitTransfersCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("sourceWarehouseLabel").GetString().Should().Be("QA-HCM — Kho Chính TP.HCM");
                doc.RootElement.GetProperty("destinationWarehouseLabel").GetString().Should().Be("QA-HN — Kho Chi Nhánh Hà Nội");
                doc.RootElement.GetProperty("imbalanceSpreadQuantity").GetDecimal().Should().Be(110m);
                doc.RootElement.GetProperty("recommendedTransferQuantity").GetDecimal().Should().Be(55m);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã hoàn tất mô phỏng tái cân bằng tồn kho toàn mạng lưới");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result != null && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave31_OperationalDashboard_DeduplicatesConsecutiveLogins_AndPrioritizesWarehouseActivities()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });

            var baseTime = DateTime.UtcNow;
            // Seed 6 consecutive login events at latest timestamps
            for (var i = 1; i <= 6; i++)
            {
                db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    Id = i,
                    Action = "Auth.Login",
                    EntityName = "UserSession",
                    EntityId = i,
                    Result = "Success",
                    Timestamp = baseTime.AddSeconds(i)
                });
            }

            // Seed 4 real warehouse operational events slightly earlier
            db.AuditLogs.AddRange(
                new ERP.Domain.Entities.AuditLog
                {
                    Id = 101,
                    WarehouseId = 1,
                    Action = "Putaway.Move",
                    EntityName = "PutawayTask",
                    EntityId = 501,
                    NewValues = "PUT-QA-501",
                    Result = "Success",
                    Timestamp = baseTime.AddMinutes(-1)
                },
                new ERP.Domain.Entities.AuditLog
                {
                    Id = 102,
                    WarehouseId = 1,
                    Action = "Picking.Pick",
                    EntityName = "PickingTask",
                    EntityId = 601,
                    NewValues = "PCK-QA-601",
                    Result = "Success",
                    Timestamp = baseTime.AddMinutes(-2)
                },
                new ERP.Domain.Entities.AuditLog
                {
                    Id = 103,
                    WarehouseId = 1,
                    Action = "Packing.ExecuteKittingVas",
                    EntityName = "KittingVas",
                    EntityId = 701,
                    NewValues = "KIT-VAS-20261020-1800",
                    Result = "Success",
                    Timestamp = baseTime.AddMinutes(-3)
                },
                new ERP.Domain.Entities.AuditLog
                {
                    Id = 104,
                    WarehouseId = 1,
                    Action = "Shipment.SyncTmsManifest",
                    EntityName = "TmsManifest",
                    EntityId = 801,
                    NewValues = "TMS-MNF-20261020-1800",
                    Result = "Success",
                    Timestamp = baseTime.AddMinutes(-4)
                }
            );
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });

            var reportService = new Mock<IReportService>();
            var controller = new ReportsController(reportService.Object, db, warehouseAuth.Object);

            var result = await controller.GetOperationalDashboard(db, warehouseAuth.Object, default);
            var ok = result.Should().BeOfType<OkObjectResult>().Subject;
            using var doc = System.Text.Json.JsonSerializer.SerializeToDocument(ok.Value);
            var recentActivities = doc.RootElement.GetProperty("recentActivities");

            // 1 deduplicated login + 4 warehouse activities = 5 items total
            recentActivities.GetArrayLength().Should().Be(5);
            var loginRowsCount = 0;
            for (var i = 0; i < recentActivities.GetArrayLength(); i++)
            {
                var rawEntity = recentActivities[i].GetProperty("RawEntityName").GetString();
                if (rawEntity == "UserSession")
                    loginRowsCount++;
            }
            loginRowsCount.Should().Be(1);
        }

        [Fact]
        public async Task Wave31_PutawayAndPickingOrchestration_ComputesRealSlottingCutoffFefoAndExpiredSweep_WithVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.WarehouseCalendarDays.Add(new ERP.Domain.Entities.WarehouseCalendarDay
            {
                Id = 1,
                WarehouseId = 1,
                DayOfWeek = (int)DayOfWeek.Monday,
                IsOpen = true,
                OutboundCutoffLocal = new TimeSpan(19, 30, 0)
            });
            db.WarehouseLocations.AddRange(
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 10,
                    WarehouseId = 1,
                    Code = "A-01-01",
                    Name = "Kệ lưu trữ A01",
                    StorageClass = "AMBIENT",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                    MaxWeightKg = 500m,
                    IsActive = true,
                    IsBlocked = false
                },
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 11,
                    WarehouseId = 1,
                    Code = "C-01-01",
                    Name = "Kệ lạnh C01",
                    StorageClass = "COLD",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                    MaxWeightKg = 300m,
                    IsActive = true,
                    IsBlocked = false
                }
            );
            db.PutawayTasks.Add(new ERP.Domain.Entities.PutawayTask
            {
                Id = 1,
                ReceiptId = 1,
                WarehouseId = 1,
                Status = ERP.Domain.Enums.PutawayTaskStatus.Open
            });
            db.PickingTasks.Add(new ERP.Domain.Entities.PickingTask
            {
                Id = 1,
                TaskCode = "PCK-QA-01",
                WarehouseId = 1,
                SourceType = "SalesOrder",
                SourceId = 1,
                SourceCode = "SO-01",
                Status = ERP.Domain.Enums.PickingTaskStatus.InProgress
            });
            db.ExportReceipts.Add(new ERP.Domain.Entities.ExportReceipt
            {
                Id = 1,
                Code = "EXP-QA-01",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ReceiptStatus.Approved,
                CreatedBy = 1
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                LocationId = 10,
                Quantity = 50m,
                ReservedQuantity = 10m,
                Status = ERP.Domain.Enums.InventoryStatus.Available
            });
            db.StockReservations.AddRange(
                new ERP.Domain.Entities.StockReservation
                {
                    Id = 1,
                    ReservationCode = "RSV-EXP-01",
                    WarehouseId = 1,
                    ProductId = 101,
                    SourceType = "Manual",
                    Status = ERP.Domain.Enums.StockReservationStatus.Active,
                    Quantity = 5m,
                    ExpiresAt = DateTime.UtcNow.AddHours(-2)
                },
                new ERP.Domain.Entities.StockReservation
                {
                    Id = 2,
                    ReservationCode = "RSV-SOON-02",
                    WarehouseId = 1,
                    ProductId = 101,
                    SourceType = "SalesOrder",
                    Status = ERP.Domain.Enums.StockReservationStatus.Active,
                    Quantity = 10m,
                    ExpiresAt = DateTime.UtcNow.AddHours(6)
                }
            );
            db.StockAllocations.Add(new ERP.Domain.Entities.StockAllocation
            {
                Id = 1,
                AllocationCode = "ALC-QA-01",
                WarehouseId = 1,
                ProductId = 101,
                LocationId = 10,
                Status = ERP.Domain.Enums.StockAllocationStatus.Active,
                Quantity = 10m
            });
            db.InventoryLots.Add(new ERP.Domain.Entities.InventoryLot
            {
                Id = 1,
                ProductId = 101,
                LotNumber = "LOT-SOON-01",
                ExpiryDate = DateTime.UtcNow.AddDays(15)
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var putawayService = new Mock<IPutawayService>();
            var putawayController = new PutawayTasksController(putawayService.Object, db, warehouseAuth.Object);

            var policyRes = await putawayController.RuleEnginePolicy(1, default);
            var policyOk = policyRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(policyOk.Value))
            {
                doc.RootElement.GetProperty("openPutawayTasksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("activeStorageLocationsCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã tải bộ quy tắc đề xuất vị trí cất hàng thực tế");
            }

            var slotRes = await putawayController.EvaluateSlotting(new PutawayTasksController.EvaluatePutawaySlottingRequest(1), default);
            var slotOk = slotRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(slotOk.Value))
            {
                doc.RootElement.GetProperty("openPutawayTasksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("candidateLocationsCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("availableCapacityLocationsCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("occupiedLocationsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("fefoHazardCompliance").GetString().Should().Be("Đạt chuẩn tương thích FEFO & phân vùng bảo quản");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã chấm điểm tối ưu xếp dỡ & phân bổ ô kệ thực tế");
            }

            var pickingService = new Mock<IPickingService>();
            var pickingController = new PickingTasksController(pickingService.Object, db, warehouseAuth.Object);

            var syncRes = await pickingController.SyncAutomationTelemetry(new PickingTasksController.SyncAutomationTelemetryRequest(1), default);
            var syncOk = syncRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(syncOk.Value))
            {
                doc.RootElement.GetProperty("activePickingTasksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("outboundCutoffLocal").GetString().Should().Be("19:30");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã đồng bộ thiết bị kho & kiểm tra tuân thủ FEFO");
            }

            var cutoffRes = await pickingController.VerifyDispatchCutoff(new PickingTasksController.VerifyPickingDispatchCutoffRequest(1), default);
            var cutoffOk = cutoffRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(cutoffOk.Value))
            {
                doc.RootElement.GetProperty("checkedExportReceiptsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("openPickingTasksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("outboundCutoffLocal").GetString().Should().Be("19:30");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất");
            }

            var sweepRes = await pickingController.SweepExpiredPreview(new PickingTasksController.SweepPickingExpiredReservationsRequest(1), default);
            var sweepOk = sweepRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(sweepOk.Value))
            {
                doc.RootElement.GetProperty("activeReservationsCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("expiredCandidateCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("expiringWithin24hCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã rà soát giữ hàng quá hạn & cam kết tồn kho");
            }

            var fefoRes = await pickingController.VerifyFefoCompliance(new PickingTasksController.VerifyPickingFefoComplianceRequest(1), default);
            var fefoOk = fefoRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(fefoOk.Value))
            {
                doc.RootElement.GetProperty("activeAllocationsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("binLockConflictCount").GetInt32().Should().Be(0);
                doc.RootElement.GetProperty("expiringLotsWithin30DaysCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Tuân thủ nghiêm ngặt chuẩn FEFO/FIFO (0 xung đột khóa kệ)");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result != null && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave31_PackingAndShipmentOrchestration_ComputesRealHuWeightVolumeAndTmsManifest_WithVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 101,
                Code = "SKU-PACK-01",
                Name = "Sản phẩm đóng gói",
                UnitId = 1,
                UnitWeightKg = 2.5m,
                UnitVolumeM3 = 0.012m,
                IsActive = true
            });
            db.PackingSessions.Add(new ERP.Domain.Entities.PackingSession
            {
                Id = 1,
                SessionCode = "PCK-SES-01",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.PackingSessionStatus.Packed
            });
            db.HandlingUnits.AddRange(
                new ERP.Domain.Entities.HandlingUnit
                {
                    Id = 1,
                    HuCode = "HU-QA-01",
                    PackingSessionId = 1,
                    WarehouseId = 1,
                    Status = ERP.Domain.Enums.HandlingUnitStatus.Staged,
                    GrossWeightKg = 14.5m,
                    VolumeM3 = 0.045m
                },
                new ERP.Domain.Entities.HandlingUnit
                {
                    Id = 2,
                    HuCode = "HU-QA-02",
                    PackingSessionId = 1,
                    WarehouseId = 1,
                    Status = ERP.Domain.Enums.HandlingUnitStatus.Loaded,
                    GrossWeightKg = 20.5m,
                    VolumeM3 = 0.075m
                }
            );
            db.Shipments.Add(new ERP.Domain.Entities.Shipment
            {
                Id = 1,
                ShipmentCode = "SHP-QA-01",
                PackingSessionId = 1,
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ShipmentStatus.Staging,
                VehiclePlate = "51C-888.99",
                SealNumber = "SEAL-01"
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var packingService = new Mock<IPackingService>();
            var packingController = new PackingSessionsController(packingService.Object, db, warehouseAuth.Object);

            var verifyRes = await packingController.VerifyPackagingProfile(new PackingSessionsController.VerifyPackagingProfileRequest(101, "CARTON-L", 1), default);
            var verifyOk = verifyRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(verifyOk.Value))
            {
                doc.RootElement.GetProperty("activePackingSessionsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("handlingUnitsCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("totalGrossWeightKg").GetDecimal().Should().Be(35.0m);
                doc.RootElement.GetProperty("totalVolumeM3").GetDecimal().Should().Be(0.12m);
                doc.RootElement.GetProperty("dimensionConfiguredProductsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã kiểm tra quy cách đóng gói & tải trọng kiện hàng thực tế");
            }

            var vasRes = await packingController.ExecuteKittingVas(new PackingSessionsController.ExecuteKittingVasRequest(1, "KIT-QA-01", null), default);
            var vasOk = vasRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(vasOk.Value))
            {
                doc.RootElement.GetProperty("activeSessionsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("handlingUnitsCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("status").GetString().Should().Be("Đã hoàn tất gia công đóng gói & dán nhãn kiện hàng");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã hoàn tất gia công đóng gói & dán nhãn kiện hàng");
            }

            var shipmentService = new Mock<IShipmentService>();
            shipmentService.Setup(x => x.ListAsync(1, null, default)).ReturnsAsync(new List<ShipmentListDto>
            {
                new()
                {
                    Id = 1,
                    ShipmentCode = "SHP-QA-01",
                    WarehouseId = 1,
                    WarehouseName = "Kho Chính TP.HCM",
                    Status = "Staging",
                    VehiclePlate = "51C-888.99",
                    SealNumber = "SEAL-01"
                }
            });
            var shipmentController = new ShipmentsController(shipmentService.Object, db, warehouseAuth.Object);

            var tmsPlanRes = await shipmentController.LoadPlanningAndTms(1, default);
            var tmsPlanOk = tmsPlanRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(tmsPlanOk.Value))
            {
                doc.RootElement.GetProperty("totalShipments").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("stagingOrLoadingCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("tmsIntegrationMode").GetString().Should().Be("Đồng bộ tự động trạng thái vận đơn & biên bản giao hàng (POD) chống trùng lặp");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã tải tổng quan ghép tải & điều phối vận chuyển");
            }

            var syncTmsRes = await shipmentController.SyncTmsManifest(new SyncTmsManifestRequest(1, "VTP-EXPRESS"), default);
            var syncTmsOk = syncTmsRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(syncTmsOk.Value))
            {
                doc.RootElement.GetProperty("syncedShipmentCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("stagedOrLoadedCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("totalHandlingUnitsCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("totalGrossWeightKg").GetDecimal().Should().Be(35.0m);
                doc.RootElement.GetProperty("totalVolumeM3").GetDecimal().Should().Be(0.12m);
                doc.RootElement.GetProperty("ediStatus").GetString().Should().Be("Đã đồng bộ bảng kê vận tải & ghép tải thành công");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã đồng bộ bảng kê vận tải & ghép tải thành công");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result != null && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave32_ProductAutoAuditGovernance_And_InventoryBucketLockVerifications_ReturnRealDatabaseMetrics_AndVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Code = "A-01-01",
                Name = "Kệ lưu trữ A01",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsPickable = true
            });
            db.Products.AddRange(
                new ERP.Domain.Entities.Product
                {
                    Id = 101,
                    Code = "SKU-W32-01",
                    Name = "Sản phẩm chuẩn hóa 01",
                    UnitId = 1,
                    UnitWeightKg = 1.5m,
                    UnitVolumeM3 = 0.01m,
                    ExpiryControl = true,
                    IsActive = true
                },
                new ERP.Domain.Entities.Product
                {
                    Id = 102,
                    Code = "SKU-W32-02",
                    Name = "Sản phẩm chuẩn hóa 02",
                    UnitId = 1,
                    IsActive = true
                }
            );
            db.ProductBarcodes.Add(new ERP.Domain.Entities.ProductBarcode
            {
                Id = 1,
                ProductId = 101,
                Value = "8938505974192"
            });
            db.QcPolicies.Add(new ERP.Domain.Entities.QcPolicy
            {
                Id = 1,
                ProductId = 101,
                RequiresQc = true,
                IsActive = true,
                EffectiveFromUtc = DateTime.UtcNow.AddDays(-5),
                Rule = "100% QC"
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                LocationId = 10,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 100m,
                ReservedQuantity = 20m
            });
            db.InventoryLocks.AddRange(
                new ERP.Domain.Entities.InventoryLock
                {
                    Id = 1,
                    WarehouseId = 1,
                    ProductId = 101,
                    LockType = ERP.Domain.Enums.InventoryLockType.CountFreeze,
                    Status = ERP.Domain.Enums.InventoryLockStatus.Active,
                    Reason = "Khóa kiểm kê định kỳ",
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.InventoryLock
                {
                    Id = 2,
                    WarehouseId = 1,
                    ProductId = 102,
                    LockType = ERP.Domain.Enums.InventoryLockType.ManualOperationalLock,
                    Status = ERP.Domain.Enums.InventoryLockStatus.Active,
                    Reason = "Khóa vận hành kiểm tra kệ",
                    CreatedBy = 1
                }
            );
            await db.SaveChangesAsync();

            var productService = new Mock<IProductService>();
            var productsController = new ProductsController(productService.Object, db);
            var govRes = await productsController.AutoAuditGovernance(default);
            var govOk = govRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(govOk.Value))
            {
                doc.RootElement.GetProperty("totalSkuCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("activeSkuCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("barcodeConfiguredCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("uomConfiguredCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("dimensionConfiguredCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("expiryControlledCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("qcAndTolerancePolicyCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã hoàn tất kiểm toán tự động chuẩn dữ liệu nền SKU");
            }

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var invStatusService = new Mock<IInventoryStatusService>();
            var invStatusController = new InventoryStatusController(invStatusService.Object, db, warehouseAuth.Object);

            var bckRes = await invStatusController.VerifyBucketInvariants(new InventoryStatusController.VerifyBucketInvariantsRequest(1), default);
            var bckOk = bckRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(bckOk.Value))
            {
                doc.RootElement.GetProperty("totalBucketsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("violationCount").GetInt32().Should().Be(0);
                doc.RootElement.GetProperty("nonPickableHoldViolationCount").GetInt32().Should().Be(0);
                doc.RootElement.GetProperty("fiveBucketEquationStatus").GetString().Should().Be("Cân bằng chính xác tuyệt đối (0 <= Đã giữ <= Tồn vật lý)");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực bất biến 5 trạng thái tồn kho & phân vùng biệt trữ");
            }

            var lckRes = await invStatusController.VerifyLockPrecedence(new InventoryStatusController.VerifyLockPrecedenceRequest(1), default);
            var lckOk = lckRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(lckOk.Value))
            {
                doc.RootElement.GetProperty("activeLocksCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("countFreezeLocksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("manualLocksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("leakCount").GetInt32().Should().Be(0);
                doc.RootElement.GetProperty("overlappingHoldPrecedence").GetString().Should().Be("Thu hồi lô hàng > Kiểm định chất lượng > Đóng băng kiểm kê > Khóa vận hành");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực thứ tự ưu tiên khóa tồn kho (0 rò rỉ xuất hàng)");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result != null && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave32_ReconciliationSiteReadiness_And_ReportsBiGlComplianceEndpoints_ReturnRealDatabaseMetrics_AndVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 80m,
                ReservedQuantity = 10m
            });
            db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                TransactionType = ERP.Domain.Enums.TransactionType.Import,
                Quantity = 80m,
                ReferenceType = "ImportReceipt",
                ReferenceId = 1,
                CreatedBy = 1,
                TransactionDate = DateTime.UtcNow.AddHours(-1)
            });
            db.Shipments.Add(new ERP.Domain.Entities.Shipment
            {
                Id = 1,
                ShipmentCode = "SHP-W32-01",
                PackingSessionId = 1,
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ShipmentStatus.Dispatched
            });
            db.PartnerDebtInvoices.AddRange(
                new ERP.Domain.Entities.PartnerDebtInvoice
                {
                    Id = 1,
                    Code = "INV-AP-01",
                    PartnerId = 1,
                    DebtType = ERP.Domain.Enums.PartnerDebtType.AP,
                    SourceType = "ImportReceipt",
                    SourceId = 1,
                    SourceCode = "IR-QA-001",
                    TotalAmount = 5000000m,
                    RemainingAmount = 3000000m,
                    Status = ERP.Domain.Enums.PartnerDebtInvoiceStatus.PartiallyPaid,
                    IssuedAtUtc = DateTime.UtcNow.AddDays(-3)
                },
                new ERP.Domain.Entities.PartnerDebtInvoice
                {
                    Id = 2,
                    Code = "INV-AR-01",
                    PartnerId = 2,
                    DebtType = ERP.Domain.Enums.PartnerDebtType.AR,
                    SourceType = "ExportReceipt",
                    SourceId = 1,
                    SourceCode = "ER-QA-001",
                    TotalAmount = 8000000m,
                    RemainingAmount = 4500000m,
                    Status = ERP.Domain.Enums.PartnerDebtInvoiceStatus.Open,
                    IssuedAtUtc = DateTime.UtcNow.AddDays(-2)
                }
            );
            db.IdempotencyRecords.Add(new ERP.Domain.Entities.IdempotencyRecord
            {
                Id = 1,
                UserId = 1,
                CommandScope = "ImportReceipt.Post",
                KeyHash = "HASH-W32-01",
                RequestFingerprint = "FP-W32-01",
                Status = ERP.Domain.Entities.IdempotencyStatus.Completed,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-30),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var reconQuery = new Mock<IInventoryReconciliationQueryService>();
            reconQuery.Setup(x => x.GetReconciliationsAsync(1, null, null, 1, int.MaxValue))
                .ReturnsAsync(new ERP.Application.Common.PagedResult<InventoryReconciliationDto>
                {
                    Items = new List<InventoryReconciliationDto>
                    {
                        new()
                        {
                            WarehouseId = 1,
                            ProductId = 101,
                            CurrentQuantity = 80m,
                            ExpectedQuantity = 80m,
                            Difference = 0m,
                            Status = "Match"
                        }
                    },
                    TotalRecords = 1,
                    PageIndex = 1,
                    PageSize = 100
                });

            var reconController = new InventoryReconciliationController(reconQuery.Object, db, warehouseAuth.Object);
            var siteRes = await reconController.VerifySiteReadiness(1, new InventoryReconciliationController.VerifySiteReadinessRequest(1), default);
            var siteOk = siteRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(siteOk.Value))
            {
                doc.RootElement.GetProperty("checkedRows").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("matchedCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("mismatchCount").GetInt32().Should().Be(0);
                doc.RootElement.GetProperty("totalTransactionsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("idempotencyRecordsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("glReconciliationStatus").GetString().Should().Be("Cân bằng 100% giữa Sổ cái Kho & Tồn vật lý");
                doc.RootElement.GetProperty("readinessVerdict").GetString().Should().Be("Đủ điều kiện mở/đóng kỳ vận hành kho");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã đối soát sổ cái bất biến & xác nhận sẵn sàng vận hành kho");
            }

            var reportsController = new ReportsController(_mockService.Object, db, warehouseAuth.Object);

            var biRes = await reportsController.SyncBiSnapshot(new ReportsController.SyncBiSnapshotRequest(1), warehouseAuth.Object, default);
            var biOk = biRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(biOk.Value))
            {
                doc.RootElement.GetProperty("totalTransactionsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("activeSkuBucketsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("totalShipmentsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã đồng bộ ảnh chụp dữ liệu phân tích BI & KPI vận hành kho");
            }

            var glRes = await reportsController.ExportGlJournal(new ReportsController.ExportGlJournalRequest(1), warehouseAuth.Object, default);
            var glOk = glRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(glOk.Value))
            {
                doc.RootElement.GetProperty("inventoryLedgerEntriesCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("openDebtInvoicesCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("totalPayableBalance").GetDecimal().Should().Be(3000000m);
                doc.RootElement.GetProperty("totalReceivableBalance").GetDecimal().Should().Be(4500000m);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã kết xuất bảng kê bút toán sổ cái kho & công nợ AP/AR");
            }

            var compRes = await reportsController.ComplianceEvidenceBundle(new ReportsController.ComplianceEvidenceBundleRequest(1), warehouseAuth.Object, default);
            var compOk = compRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(compOk.Value))
            {
                doc.RootElement.GetProperty("immutableAuditLogCount").GetInt32().Should().BeGreaterThanOrEqualTo(3);
                doc.RootElement.GetProperty("idempotencyRecordCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("immutableLedgerCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("sodStatus").GetString().Should().Be("Đã xác thực phân tách nhiệm vụ Maker-Checker & nhật ký bất biến");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã đóng gói hồ sơ bằng chứng kiểm toán & tuân thủ vận hành");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result != null && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave32_SalesOrders_EvaluateAtpRouting_PrioritizesQaHcmWhenZeroStock()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.AddRange(
                new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH-EMPTY", Name = "Kho Phụ", IsActive = true },
                new ERP.Domain.Entities.Warehouse { Id = 2, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true }
            );
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1, 2 });

            var soService = new Mock<ISalesOrderBackorderService>();
            soService.Setup(x => x.ListSalesOrdersAsync(null, null, default)).ReturnsAsync(new List<SalesOrderListDto>());

            var controller = new SalesOrdersController(soService.Object, db, warehouseAuth.Object);
            var res = await controller.EvaluateAtpRouting(new EvaluateAtpRoutingRequest(null), default);
            var ok = res.Should().BeOfType<OkObjectResult>().Subject;
            using var doc = System.Text.Json.JsonSerializer.SerializeToDocument(ok.Value);
            doc.RootElement.GetProperty("optimalWarehouseNode").GetString().Should().Be("QA-HCM — Kho Chính TP.HCM");
        }

        [Fact]
        public async Task Wave33_InventoryWhatIfSimulation_AdvancedPlanning_And_DecisionPolicyGovernance_ComputeRealMetrics_EnforceViewerGuard_AndReturnVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Code = "A-01-01-01",
                Name = "Kệ A1",
                IsActive = true,
                IsBlocked = false,
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage
            });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 101,
                Code = "SKU-W33-01",
                Name = "Sản phẩm hoạch định 01",
                UnitId = 1,
                IsActive = true,
                TrackingType = ERP.Domain.Enums.ProductTrackingType.Lot,
                ExpiryControl = true
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                LocationId = 10,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 150m,
                ReservedQuantity = 30m
            });
            db.PurchaseOrders.Add(new ERP.Domain.Entities.PurchaseOrder
            {
                Id = 1,
                Code = "PO-W33-01",
                WarehouseId = 1,
                SupplierId = 1,
                Status = ERP.Domain.Enums.PurchaseOrderStatus.Open,
                OrderDate = DateTime.UtcNow.AddDays(-2),
                CreatedBy = 1
            });
            db.PurchaseOrderLines.Add(new ERP.Domain.Entities.PurchaseOrderLine
            {
                Id = 1,
                PurchaseOrderId = 1,
                LineNo = 1,
                ProductId = 101,
                OrderedQuantity = 50m,
                BaseOrderedQuantity = 50m,
                OperationUnitId = 1,
                BaseUnitId = 1
            });
            db.SalesOrders.Add(new ERP.Domain.Entities.SalesOrder
            {
                Id = 1,
                OrderCode = "SO-W33-01",
                WarehouseId = 1,
                CustomerId = 1,
                Status = ERP.Domain.Enums.SalesOrderStatus.Released,
                CreatedBy = 1
            });
            db.SalesOrderLines.Add(new ERP.Domain.Entities.SalesOrderLine
            {
                Id = 1,
                SalesOrderId = 1,
                ProductId = 101,
                OrderedQuantity = 90m,
                CancelledQuantity = 10m
            });
            db.StockReservations.Add(new ERP.Domain.Entities.StockReservation
            {
                Id = 1,
                ReservationCode = "RSV-W33-01",
                WarehouseId = 1,
                ProductId = 101,
                Quantity = 30m,
                Status = ERP.Domain.Enums.StockReservationStatus.Active,
                SourceType = "SalesOrder",
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(3)
            });
            db.InventoryLocks.Add(new ERP.Domain.Entities.InventoryLock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                LockType = ERP.Domain.Enums.InventoryLockType.CountFreeze,
                Status = ERP.Domain.Enums.InventoryLockStatus.Active,
                Reason = "Khóa kiểm kê định kỳ",
                CreatedBy = 1
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var viewerController = new InventoryControlController(
                new Mock<IInventoryLockService>().Object,
                new Mock<IInventoryMovementService>().Object,
                db,
                warehouseAuth.Object)
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

            (await viewerController.RunWhatIfSimulation(db, warehouseAuth.Object, new InventoryControlController.WhatIfSimulationRequest(1, 1.25m, 14), default))
                .Should().BeOfType<ForbidResult>();
            (await viewerController.EvaluateDecisionPolicyGovernance(new InventoryControlController.EvaluateDecisionPolicyRequest(1), default))
                .Should().BeOfType<ForbidResult>();

            var managerController = new InventoryControlController(
                new Mock<IInventoryLockService>().Object,
                new Mock<IInventoryMovementService>().Object,
                db,
                warehouseAuth.Object);

            var planRes = await managerController.AdvancedPlanningSuite(db, warehouseAuth.Object, 1, default);
            var planOk = planRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(planOk.Value))
            {
                doc.RootElement.GetProperty("totalAvailableQty").GetDecimal().Should().Be(120m);
                doc.RootElement.GetProperty("totalOpenDemandQty").GetDecimal().Should().Be(80m);
                doc.RootElement.GetProperty("incomingPoQty").GetDecimal().Should().Be(50m);
                doc.RootElement.GetProperty("activeReservationsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã tải bộ công cụ hoạch định tồn kho nâng cao & dự báo nhu cầu");
            }

            var simRes = await managerController.RunWhatIfSimulation(
                db,
                warehouseAuth.Object,
                new InventoryControlController.WhatIfSimulationRequest(1, 1.25m, 14, 25m, 2),
                default);
            var simOk = simRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(simOk.Value))
            {
                doc.RootElement.GetProperty("totalAvailableQty").GetDecimal().Should().Be(120m);
                doc.RootElement.GetProperty("totalOpenDemandQty").GetDecimal().Should().Be(80m);
                doc.RootElement.GetProperty("incomingPoQty").GetDecimal().Should().Be(50m);
                doc.RootElement.GetProperty("projectedEndingStock").GetDecimal().Should().Be(66m);
                doc.RootElement.GetProperty("scenarioStatus").GetString().Should().Be("Đủ tồn kho đáp ứng");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đủ tồn kho đáp ứng");
            }

            var govRes = await managerController.EvaluateDecisionPolicyGovernance(
                new InventoryControlController.EvaluateDecisionPolicyRequest(1),
                default);
            var govOk = govRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(govOk.Value))
            {
                doc.RootElement.GetProperty("evaluatedStockBucketsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("trackedSkuCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("activeLocationsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("activeReservationsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("status").GetString().Should().Be("Đã xác thực khung quản trị chính sách quyết định tồn kho");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực khung quản trị chính sách quyết định tồn kho");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result != null && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave33_StocktakeFreeze_RmaIntakeCheck_ApprovalsEscalation_And_ExceptionAcknowledge_ReturnRealMetrics_AndVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.Stocktakes.Add(new ERP.Domain.Entities.Stocktake
            {
                Id = 1,
                Code = "ST-W33-01",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ReceiptStatus.Draft,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-30)
            });
            db.InventoryLocks.Add(new ERP.Domain.Entities.InventoryLock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                LockType = ERP.Domain.Enums.InventoryLockType.CountFreeze,
                Status = ERP.Domain.Enums.InventoryLockStatus.Active,
                Reason = "Khóa kiểm kê ST-W33-01",
                CreatedBy = 1
            });
            db.ImportReceipts.Add(new ERP.Domain.Entities.ImportReceipt
            {
                Id = 1,
                Code = "IR-RMA-W33-01",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ReceiptStatus.Draft,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow.AddHours(-36)
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                Quantity = 12m,
                ReservedQuantity = 0m
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var stocktakeController = new StocktakesController(
                new Mock<IStocktakeService>().Object,
                new Mock<IStocktakeQueryService>().Object,
                db,
                warehouseAuth.Object);

            var stReasonRes = await stocktakeController.GetReasonCodesAndAdjustments(1, default);
            var stReasonOk = stReasonRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(stReasonOk.Value))
            {
                doc.RootElement.GetProperty("openStocktakesCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("activeCountFreezeLocksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã tải danh mục mã lý do & lịch sử đề xuất điều chỉnh kiểm kê");
            }

            var stFreezeRes = await stocktakeController.VerifyFreezePolicy(new StocktakesController.VerifyStocktakeFreezePolicyRequest(1), default);
            var stFreezeOk = stFreezeRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(stFreezeOk.Value))
            {
                doc.RootElement.GetProperty("openStocktakesCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("activeCountFreezeLocksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực chính sách đóng băng kiểm kê & mã lý do điều chỉnh");
            }

            var importController = new ImportReceiptsController(
                new Mock<IImportReceiptService>().Object,
                null,
                db,
                warehouseAuth.Object);

            var rmaRes = await importController.RmaIntakeCheck(
                new ImportReceiptsController.RmaIntakeCheckRequest("IR-RMA-W33-01", "Hàng trả về kiểm tra ngoại quan", 1, "RMA-REF-01"),
                default);
            var rmaOk = rmaRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(rmaOk.Value))
            {
                doc.RootElement.GetProperty("quarantineBucketCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("pendingQcOrRmaReceiptsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("openStocktakesCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("activeCountFreezeLocksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ");
            }

            var approvalService = new Mock<IApprovalWorkflowService>();
            approvalService.Setup(x => x.GetQueueAsync(It.IsAny<ApprovalQueueQuery>(), default))
                .ReturnsAsync(new ERP.Application.Common.PagedResult<ApprovalQueueItem>
                {
                    Items = new[]
                    {
                        new ApprovalQueueItem { DocumentType = "ImportReceipt", DocumentId = 1, DocumentCode = "IR-RMA-W33-01", WarehouseId = 1, SlaStatus = "Overdue", WaitingMinutes = 180 },
                        new ApprovalQueueItem { DocumentType = "Stocktake", DocumentId = 1, DocumentCode = "ST-W33-01", WarehouseId = 1, SlaStatus = "Overdue", WaitingMinutes = 150 }
                    },
                    TotalRecords = 2
                });

            var approvalsController = new ApprovalsController(
                approvalService.Object,
                db,
                warehouseAuth.Object);

            var escRes = await approvalsController.EscalateOverdue(
                new EscalateOverdueApprovalsRequest(1),
                default);
            var escOk = escRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(escOk.Value))
            {
                doc.RootElement.GetProperty("overdueCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("newlyEscalatedCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("escalatedStatus").GetString().Should().Be("Đã chuyển cấp quản lý phê duyệt khẩn");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã kích hoạt chuyển cấp phê duyệt các hồ sơ quá hạn SLA");
            }

            var reportsController = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var ackRes = await reportsController.AcknowledgeOperationalException(
                new ReportsController.AcknowledgeOperationalExceptionDto("EX-W33-01", 1, "Đã phân công xử lý", "SHORT_PICK"),
                db,
                warehouseAuth.Object,
                default);
            var ackOk = ackRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(ackOk.Value))
            {
                doc.RootElement.GetProperty("acknowledged").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("acknowledgedExceptionsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã tiếp nhận và phân công xử lý ngoại lệ vận hành");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result != null && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave34_InventoryWhatIfSimulation_ReturnsExplicitIncomingPoReservedAndVietnameseFormulaSummary_ForSufficientAndShortageScenarios()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 101,
                Code = "SKU-W34-01",
                Name = "Sản phẩm mô phỏng What-If",
                UnitId = 1,
                IsActive = true
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 150m,
                ReservedQuantity = 30m
            });
            db.PurchaseOrders.Add(new ERP.Domain.Entities.PurchaseOrder
            {
                Id = 1,
                Code = "PO-W34-01",
                WarehouseId = 1,
                SupplierId = 1,
                Status = ERP.Domain.Enums.PurchaseOrderStatus.Open,
                OrderDate = DateTime.UtcNow.AddDays(-1),
                CreatedBy = 1
            });
            db.PurchaseOrderLines.Add(new ERP.Domain.Entities.PurchaseOrderLine
            {
                Id = 1,
                PurchaseOrderId = 1,
                LineNo = 1,
                ProductId = 101,
                OrderedQuantity = 50m,
                BaseOrderedQuantity = 50m,
                OperationUnitId = 1,
                BaseUnitId = 1
            });
            db.SalesOrders.Add(new ERP.Domain.Entities.SalesOrder
            {
                Id = 1,
                OrderCode = "SO-W34-01",
                WarehouseId = 1,
                CustomerId = 1,
                Status = ERP.Domain.Enums.SalesOrderStatus.Released,
                CreatedBy = 1
            });
            db.SalesOrderLines.Add(new ERP.Domain.Entities.SalesOrderLine
            {
                Id = 1,
                SalesOrderId = 1,
                ProductId = 101,
                OrderedQuantity = 90m,
                CancelledQuantity = 10m
            });
            db.StockReservations.Add(new ERP.Domain.Entities.StockReservation
            {
                Id = 1,
                ReservationCode = "RSV-W34-01",
                WarehouseId = 1,
                ProductId = 101,
                Quantity = 30m,
                ConsumedQuantity = 5m,
                ReleasedQuantity = 0m,
                Status = ERP.Domain.Enums.StockReservationStatus.Active,
                SourceType = "SalesOrder",
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(2)
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new InventoryControlController(
                new Mock<IInventoryLockService>().Object,
                new Mock<IInventoryMovementService>().Object,
                db,
                warehouseAuth.Object);

            var sufficientRes = await controller.RunWhatIfSimulation(
                db,
                warehouseAuth.Object,
                new InventoryControlController.WhatIfSimulationRequest(1, 1.25m, 14, 25m, 0),
                default);
            var sufficientOk = sufficientRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(sufficientOk.Value))
            {
                doc.RootElement.GetProperty("currentAvailableStock").GetDecimal().Should().Be(120m);
                doc.RootElement.GetProperty("totalAvailableQty").GetDecimal().Should().Be(120m);
                doc.RootElement.GetProperty("totalOpenDemandQty").GetDecimal().Should().Be(80m);
                doc.RootElement.GetProperty("incomingPoQty").GetDecimal().Should().Be(50m);
                doc.RootElement.GetProperty("incomingPurchaseOrderQty").GetDecimal().Should().Be(50m);
                doc.RootElement.GetProperty("activeReservedQty").GetDecimal().Should().Be(25m);
                doc.RootElement.GetProperty("simulatedDemand").GetDecimal().Should().Be(100m);
                doc.RootElement.GetProperty("projectedEndingStock").GetDecimal().Should().Be(70m);
                doc.RootElement.GetProperty("formulaSummary").GetString().Should().Be(
                    "Tồn khả dụng (120 Thùng) + Đang nhập PO (50 Thùng) - Cầu kịch bản (100 Thùng) = Dự kiến cuối kỳ (70 Thùng)");
                doc.RootElement.GetProperty("scenarioStatus").GetString().Should().Be("Đủ tồn kho đáp ứng");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đủ tồn kho đáp ứng");
            }

            var shortageRes = await controller.RunWhatIfSimulation(
                db,
                warehouseAuth.Object,
                new InventoryControlController.WhatIfSimulationRequest(1, 2.5m, 14, 150m, 0),
                default);
            var shortageOk = shortageRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(shortageOk.Value))
            {
                doc.RootElement.GetProperty("projectedEndingStock").GetDecimal().Should().Be(-30m);
                doc.RootElement.GetProperty("formulaSummary").GetString().Should().Be(
                    "Tồn khả dụng (120 Thùng) + Đang nhập PO (50 Thùng) - Cầu kịch bản (200 Thùng) = Dự kiến cuối kỳ (-30 Thùng)");
                doc.RootElement.GetProperty("scenarioStatus").GetString().Should().Be("Cảnh báo thiếu hụt tồn kho");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Cảnh báo thiếu hụt tồn kho");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave34_ExportCutoff_StocktakeGovernance_Approvals_And_ImportQcRmaEndpoints_EnforceViewerGuard_WriteAuditLog_AndReturnVietnameseLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.WarehouseCalendarDays.Add(new ERP.Domain.Entities.WarehouseCalendarDay
            {
                Id = 1,
                WarehouseId = 1,
                DayOfWeek = (int)DayOfWeek.Tuesday,
                IsOpen = true,
                OutboundCutoffLocal = new TimeSpan(20, 30, 0)
            });
            db.ExportReceipts.Add(new ERP.Domain.Entities.ExportReceipt
            {
                Id = 1,
                Code = "EXP-W34-01",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ReceiptStatus.Approved,
                CreatedBy = 1
            });
            db.PickingTasks.Add(new ERP.Domain.Entities.PickingTask
            {
                Id = 1,
                TaskCode = "PCK-W34-01",
                WarehouseId = 1,
                SourceType = "ExportReceipt",
                SourceId = 1,
                SourceCode = "EXP-W34-01",
                Status = ERP.Domain.Enums.PickingTaskStatus.Open
            });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 101,
                Code = "SKU-W34-01",
                Name = "Sản phẩm kiểm định",
                UnitId = 1,
                IsActive = true
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 1,
                WarehouseId = 1,
                ProductId = 101,
                Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                Quantity = 10m,
                ReservedQuantity = 0m
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var exportService = new Mock<IExportReceiptService>();
            var viewerExportController = new ExportReceiptsController(exportService.Object, db, warehouseAuth.Object)
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
            (await viewerExportController.VerifyDispatchCutoff(new ExportReceiptsController.VerifyDispatchCutoffRequest(1), default))
                .Should().BeOfType<ForbidResult>();

            var exportController = new ExportReceiptsController(exportService.Object, db, warehouseAuth.Object);
            var cutoffRes = await exportController.VerifyDispatchCutoff(new ExportReceiptsController.VerifyDispatchCutoffRequest(1), default);
            var cutoffOk = cutoffRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(cutoffOk.Value))
            {
                doc.RootElement.GetProperty("checkedExportReceiptsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("openPickingTasksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("outboundCutoffLocal").GetString().Should().Be("20:30");
                doc.RootElement.GetProperty("singleShipDeductionGuard").GetString().Should().Be("Chốt trừ tồn kho 1 lần duy nhất khi xác nhận xuất");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã đối soát giờ chốt xuất kho & khóa trừ tồn đơn nhất");
            }

            var stocktakeController = new StocktakesController(
                new Mock<IStocktakeService>().Object,
                new Mock<IStocktakeQueryService>().Object,
                db,
                warehouseAuth.Object);
            var propRes = await stocktakeController.CreateAdjustmentProposal(
                new StocktakesController.CreateAdjustmentProposalRequest(WarehouseId: 1, ProductCode: "SKU-W34-01", DeltaQuantity: 3m),
                default);
            var propOk = propRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(propOk.Value))
            {
                doc.RootElement.GetProperty("status").GetString().Should().Be("Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be(
                    "Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm");
            }

            var importService = new Mock<IImportReceiptService>();
            importService.Setup(x => x.GetAllAsync(null)).ReturnsAsync(new List<ERP.Application.DTOs.ImportReceiptDto>
            {
                new() { Id = 1, Code = "IR-W34-01", Status = "Draft", Note = "Hàng trả về RMA" }
            });
            var importController = new ImportReceiptsController(importService.Object, null, db, warehouseAuth.Object);
            var qcSummaryRes = await importController.GetQcAndRmaSummary();
            var qcSummaryOk = qcSummaryRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(qcSummaryOk.Value))
            {
                doc.RootElement.GetProperty("totalReceiptsInspected").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("rmaEligibleReceiptsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về");
            }

            var rtvRes = await importController.ExecuteRtvRefurbish(
                new ImportReceiptsController.ExecuteRtvRefurbishRequest(WarehouseId: 1, DispositionType: "RTV"),
                default);
            var rtvOk = rtvRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(rtvOk.Value))
            {
                doc.RootElement.GetProperty("nonAvailableBucketsCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã thực thi & Ghi sổ kiểm toán");
            }

            db.AuditLogs.Should().NotBeEmpty();
            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave35_UserDisplayNames_AreLocalizedToVietnamese_AcrossStocktakeLedgerApprovalImportAndExportServices()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var roleManager = new ERP.Domain.Entities.Role { Id = 1, RoleName = "Manager" };
            var roleStaff = new ERP.Domain.Entities.Role { Id = 2, RoleName = "WarehouseStaff" };
            db.Roles.AddRange(roleManager, roleStaff);
            db.RolePermissions.AddRange(
                new ERP.Domain.Entities.RolePermission
                {
                    RoleId = 1,
                    Permission = new ERP.Domain.Entities.Permission("receipt.read", "Đọc phiếu nhập") { Id = 1 }
                },
                new ERP.Domain.Entities.RolePermission
                {
                    RoleId = 1,
                    Permission = new ERP.Domain.Entities.Permission("export_receipt.read", "Đọc phiếu xuất") { Id = 2 }
                }
            );

            var uManager = new ERP.Domain.Entities.User
            {
                Id = 10,
                Username = "qa_manager_wh01",
                FullName = "QA Manager WH01",
                Email = "mgr@test.local",
                PasswordHash = "h",
                RoleId = 1,
                Role = roleManager,
                IsActive = true
            };
            var uStaff = new ERP.Domain.Entities.User
            {
                Id = 11,
                Username = "qa_staff_wh01",
                FullName = "QA Staff WH01",
                Email = "staff@test.local",
                PasswordHash = "h",
                RoleId = 2,
                Role = roleStaff,
                IsActive = true
            };
            var uSupervisor = new ERP.Domain.Entities.User
            {
                Id = 12,
                Username = "qa_supervisor_wh01",
                FullName = "QA Supervisor WH01",
                Email = "sup@test.local",
                PasswordHash = "h",
                RoleId = 1,
                Role = roleManager,
                IsActive = true
            };
            var uAdmin = new ERP.Domain.Entities.User
            {
                Id = 13,
                Username = "qa_admin",
                FullName = "QA System Admin",
                Email = "admin@test.local",
                PasswordHash = "h",
                RoleId = 1,
                Role = roleManager,
                IsActive = true
            };
            db.Users.AddRange(uManager, uStaff, uSupervisor, uAdmin);

            var wh = new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true };
            var unit = new ERP.Domain.Entities.Unit { Id = 1, Code = "THÙNG", Name = "THÙNG", IsActive = true };
            var prod = new ERP.Domain.Entities.Product { Id = 101, Code = "SKU-W35-01", Name = "Sản phẩm chuẩn hóa W35", UnitId = 1, Unit = unit, IsActive = true };
            db.Warehouses.Add(wh);
            db.Units.Add(unit);
            db.Products.Add(prod);

            var st = new ERP.Domain.Entities.Stocktake
            {
                Id = 1,
                Code = "ST-W35-01",
                WarehouseId = 1,
                Warehouse = wh,
                Status = ERP.Domain.Enums.ReceiptStatus.Draft,
                CreatedBy = 11,
                CreatedByUser = uStaff,
                ApprovedBy = 10,
                ApprovedByUser = uManager,
                CreatedAt = DateTime.UtcNow.AddHours(-2)
            };
            db.Stocktakes.Add(st);

            db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
            {
                Id = 1,
                WarehouseId = 1,
                Warehouse = wh,
                ProductId = 101,
                Product = prod,
                TransactionType = ERP.Domain.Enums.TransactionType.Import,
                InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 25m,
                ReferenceId = 1,
                ReferenceType = "ImportReceipt",
                TransactionDate = DateTime.UtcNow.AddHours(-1),
                CreatedBy = 12,
                CreatedByUser = uSupervisor
            });

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                Id = 1,
                UserId = 13,
                User = uAdmin,
                WarehouseId = 1,
                EntityName = "Stocktake",
                EntityId = 1,
                Action = "Stocktake.Created",
                Result = "Success",
                Timestamp = DateTime.UtcNow.AddMinutes(-30)
            });

            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            var stQuerySvc = new ERP.Infrastructure.Queries.StocktakeQueryService(db, warehouseAuth.Object);
            var stList = (await stQuerySvc.GetStocktakesAsync()).ToList();
            stList.Should().ContainSingle();
            stList[0].CreatedByName.Should().Be("Nhân viên Kho WH01");
            stList[0].ApprovedByName.Should().Be("Quản lý Kho WH01");

            var stById = await stQuerySvc.GetStocktakeByIdAsync(1);
            stById.CreatedByName.Should().Be("Nhân viên Kho WH01");
            stById.ApprovedByName.Should().Be("Quản lý Kho WH01");

            var stMapped = ERP.Application.Services.StocktakeService.MapToDto(st);
            stMapped.CreatedByName.Should().Be("Nhân viên Kho WH01");
            stMapped.ApprovedByName.Should().Be("Quản lý Kho WH01");

            var txQuerySvc = new ERP.Infrastructure.Queries.InventoryTransactionQueryService(db, warehouseAuth.Object);
            var txPage = await txQuerySvc.GetHistoryAsync(null, null, null, 1, null, null, null, null, 1, 20);
            txPage.Items.Should().ContainSingle();
            txPage.Items[0].CreatedByName.Should().Be("Giám sát Kho WH01");

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
            currentUser.SetupGet(x => x.UserId).Returns(10);
            currentUser.SetupGet(x => x.Role).Returns("Manager");
            var reqMeta = new Mock<IRequestMetadata>();

            var approvalSvc = new ERP.Infrastructure.Services.ApprovalWorkflowService(
                db,
                currentUser.Object,
                warehouseAuth.Object,
                reqMeta.Object);

            var queuePage = await approvalSvc.GetQueueAsync(new ApprovalQueueQuery { WarehouseId = 1, PageIndex = 1, PageSize = 20 });
            queuePage.Items.Should().ContainSingle();
            queuePage.Items[0].CreatorName.Should().Be("Nhân viên Kho WH01");

            var historyPage = await approvalSvc.GetHistoryAsync(new ApprovalHistoryQuery { WarehouseId = 1, PageIndex = 1, PageSize = 20 });
            historyPage.Items.Should().ContainSingle();
            historyPage.Items[0].ActorName.Should().Be("Quản trị viên Hệ thống");

            ReportsController.NormalizeActorDisplayName("QA Manager WH01").Should().Be("Quản lý Kho WH01");
            ReportsController.NormalizeActorDisplayName("qa_staff_wh01").Should().Be("Nhân viên Kho WH01");
            ReportsController.NormalizeActorDisplayName("QA Supervisor WH01").Should().Be("Giám sát Kho WH01");
            ReportsController.NormalizeActorDisplayName("QA System Admin").Should().Be("Quản trị viên Hệ thống");

            var importRepo = new ERP.Infrastructure.Repositories.ImportReceiptRepository(db, warehouseAuth.Object);
            db.ImportReceipts.Add(new ERP.Domain.Entities.ImportReceipt
            {
                Id = 1,
                Code = "IR-W35-01",
                WarehouseId = 1,
                Warehouse = wh,
                Status = ERP.Domain.Enums.ReceiptStatus.Posted,
                CreatedBy = 11,
                CreatedByUser = uStaff,
                ApprovedBy = 10,
                ApprovedByUser = uManager,
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            });
            var exportRepo = new ERP.Infrastructure.Repositories.ExportReceiptRepository(db, warehouseAuth.Object);
            db.ExportReceipts.Add(new ERP.Domain.Entities.ExportReceipt
            {
                Id = 1,
                Code = "ER-W35-01",
                WarehouseId = 1,
                Warehouse = wh,
                Status = ERP.Domain.Enums.ReceiptStatus.Dispatched,
                CreatedBy = 11,
                CreatedByUser = uStaff,
                ApprovedBy = 10,
                ApprovedByUser = uManager,
                DispatchedBy = 12,
                DispatchedByUser = uSupervisor,
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            });
            await db.SaveChangesAsync();

            var importSvc = new ERP.Application.Services.ImportReceiptService(
                importRepo,
                new Mock<ERP.Domain.Interfaces.IInventoryStockRepository>().Object,
                new Mock<ERP.Domain.Interfaces.IInventoryTransactionRepository>().Object,
                new Mock<ERP.Domain.Interfaces.IWarehouseRepository>().Object,
                new Mock<ERP.Domain.Interfaces.IProductRepository>().Object,
                new Mock<ERP.Domain.Interfaces.IUnitOfWork>().Object,
                new Mock<ERP.Domain.Interfaces.IAuditLogRepository>().Object,
                warehouseAuth.Object,
                currentUser.Object,
                new Mock<ERP.Domain.Interfaces.IUserRepository>().Object);
            var irList = (await importSvc.GetAllAsync()).ToList();
            irList.Should().ContainSingle();
            irList[0].CreatedByName.Should().Be("Nhân viên Kho WH01");
            irList[0].ApprovedByName.Should().Be("Quản lý Kho WH01");

            var exportSvc = new ERP.Application.Services.ExportReceiptService(
                exportRepo,
                new Mock<ERP.Domain.Interfaces.IInventoryStockRepository>().Object,
                new Mock<ERP.Domain.Interfaces.IInventoryTransactionRepository>().Object,
                new Mock<ERP.Domain.Interfaces.IUnitOfWork>().Object,
                new Mock<ERP.Domain.Interfaces.IAuditLogRepository>().Object,
                warehouseAuth.Object,
                currentUser.Object,
                new Mock<IStockReservationService>().Object);
            var erDto = await exportSvc.GetByIdAsync(1);
            erDto.CreatedByName.Should().Be("Nhân viên Kho WH01");
            erDto.ApprovedByName.Should().Be("Quản lý Kho WH01");
            erDto.DispatchedByName.Should().Be("Giám sát Kho WH01");
        }

        [Fact]
        public async Task Wave35_StocktakeAdjustmentProposal_UsesPureVietnameseSodStatus_WithoutEnglishAbbreviation()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "QA-HCM", Name = "Kho Chính TP.HCM", IsActive = true });
            db.Products.Add(new ERP.Domain.Entities.Product { Id = 101, Code = "SKU-W35-02", Name = "Sản phẩm kiểm kê W35", UnitId = 1, IsActive = true });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new StocktakesController(
                new Mock<IStocktakeService>().Object,
                new Mock<IStocktakeQueryService>().Object,
                db,
                warehouseAuth.Object);

            var createRes = await controller.CreateAdjustmentProposal(
                new StocktakesController.CreateAdjustmentProposalRequest(WarehouseId: 1, ProductCode: "SKU-W35-02", DeltaQuantity: -2m),
                default);
            var createOk = createRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(createOk.Value))
            {
                doc.RootElement.GetProperty("status").GetString().Should().Be("Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm");
            }

            var listRes = await controller.GetReasonCodesAndAdjustments(1, default);
            var listOk = listRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(listOk.Value))
            {
                var firstProposal = doc.RootElement.GetProperty("recentProposals")[0];
                firstProposal.GetProperty("status").GetString().Should().Be("Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm");
                firstProposal.GetProperty("statusLabel").GetString().Should().Be("Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave36_InventorySixTabs_NormalizeUnitNameActorNameAndTrimStatusReferenceType()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Users.Add(new ERP.Domain.Entities.User
            {
                Id = 3601,
                Username = "qa_manager_wh01",
                FullName = "QA Manager WH01 ",
                PasswordHash = "x"
            });
            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse
            {
                Id = 1,
                Code = "WH01 ",
                Name = "Kho Trung Tâm WH01 "
            });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Code = "A-01-01 ",
                Name = "Kệ A1 "
            });
            db.Units.Add(new ERP.Domain.Entities.Unit
            {
                Id = 1,
                Code = "QA_UNIT",
                Name = "QA-PCS "
            });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 360,
                Code = "SKU-W36-01 ",
                Name = "Thùng carton đóng gói 5 lớp ",
                UnitId = 1,
                IsActive = true
            });
            db.InventoryLots.Add(new ERP.Domain.Entities.InventoryLot
            {
                Id = 3600,
                ProductId = 360,
                LotNumber = "LOT-W36-01 ",
                ManufactureDate = DateTime.UtcNow.Date.AddDays(-60),
                ExpiryDate = DateTime.UtcNow.Date.AddDays(15)
            });
            db.InventoryStatusDefinitions.Add(new ERP.Domain.Entities.InventoryStatusDefinition
            {
                Id = (int)ERP.Domain.Enums.InventoryStatus.Available,
                Code = "AVAILABLE ",
                Name = "Khả dụng",
                IsAvailable = true,
                IsReservable = true,
                IsAllocatable = true,
                IsPickable = true,
                IsShippable = true
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 3601,
                WarehouseId = 1,
                ProductId = 360,
                LocationId = 10,
                LotId = 3600,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 50m,
                ReservedQuantity = 5m,
                LastUpdated = DateTime.UtcNow
            });
            db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
            {
                Id = 3602,
                WarehouseId = 1,
                ProductId = 360,
                LocationId = 10,
                LotId = 3600,
                TransactionType = ERP.Domain.Enums.TransactionType.Import,
                InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 50m,
                ReferenceId = 9001,
                ReferenceType = "ImportReceipt ",
                TransactionDate = DateTime.UtcNow.AddHours(-2),
                CreatedBy = 3601,
                Note = "Nhập kho lô hàng kiểm định Wave 36 "
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var currentUser = new Mock<ICurrentUser>();
            currentUser.SetupGet(x => x.UserId).Returns(3601);

            var txQuery = new ERP.Infrastructure.Queries.InventoryTransactionQueryService(db, warehouseAuth.Object);
            var txPage = await txQuery.GetHistoryAsync(null, null, null, 1, null, null, null, null, 1, 10);
            txPage.Items.Should().ContainSingle();
            txPage.Items[0].UnitName.Should().Be("THÙNG");
            txPage.Items[0].CreatedByName.Should().Be("Quản lý Kho WH01");
            txPage.Items[0].ReferenceType.Should().Be("Phiếu nhập kho");
            txPage.Items[0].InventoryStatus.Should().Be("Khả dụng");

            var statusService = new ERP.Infrastructure.Services.InventoryStatusService(db, warehouseAuth.Object, currentUser.Object);
            var buckets = await statusService.GetBucketsAsync(warehouseId: 1);
            buckets.Should().ContainSingle();
            buckets[0].Status.Should().Be("AVAILABLE");
            buckets[0].ProductCode.Should().Be("SKU-W36-01");
            buckets[0].LotNumber.Should().Be("LOT-W36-01");

            var reportsController = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var inOutRes = await reportsController.GetInventoryInOutReport(
                DateTime.UtcNow.AddDays(-7),
                DateTime.UtcNow.AddDays(1),
                1,
                null);
            var inOutOk = inOutRes.Should().BeOfType<OkObjectResult>().Subject;
            var inOutList = inOutOk.Value.Should().BeAssignableTo<IEnumerable<InventoryInOutReportDto>>().Subject.ToList();
            inOutList.Should().ContainSingle();
            inOutList[0].UnitName.Should().Be("THÙNG");
            inOutList[0].ProductCode.Should().Be("SKU-W36-01");

            var expiryRes = await reportsController.GetExpiryAgingReport(db, warehouseAuth.Object, warehouseId: 1, productId: null, nearExpiryDays: 30, cancellationToken: default);
            var expiryOk = expiryRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(expiryOk.Value))
            {
                var first = doc.RootElement[0];
                first.GetProperty("UnitName").GetString().Should().Be("THÙNG");
                first.GetProperty("Status").GetString().Should().Be("Khả dụng");
                first.GetProperty("StatusLabel").GetString().Should().Be("Khả dụng");
                first.GetProperty("ExpiryStatusLabel").GetString().Should().Be("Cận hạn sử dụng");
            }
        }

        [Fact]
        public async Task Wave36_ImportReceipts_RmaIntakeAndExecuteRtvRefurbish_ReturnPureVietnameseDispositionLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 3610,
                WarehouseId = 1,
                ProductId = 360,
                LocationId = 10,
                Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                Quantity = 6m,
                ReservedQuantity = 0m,
                LastUpdated = DateTime.UtcNow
            });
            db.ImportReceipts.Add(new ERP.Domain.Entities.ImportReceipt
            {
                Id = 3611,
                WarehouseId = 1,
                Code = "IR-W36-01",
                Status = ERP.Domain.Enums.ReceiptStatus.Draft
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new ImportReceiptsController(
                new Mock<IImportReceiptService>().Object,
                null,
                db,
                warehouseAuth.Object);

            var rmaRes = await controller.RmaIntakeCheck(
                new ImportReceiptsController.RmaIntakeCheckRequest("IR-W36-01", "Kiểm định hàng hoàn trả", 1, "RMA-W36-01"),
                default);
            var rmaOk = rmaRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(rmaOk.Value))
            {
                doc.RootElement.GetProperty("status").GetString().Should().Be("Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ");
                doc.RootElement.GetProperty("targetBucket").GetString().Should().Be("Khu vực biệt trữ chờ kiểm định");
                doc.RootElement.GetProperty("nextStep").GetString().Should().Be("Chuyển trạm kiểm định chất lượng đầu vào");
                doc.RootElement.GetProperty("dispositionSummary").GetString().Should().Contain("Đã tiếp nhận kiểm định hàng trả về");
            }

            var dispRes = await controller.ExecuteRtvRefurbish(
                new ImportReceiptsController.ExecuteRtvRefurbishRequest(WarehouseId: 1),
                default);
            var dispOk = dispRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(dispOk.Value))
            {
                doc.RootElement.GetProperty("status").GetString().Should().Be("Đã thực thi & Ghi sổ kiểm toán");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã thực thi & Ghi sổ kiểm toán");
                doc.RootElement.GetProperty("dispositionType").GetString().Should().Be("Tân trang hàng trả về & chuyển về tồn khả dụng");
                doc.RootElement.GetProperty("actionType").GetString().Should().Be("Tân trang hàng trả về & chuyển về tồn khả dụng");
                doc.RootElement.GetProperty("dispositionSummary").GetString().Should().Contain("Tân trang hàng trả về & chuyển về tồn khả dụng");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave37_InventoryStatus_VerifyBucketInvariants_ReturnsPureVietnameseWithoutSodOrRecallOrQc()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse
            {
                Id = 1,
                Code = "WH01",
                Name = "Kho Trung Tâm WH01",
                IsActive = true
            });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Code = "A-01-01",
                Name = "Kệ lưu trữ A01",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsPickable = true
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 3701,
                WarehouseId = 1,
                ProductId = 370,
                LocationId = 10,
                Status = ERP.Domain.Enums.InventoryStatus.Available,
                Quantity = 120m,
                ReservedQuantity = 15m,
                LastUpdated = DateTime.UtcNow
            });
            db.InventoryLocks.AddRange(
                new ERP.Domain.Entities.InventoryLock
                {
                    Id = 3701,
                    WarehouseId = 1,
                    ProductId = 370,
                    LockType = ERP.Domain.Enums.InventoryLockType.RecallHold,
                    Status = ERP.Domain.Enums.InventoryLockStatus.Active,
                    Reason = "Thu hồi lô hàng kiểm định Wave 37",
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.InventoryLock
                {
                    Id = 3702,
                    WarehouseId = 1,
                    ProductId = 370,
                    LockType = ERP.Domain.Enums.InventoryLockType.CountFreeze,
                    Status = ERP.Domain.Enums.InventoryLockStatus.Active,
                    Reason = "Đóng băng kiểm kê định kỳ Wave 37",
                    CreatedBy = 1
                });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new InventoryStatusController(
                new Mock<IInventoryStatusService>().Object,
                db,
                warehouseAuth.Object);

            var bckRes = await controller.VerifyBucketInvariants(
                new InventoryStatusController.VerifyBucketInvariantsRequest(1),
                default);
            var bckOk = bckRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(bckOk.Value))
            {
                var guard = doc.RootElement.GetProperty("scrapApprovalGuard").GetString();
                guard.Should().Be("Bắt buộc phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm khi hủy/thanh lý");
                guard.Should().NotContain("SoD");
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực bất biến 5 trạng thái tồn kho & phân vùng biệt trữ");
            }

            var lckRes = await controller.VerifyLockPrecedence(
                new InventoryStatusController.VerifyLockPrecedenceRequest(1),
                default);
            var lckOk = lckRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(lckOk.Value))
            {
                var precedence = doc.RootElement.GetProperty("overlappingHoldPrecedence").GetString();
                precedence.Should().Be("Thu hồi lô hàng > Kiểm định chất lượng > Đóng băng kiểm kê > Khóa vận hành");
                precedence.Should().NotContain("Recall").And.NotContain("QC");
                doc.RootElement.GetProperty("activeLocksCount").GetInt32().Should().Be(2);
                doc.RootElement.GetProperty("recallLocksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("countFreezeLocksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã xác thực thứ tự ưu tiên khóa tồn kho (0 rò rỉ xuất hàng)");
            }

            db.AuditLogs.Should().HaveCount(2);
            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave37_InventoryTransactionsAndExpiryAging_NormalizeMatchNoteStatusAndReferenceTypeToVietnamese()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Users.Add(new ERP.Domain.Entities.User
            {
                Id = 3701,
                Username = "qa_staff_wh01",
                FullName = "QA Staff WH01 ",
                PasswordHash = "x"
            });
            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse
            {
                Id = 1,
                Code = "WH01 ",
                Name = "Kho Trung Tâm WH01 "
            });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Code = "A-01-01 ",
                Name = "Kệ A1 "
            });
            db.Units.Add(new ERP.Domain.Entities.Unit
            {
                Id = 1,
                Code = "QA_UNIT",
                Name = "QA_UNIT"
            });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 370,
                Code = "SKU-W37-01 ",
                Name = "Thùng nhựa công nghiệp Wave 37 ",
                UnitId = 1,
                IsActive = true
            });
            db.InventoryLots.Add(new ERP.Domain.Entities.InventoryLot
            {
                Id = 3700,
                ProductId = 370,
                LotNumber = "LOT-W37-01 ",
                ManufactureDate = DateTime.UtcNow.Date.AddDays(-30),
                ExpiryDate = DateTime.UtcNow.Date.AddDays(20)
            });
            db.InventoryStocks.Add(new ERP.Domain.Entities.InventoryStock
            {
                Id = 3701,
                WarehouseId = 1,
                ProductId = 370,
                LocationId = 10,
                LotId = 3700,
                Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                Quantity = 40m,
                ReservedQuantity = 0m,
                LastUpdated = DateTime.UtcNow
            });
            db.InventoryTransactions.AddRange(
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 3702,
                    WarehouseId = 1,
                    ProductId = 370,
                    LocationId = 10,
                    LotId = 3700,
                    TransactionType = ERP.Domain.Enums.TransactionType.Import,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.QcHold,
                    ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    Quantity = 40m,
                    ReferenceId = 9701,
                    ReferenceType = "QASeedReconcile ",
                    TransactionDate = DateTime.UtcNow.AddHours(-1),
                    CreatedBy = 3701,
                    Note = "Đồng bộ đối chiếu sổ cái tồn kho chuẩn QA (100% Match) "
                },
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 3703,
                    WarehouseId = 1,
                    ProductId = 370,
                    LocationId = 10,
                    LotId = 3700,
                    TransactionType = ERP.Domain.Enums.TransactionType.Move,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 10m,
                    ReferenceId = 9702,
                    ReferenceType = "InternalMove ",
                    TransactionDate = DateTime.UtcNow.AddHours(-2),
                    CreatedBy = 3701,
                    Note = "Luân chuyển kệ nội bộ "
                });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var txQuery = new ERP.Infrastructure.Queries.InventoryTransactionQueryService(db, warehouseAuth.Object);
            var txPage = await txQuery.GetHistoryAsync(null, null, null, 1, null, null, null, null, 1, 10);
            txPage.Items.Should().HaveCount(2);
            var txFirst = txPage.Items[0];
            txFirst.UnitName.Should().Be("THÙNG");
            txFirst.CreatedByName.Should().Be("Nhân viên Kho WH01");
            txFirst.Note.Should().Be("Đồng bộ đối chiếu sổ cái tồn kho chuẩn QA (Khớp 100%)");
            txFirst.Note.Should().NotContain("100% Match");
            txFirst.ReferenceType.Should().Be("Đối soát sổ cái");
            txFirst.InventoryStatus.Should().Be("Cách ly");
            txFirst.FromInventoryStatus.Should().Be("Tạm giữ chờ kiểm định");
            txFirst.ToInventoryStatus.Should().Be("Cách ly");

            var txSecond = txPage.Items[1];
            txSecond.ReferenceType.Should().Be("Di chuyển nội bộ");
            txSecond.InventoryStatus.Should().Be("Khả dụng");

            var reportsController = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var expiryRes = await reportsController.GetExpiryAgingReport(
                db,
                warehouseAuth.Object,
                warehouseId: 1,
                productId: null,
                nearExpiryDays: 30,
                cancellationToken: default);
            var expiryOk = expiryRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(expiryOk.Value))
            {
                var first = doc.RootElement[0];
                first.GetProperty("UnitName").GetString().Should().Be("THÙNG");
                first.GetProperty("Status").GetString().Should().Be("Cách ly");
                first.GetProperty("StatusLabel").GetString().Should().Be("Cách ly");
                first.GetProperty("ExpiryStatusLabel").GetString().Should().Be("Cận hạn sử dụng");
            }
        }

        [Fact]
        public async Task Wave38_GetExpiryAgingReport_EnforcesZeroAvailableQuantity_ForNonAvailableOrExpiredBuckets()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var today = DateTime.UtcNow.Date;

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse
            {
                Id = 1,
                Code = "WH01",
                Name = "Kho Trung Tâm WH01",
                IsActive = true
            });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Code = "A-01-01",
                Name = "Kệ A1",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsPickable = true
            });
            db.Units.Add(new ERP.Domain.Entities.Unit
            {
                Id = 1,
                Code = "THÙNG",
                Name = "THÙNG",
                IsActive = true
            });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 380,
                Code = "SKU-W38-01",
                Name = "Sản phẩm kiểm thử FEFO Wave 38",
                UnitId = 1,
                IsActive = true
            });
            db.InventoryLots.AddRange(
                new ERP.Domain.Entities.InventoryLot
                {
                    Id = 3801,
                    ProductId = 380,
                    LotNumber = "LOT-VALID-01",
                    ManufactureDate = today.AddDays(-20),
                    ExpiryDate = today.AddDays(45)
                },
                new ERP.Domain.Entities.InventoryLot
                {
                    Id = 3802,
                    ProductId = 380,
                    LotNumber = "LOT-EXPIRED-02",
                    ManufactureDate = today.AddDays(-120),
                    ExpiryDate = today.AddDays(-5)
                });

            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3811,
                    WarehouseId = 1,
                    ProductId = 380,
                    LocationId = 10,
                    LotId = 3801,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 100m,
                    ReservedQuantity = 15m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3812,
                    WarehouseId = 1,
                    ProductId = 380,
                    LocationId = 10,
                    LotId = 3802,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 60m,
                    ReservedQuantity = 5m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3813,
                    WarehouseId = 1,
                    ProductId = 380,
                    LocationId = 10,
                    LotId = 3801,
                    Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    Quantity = 40m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3814,
                    WarehouseId = 1,
                    ProductId = 380,
                    LocationId = 10,
                    LotId = 3801,
                    Status = ERP.Domain.Enums.InventoryStatus.Damaged,
                    Quantity = 25m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3815,
                    WarehouseId = 1,
                    ProductId = 380,
                    LocationId = 10,
                    LotId = 3801,
                    Status = ERP.Domain.Enums.InventoryStatus.QcHold,
                    Quantity = 30m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3816,
                    WarehouseId = 1,
                    ProductId = 380,
                    LocationId = 10,
                    LotId = 3801,
                    Status = ERP.Domain.Enums.InventoryStatus.RecallBlocked,
                    Quantity = 50m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var res = await controller.GetExpiryAgingReport(
                db,
                warehouseAuth.Object,
                warehouseId: 1,
                productId: 380,
                nearExpiryDays: 30,
                cancellationToken: default);
            var ok = res.Should().BeOfType<OkObjectResult>().Subject;

            using var doc = System.Text.Json.JsonSerializer.SerializeToDocument(ok.Value);
            var items = doc.RootElement.EnumerateArray().ToList();
            items.Should().HaveCount(6);

            var byStockId = items.ToDictionary(
                x => x.GetProperty("InventoryStockId").GetInt32(),
                x => x);

            byStockId[3811].GetProperty("AvailableQuantity").GetDecimal().Should().Be(85m);
            byStockId[3811].GetProperty("ExpiryStatusLabel").GetString().Should().Be("Còn hạn tốt");

            byStockId[3812].GetProperty("AvailableQuantity").GetDecimal().Should().Be(0m);
            byStockId[3812].GetProperty("ExpiryStatus").GetString().Should().Be("EXPIRED");
            byStockId[3812].GetProperty("ExpiryStatusLabel").GetString().Should().Be("Đã hết hạn");

            byStockId[3813].GetProperty("AvailableQuantity").GetDecimal().Should().Be(0m);
            byStockId[3813].GetProperty("StatusLabel").GetString().Should().Be("Cách ly");

            byStockId[3814].GetProperty("AvailableQuantity").GetDecimal().Should().Be(0m);
            byStockId[3814].GetProperty("StatusLabel").GetString().Should().Be("Hư hỏng");

            byStockId[3815].GetProperty("AvailableQuantity").GetDecimal().Should().Be(0m);
            byStockId[3815].GetProperty("StatusLabel").GetString().Should().Be("Tạm giữ chờ kiểm định");

            byStockId[3816].GetProperty("AvailableQuantity").GetDecimal().Should().Be(0m);
            byStockId[3816].GetProperty("StatusLabel").GetString().Should().Be("Phong tỏa thu hồi");
        }

        [Fact]
        public async Task Wave38_GetAnalyticsSummary_ComputesRealTurnaroundHours_AndVietnameseMovementClassLabels()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var now = DateTime.UtcNow;

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse
            {
                Id = 1,
                Code = "WH01",
                Name = "Kho Trung Tâm WH01",
                IsActive = true
            });
            db.Units.Add(new ERP.Domain.Entities.Unit
            {
                Id = 1,
                Code = "QA_UNIT",
                Name = "QA_UNIT",
                IsActive = true
            });
            db.Products.AddRange(
                new ERP.Domain.Entities.Product { Id = 381, Code = "SKU-FAST-38", Name = "Sản phẩm nhanh 38", UnitId = 1, IsActive = true },
                new ERP.Domain.Entities.Product { Id = 382, Code = "SKU-NORM-38", Name = "Sản phẩm thường 38", UnitId = 1, IsActive = true },
                new ERP.Domain.Entities.Product { Id = 383, Code = "SKU-SLOW-38", Name = "Sản phẩm chậm 38", UnitId = 1, IsActive = true },
                new ERP.Domain.Entities.Product { Id = 384, Code = "SKU-IDLE-38", Name = "Sản phẩm đứng 38", UnitId = 1, IsActive = true });

            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock { Id = 3821, WarehouseId = 1, ProductId = 381, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 100m, ReservedQuantity = 0m },
                new ERP.Domain.Entities.InventoryStock { Id = 3822, WarehouseId = 1, ProductId = 382, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 100m, ReservedQuantity = 0m },
                new ERP.Domain.Entities.InventoryStock { Id = 3823, WarehouseId = 1, ProductId = 383, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 100m, ReservedQuantity = 0m },
                new ERP.Domain.Entities.InventoryStock { Id = 3824, WarehouseId = 1, ProductId = 384, Status = ERP.Domain.Enums.InventoryStatus.Available, Quantity = 100m, ReservedQuantity = 0m });

            db.InventoryTransactions.AddRange(
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 3831,
                    WarehouseId = 1,
                    ProductId = 381,
                    TransactionType = ERP.Domain.Enums.TransactionType.Export,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 50m,
                    TransactionDate = now.AddDays(-2),
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 3832,
                    WarehouseId = 1,
                    ProductId = 382,
                    TransactionType = ERP.Domain.Enums.TransactionType.Export,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 20m,
                    TransactionDate = now.AddDays(-2),
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 3833,
                    WarehouseId = 1,
                    ProductId = 383,
                    TransactionType = ERP.Domain.Enums.TransactionType.Export,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 5m,
                    TransactionDate = now.AddDays(-2),
                    CreatedBy = 1
                });

            db.ImportReceipts.AddRange(
                new ERP.Domain.Entities.ImportReceipt
                {
                    Id = 3841,
                    Code = "IR-W38-01",
                    WarehouseId = 1,
                    Status = ERP.Domain.Enums.ReceiptStatus.Posted,
                    CreatedAt = now.AddHours(-10),
                    ApprovedAt = now.AddHours(-8),
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.ImportReceipt
                {
                    Id = 3842,
                    Code = "IR-W38-02",
                    WarehouseId = 1,
                    Status = ERP.Domain.Enums.ReceiptStatus.Posted,
                    CreatedAt = now.AddHours(-9),
                    ApprovedAt = now.AddHours(-6),
                    CreatedBy = 1
                });

            db.ExportReceipts.AddRange(
                new ERP.Domain.Entities.ExportReceipt
                {
                    Id = 3851,
                    Code = "ER-W38-01",
                    WarehouseId = 1,
                    Status = ERP.Domain.Enums.ReceiptStatus.Dispatched,
                    CreatedAt = now.AddHours(-12),
                    ApprovedAt = now.AddHours(-10),
                    DispatchedAt = now.AddHours(-8),
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.ExportReceipt
                {
                    Id = 3852,
                    Code = "ER-W38-02",
                    WarehouseId = 1,
                    Status = ERP.Domain.Enums.ReceiptStatus.Dispatched,
                    CreatedAt = now.AddHours(-14),
                    ApprovedAt = now.AddHours(-11),
                    DispatchedAt = now.AddHours(-8),
                    CreatedBy = 1
                });

            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var res = await controller.GetAnalyticsSummary(db, warehouseAuth.Object, warehouseId: 1, days: 30, cancellationToken: default);
            var ok = res.Should().BeOfType<OkObjectResult>().Subject;

            using var doc = System.Text.Json.JsonSerializer.SerializeToDocument(ok.Value);
            var root = doc.RootElement;

            root.GetProperty("avgInboundTurnaroundHours").GetDecimal().Should().Be(2.5m);
            root.GetProperty("avgOutboundTurnaroundHours").GetDecimal().Should().Be(5.0m);
            root.GetProperty("inboundOutboundSla").GetProperty("avgInboundTurnaroundHours").GetDecimal().Should().Be(2.5m);
            root.GetProperty("inboundOutboundSla").GetProperty("avgOutboundTurnaroundHours").GetDecimal().Should().Be(5.0m);

            var turnoverRows = root.GetProperty("turnoverRows").EnumerateArray().ToList();
            turnoverRows.Should().HaveCount(4);

            var byCode = turnoverRows.ToDictionary(
                x => x.GetProperty("productCode").GetString()!,
                x => x);

            byCode["SKU-FAST-38"].GetProperty("movementClassLabel").GetString().Should().Be("Luân chuyển nhanh");
            byCode["SKU-FAST-38"].GetProperty("unitName").GetString().Should().Be("THÙNG");

            byCode["SKU-NORM-38"].GetProperty("movementClassLabel").GetString().Should().Be("Luân chuyển bình thường");
            byCode["SKU-SLOW-38"].GetProperty("movementClassLabel").GetString().Should().Be("Luân chuyển chậm");
            byCode["SKU-IDLE-38"].GetProperty("movementClassLabel").GetString().Should().Be("Chưa phát sinh xuất");
        }

        [Fact]
        public async Task Wave39_AdvancedPlanningSuite_And_RunWhatIfSimulation_ExcludeExpiredLotsAndNonAvailableBuckets_MatchingExact211AvailableStock_WithThungUnitInFormulaSummary()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var today = DateTime.UtcNow.Date;

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse
            {
                Id = 1,
                Code = "WH01",
                Name = "Kho Trung Tâm WH01",
                IsActive = true
            });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Code = "A-01-01",
                Name = "Kệ A1",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsPickable = true
            });
            db.Units.Add(new ERP.Domain.Entities.Unit
            {
                Id = 1,
                Code = "THÙNG",
                Name = "THÙNG",
                IsActive = true
            });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 390,
                Code = "SKU-W39-01",
                Name = "Sản phẩm hoạch định Wave 39",
                UnitId = 1,
                IsActive = true,
                TrackingType = ERP.Domain.Enums.ProductTrackingType.Lot,
                ExpiryControl = true
            });

            db.InventoryLots.AddRange(
                new ERP.Domain.Entities.InventoryLot
                {
                    Id = 3901,
                    ProductId = 390,
                    LotNumber = "LOT-VALID-39A",
                    ManufactureDate = today.AddDays(-30),
                    ExpiryDate = today.AddDays(60)
                },
                new ERP.Domain.Entities.InventoryLot
                {
                    Id = 3902,
                    ProductId = 390,
                    LotNumber = "LOT-VALID-39B",
                    ManufactureDate = today.AddDays(-20),
                    ExpiryDate = today.AddDays(15)
                },
                new ERP.Domain.Entities.InventoryLot
                {
                    Id = 3903,
                    ProductId = 390,
                    LotNumber = "LOT-EXPIRED-39C",
                    ManufactureDate = today.AddDays(-120),
                    ExpiryDate = today.AddDays(-3)
                });

            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3911,
                    WarehouseId = 1,
                    ProductId = 390,
                    LocationId = 10,
                    LotId = 3901,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 125m,
                    ReservedQuantity = 5m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3912,
                    WarehouseId = 1,
                    ProductId = 390,
                    LocationId = 10,
                    LotId = 3902,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 95m,
                    ReservedQuantity = 4m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3913,
                    WarehouseId = 1,
                    ProductId = 390,
                    LocationId = 10,
                    LotId = 3903,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 60m,
                    ReservedQuantity = 5m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3914,
                    WarehouseId = 1,
                    ProductId = 390,
                    LocationId = 10,
                    LotId = 3901,
                    Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    Quantity = 25m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3915,
                    WarehouseId = 1,
                    ProductId = 390,
                    LocationId = 10,
                    LotId = 3901,
                    Status = ERP.Domain.Enums.InventoryStatus.Damaged,
                    Quantity = 12m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3916,
                    WarehouseId = 1,
                    ProductId = 390,
                    LocationId = 10,
                    LotId = 3901,
                    Status = ERP.Domain.Enums.InventoryStatus.QcHold,
                    Quantity = 30m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3917,
                    WarehouseId = 1,
                    ProductId = 390,
                    LocationId = 10,
                    LotId = 3901,
                    Status = ERP.Domain.Enums.InventoryStatus.RecallBlocked,
                    Quantity = 50m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 3918,
                    WarehouseId = 1,
                    ProductId = 390,
                    LocationId = 10,
                    LotId = 3903,
                    Status = ERP.Domain.Enums.InventoryStatus.Expired,
                    Quantity = 20m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                });

            db.PurchaseOrders.Add(new ERP.Domain.Entities.PurchaseOrder
            {
                Id = 3901,
                Code = "PO-W39-01",
                WarehouseId = 1,
                SupplierId = 1,
                Status = ERP.Domain.Enums.PurchaseOrderStatus.Open,
                OrderDate = DateTime.UtcNow.AddDays(-1),
                CreatedBy = 1
            });
            db.PurchaseOrderLines.Add(new ERP.Domain.Entities.PurchaseOrderLine
            {
                Id = 3901,
                PurchaseOrderId = 3901,
                LineNo = 1,
                ProductId = 390,
                OrderedQuantity = 50m,
                BaseOrderedQuantity = 50m,
                OperationUnitId = 1,
                BaseUnitId = 1
            });
            db.SalesOrders.Add(new ERP.Domain.Entities.SalesOrder
            {
                Id = 3901,
                OrderCode = "SO-W39-01",
                WarehouseId = 1,
                CustomerId = 1,
                Status = ERP.Domain.Enums.SalesOrderStatus.Released,
                CreatedBy = 1
            });
            db.SalesOrderLines.Add(new ERP.Domain.Entities.SalesOrderLine
            {
                Id = 3901,
                SalesOrderId = 3901,
                ProductId = 390,
                OrderedQuantity = 100m,
                CancelledQuantity = 0m
            });

            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new InventoryControlController(
                new Mock<IInventoryLockService>().Object,
                new Mock<IInventoryMovementService>().Object,
                db,
                warehouseAuth.Object);

            var planRes = await controller.AdvancedPlanningSuite(db, warehouseAuth.Object, 1, default);
            var planOk = planRes.Should().BeOfType<OkObjectResult>().Subject;
            decimal planAvailable;
            using (var planDoc = System.Text.Json.JsonSerializer.SerializeToDocument(planOk.Value))
            {
                var root = planDoc.RootElement;
                planAvailable = root.GetProperty("totalAvailableQty").GetDecimal();
                planAvailable.Should().Be(211m);
                root.GetProperty("currentAvailableStock").GetDecimal().Should().Be(211m);
                root.GetProperty("totalAtpQuantity").GetDecimal().Should().Be(211m);
                root.GetProperty("atpCtpSummary").GetProperty("availableToPromiseQty").GetDecimal().Should().Be(211m);
            }

            var simRes = await controller.RunWhatIfSimulation(
                db,
                warehouseAuth.Object,
                new InventoryControlController.WhatIfSimulationRequest(1, 1.5m, 14, 50m, 0),
                default);
            var simOk = simRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var simDoc = System.Text.Json.JsonSerializer.SerializeToDocument(simOk.Value))
            {
                var root = simDoc.RootElement;
                var simAvailable = root.GetProperty("totalAvailableQty").GetDecimal();
                simAvailable.Should().Be(211m);
                simAvailable.Should().Be(planAvailable);
                root.GetProperty("currentAvailableStock").GetDecimal().Should().Be(211m);
                root.GetProperty("totalAvailable").GetDecimal().Should().Be(211m);
                root.GetProperty("totalAtpQuantity").GetDecimal().Should().Be(211m);
                root.GetProperty("incomingPoQty").GetDecimal().Should().Be(50m);
                root.GetProperty("simulatedDemand").GetDecimal().Should().Be(150m);
                root.GetProperty("projectedEndingStock").GetDecimal().Should().Be(111m);

                var formula = root.GetProperty("formulaSummary").GetString();
                formula.Should().Contain("Thùng");
                formula.Should().Be("Tồn khả dụng (211 Thùng) + Đang nhập PO (50 Thùng) - Cầu kịch bản (150 Thùng) = Dự kiến cuối kỳ (111 Thùng)");
            }

            var reportsController = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var analyticsRes = await reportsController.GetAnalyticsSummary(db, warehouseAuth.Object, 1, 30, default);
            var analyticsOk = analyticsRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var analyticsDoc = System.Text.Json.JsonSerializer.SerializeToDocument(analyticsOk.Value))
            {
                analyticsDoc.RootElement.GetProperty("totalAvailableQty").GetDecimal().Should().Be(211m);
                analyticsDoc.RootElement.GetProperty("currentAvailableStock").GetDecimal().Should().Be(211m);
                analyticsDoc.RootElement.GetProperty("totalAtpQuantity").GetDecimal().Should().Be(211m);
            }
        }

        [Fact]
        public async Task Wave39_InventoryTransactionQueryService_And_ReportsController_NormalizeChoQcNote_ToVietnameseQualityInspectionLabel()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Users.Add(new ERP.Domain.Entities.User
            {
                Id = 3901,
                Username = "qa_staff_wh01",
                FullName = "QA Staff WH01",
                PasswordHash = "x"
            });
            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse
            {
                Id = 1,
                Code = "WH01",
                Name = "Kho Trung Tâm WH01",
                IsActive = true
            });
            db.Units.Add(new ERP.Domain.Entities.Unit
            {
                Id = 1,
                Code = "QA_UNIT",
                Name = "QA_UNIT",
                IsActive = true
            });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 391,
                Code = "SKU-W39-02",
                Name = "Sản phẩm sổ cái Wave 39",
                UnitId = 1,
                IsActive = true
            });

            db.InventoryTransactions.AddRange(
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 3921,
                    WarehouseId = 1,
                    ProductId = 391,
                    TransactionType = ERP.Domain.Enums.TransactionType.Import,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 75m,
                    ReferenceId = 3901,
                    ReferenceType = "ImportReceipt",
                    TransactionDate = DateTime.UtcNow.AddHours(-1),
                    CreatedBy = 3901,
                    Note = "Nhập kho lô hàng tạm giữ chờ QC (100% Match) "
                },
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 3922,
                    WarehouseId = 1,
                    ProductId = 391,
                    TransactionType = ERP.Domain.Enums.TransactionType.StatusChange,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.QcHold,
                    Quantity = 15m,
                    ReferenceId = 3902,
                    ReferenceType = "InventoryStatusChange",
                    TransactionDate = DateTime.UtcNow.AddHours(-2),
                    CreatedBy = 3901,
                    Note = "Lô cận hạn đang cách ly chờ QC "
                });

            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var txQuery = new ERP.Infrastructure.Queries.InventoryTransactionQueryService(db, warehouseAuth.Object);
            var txPage = await txQuery.GetHistoryAsync(null, null, null, 1, 391, null, null, null, 1, 10);
            txPage.Items.Should().HaveCount(2);

            txPage.Items[0].Note.Should().Be("Nhập kho lô hàng tạm giữ chờ kiểm định chất lượng (Khớp 100%)");
            txPage.Items[0].Note.Should().NotContain("chờ QC").And.NotContain("100% Match");

            txPage.Items[1].Note.Should().Be("Lô cận hạn đang cách ly chờ kiểm định chất lượng");
            txPage.Items[1].Note.Should().NotContain("chờ QC");

            ReportsController.NormalizeTransactionNote(" Lô hàng nhập chờ QC (100% Match) ")
                .Should().Be("Lô hàng nhập chờ kiểm định chất lượng (Khớp 100%)");

            var reportsController = new ReportsController(_mockService.Object, db, warehouseAuth.Object);
            var inOutRes = await reportsController.GetInventoryInOutReport(
                DateTime.UtcNow.AddDays(-7),
                DateTime.UtcNow.AddDays(1),
                1,
                391);
            var inOutOk = inOutRes.Should().BeOfType<OkObjectResult>().Subject;
            var inOutList = inOutOk.Value.Should().BeAssignableTo<IEnumerable<InventoryInOutReportDto>>().Subject.ToList();
            inOutList.Should().ContainSingle();
            inOutList[0].UnitName.Should().Be("THÙNG");
            inOutList[0].ImportQuantity.Should().Be(75m);
            inOutList[0].ClosingQuantity.Should().Be(75m);
        }

        [Fact]
        public async Task Wave40_WarehouseService_SeedsAllTenSystemLocations_And_DeletesEmptyWarehouseCleanly()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(It.IsAny<int>(), default)).Returns(Task.CompletedTask);
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });

            var repo = new ERP.Infrastructure.Repositories.WarehouseRepository(db);
            var service = new ERP.Application.Services.WarehouseService(repo, warehouseAuth.Object);

            var created = await service.CreateWarehouseAsync(new CreateWarehouseDto
            {
                Code = "WH-W40",
                Name = "Kho Kiểm Thử Wave 40",
                Address = "Khu Công Nghiệp Tân Bình"
            }, "admin");

            var locations = await db.WarehouseLocations
                .Where(x => x.WarehouseId == created.Id)
                .OrderBy(x => x.Code)
                .ToListAsync();

            var expectedCodes = new[]
            {
                "RECEIVING",
                "STAGING",
                "QC_HOLD",
                "QUARANTINE",
                "DAMAGED",
                "RETURNS_STAGE",
                "PACKING_STAGE",
                "SHIPPING_STAGE",
                "SCRAP",
                "LEGACY"
            };

            locations.Select(x => x.Code).Should().BeEquivalentTo(expectedCodes);
            locations.Should().OnlyContain(x => x.IsSystemManaged && x.IsActive);

            locations.Count(x => x.LocationType == ERP.Domain.Enums.WarehouseLocationType.Receiving).Should().Be(1);
            var damaged = locations.Single(x => x.Code == "DAMAGED");
            damaged.LocationType.Should().Be(ERP.Domain.Enums.WarehouseLocationType.Damaged);
            damaged.IsPickable.Should().BeFalse();
            damaged.IsReceivable.Should().BeFalse();

            await service.DeleteWarehouseAsync(created.Id);

            (await db.Warehouses.AnyAsync(x => x.Id == created.Id)).Should().BeFalse();
            (await db.WarehouseLocations.AnyAsync(x => x.WarehouseId == created.Id)).Should().BeFalse();
        }

        [Fact]
        public async Task Wave40_ProductsController_SupportsProductUomQcPolicyAndTolerancePolicyCrud()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Units.AddRange(
                new ERP.Domain.Entities.Unit { Id = 1, Code = "CAI", Name = "Cái", IsActive = true },
                new ERP.Domain.Entities.Unit { Id = 2, Code = "THUNG", Name = "Thùng", IsActive = true });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 4001,
                Code = "SKU-W40-01",
                Name = "Sản phẩm Cấu hình Wave 40",
                UnitId = 1,
                IsActive = true
            });
            await db.SaveChangesAsync();

            var productService = new Mock<IProductService>();
            var controller = new ProductsController(productService.Object, db);

            var createUomRes = await controller.UpsertProductUom(
                4001,
                new ProductsController.UpsertProductUomRequest(
                    UnitId: 2,
                    ConversionFactor: 24m,
                    IsPurchaseDefault: true,
                    IsSalesDefault: false),
                default);
            createUomRes.Should().BeOfType<OkObjectResult>();

            var listUomsRes = await controller.GetProductUoms(4001, default);
            var listUomsOk = listUomsRes.Should().BeOfType<OkObjectResult>().Subject;
            int createdUomId;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(listUomsOk.Value))
            {
                var uoms = doc.RootElement.EnumerateArray().ToList();
                uoms.Should().ContainSingle();
                uoms[0].GetProperty("conversionFactor").GetDecimal().Should().Be(24m);
                createdUomId = uoms[0].GetProperty("id").GetInt32();
            }

            var createQcRes = await controller.UpsertQcPolicy(
                new ProductsController.UpsertQcPolicyRequest(
                    ProductId: 4001,
                    Mode: "Full",
                    SamplePercentage: 100m,
                    RequireBeforePutaway: true),
                default);
            createQcRes.Should().BeOfType<OkObjectResult>();

            var listQcRes = await controller.GetQcPolicies(4001, null, default);
            var listQcOk = listQcRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(listQcOk.Value))
            {
                var policies = doc.RootElement.EnumerateArray().ToList();
                policies.Should().ContainSingle();
                policies[0].GetProperty("mode").GetString().Should().Be("Full");
                policies[0].GetProperty("modeLabel").GetString().Should().Be("Kiểm định toàn phần (100%)");
            }

            var createTolRes = await controller.UpsertTolerancePolicy(
                new ProductsController.UpsertTolerancePolicyRequest(
                    ProductId: 4001,
                    WarehouseId: 1,
                    OverReceiptTolerancePercent: 2.5m,
                    AbsoluteQuantityTolerance: 5m),
                default);
            createTolRes.Should().BeOfType<OkObjectResult>();

            var listTolRes = await controller.GetTolerancePolicies(4001, null, default);
            var listTolOk = listTolRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(listTolOk.Value))
            {
                var items = doc.RootElement.EnumerateArray().ToList();
                items.Should().ContainSingle();
                items[0].GetProperty("overReceiptTolerancePercent").GetDecimal().Should().Be(2.5m);
                items[0].GetProperty("warehouseId").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
            }

            var deleteUomRes = await controller.DeleteProductUom(4001, createdUomId, default);
            deleteUomRes.Should().BeOfType<OkObjectResult>();
            (await db.ProductUoms.Where(x => x.Id == createdUomId).AnyAsync()).Should().BeFalse();
        }

        [Fact]
        public async Task Wave40_OperationalEndpoints_StocktakeAdjustmentApprove_RtvRefurbishMutation_And_PickingWaveRelease_PersistRealStateAndLedger()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Trung Tâm WH01", IsActive = true });
            db.WarehouseLocations.Add(new ERP.Domain.Entities.WarehouseLocation
            {
                Id = 10,
                WarehouseId = 1,
                Code = "A-01-01",
                Name = "Kệ lưu trữ A01",
                LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                IsActive = true,
                IsPickable = true
            });
            db.Units.Add(new ERP.Domain.Entities.Unit { Id = 1, Code = "THÙNG", Name = "THÙNG", IsActive = true });
            db.Products.Add(new ERP.Domain.Entities.Product { Id = 4010, Code = "SKU-W40-OP", Name = "Sản phẩm vận hành Wave 40", UnitId = 1, IsActive = true });
            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4001,
                    WarehouseId = 1,
                    ProductId = 4010,
                    LocationId = 10,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 50m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4002,
                    WarehouseId = 1,
                    ProductId = 4010,
                    LocationId = 10,
                    Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    Quantity = 10m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                });
            db.PickingTasks.Add(new ERP.Domain.Entities.PickingTask
            {
                Id = 4050,
                WarehouseId = 1,
                TaskCode = "PICK-W40-01",
                SourceType = "SalesOrder",
                SourceId = 9001,
                SourceCode = "SO-W40-01",
                Status = ERP.Domain.Enums.PickingTaskStatus.Open,
                Priority = 2,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow,
                Lines =
                [
                    new ERP.Domain.Entities.PickingTaskLine
                    {
                        Id = 4051,
                        PickingTaskId = 4050,
                        ProductId = 4010,
                        SourceLocationId = 10,
                        RequestedQuantity = 5m,
                        PickedQuantity = 0m,
                        Status = ERP.Domain.Enums.PickingTaskLineStatus.Open
                    }
                ]
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var stocktakesController = new StocktakesController(
                new Mock<IStocktakeService>().Object,
                new Mock<IStocktakeQueryService>().Object,
                db,
                warehouseAuth.Object);

            var propRes = await stocktakesController.CreateAdjustmentProposal(
                new StocktakesController.CreateAdjustmentProposalRequest(
                    WarehouseId: 1,
                    ProductCode: "SKU-W40-OP",
                    DeltaQuantity: 5m,
                    ReasonCode: "CYCLE_COUNT_GAIN",
                    Note: "Kiểm kê phát hiện thừa 5 thùng",
                    LocationCode: "A-01-01"),
                default);
            var propOk = propRes.Should().BeOfType<OkObjectResult>().Subject;
            long proposalAuditId;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(propOk.Value))
            {
                proposalAuditId = doc.RootElement.GetProperty("proposalAuditId").GetInt64();
                doc.RootElement.GetProperty("status").GetString().Should().Contain("Chờ phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm");
            }

            var approveRes = await stocktakesController.ApproveAdjustmentProposal(
                proposalAuditId,
                new StocktakesController.ApproveAdjustmentProposalRequest(
                    LocationCode: "A-01-01",
                    Note: "Duyệt ghi sổ điều chỉnh kiểm kê Wave 40"),
                default);
            var approveOk = approveRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(approveOk.Value))
            {
                doc.RootElement.GetProperty("resultingQuantity").GetDecimal().Should().Be(55m);
                doc.RootElement.GetProperty("status").GetString().Should().Be("Đã phê duyệt & Ghi sổ");
            }

            (await db.InventoryStocks.SingleAsync(x => x.Id == 4001)).Quantity.Should().Be(55m);

            var importController = new ImportReceiptsController(
                new Mock<IImportReceiptService>().Object,
                null,
                db,
                warehouseAuth.Object);

            var rtvRes = await importController.ExecuteRtvRefurbish(
                new ImportReceiptsController.ExecuteRtvRefurbishRequest(
                    WarehouseId: 1,
                    ActionType: "REFURBISH",
                    Quantity: 4m,
                    ProductCode: "SKU-W40-OP",
                    LocationCode: "A-01-01"),
                default);
            var rtvOk = rtvRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(rtvOk.Value))
            {
                doc.RootElement.GetProperty("ledgerUpdated").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("executedStockMutation").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("resultingStockQuantity").GetDecimal().Should().Be(59m);
            }

            (await db.InventoryStocks.SingleAsync(x => x.Id == 4001)).Quantity.Should().Be(59m);
            (await db.InventoryStocks.SingleAsync(x => x.Id == 4002)).Quantity.Should().Be(6m);

            var pickingController = new PickingTasksController(
                new Mock<IPickingService>().Object,
                db,
                warehouseAuth.Object);

            var waveRes = await pickingController.ReleaseWave(
                new PickingTasksController.ReleasePickingWaveRequest(
                    WarehouseId: 1,
                    WaveCode: "WAVE-W40-01",
                    AssignToUserId: 99,
                    Note: "Phát hành sóng lấy hàng Wave 40"),
                default);
            var waveOk = waveRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(waveOk.Value))
            {
                doc.RootElement.GetProperty("waveCode").GetString().Should().Be("WAVE-W40-01");
                doc.RootElement.GetProperty("releasedTasksCount").GetInt32().Should().Be(1);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã phát hành sóng lấy hàng");
            }

            var updatedTask = await db.PickingTasks.SingleAsync(x => x.Id == 4050);
            updatedTask.AssignedUserId.Should().Be(99);
            updatedTask.Status.Should().Be(ERP.Domain.Enums.PickingTaskStatus.Assigned);
            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave41_BarcodeLookup_ParsesGs1128CompositeString_AndResolvesGtinLotExpirySerialSscc()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Trung Tâm WH01", IsActive = true });
            db.Units.Add(new ERP.Domain.Entities.Unit { Id = 1, Code = "THÙNG", Name = "THÙNG", IsActive = true });
            var product = new ERP.Domain.Entities.Product
            {
                Id = 4101,
                Code = "SKU-W41-GS1",
                Name = "Sữa Tiệt Trùng Chuẩn GS1-128",
                UnitId = 1,
                TrackingType = ERP.Domain.Enums.ProductTrackingType.Lot,
                ExpiryControl = true,
                IsActive = true
            };
            db.Products.Add(product);
            db.ProductBarcodes.Add(new ERP.Domain.Entities.ProductBarcode
            {
                Id = 4101,
                ProductId = 4101,
                Product = product,
                Value = "8938505974192"
            });
            db.InventoryLots.Add(new ERP.Domain.Entities.InventoryLot
            {
                Id = 4110,
                ProductId = 4101,
                Product = product,
                LotNumber = "LOTW4101",
                ManufactureDate = new DateTime(2026, 1, 1),
                ExpiryDate = new DateTime(2026, 12, 31)
            });
            db.InventorySerials.Add(new ERP.Domain.Entities.InventorySerial
            {
                Id = 4120,
                ProductId = 4101,
                Product = product,
                SerialNumber = "SNW410099"
            });
            db.HandlingUnits.Add(new ERP.Domain.Entities.HandlingUnit
            {
                Id = 4130,
                WarehouseId = 1,
                HuCode = "HU-W41-01",
                Barcode = "00389385059700000018",
                Sscc = "389385059700000018",
                Type = ERP.Domain.Enums.HandlingUnitType.Pallet,
                Status = ERP.Domain.Enums.HandlingUnitStatus.Staged
            });
            await db.SaveChangesAsync();

            var catalogService = new Mock<IProductCatalogService>();
            var controller = new ProductBarcodeLookupController(catalogService.Object, db);

            var gs1Scan = "(01)08938505974192(17)261231(10)LOTW4101(21)SNW410099(00)389385059700000018";
            var res = await controller.Resolve(gs1Scan, warehouseId: 1, ct: default);
            var ok = res.Should().BeOfType<OkObjectResult>().Subject;
            var dto = ok.Value.Should().BeOfType<BarcodeResolutionDto>().Subject;

            dto.IsGs1128.Should().BeTrue();
            dto.IsGs1Composite.Should().BeTrue();
            dto.ProductId.Should().Be(4101);
            dto.ProductCode.Should().Be("SKU-W41-GS1");
            dto.Gtin.Should().Be("08938505974192");
            dto.NormalizedBarcode.Should().Be("8938505974192");
            dto.LotNumber.Should().Be("LOTW4101");
            dto.LotId.Should().Be(4110);
            dto.ExpiryDate.Should().Be("2026-12-31");
            dto.ExpiryDateDisplay.Should().Be("31/12/2026");
            dto.SerialNumber.Should().Be("SNW410099");
            dto.SerialId.Should().Be(4120);
            dto.Sscc.Should().Be("389385059700000018");
            dto.HandlingUnitId.Should().Be(4130);
            dto.StatusLabel.Should().Be("Đã giải mã thành công chuỗi mã vạch chuẩn GS1-128");

            var postRes = await controller.ParseGs1(new ProductBarcodeLookupController.ParseGs1Request(Barcode: gs1Scan, WarehouseId: 1), ct: default);
            var postOk = postRes.Should().BeOfType<OkObjectResult>().Subject;
            var postDto = postOk.Value.Should().BeOfType<BarcodeResolutionDto>().Subject;
            postDto.IsGs1128.Should().BeTrue();
            postDto.ProductId.Should().Be(4101);
        }

        [Fact]
        public async Task Wave41_InventoryControl_ExecuteReplenishment_MovesStockBetweenReserveAndPickFace_AndWritesMoveTransaction()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Trung Tâm WH01", IsActive = true });
            db.WarehouseLocations.AddRange(
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 101,
                    WarehouseId = 1,
                    Code = "RESERVE-B01",
                    Name = "Kệ dự trữ tầng cao B01",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                    IsActive = true,
                    IsBlocked = false,
                    IsSystemManaged = false,
                    IsPickable = false
                },
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 102,
                    WarehouseId = 1,
                    Code = "PICK-A01",
                    Name = "Kệ nhặt hàng tầng thấp A01",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Storage,
                    IsActive = true,
                    IsBlocked = false,
                    IsSystemManaged = false,
                    IsPickable = true
                });
            db.Units.Add(new ERP.Domain.Entities.Unit { Id = 1, Code = "THÙNG", Name = "THÙNG", IsActive = true });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 4102,
                Code = "SKU-W41-REP",
                Name = "Sản phẩm bổ sung kệ nhặt Wave 41",
                UnitId = 1,
                IsActive = true
            });
            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4151,
                    WarehouseId = 1,
                    ProductId = 4102,
                    LocationId = 101,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 80m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4152,
                    WarehouseId = 1,
                    ProductId = 4102,
                    LocationId = 102,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 5m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new InventoryControlController(
                new Mock<IInventoryLockService>().Object,
                new Mock<IInventoryMovementService>().Object,
                db,
                warehouseAuth.Object);

            var execRes = await controller.ExecuteReplenishment(
                new InventoryControlController.ExecuteReplenishmentRequest(
                    WarehouseId: 1,
                    ProductId: 4102,
                    SourceLocationId: 101,
                    TargetLocationId: 102,
                    Quantity: 20m,
                    Note: "Bổ sung 20 thùng từ kệ dự trữ xuống kệ nhặt hàng"),
                db,
                warehouseAuth.Object,
                default);
            var execOk = execRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(execOk.Value))
            {
                doc.RootElement.GetProperty("stockMutated").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("executed").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("movedQuantity").GetDecimal().Should().Be(20m);
                doc.RootElement.GetProperty("sourceRemainingQuantity").GetDecimal().Should().Be(60m);
                doc.RootElement.GetProperty("targetResultingQuantity").GetDecimal().Should().Be(25m);
                doc.RootElement.GetProperty("statusLabel").GetString().Should().Be("Đã thực thi bổ sung tồn kho từ kệ dự trữ sang kệ nhặt");
            }

            (await db.InventoryStocks.SingleAsync(x => x.Id == 4151)).Quantity.Should().Be(60m);
            (await db.InventoryStocks.SingleAsync(x => x.Id == 4152)).Quantity.Should().Be(25m);
            (await db.InventoryTransactions.AnyAsync(t =>
                t.WarehouseId == 1 &&
                t.ProductId == 4102 &&
                t.TransactionType == ERP.Domain.Enums.TransactionType.Move &&
                t.ReferenceType == "Replenishment" &&
                t.Quantity == 20m)).Should().BeTrue();
            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave41_PackingKittingVas_And_DockYardCrossDock_MutateRealInventoryStocksAndWriteLedgerTransactions_WhenExecuteStockMutationTrue()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.Add(new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho Trung Tâm WH01", IsActive = true });
            db.WarehouseLocations.AddRange(
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 201,
                    WarehouseId = 1,
                    Code = "RECEIVING",
                    Name = "Vị trí nhận hàng",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Receiving,
                    IsActive = true,
                    IsReceivable = true,
                    IsSystemManaged = true
                },
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 202,
                    WarehouseId = 1,
                    Code = "STAGING",
                    Name = "Vị trí tập kết trung chuyển",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Staging,
                    IsActive = true,
                    IsSystemManaged = true
                },
                new ERP.Domain.Entities.WarehouseLocation
                {
                    Id = 203,
                    WarehouseId = 1,
                    Code = "PACKING_STAGE",
                    Name = "Vị trí tập kết đóng gói",
                    LocationType = ERP.Domain.Enums.WarehouseLocationType.Staging,
                    IsActive = true,
                    IsSystemManaged = true
                });
            db.Units.Add(new ERP.Domain.Entities.Unit { Id = 1, Code = "THÙNG", Name = "THÙNG", IsActive = true });
            db.Products.AddRange(
                new ERP.Domain.Entities.Product { Id = 4111, Code = "SKU-COMP-41", Name = "Linh kiện thành phần Wave 41", UnitId = 1, IsActive = true },
                new ERP.Domain.Entities.Product { Id = 4112, Code = "SKU-KIT-41", Name = "Bộ sản phẩm khuyến mãi KIT Wave 41", UnitId = 1, IsActive = true },
                new ERP.Domain.Entities.Product { Id = 4113, Code = "SKU-XD-41", Name = "Hàng trung chuyển Cross-Dock Wave 41", UnitId = 1, IsActive = true });
            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4161,
                    WarehouseId = 1,
                    ProductId = 4111,
                    LocationId = 203,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 100m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4162,
                    WarehouseId = 1,
                    ProductId = 4112,
                    LocationId = 203,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 10m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4163,
                    WarehouseId = 1,
                    ProductId = 4113,
                    LocationId = 201,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 40m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(default)).ReturnsAsync(new[] { 1 });
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var packingController = new PackingSessionsController(
                new Mock<IPackingService>().Object,
                db,
                warehouseAuth.Object);

            var kitRes = await packingController.ExecuteKittingVas(
                new PackingSessionsController.ExecuteKittingVasRequest(
                    WarehouseId: 1,
                    KitSkuCode: "SKU-KIT-41",
                    VasServiceType: "Đóng bộ khuyến mãi 2 trong 1",
                    ComponentProductId: 4111,
                    KitProductId: 4112,
                    LocationId: 203,
                    KitQuantity: 10m,
                    ComponentRatio: 2m,
                    ExecuteStockMutation: true),
                default);
            var kitOk = kitRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(kitOk.Value))
            {
                doc.RootElement.GetProperty("executedStockMutation").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("ledgerUpdated").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("componentConsumedQuantity").GetDecimal().Should().Be(20m);
                doc.RootElement.GetProperty("kitProducedQuantity").GetDecimal().Should().Be(10m);
                doc.RootElement.GetProperty("componentRemainingQuantity").GetDecimal().Should().Be(80m);
                doc.RootElement.GetProperty("kitResultingQuantity").GetDecimal().Should().Be(20m);
            }

            (await db.InventoryStocks.SingleAsync(x => x.Id == 4161)).Quantity.Should().Be(80m);
            (await db.InventoryStocks.SingleAsync(x => x.Id == 4162)).Quantity.Should().Be(20m);
            (await db.InventoryTransactions.CountAsync(t => t.ReferenceType == "KittingVas")).Should().Be(2);

            var dockService = new Mock<IDockYardService>();
            var dockController = new DockYardController(
                dockService.Object,
                db,
                warehouseAuth.Object);

            var xdRes = await dockController.DispatchCrossdock(
                new DispatchCrossdockRequest(
                    WarehouseId: 1,
                    DockDoorCode: "DOCK-XD-01",
                    InboundAppointmentCode: "APPT-IN-W41",
                    OutboundReferenceCode: "SO-OUT-W41",
                    ProductId: 4113,
                    SourceLocationId: 201,
                    TargetLocationId: 202,
                    Quantity: 15m,
                    ExecuteStockMutation: true),
                default);
            var xdOk = xdRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(xdOk.Value))
            {
                doc.RootElement.GetProperty("executedStockMutation").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("ledgerUpdated").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("routedQuantity").GetDecimal().Should().Be(15m);
                doc.RootElement.GetProperty("sourceRemainingQuantity").GetDecimal().Should().Be(25m);
                doc.RootElement.GetProperty("targetResultingQuantity").GetDecimal().Should().Be(15m);
                doc.RootElement.GetProperty("dispatchStatus").GetString().Should().Be("Đã phát lệnh điều phối trung chuyển thẳng");
            }

            (await db.InventoryStocks.SingleAsync(x => x.Id == 4163)).Quantity.Should().Be(25m);
            (await db.InventoryStocks.SingleAsync(x => x.WarehouseId == 1 && x.ProductId == 4113 && x.LocationId == 202)).Quantity.Should().Be(15m);
            (await db.InventoryTransactions.AnyAsync(t =>
                t.ReferenceType == "CrossDock" &&
                t.TransactionType == ERP.Domain.Enums.TransactionType.Move &&
                t.Quantity == 15m)).Should().BeTrue();
            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave42_BusinessPartners_3PlBillingSummaryAndStatement_ComputesRealLedgerFeesAndVat()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.BusinessPartners.Add(new ERP.Domain.Entities.BusinessPartner
            {
                Id = 4201,
                Code = "BP-3PL-W42",
                Name = "Công ty Tiếp vận 3PL Quốc tế Wave 42",
                IsSupplier = true,
                IsCustomer = true,
                IsActive = true
            });
            db.InventoryStocks.AddRange(
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4211,
                    WarehouseId = 1,
                    ProductId = 1,
                    LocationId = 1,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 100m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                },
                new ERP.Domain.Entities.InventoryStock
                {
                    Id = 4212,
                    WarehouseId = 1,
                    ProductId = 2,
                    LocationId = 1,
                    Status = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 50m,
                    ReservedQuantity = 0m,
                    LastUpdated = DateTime.UtcNow
                });
            db.InventoryTransactions.AddRange(
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 4221,
                    WarehouseId = 1,
                    ProductId = 1,
                    TransactionType = ERP.Domain.Enums.TransactionType.Import,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 80m,
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 4222,
                    WarehouseId = 1,
                    ProductId = 1,
                    TransactionType = ERP.Domain.Enums.TransactionType.TransferIn,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 20m,
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 4223,
                    WarehouseId = 1,
                    ProductId = 1,
                    TransactionType = ERP.Domain.Enums.TransactionType.Export,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 40m,
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = 1
                },
                new ERP.Domain.Entities.InventoryTransaction
                {
                    Id = 4224,
                    WarehouseId = 1,
                    ProductId = 1,
                    TransactionType = ERP.Domain.Enums.TransactionType.Ship,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                    Quantity = 10m,
                    TransactionDate = DateTime.UtcNow,
                    CreatedBy = 1
                });
            await db.SaveChangesAsync();

            var partnerService = new Mock<IBusinessPartnerService>();
            var controller = new BusinessPartnersController(partnerService.Object, db);

            var summaryRes = await controller.Get3PlBillingSummary(
                partnerId: 4201,
                warehouseId: 1,
                billingPeriod: "2026-10",
                ct: default);
            var summaryOk = summaryRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(summaryOk.Value))
            {
                var root = doc.RootElement;
                root.GetProperty("partnerCode").GetString().Should().Be("BP-3PL-W42");
                root.GetProperty("totalOnHandUnits").GetDecimal().Should().Be(150m);
                root.GetProperty("inboundUnits").GetDecimal().Should().Be(100m);
                root.GetProperty("outboundUnits").GetDecimal().Should().Be(50m);
                root.GetProperty("storageFeeAmount").GetDecimal().Should().Be(2250000m);
                root.GetProperty("inboundHandlingFeeAmount").GetDecimal().Should().Be(800000m);
                root.GetProperty("outboundHandlingFeeAmount").GetDecimal().Should().Be(450000m);
                root.GetProperty("subTotalAmount").GetDecimal().Should().Be(3500000m);
                root.GetProperty("vatRatePercent").GetDecimal().Should().Be(10m);
                root.GetProperty("vatAmount").GetDecimal().Should().Be(350000m);
                root.GetProperty("totalBillingAmount").GetDecimal().Should().Be(3850000m);
                root.GetProperty("statementLines").GetArrayLength().Should().Be(3);
            }

            var genRes = await controller.Generate3PlStatement(
                new BusinessPartnersController.Generate3PlStatementRequest(
                    PartnerId: 4201,
                    WarehouseId: 1,
                    BillingPeriod: "2026-10"),
                default);
            var genOk = genRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(genOk.Value))
            {
                var root = doc.RootElement;
                root.GetProperty("status").GetString().Should().Be("ISSUED");
                root.GetProperty("statusLabel").GetString().Should().Be("Đã phát hành bảng kê phí dịch vụ 3PL");
                root.GetProperty("totalBillingAmount").GetDecimal().Should().Be(3850000m);
            }

            db.AuditLogs.Should().ContainSingle(a =>
                a.EntityName == "ThreePlBillingStatement" &&
                a.Action == "BusinessPartner.Generate3PlStatement" &&
                a.Result == "Success");
        }

        [Fact]
        public async Task Wave42_PermissionsAndSecurity_SaaSQuotasAndRfc6238TotpMfa_GenerateAndVerify()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.Warehouses.AddRange(
                new ERP.Domain.Entities.Warehouse { Id = 1, Code = "WH01", Name = "Kho WH01", IsActive = true },
                new ERP.Domain.Entities.Warehouse { Id = 2, Code = "WH02", Name = "Kho WH02", IsActive = true });
            db.Users.Add(new ERP.Domain.Entities.User
            {
                Id = 4201,
                Username = "admin_w42",
                FullName = "Quản trị viên Wave 42",
                PasswordHash = "hash",
                IsActive = true
            });
            db.Units.Add(new ERP.Domain.Entities.Unit { Id = 1, Code = "THÙNG", Name = "THÙNG", IsActive = true });
            db.Products.Add(new ERP.Domain.Entities.Product
            {
                Id = 4201,
                Code = "SKU-W42-01",
                Name = "Sản phẩm Wave 42",
                UnitId = 1,
                IsActive = true
            });
            await db.SaveChangesAsync();

            var permissionsController = new PermissionsController(db);
            var tenantsSummaryRes = await permissionsController.GetTenantsSummary(default);
            var tenantsSummaryOk = tenantsSummaryRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(tenantsSummaryOk.Value))
            {
                var root = doc.RootElement;
                root.GetProperty("activeWarehousesCount").GetInt32().Should().Be(2);
                root.GetProperty("activeUsersCount").GetInt32().Should().Be(1);
                root.GetProperty("activeSkusCount").GetInt32().Should().Be(1);
                root.GetProperty("withinQuotas").GetBoolean().Should().BeTrue();
                root.GetProperty("quotaStatus").GetString().Should().Be("TRONG_HAN_NGACH");
                root.GetProperty("planName").GetString().Should().Be("Gói Doanh nghiệp Mở rộng (Enterprise SaaS)");
            }

            var verifyQuotasRes = await permissionsController.VerifyTenantQuotas(
                new PermissionsController.VerifyTenantQuotasRequest(TenantCode: "TNT-ERP-KHO-VN"),
                default);
            verifyQuotasRes.Should().BeOfType<OkObjectResult>();

            var securityController = new AccountSecurityController(
                new Mock<IAccountAdminService>().Object,
                new Mock<IUserSessionService>().Object,
                db);

            var setupRes = await securityController.SetupTotp(4201, default);
            var setupOk = setupRes.Should().BeOfType<OkObjectResult>().Subject;
            string currentTotpCode;
            string base32Secret;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(setupOk.Value))
            {
                var root = doc.RootElement;
                base32Secret = root.GetProperty("base32Secret").GetString()!;
                currentTotpCode = root.GetProperty("currentTotpCode").GetString()!;
                base32Secret.Should().HaveLength(32);
                currentTotpCode.Should().HaveLength(6);
                root.GetProperty("otpauthUri").GetString().Should().StartWith("otpauth://totp/ERP_WMS:admin_w42?secret=");
                root.GetProperty("statusLabel").GetString().Should().Be("Đã khởi tạo khóa xác thực 2 lớp TOTP MFA chuẩn RFC 6238");
            }

            var verifyRes = await securityController.VerifyTotp(
                4201,
                new AccountSecurityController.VerifyTotpRequest(Code: currentTotpCode, Base32Secret: base32Secret),
                default);
            var verifyOk = verifyRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(verifyOk.Value))
            {
                var root = doc.RootElement;
                root.GetProperty("verified").GetBoolean().Should().BeTrue();
                root.GetProperty("statusLabel").GetString().Should().Be("Xác thực mã TOTP 6 chữ số thành công");
            }

            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }

        [Fact]
        public async Task Wave42_InventoryReconciliation_OutboxMessagesAndDispatch_ReturnsRealDocumentReferences()
        {
            var options = new DbContextOptionsBuilder<ERP.Infrastructure.Persistence.ErpKhoDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            await using var db = new ERP.Infrastructure.Persistence.ErpKhoDbContext(options);

            db.ImportReceipts.Add(new ERP.Domain.Entities.ImportReceipt
            {
                Id = 4201,
                Code = "IR-QA-001",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ReceiptStatus.Posted,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow
            });
            db.ExportReceipts.Add(new ERP.Domain.Entities.ExportReceipt
            {
                Id = 4202,
                Code = "ER-QA-001",
                WarehouseId = 1,
                Status = ERP.Domain.Enums.ReceiptStatus.Dispatched,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow
            });
            db.Shipments.Add(new ERP.Domain.Entities.Shipment
            {
                Id = 4203,
                ShipmentCode = "SHP-QA-001",
                WarehouseId = 1,
                SourceType = "SalesOrder",
                SourceId = 1,
                SourceCode = "SO-QA-001",
                Status = ERP.Domain.Enums.ShipmentStatus.Dispatched,
                CreatedBy = 1,
                CreatedAt = DateTime.UtcNow
            });
            db.PartnerDebtInvoices.Add(new ERP.Domain.Entities.PartnerDebtInvoice
            {
                Id = 4204,
                Code = "AP-IR-QA0001",
                PartnerId = 1,
                DebtType = ERP.Domain.Enums.PartnerDebtType.AP,
                SourceType = "ImportReceipt",
                SourceId = 4201,
                SourceCode = "IR-QA-001",
                TotalAmount = 5000000m,
                PaidAmount = 0m,
                RemainingAmount = 5000000m,
                Status = ERP.Domain.Enums.PartnerDebtInvoiceStatus.Open,
                CreatedBy = 1,
                CreatedAtUtc = DateTime.UtcNow,
                IssuedAtUtc = DateTime.UtcNow,
                DueDateUtc = DateTime.UtcNow.AddDays(30)
            });
            await db.SaveChangesAsync();

            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
            warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, default)).Returns(Task.CompletedTask);

            var controller = new InventoryReconciliationController(
                new Mock<IInventoryReconciliationQueryService>().Object,
                db,
                warehouseAuth.Object);

            var listRes = await controller.GetOutboxMessages(warehouseId: 1, cancellationToken: default);
            var listOk = listRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(listOk.Value))
            {
                var root = doc.RootElement;
                root.GetProperty("totalMessages").GetInt32().Should().Be(4);
                root.GetProperty("pendingCount").GetInt32().Should().Be(0);
                var messages = root.GetProperty("messages").EnumerateArray().ToList();
                messages.Select(m => m.GetProperty("referenceCode").GetString()).Should().Contain(
                [
                    "IR-QA-001",
                    "SHP-QA-001",
                    "AP-IR-QA0001",
                    "ER-QA-001"
                ]);
            }

            var dispatchRes = await controller.DispatchPendingOutboxMessages(
                warehouseId: 1,
                request: new InventoryReconciliationController.DispatchOutboxRequest(WarehouseId: 1),
                cancellationToken: default);
            var dispatchOk = dispatchRes.Should().BeOfType<OkObjectResult>().Subject;
            using (var doc = System.Text.Json.JsonSerializer.SerializeToDocument(dispatchOk.Value))
            {
                var root = doc.RootElement;
                root.GetProperty("dispatchedCount").GetInt32().Should().Be(4);
                root.GetProperty("pendingCount").GetInt32().Should().Be(0);
                root.GetProperty("statusLabel").GetString().Should().Be("Đã chuyển phát thành công toàn bộ thông điệp tích hợp Outbox");
            }

            db.AuditLogs.Should().ContainSingle(a =>
                a.EntityName == "IntegrationOutboxBatch" &&
                a.Action == "InventoryReconciliation.DispatchOutbox" &&
                a.Result == "Success");
        }

        private static void KeepUnusedMarkerAssertion(ERP.Domain.Entities.PickingTask updatedTask, ERP.Infrastructure.Persistence.ErpKhoDbContext db)
        {
            updatedTask.AssignedUserId.Should().Be(99);
            updatedTask.Status.Should().Be(ERP.Domain.Enums.PickingTaskStatus.Assigned);
            db.AuditLogs.Should().OnlyContain(a => a.Result == "Success" && a.Result.Length <= 32);
        }
    }
}