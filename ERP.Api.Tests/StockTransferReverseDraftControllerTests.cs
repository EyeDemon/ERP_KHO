using System.Reflection;
using ERP.Api.Authorization;
using ERP.Api.Controllers;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace ERP.Api.Tests;

public sealed class StockTransferReverseDraftControllerTests
{
    [Fact]
    public async Task ReverseDraftUsesNativeDocumentService()
    {
        var service = new Mock<IStockTransferService>(MockBehavior.Strict);
        var request = new CreateReverseStockTransferDto
        { ReasonCode = "TRANSFER_ROUTE_ERROR", Reason = "Trả về sau khi nhận" };
        service.Setup(x => x.CreateReverseDraftAsync(42, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockTransferDto { Id = 43, ReverseOfTransferId = 42 });
        var controller = new StockTransfersController(service.Object);
        var result = Assert.IsType<CreatedAtActionResult>(
            await controller.ReverseDraft(42, request, CancellationToken.None));
        Assert.Equal(nameof(StockTransfersController.GetById), result.ActionName);
        Assert.Equal(43, Assert.IsType<StockTransferDto>(result.Value).Id);
        service.Verify(x => x.CreateReverseDraftAsync(42, request, It.IsAny<CancellationToken>()), Times.Once);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public void ReverseDraftRequiresReversalPermissionAndIdempotency()
    {
        var method = typeof(StockTransfersController).GetMethod(nameof(StockTransfersController.ReverseDraft))!;
        Assert.Equal(AppPermissions.InventoryReversalCreate,
            method.GetCustomAttribute<PermissionAuthorizeAttribute>()?.Permission);
        Assert.NotNull(method.GetCustomAttribute<IdempotentCommandAttribute>());
    }
}
