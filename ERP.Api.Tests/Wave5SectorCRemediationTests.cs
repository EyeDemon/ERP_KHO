using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Api.Middleware;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Application.Options;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ERP.Api.Tests;

public sealed class Wave5SectorCRemediationTests
{
    private static ErpKhoDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ErpKhoDbContext>()
            .UseInMemoryDatabase("Wave5SectorC_" + Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ErpKhoDbContext(options);
    }

    [Theory]
    [InlineData(ReceiptStatus.Received)]
    [InlineData(ReceiptStatus.QcCompleted)]
    [InlineData(ReceiptStatus.ReadyToPost)]
    public async Task W5_06_ApprovalWorkflowService_RejectImportReceipt_AllowsAllPendingApprovalStatuses_AndLogsActualOldStatus(ReceiptStatus initialStatus)
    {
        await using var db = CreateDbContext();

        var role = new Role { Id = 1, RoleName = "Manager" };
        var perm = new Permission("approval.reject", "Reject approvals") { Id = 1 };
        var rolePerm = new RolePermission { RoleId = 1, PermissionId = 1, Role = role, Permission = perm };
        var maker = new User { Id = 10, Username = "maker", FullName = "Maker", PasswordHash = "h", RoleId = 1, Role = role, IsActive = true };
        var checker = new User { Id = 20, Username = "checker", FullName = "Checker", PasswordHash = "h", RoleId = 1, Role = role, IsActive = true };
        var warehouse = new Warehouse { Id = 1, Code = "WH01", Name = "Main Warehouse", IsActive = true };

        var receipt = new ImportReceipt
        {
            Id = 100,
            Code = "IR-100",
            WarehouseId = 1,
            Warehouse = warehouse,
            Status = initialStatus,
            CreatedBy = 10,
            CreatedByUser = maker,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };

        db.Roles.Add(role);
        db.Permissions.Add(perm);
        db.RolePermissions.Add(rolePerm);
        db.Users.AddRange(maker, checker);
        db.Warehouses.Add(warehouse);
        db.ImportReceipts.Add(receipt);
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(20);
        currentUser.SetupGet(x => x.Role).Returns("Manager");

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var metadata = new Mock<IRequestMetadata>();
        metadata.SetupGet(x => x.CorrelationId).Returns("corr-w5-06");

        var service = new ApprovalWorkflowService(
            db,
            currentUser.Object,
            warehouseAuth.Object,
            metadata.Object);

        var result = await service.RejectAsync("IMPORT_RECEIPT", 100, "Hồ sơ QC chưa hợp lệ");

        result.DocumentType.Should().Be("ImportReceipt");
        (await db.ImportReceipts.SingleAsync(x => x.Id == 100)).Status.Should().Be(ReceiptStatus.Cancelled);

        var audit = await db.AuditLogs.SingleAsync(a => a.EntityName == "ImportReceipt" && a.EntityId == 100);
        audit.OldValues.Should().Contain(initialStatus.ToString());
        audit.NewValues.Should().Contain(ReceiptStatus.Cancelled.ToString());
    }

