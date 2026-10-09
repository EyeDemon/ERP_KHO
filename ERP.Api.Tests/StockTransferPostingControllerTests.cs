using ERP.Api.Controllers;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public sealed class StockTransferPostingControllerTests
{
    [Fact]
    public async Task DispatchAndReceiveUseDocumentServiceRatherThanDirectLedgerEndpoints()
    {
        var service = new Mock<IStockTransferService>(MockBehavior.Strict);
        var receipt = new ReceiveStockTransferDto
        {
            Details = [new() { ProductId = 9, ReceivedQuantity = 3, MissingQuantity = 1 }]
        };
        service.Setup(x => x.DispatchAsync(17, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        service.Setup(x => x.ReceiveAsync(17, receipt, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = new StockTransfersController(service.Object);
        Assert.IsType<NoContentResult>(await controller.Dispatch(17, CancellationToken.None));
        Assert.IsType<NoContentResult>(await controller.Receive(17, receipt, CancellationToken.None));

        service.Verify(x => x.DispatchAsync(17, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.ReceiveAsync(17, receipt, It.IsAny<CancellationToken>()), Times.Once);
        service.VerifyNoOtherCalls();
    }
}
