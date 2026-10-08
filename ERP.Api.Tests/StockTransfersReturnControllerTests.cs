using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Application.Inventory;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public sealed class StockTransfersReturnControllerTests
{
    [Fact]
    public async Task ReturnDelegatesDocumentScopedReasonWithoutDirectLedgerMutation()
    {
        var service = new Mock<IStockTransferService>(MockBehavior.Strict);
        var request = new ReturnStockTransferDto
        { ReasonCode = "TRANSFER_DISPATCH_ERROR", Reason = "Điều chuyển sai" };
        service.Setup(x => x.ReturnAsync(42, request, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = new StockTransfersController(service.Object);

        var result = await controller.Return(42, request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        service.Verify(x => x.ReturnAsync(42, request, It.IsAny<CancellationToken>()), Times.Once);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public void ReturnReasonsExposeControlledReadOnlyCatalog()
    {
        var controller = new StockTransfersController(Mock.Of<IStockTransferService>());
        var result = Assert.IsType<OkObjectResult>(controller.ReturnReasons());
        Assert.Same(StockTransferReturnReasonCatalog.All, result.Value);
        Assert.Equal(3, StockTransferReturnReasonCatalog.All.Count);
        Assert.False(StockTransferReturnReasonCatalog.IsAllowed("UNKNOWN"));
    }
}