    [Fact]
    public async Task W5_06_ApprovalsController_SlaSummaryAndEscalateOverdue_PaginatesAllPages_AndDeduplicatesEscalationAudit()
    {
        await using var db = CreateDbContext();

        var allItems = Enumerable.Range(1, 150)
            .Select(i => new ApprovalQueueItem
            {
                DocumentType = "ImportReceipt",
                DocumentId = i,
                DocumentCode = $"IR-{i:D4}",
                PendingState = "Received",
                WarehouseId = 1,
                WarehouseName = "WH1",
                CreatorId = 10,
                CreatorName = "Maker",
                RequestedAtUtc = i <= 120 ? DateTime.UtcNow.AddHours(-10) : DateTime.UtcNow.AddHours(-1),
                WaitingMinutes = i <= 120 ? 600 : 60,
                SlaStatus = i <= 120 ? "Overdue" : "Normal",
                TotalQuantity = 10m
            })
            .ToList();

        var service = new Mock<IApprovalWorkflowService>();
        service.Setup(s => s.GetQueueAsync(It.IsAny<ApprovalQueueQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApprovalQueueQuery q, CancellationToken _) =>
            {
                var skip = (q.PageIndex - 1) * q.PageSize;
                var pageItems = allItems.Skip(skip).Take(q.PageSize).ToList();
                return new PagedResult<ApprovalQueueItem>
                {
                    Items = pageItems,
                    PageIndex = q.PageIndex,
                    PageSize = q.PageSize,
                    TotalRecords = allItems.Count
                };
            });

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(true);
        currentUser.SetupGet(u => u.UserId).Returns(42);

        var controller = new ApprovalsController(service.Object, db, null!, currentUser.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "42"),
                        new Claim(ClaimTypes.Role, AppRoles.Manager)
                    ], "TestAuth"))
                }
            }
        };

        var slaResult = await controller.SlaSummary(null, CancellationToken.None) as OkObjectResult;
        slaResult.Should().NotBeNull();
        using var slaDoc = JsonDocument.Parse(JsonSerializer.Serialize(slaResult!.Value));
        slaDoc.RootElement.GetProperty("totalPending").GetInt32().Should().Be(150);
        slaDoc.RootElement.GetProperty("overdueCount").GetInt32().Should().Be(120);

        var escalate1 = await controller.EscalateOverdue(null, CancellationToken.None) as OkObjectResult;
        escalate1.Should().NotBeNull();
        using var escDoc1 = JsonDocument.Parse(JsonSerializer.Serialize(escalate1!.Value));
        escDoc1.RootElement.GetProperty("overdueCount").GetInt32().Should().Be(120);
        escDoc1.RootElement.GetProperty("newlyEscalatedCount").GetInt32().Should().Be(120);

        var logsAfterFirst = await db.AuditLogs.Where(a => a.Action == "Approval.SlaEscalated").ToListAsync();
        logsAfterFirst.Should().HaveCount(120);
        logsAfterFirst.Should().OnlyContain(a => a.UserId == 42);

        var escalate2 = await controller.EscalateOverdue(null, CancellationToken.None) as OkObjectResult;
        using var escDoc2 = JsonDocument.Parse(JsonSerializer.Serialize(escalate2!.Value));
        escDoc2.RootElement.GetProperty("newlyEscalatedCount").GetInt32().Should().Be(0);
        (await db.AuditLogs.CountAsync(a => a.Action == "Approval.SlaEscalated")).Should().Be(120);
    }

    [Fact]
    public async Task W5_10_IdempotentCommandFilter_DoesNotPersistFailed4xxOrForbid_TruncatesOversizedPayload_AndIncludesQueryString()
    {
        await using var db = CreateDbContext();
        var metadata = new Mock<IRequestMetadata>();
        metadata.SetupGet(x => x.CorrelationId).Returns("corr-w5-10");
        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();

        var filter = new IdempotentCommandFilter(
            "Test.Command",
            db,
            metadata.Object,
            warehouseAuth.Object);

        static ActionExecutingContext CreateContext(string key, string queryString = "")
        {
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "7")], "TestAuth"))
            };
            httpContext.Request.Headers["Idempotency-Key"] = key;
            httpContext.Request.QueryString = new QueryString(queryString);
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            return new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object?> { ["dto"] = new { Code = "A1" } },
                controller: new object());
        }

        // 1. 400 BadRequest should NOT persist any IdempotencyRecord (even Processing)
        var badReqCtx = CreateContext("key-400");
        var badReqHash = IdempotentCommandFilter.Hash("key-400");
        await filter.OnActionExecutionAsync(badReqCtx, () =>
        {
            var executed = new ActionExecutedContext(badReqCtx, badReqCtx.Filters, badReqCtx.Controller)
            {
                Result = new BadRequestObjectResult(new { message = "Invalid input" })
            };
            return Task.FromResult(executed);
        });
        (await db.IdempotencyRecords.CountAsync(r => r.KeyHash == badReqHash)).Should().Be(0);

        // 2. ForbidResult (403) should NOT persist any IdempotencyRecord (even Processing)
        var forbidCtx = CreateContext("key-403");
        var forbidHash = IdempotentCommandFilter.Hash("key-403");
        await filter.OnActionExecutionAsync(forbidCtx, () =>
        {
            var executed = new ActionExecutedContext(forbidCtx, forbidCtx.Filters, forbidCtx.Controller)
            {
                Result = new ForbidResult()
            };
            return Task.FromResult(executed);
        });
        (await db.IdempotencyRecords.CountAsync(r => r.KeyHash == forbidHash)).Should().Be(0);

        // 3. Oversized payload (> 65,536 chars) should succeed and store structured valid JSON envelope preserving core DTO fields
        var largeCtx = CreateContext("key-large", "?warehouseId=1");
        var largeHash = IdempotentCommandFilter.Hash("key-large");
        var largePayload = new string('X', 70_000);
        await filter.OnActionExecutionAsync(largeCtx, () =>
        {
            var executed = new ActionExecutedContext(largeCtx, largeCtx.Filters, largeCtx.Controller)
            {
                Result = new OkObjectResult(new
                {
                    id = 42,
                    code = "DOC-42",
                    status = "Posted",
                    rowVersion = "AQIDBA==",
                    lines = new[] { new { id = 1, blob = largePayload } }
                })
            };
            return Task.FromResult(executed);
        });

        var savedRecord = await db.IdempotencyRecords.SingleAsync(r => r.KeyHash == largeHash);
        savedRecord.Status.Should().Be(IdempotencyStatus.Completed);
        using var replayDoc = JsonDocument.Parse(savedRecord.ResponseBody!);
        replayDoc.RootElement.GetProperty("idempotentReplay").GetBoolean().Should().BeTrue();
        replayDoc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue();
        replayDoc.RootElement.GetProperty("statusCode").GetInt32().Should().Be(200);
        replayDoc.RootElement.GetProperty("id").GetInt32().Should().Be(42);
        replayDoc.RootElement.GetProperty("code").GetString().Should().Be("DOC-42");
        replayDoc.RootElement.GetProperty("status").GetString().Should().Be("Posted");
        replayDoc.RootElement.GetProperty("rowVersion").GetString().Should().Be("AQIDBA==");
        replayDoc.RootElement.GetProperty("lines").GetArrayLength().Should().Be(0);

        // 4. Fingerprint and ComputeRequestHash include QueryString
        var fp1 = IdempotentCommandFilter.Fingerprint("Test.Command", new Dictionary<string, object?> { ["id"] = 1 }, "?warehouseId=1");
        var fp2 = IdempotentCommandFilter.Fingerprint("Test.Command", new Dictionary<string, object?> { ["id"] = 1 }, "?warehouseId=2");
        fp1.Should().NotBe(fp2);
    }

    [Theory]
    [InlineData(typeof(ProductsController), nameof(ProductsController.Create))]
    [InlineData(typeof(ProductsController), nameof(ProductsController.Update))]
    [InlineData(typeof(ProductsController), nameof(ProductsController.Delete))]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.Create))]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.Update))]
    [InlineData(typeof(WarehousesController), nameof(WarehousesController.Delete))]
    [InlineData(typeof(UnitsController), nameof(UnitsController.Create))]
    [InlineData(typeof(UnitsController), nameof(UnitsController.Update))]
    [InlineData(typeof(UnitsController), nameof(UnitsController.Delete))]
    [InlineData(typeof(ProductCategoriesController), nameof(ProductCategoriesController.Create))]
    [InlineData(typeof(ProductCategoriesController), nameof(ProductCategoriesController.Update))]
    [InlineData(typeof(ProductCategoriesController), nameof(ProductCategoriesController.Delete))]
    [InlineData(typeof(ProductBarcodesController), nameof(ProductBarcodesController.Create))]
    [InlineData(typeof(ProductBarcodesController), nameof(ProductBarcodesController.Delete))]
    [InlineData(typeof(BusinessPartnersController), nameof(BusinessPartnersController.Create))]
    [InlineData(typeof(BusinessPartnersController), nameof(BusinessPartnersController.Update))]
    [InlineData(typeof(BusinessPartnersController), nameof(BusinessPartnersController.Delete))]
    [InlineData(typeof(StockReservationsController), nameof(StockReservationsController.Release))]
    [InlineData(typeof(StockReservationsController), nameof(StockReservationsController.Expire))]
    [InlineData(typeof(DockYardController), nameof(DockYardController.UpdateDock))]
    [InlineData(typeof(DockYardController), nameof(DockYardController.UpdateYardSlot))]
    [InlineData(typeof(WarehouseCalendarsController), nameof(WarehouseCalendarsController.Update))]
    [InlineData(typeof(WarehouseCalendarsController), nameof(WarehouseCalendarsController.CreateShift))]
    [InlineData(typeof(WarehouseCalendarsController), nameof(WarehouseCalendarsController.UpdateShift))]
    [InlineData(typeof(WarehouseCalendarsController), nameof(WarehouseCalendarsController.CreateException))]
    [InlineData(typeof(PutawayTasksController), nameof(PutawayTasksController.CreateLocation))]
    [InlineData(typeof(PutawayTasksController), nameof(PutawayTasksController.UpdateLocationLayout))]
    public void W5_10_MasterDataAndOperationalMutationEndpoints_HaveIdempotentCommandAttribute(Type controllerType, string methodName)
    {
        var method = controllerType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        method.Should().NotBeNull();
        method!.GetCustomAttribute<IdempotentCommandAttribute>().Should().NotBeNull();
    }

    [Fact]
    public async Task W5_11_And_W5_14b_ReportsController_EnforcesPermissions_Returns403OnUnauthorizedWarehouse_FiltersAnalyticsByDays_AndScopesExceptionAcknowledgeByType()
    {
        typeof(ReportsController).GetCustomAttributes<PermissionAuthorizeAttribute>()
            .Select(a => a.Permission)
            .Should().Contain(AppPermissions.InventoryRead);

        typeof(ReportsController).GetMethod(nameof(ReportsController.GetInventoryInOutReport))!
            .GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
            .Select(a => a.Roles)
            .Should().Contain(AppRoles.AdminManagerOrViewer);

        await using var db = CreateDbContext();
        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH1", Name = "Main", IsActive = true });
        db.Products.Add(new Product { Id = 1, Code = "SKU-1", Name = "Product 1", UnitId = 1, IsActive = true });

        // Old putaway task outside 7-day window (should be excluded when days=7)
        db.PutawayTasks.Add(new PutawayTask
        {
            Id = 1,
            ReceiptId = 1,
            WarehouseId = 1,
            Status = PutawayTaskStatus.Completed,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-20)
        });
        // Recent putaway task inside 7-day window
        db.PutawayTasks.Add(new PutawayTask
        {
            Id = 2,
            ReceiptId = 2,
            WarehouseId = 1,
            Status = PutawayTaskStatus.Completed,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });

        // Export receipt that ALSO has a delivered shipment -> should not be double-counted in outboundShippedCount
        db.ExportReceipts.Add(new ExportReceipt
        {
            Id = 10,
            Code = "EXP-10",
            WarehouseId = 1,
            Status = ReceiptStatus.Posted,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        db.Shipments.Add(new Shipment
        {
            Id = 20,
            ShipmentCode = "SHP-20",
            WarehouseId = 1,
            SourceType = "ExportReceipt",
            SourceId = 10,
            SourceCode = "EXP-10",
            Status = ShipmentStatus.Delivered,
            CreatedBy = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });

        await db.SaveChangesAsync();

        var reportService = new Mock<IReportService>();
        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1 });
        warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        warehouseAuth.Setup(x => x.EnsureWarehouseAccessAsync(99, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = new ReportsController(reportService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "1"),
                        new Claim(ClaimTypes.Role, AppRoles.Manager)
                    ], "TestAuth"))
                }
            }
        };

        // Unauthorized warehouseId=99 must return ForbidResult instead of 200 OK with empty/100% metrics
        (await controller.GetWarehouseTasks(db, warehouseAuth.Object, warehouseId: 99, cancellationToken: CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.GetOperationalExceptions(db, warehouseAuth.Object, warehouseId: 99, cancellationToken: CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.GetAnalyticsSummary(db, warehouseAuth.Object, warehouseId: 99, days: 7, cancellationToken: CancellationToken.None)).Should().BeOfType<ForbidResult>();
        (await controller.GetExpiryAgingReport(db, warehouseAuth.Object, warehouseId: 99, cancellationToken: CancellationToken.None)).Should().BeOfType<ForbidResult>();

        // Analytics summary for authorized warehouseId=1 with days=7
        var analyticsRes = await controller.GetAnalyticsSummary(db, warehouseAuth.Object, 1, 7, CancellationToken.None) as OkObjectResult;
        analyticsRes.Should().NotBeNull();
        using var analyticsDoc = JsonDocument.Parse(JsonSerializer.Serialize(analyticsRes!.Value));
        analyticsDoc.RootElement.GetProperty("putawayCompletedCount").GetInt32().Should().Be(1);
        analyticsDoc.RootElement.GetProperty("outboundShippedCount").GetInt32().Should().Be(1);

        // Exception acknowledgement scoped by ExceptionType + ExceptionCode
        var ackRes = await controller.AcknowledgeOperationalException(
            new ReportsController.AcknowledgeOperationalExceptionDto("CODE-DUP", 1, "Checked", "PUTAWAY_EXCEPTION"),
            db,
            warehouseAuth.Object,
            CancellationToken.None) as OkObjectResult;
        ackRes.Should().NotBeNull();

        var audit = await db.AuditLogs.SingleAsync(a => a.Action == "Exception.Acknowledged");
        audit.OldValues.Should().Be("PUTAWAY_EXCEPTION:CODE-DUP");
    }

    [Fact]
    public async Task W5_17_InventoryReversalAndTraceabilityControllers_SummariesCountFullAuthorizedSetWithoutCap()
    {
        await using var db = CreateDbContext();

        for (var i = 1; i <= 25; i++)
        {
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                Id = i,
                ProductId = 1,
                WarehouseId = 1,
                TransactionType = i <= 15 ? TransactionType.Move : TransactionType.StatusChange,
                Quantity = 5m,
                ReferenceType = "Move",
                ReferenceId = i,
                CreatedBy = 1,
                TransactionDate = DateTime.UtcNow.AddMinutes(-i),
                ReversalOfTransactionId = null
            });
        }

        for (var i = 1; i <= 210; i++)
        {
            db.InventoryStocks.Add(new InventoryStock
            {
                Id = i,
                ProductId = 1,
                WarehouseId = 1,
                LotId = i <= 180 ? i : null,
                SerialId = i <= 50 ? i : null,
                Quantity = 10m,
                ReservedQuantity = 0m,
                Status = i <= 200 ? InventoryStatus.Available : InventoryStatus.Quarantine,
                LastUpdated = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        var reversalService = new Mock<IInventoryReversalService>();
        var traceabilityService = new Mock<IInventoryTraceabilityQueryService>();
        var warehouseAccess = new Mock<IWarehouseAuthorizationService>();
        warehouseAccess.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1 });

        var controller = new InventoryReversalTraceabilityController(
            reversalService.Object,
            traceabilityService.Object,
            db,
            warehouseAccess.Object);

        var revSummary = await controller.ReversalSummary(null, CancellationToken.None) as OkObjectResult;
        revSummary.Should().NotBeNull();
        using var revDoc = JsonDocument.Parse(JsonSerializer.Serialize(revSummary!.Value));
        revDoc.RootElement.GetProperty("totalCandidatesInWindow").GetInt32().Should().Be(25);
        revDoc.RootElement.GetProperty("moveTransactionCount").GetInt32().Should().Be(15);
        revDoc.RootElement.GetProperty("statusChangeTransactionCount").GetInt32().Should().Be(10);

        var traceSummary = await controller.TraceabilitySummary(null, null, CancellationToken.None) as OkObjectResult;
        traceSummary.Should().NotBeNull();
        using var traceDoc = JsonDocument.Parse(JsonSerializer.Serialize(traceSummary!.Value));
        traceDoc.RootElement.GetProperty("totalBuckets").GetInt32().Should().Be(210);
        traceDoc.RootElement.GetProperty("lotTrackedBucketCount").GetInt32().Should().Be(180);
        traceDoc.RootElement.GetProperty("serialTrackedBucketCount").GetInt32().Should().Be(50);
        traceDoc.RootElement.GetProperty("availableBucketCount").GetInt32().Should().Be(200);
        traceDoc.RootElement.GetProperty("nonAvailableBucketCount").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task W5_21_WarehouseAuthorizationService_FiltersOutDeactivatedWarehouses_AndAccountSecurityController_PreventsSelfLockAndChecksRowVersion()
    {
        await using var db = CreateDbContext();
        db.Warehouses.AddRange(
            new Warehouse { Id = 1, Code = "WH1", Name = "Active Warehouse", IsActive = true },
            new Warehouse { Id = 2, Code = "WH2", Name = "Deactivated Warehouse", IsActive = false });
        db.UserWarehouses.AddRange(
            new UserWarehouse { UserId = 5, WarehouseId = 1 },
            new UserWarehouse { UserId = 5, WarehouseId = 2 });

        var adminRole = new Role { Id = 1, RoleName = AppRoles.Admin };
        var staffUser = new User { Id = 5, Username = "staff", FullName = "Staff", PasswordHash = "hash", RoleId = 1, Role = adminRole, IsActive = true, SecurityRevision = 3 };
        var adminUser = new User { Id = 99, Username = "admin", FullName = "Admin", PasswordHash = "hash", RoleId = 1, Role = adminRole, IsActive = true, SecurityRevision = 1 };
        db.Roles.Add(adminRole);
        db.Users.AddRange(staffUser, adminUser);
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(5);
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.IsGlobalAdmin).Returns(false);

        var authService = new WarehouseAuthorizationService(db, currentUser.Object);
        var accessible = await authService.GetAccessibleWarehouseIdsAsync();
        accessible.Should().Equal(1);
        (await authService.CanAccessWarehouseAsync(2)).Should().BeFalse();

        // Global admin also only receives active warehouses
        currentUser.SetupGet(x => x.IsGlobalAdmin).Returns(true);
        (await authService.GetAccessibleWarehouseIdsAsync()).Should().Equal(1);
        (await authService.CanAccessWarehouseAsync(2)).Should().BeFalse();

        // AccountSecurityController: self-lock and self-reset prevention + stale RowVersion detection
        var secController = new AccountSecurityController(
            new Mock<IAccountAdminService>().Object,
            new Mock<IUserSessionService>().Object,
            db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, "99"),
                        new Claim(ClaimTypes.Role, AppRoles.Admin)
                    ], "TestAuth"))
                }
            }
        };

        // Self-lock must throw BusinessRuleException
        Func<Task> selfLock = () => secController.Lock(99, new AccountSecurityCommandDto(null), CancellationToken.None);
        await selfLock.Should().ThrowAsync<BusinessRuleException>();

        // Self-reset must throw BusinessRuleException
        Func<Task> selfReset = () => secController.ResetPassword(99, new ResetPasswordAdminRequest("ValidPass#123", "Self reset"), CancellationToken.None);
        await selfReset.Should().ThrowAsync<BusinessRuleException>();

        // Stale RowVersion on ResetPassword must throw ConcurrencyException
        Func<Task> staleReset = () => secController.ResetPassword(
            5,
            new ResetPasswordAdminRequest("ValidPass#123", "Admin reset", RowVersion: "stale-row-version"),
            CancellationToken.None);
        await staleReset.Should().ThrowAsync<ConcurrencyException>();
    }

    [Fact]
    public async Task W5_29_GlobalExceptionHandlingMiddleware_MapsEfCoreDbUpdateConcurrencyExceptionTo409Conflict()
    {
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw new DbUpdateConcurrencyException("Simulated EF Core optimistic concurrency conflict"),
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("message").GetString()
            .Should().Be("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
    }

    [Fact]
    public async Task W5_25_CorsPolicyAllowsPatch_AndHealthEndpointsAreEnabledByDefaultInDevelopment()
    {
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["JwtSettings:Secret"] = "test_wave5_sector_c_secret_key_minimum_32_bytes_long",
                        ["JwtSettings:Issuer"] = "ErpKhoApi",
                        ["JwtSettings:Audience"] = "ErpKhoClient",
                        ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\mssqllocaldb;Database=ERP_KHO;Trusted_Connection=True",
                        ["Cors:AllowedOrigins:0"] = "https://allowed.test",
                        ["DatabaseInitialization:ValidateMigrationsOnStartup"] = "false",
                        ["SeedDemoUsersOnStartup"] = "false"
                    }));
                builder.ConfigureServices(services =>
                {
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<ErpKhoDbContext>(services);
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<DbContextOptions<ErpKhoDbContext>>(services);
                    Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<ErpKhoDbContext>>(services);
                    services.AddDbContext<ErpKhoDbContext>(options => options.UseInMemoryDatabase($"w5-25-{Guid.NewGuid():N}"));
                });
            });

        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var corsProvider = factory.Services.GetRequiredService<Microsoft.AspNetCore.Cors.Infrastructure.ICorsPolicyProvider>();
        var policy = await corsProvider.GetPolicyAsync(new DefaultHttpContext(), "ConfiguredOrigins");
        policy.Should().NotBeNull();
        policy!.Methods.Should().Contain("PATCH");

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/Auth/login");
        preflight.Headers.Add("Origin", "http://localhost:5173");
        preflight.Headers.Add("Access-Control-Request-Method", "PATCH");
        preflight.Headers.Add("Access-Control-Request-Headers", "idempotency-key,content-type");
        var preflightResponse = await client.SendAsync(preflight);
        preflightResponse.Headers.GetValues("Access-Control-Allow-Origin")
            .Should().Contain("http://localhost:5173");
        preflightResponse.Headers.GetValues("Access-Control-Allow-Methods")
            .Should().Contain(m => m.Contains("PATCH", StringComparison.OrdinalIgnoreCase));

        using var healthLive = await client.GetAsync("/health/live");
        healthLive.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task W6_03_WarehouseCalendarService_EvaluatesMultiDayDateRanges_IntraDayMaintenanceWindows_AndAuditLogCompositeIndexes()
    {
        await using var db = CreateDbContext();

        var auditEntityType = db.Model.FindEntityType(typeof(AuditLog))!;
        var indexNames = auditEntityType.GetIndexes().Select(i => i.GetDatabaseName()).ToList();
        indexNames.Should().Contain("IX_AuditLogs_EntityName_Action_EntityId_Timestamp");
        indexNames.Should().Contain("IX_AuditLogs_EntityName_WarehouseId_Timestamp");

        db.Warehouses.Add(new Warehouse { Id = 1, Code = "WH-CAL", Name = "Calendar WH", IsActive = true });
        var calendar = new WarehouseCalendar
        {
            WarehouseId = 1,
            TimeZoneId = "UTC",
            Days = Enumerable.Range(0, 7).Select(d => new WarehouseCalendarDay
            {
                WarehouseId = 1,
                DayOfWeek = d,
                IsOpen = true,
                OpensAtLocal = new TimeSpan(6, 0, 0),
                ClosesAtLocal = new TimeSpan(22, 0, 0)
            }).ToList()
        };
        db.WarehouseCalendars.Add(calendar);

        // 1. Multi-day holiday range: 2026-10-18..2026-10-20 (must block 2026-10-19!)
        // 2. Intra-day maintenance window: 2026-10-21 08:00..12:00 (must block 10:00, but allow 15:00!)
        db.AuditLogs.AddRange(
            new AuditLog
            {
                EntityName = "WarehouseCalendarException",
                EntityId = 1,
                WarehouseId = 1,
                Action = "HOLIDAY_CLOSURE",
                OldValues = "2026-10-18..2026-10-20",
                NewValues = "2026-10-18..2026-10-20",
                Result = "Success",
                Timestamp = DateTime.UtcNow.AddMinutes(-5)
            },
            new AuditLog
            {
                EntityName = "WarehouseCalendarException",
                EntityId = 1,
                WarehouseId = 1,
                Action = "MAINTENANCE_WINDOW",
                OldValues = "2026-10-21 08:00..12:00",
                NewValues = "2026-10-21 08:00..12:00",
                Result = "Success",
                Timestamp = DateTime.UtcNow.AddMinutes(-1)
            });
        await db.SaveChangesAsync();

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.UserId).Returns(1);

        var calService = new WarehouseCalendarService(db, warehouseAuth.Object, currentUser.Object, TimeProvider.System);

        // Middle day of multi-day holiday range (2026-10-19 10:00 UTC) must be blocked
        Func<Task> midHoliday = () => calService.ValidateOperatingWindowAsync(1, new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc));
        await midHoliday.Should().ThrowAsync<BusinessRuleException>();

        // Inside maintenance window (2026-10-21 10:00 UTC) must be blocked
        Func<Task> inMaint = () => calService.ValidateOperatingWindowAsync(1, new DateTime(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc));
        await inMaint.Should().ThrowAsync<BusinessRuleException>();

        // Outside maintenance window on same day (2026-10-21 15:00 UTC) must be open
        (await calService.ValidateOperatingWindowAsync(1, new DateTime(2026, 10, 21, 15, 0, 0, DateTimeKind.Utc))).Should().BeTrue();
    }

    [Fact]
    public async Task W6_04_ApprovalWorkflowService_MasksBlindStocktakeSystemQuantity_AndWarehouseServiceSeedsNonPickableDamagedLocation()
    {
        await using var db = CreateDbContext();
        var role = new Role { Id = 1, RoleName = AppRoles.Manager };
        var creator = new User { Id = 10, Username = "creator", FullName = "Creator", PasswordHash = "h", RoleId = 1, Role = role, IsActive = true };
        var checker = new User { Id = 20, Username = "checker", FullName = "Checker", PasswordHash = "h", RoleId = 1, Role = role, IsActive = true };
        var unit = new Unit { Id = 1, Code = "EA", Name = "Each", DecimalPlaces = 0 };
        var product = new Product { Id = 1, Code = "SKU-BLIND", Name = "Blind Product", UnitId = 1, Unit = unit, IsActive = true };
        var warehouse = new Warehouse { Id = 1, Code = "WH1", Name = "Main WH", IsActive = true };

        db.Roles.Add(role);
        db.Users.AddRange(creator, checker);
        db.Units.Add(unit);
        db.Products.Add(product);
        db.Warehouses.Add(warehouse);
        db.Stocktakes.Add(new Stocktake
        {
            Id = 100,
            Code = "ST-BLIND-100",
            WarehouseId = 1,
            Warehouse = warehouse,
            CreatedBy = 10,
            CreatedByUser = creator,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            Status = ReceiptStatus.Draft,
            Note = "[BLIND] Cycle count",
            Details =
            [
                new StocktakeDetail
                {
                    Id = 1001,
                    StocktakeId = 100,
                    ProductId = 1,
                    Product = product,
                    SystemQuantity = 75m,
                    ActualQuantity = null
                }
            ]
        });
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(x => x.IsAuthenticated).Returns(true);
        currentUser.SetupGet(x => x.UserId).Returns(20);
        currentUser.SetupGet(x => x.Role).Returns(AppRoles.Manager);

        var warehouseAuth = new Mock<IWarehouseAuthorizationService>();
        warehouseAuth.Setup(x => x.GetAccessibleWarehouseIdsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { 1 });

        var metadata = new Mock<IRequestMetadata>();
        var approvalService = new ApprovalWorkflowService(db, currentUser.Object, warehouseAuth.Object, metadata.Object);

        var queue = await approvalService.GetQueueAsync(new ApprovalQueueQuery { DocumentType = "Stocktake", PageIndex = 1, PageSize = 20 });
        queue.Items.Should().ContainSingle();
        queue.Items[0].TotalQuantity.Should().Be(0m);

        var detail = await approvalService.GetDetailAsync("Stocktake", 100);
        detail.Lines.Should().ContainSingle();
        detail.Lines[0].Quantity.Should().Be(0m);

        // Also verify WarehouseService.CreateWarehouseAsync seeds non-pickable DAMAGED system location
        var whRepo = new ERP.Infrastructure.Repositories.WarehouseRepository(db);
        var whService = new ERP.Application.Services.WarehouseService(whRepo, warehouseAuth.Object);
        var createdWh = await whService.CreateWarehouseAsync(new CreateWarehouseDto
        {
            Code = "WH-NEW",
            Name = "New Warehouse",
            Address = "123 Street"
        }, "admin");

        var damagedLoc = await db.WarehouseLocations.SingleOrDefaultAsync(x => x.WarehouseId == createdWh.Id && x.Code == "DAMAGED");
        damagedLoc.Should().NotBeNull();
        damagedLoc!.LocationType.Should().Be(WarehouseLocationType.Damaged);
        damagedLoc.IsPickable.Should().BeFalse();
        damagedLoc.IsReceivable.Should().BeFalse();
        damagedLoc.IsSystemManaged.Should().BeTrue();
    }

    [Fact]
    public async Task W8_03_Race4_IdempotentCommandFilter_FiveConcurrentRequests_NeverThrows500()
    {
        var dbName = "Wave8_IdemRace4_" + Guid.NewGuid().ToString("N");
        DbContextOptions<ErpKhoDbContext> CreateSharedOptions() =>
            new DbContextOptionsBuilder<ErpKhoDbContext>()
                .UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

        var idempotencyKey = "W8-API-RACE4-" + Guid.NewGuid().ToString("N");
        var handlerInvocationCount = 0;
        using var startGate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, 5).Select(async _ =>
        {
            await Task.Yield();
            startGate.Wait();

            await using var workerDb = new ErpKhoDbContext(CreateSharedOptions());
            var metadata = new Mock<IRequestMetadata>();
            metadata.SetupGet(x => x.CorrelationId).Returns("corr-w8-race4");
            var warehouseAuth = new Mock<IWarehouseAuthorizationService>();

            var filter = new IdempotentCommandFilter(
                "ImportReceipts.Create",
                workerDb,
                metadata.Object,
                warehouseAuth.Object);

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "17")], "TestAuth"))
            };
            httpContext.Request.Headers["Idempotency-Key"] = idempotencyKey;
            var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
            var executingCtx = new ActionExecutingContext(
                actionContext,
                new List<IFilterMetadata>(),
                new Dictionary<string, object?> { ["dto"] = new { Code = "IR-W8-01", WarehouseId = 1 } },
                controller: new object());

            IActionResult? finalResult = null;
            await filter.OnActionExecutionAsync(executingCtx, async () =>
            {
                Interlocked.Increment(ref handlerInvocationCount);
                await Task.Delay(40);
                var executed = new ActionExecutedContext(executingCtx, executingCtx.Filters, executingCtx.Controller)
                {
                    Result = new OkObjectResult(new { id = 801, code = "IR-W8-01", status = "Draft" })
                };
                finalResult = executed.Result;
                return executed;
            });

            return executingCtx.Result ?? finalResult;
        }).ToArray();

        startGate.Set();
        var results = await Task.WhenAll(tasks);

        results.Should().HaveCount(5);
        results.Should().NotContainNulls("every concurrent request must produce a clean HTTP result without throwing 500");
        handlerInvocationCount.Should().BeGreaterThanOrEqualTo(1);
    }
}
