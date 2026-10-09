using System.Collections.Generic;
using System.Threading.Tasks;
using ERP.Api.Controllers;
using ERP.Api.Authorization;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;
using Moq;
using Xunit;

namespace ERP.Api.Tests
{
    public class InventoryReconciliationControllerTests
    {
        [Fact]
        public void Controller_RequiresAuthenticationAndInventoryLedgerPermission()
        {
            var attribute = typeof(InventoryReconciliationController).GetCustomAttribute<AuthorizeAttribute>();
            attribute.Should().NotBeNull();
            attribute!.Roles.Should().BeNull();
            var permission = typeof(InventoryReconciliationController)
                .GetCustomAttribute<PermissionAuthorizeAttribute>();
            permission.Should().NotBeNull();
            permission!.Permission.Should().Be(AppPermissions.InventoryLedgerRead);
        }

        [Fact]
        public async Task Warehouses_ReturnsOnlyAuthorizedSelectorData()
        {
            var mock = new Mock<IInventoryReconciliationQueryService>(MockBehavior.Strict);
            IReadOnlyList<InventoryReconciliationWarehouseDto> expected =
            [
                new InventoryReconciliationWarehouseDto { Id = 7, Code = "W-7", Name = "Kho số 7" }
            ];
            mock.Setup(x => x.GetAccessibleWarehousesAsync()).ReturnsAsync(expected);
            var result = await new InventoryReconciliationController(mock.Object).Warehouses();
            var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(expected);
            mock.Verify(x => x.GetAccessibleWarehousesAsync(), Times.Once);
            mock.VerifyNoOtherCalls();
        }


        [Fact]
        public async Task Investigation_IsAuthorizedAndReturnsTypedReadOnlyEvidence()
        {
            var method = typeof(InventoryReconciliationController)
                .GetMethod(nameof(InventoryReconciliationController.Investigation));
            method.Should().NotBeNull();
            var route = method!.GetCustomAttribute<HttpGetAttribute>();
            route.Should().NotBeNull();
            route!.Template.Should().Be("investigation");
            typeof(InventoryReconciliationController)
                .GetCustomAttribute<PermissionAuthorizeAttribute>()!
                .Permission.Should().Be(AppPermissions.InventoryLedgerRead);

            var mock = new Mock<IInventoryReconciliationQueryService>(MockBehavior.Strict);
            var result = new InventoryReconciliationInvestigationDto
            {
                WarehouseId = 7, ProductId = 17, EventAnchorId = 32,
                EventBeforeId = 20, NextEventBeforeId = 18,
                LedgerHasEventsAfterAnchor = true,
                CurrentQuantity = 9, ExpectedQuantity = 11, Difference = -2,
                BucketCount = 1, EventCount = 2, EventsTruncated = false,
                IsReadOnly = true, AvailableLedgerExpectedIsPartial = true,
                AllStatusCurrentQuantity = 12m, AllStatusReservedQuantity = 2m,
                AllStatusExpectedQuantity = 12m, AllStatusDifference = 0m,
                UnclassifiedLedgerEventCount = 0,
                StatusBreakdown = [new InventoryReconciliationStatusEvidenceDto
                {
                    Status = "QcHold", CurrentQuantity = 3m, ReservedQuantity = 0m,
                    BucketCount = 1, DirectLedgerNetQuantity = 0m,
                    StatusChangeInQuantity = 3m, StatusChangeOutQuantity = 0m,
                    ExpectedQuantity = 3m, Difference = 0m
                }],
                Events = [new InventoryReconciliationEvidenceEventDto
                {
                    TransactionId = 32, TransactionType = "Import",
                    Quantity = 11, SignedQuantity = 11
                }]
            };
            mock.Setup(x => x.GetInvestigationAsync(7, 17, 32, 50, 20))
                .ReturnsAsync(result);
            var response = await new InventoryReconciliationController(mock.Object)
                .Investigation(7, 17, 32, 50, 20);
            var ok = response.Result.Should().BeOfType<OkObjectResult>().Subject;
            ok.Value.Should().BeSameAs(result);
            mock.Verify(x => x.GetInvestigationAsync(7, 17, 32, 50, 20), Times.Once);
            mock.VerifyNoOtherCalls();
            var json = System.Text.Json.JsonSerializer.SerializeToElement(result,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            json.GetProperty("eventBeforeId").GetInt32().Should().Be(20);
            json.GetProperty("nextEventBeforeId").GetInt32().Should().Be(18);
            json.GetProperty("ledgerHasEventsAfterAnchor").GetBoolean().Should().BeTrue();
            json.GetProperty("allStatusCurrentQuantity").GetDecimal().Should().Be(12m);
            json.GetProperty("allStatusExpectedQuantity").GetDecimal().Should().Be(12m);
            json.GetProperty("unclassifiedLedgerEventCount").GetInt32().Should().Be(0);
            json.GetProperty("availableLedgerExpectedIsPartial").GetBoolean().Should().BeTrue();
            var status = json.GetProperty("statusBreakdown").EnumerateArray().Single();
            status.GetProperty("status").GetString().Should().Be("QcHold");
            status.GetProperty("statusChangeInQuantity").GetDecimal().Should().Be(3m);
            status.GetProperty("expectedQuantity").GetDecimal().Should().Be(3m);
            status.GetProperty("difference").GetDecimal().Should().Be(0m);
        }

        [Fact]
        public async Task GetReconciliations_ReturnsOkResult_WithPagedResult()
        {
            var mockQueryService = new Mock<IInventoryReconciliationQueryService>();
            
            var expectedResult = new PagedResult<InventoryReconciliationDto>
            {
                Items = new List<InventoryReconciliationDto>
                {
                    new InventoryReconciliationDto { ProductId = 1, WarehouseId = 1, CurrentQuantity = 10, ExpectedQuantity = 10, Difference = 0, Status = "Match" }
                },
                TotalRecords = 1,
                PageIndex = 1,
                PageSize = 20
            };

            mockQueryService.Setup(s => s.GetReconciliationsAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int>()))
                .ReturnsAsync(expectedResult);

            var controller = new InventoryReconciliationController(mockQueryService.Object);

            var result = await controller.GetReconciliations(null, null, null, 1, 20);

            var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
            var returnedResult = okResult.Value.Should().BeOfType<PagedResult<InventoryReconciliationDto>>().Subject;
            
            returnedResult.TotalRecords.Should().Be(1);
            returnedResult.Items[0].Status.Should().Be("Match");
        }
    }
}
