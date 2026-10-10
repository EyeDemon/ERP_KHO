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
    public async Task LifecycleCommandsDelegateThroughStockTransferService()
    {
        var service = new Mock<IStockTransferService>(MockBehavior.Strict);
        var request = new CreateStockTransferDto
        {
            SourceWarehouseId = 1, DestinationWarehouseId = 2,
            Details = [new() { ProductId = 9, Quantity = 3 }]
        };
        var update = new UpdateStockTransferDto
        {
            SourceWarehouseId = 1, DestinationWarehouseId = 2,
            Details = [new() { ProductId = 9, Quantity = 4 }]
        };
        service.Setup(x => x.CreateAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTransferDto { Id = 17, Code = "TRF-17" });
        service.Setup(x => x.UpdateAsync(17, update, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        service.Setup(x => x.ApproveAsync(17, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        service.Setup(x => x.CompleteAsync(17, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        service.Setup(x => x.CancelAsync(18, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = new StockTransfersController(service.Object);

        var created = Assert.IsType<CreatedAtActionResult>(
            await controller.Create(request, CancellationToken.None));
        Assert.Equal(nameof(StockTransfersController.GetById), created.ActionName);
        Assert.Equal(17, created.RouteValues!["id"]);
        Assert.IsType<NoContentResult>(
            await controller.Update(17, update, CancellationToken.None));
        Assert.IsType<NoContentResult>(
            await controller.Approve(17, CancellationToken.None));
        Assert.IsType<NoContentResult>(
            await controller.Complete(17, CancellationToken.None));
        Assert.IsType<NoContentResult>(
            await controller.Cancel(18, CancellationToken.None));

        service.Verify(x => x.CreateAsync(request, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.UpdateAsync(17, update, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.ApproveAsync(17, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.CompleteAsync(17, It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(x => x.CancelAsync(18, It.IsAny<CancellationToken>()), Times.Once);
        service.VerifyNoOtherCalls();
    }

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
