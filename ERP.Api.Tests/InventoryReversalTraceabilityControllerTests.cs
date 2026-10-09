using ERP.Api.Controllers;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public sealed class InventoryReversalTraceabilityControllerTests
{
    [Fact]
    public async Task Candidates_ForwardsWarehouseIdTransactionIdAndPagingWithoutReadSideMutation()
    {
        var reversal = new Mock<IInventoryReversalService>(MockBehavior.Strict);
        var page = new PagedResult<InventoryReversalCandidateDto>
        {
            Items = [new InventoryReversalCandidateDto { Id = 41, WarehouseId = 2, IsReversed = true }],
            TotalRecords = 1,
            PageIndex = 3,
            PageSize = 10
        };
        reversal.Setup(x => x.GetCandidatesAsync(2, 3, 10, 41, true, "ABC", It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
        var controller = new InventoryReversalTraceabilityController(
            reversal.Object, Mock.Of<IInventoryTraceabilityQueryService>());

        var result = await controller.Candidates(2, 41, true, "ABC", 3, 10);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(page, ok.Value);
        reversal.Verify(x => x.GetCandidatesAsync(2, 3, 10, 41, true, "ABC", It.IsAny<CancellationToken>()),
            Times.Once);
        reversal.Verify(x => x.ReverseAsync(It.IsAny<CreateInventoryReversalDto>(),
            It.IsAny<CancellationToken>()), Times.Never);
        reversal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reasons_AuthoritativeCatalog_RequiresReadOnlyDelegation()
    {
        var reversal = new Mock<IInventoryReversalService>(MockBehavior.Strict);
        IReadOnlyList<InventoryReversalReasonDto> items =
        [
            new InventoryReversalReasonDto { Code = "LOCATION_ERROR", Name = "Sai vị trí lưu kho", TransactionType = "Move" }
        ];
        reversal.Setup(x => x.GetReversalReasonsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);
        var controller = new InventoryReversalTraceabilityController(
            reversal.Object, Mock.Of<IInventoryTraceabilityQueryService>());

        var response = await controller.ReversalReasons();
        Assert.Same(items, Assert.IsType<OkObjectResult>(response.Result).Value);
        reversal.Verify(x => x.GetReversalReasonsAsync(It.IsAny<CancellationToken>()), Times.Once);
        reversal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WarehouseOnlyTrace_ForwardsExplicitScopeWithoutBroadeningToAllWarehouses()
    {
        var reversal = new Mock<IInventoryReversalService>(MockBehavior.Strict);
        var trace = new Mock<IInventoryTraceabilityQueryService>(MockBehavior.Strict);
        var response = new InventoryTraceabilityResultDto
        {
            CurrentBuckets = [new InventoryTraceabilityBucketDto
            {
                ProductId = 9, WarehouseId = 7, OnHandQuantity = 4
            }]
        };
        trace.Setup(x => x.TraceAsync(7, null, null, null, null, null, 50,
            It.IsAny<CancellationToken>(), 500)).ReturnsAsync(response);
        var controller = new InventoryReversalTraceabilityController(reversal.Object, trace.Object);

        var result = await controller.Trace(7, null, null, null, null, null, 50, 500);
        Assert.Same(response, Assert.IsType<OkObjectResult>(result.Result).Value);
        trace.Verify(x => x.TraceAsync(7, null, null, null, null, null, 50,
            It.IsAny<CancellationToken>(), 500), Times.Once);
        trace.VerifyNoOtherCalls();
        reversal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Warehouses_DelegatesToAuthorizedScopeService()
    {
        var reversal = new Mock<IInventoryReversalService>(MockBehavior.Strict);
        IReadOnlyList<InventoryReversalWarehouseDto> granted =
        [
            new InventoryReversalWarehouseDto { Id = 2, Code = "W2", Name = "Kho được phép" }
        ];
        reversal.Setup(x => x.GetReversalWarehousesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(granted);
        var controller = new InventoryReversalTraceabilityController(
            reversal.Object, Mock.Of<IInventoryTraceabilityQueryService>());

        var result = await controller.ReversalWarehouses();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(granted, ok.Value);
        reversal.Verify(x => x.GetReversalWarehousesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        reversal.VerifyNoOtherCalls();
    }
}
