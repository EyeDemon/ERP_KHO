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
    public async Task TraceabilityWarehouses_UsesTheTracePermissionScopedQueryNotReversalLedger()
    {
        var reversal = new Mock<IInventoryReversalService>(MockBehavior.Strict);
        var trace = new Mock<IInventoryTraceabilityQueryService>(MockBehavior.Strict);
        IReadOnlyList<InventoryReversalWarehouseDto> permitted =
        [
            new() { Id = 2, Code = "KHO-02", Name = "Kho miền Nam" }
        ];
        trace.Setup(x => x.GetAccessibleWarehousesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(permitted);
        var controller = new InventoryReversalTraceabilityController(reversal.Object, trace.Object);

        var result = await controller.TraceabilityWarehouses();
        Assert.Same(permitted, Assert.IsType<OkObjectResult>(result.Result).Value);
        trace.Verify(x => x.GetAccessibleWarehousesAsync(It.IsAny<CancellationToken>()), Times.Once);
        trace.VerifyNoOtherCalls();
        reversal.VerifyNoOtherCalls();

        var method = typeof(InventoryReversalTraceabilityController)
            .GetMethod(nameof(InventoryReversalTraceabilityController.TraceabilityWarehouses))!;
        var permission = Assert.Single(method.GetCustomAttributes(
            typeof(ERP.Api.Authorization.PermissionAuthorizeAttribute), inherit: false));
        Assert.Equal(ERP.Api.Authorization.AppPermissions.InventoryTraceabilityRead,
            ((ERP.Api.Authorization.PermissionAuthorizeAttribute)permission).Permission);
    }

    [Fact]
    public async Task WarehouseOnlyTrace_ForwardsExplicitScopeWithoutBroadeningToAllWarehouses()
    {
        var reversal = new Mock<IInventoryReversalService>(MockBehavior.Strict);
        var trace = new Mock<IInventoryTraceabilityQueryService>(MockBehavior.Strict);
        var response = new InventoryTraceabilityResultDto
        {
            RelatedDocuments = [new InventoryTraceabilityRelatedDocumentDto
            {
                WarehouseId = 7, ReferenceType = "GoodsReceipt", ReferenceId = 21,
                EventCount = 2
            }],
            CurrentBuckets = [new InventoryTraceabilityBucketDto
            {
                ProductId = 9, WarehouseId = 7, OnHandQuantity = 4
            }]
        };
        trace.Setup(x => x.TraceAsync(7, null, null, null, null, null, 50,
            It.IsAny<CancellationToken>(), 500, 0, null)).ReturnsAsync(response);
        var controller = new InventoryReversalTraceabilityController(reversal.Object, trace.Object);

        var result = await controller.Trace(7, null, null, null, null, null, 50, 500);
        Assert.Same(response, Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Single(response.RelatedDocuments);
        Assert.Equal("GoodsReceipt", response.RelatedDocuments[0].ReferenceType);
        trace.Verify(x => x.TraceAsync(7, null, null, null, null, null, 50,
            It.IsAny<CancellationToken>(), 500, 0, null), Times.Once);
        trace.VerifyNoOtherCalls();
        reversal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CombinedReferenceTrace_ForwardsAllFiltersWithoutDroppingDocumentScope()
    {
        var reversal = new Mock<IInventoryReversalService>(MockBehavior.Strict);
        var trace = new Mock<IInventoryTraceabilityQueryService>(MockBehavior.Strict);
        var response = new InventoryTraceabilityResultDto();
        trace.Setup(x => x.TraceAsync(7, 9, "LOT-A", null, "StockTransfer", 42, 50,
            It.IsAny<CancellationToken>(), 0, 0, null)).ReturnsAsync(response);
        var controller = new InventoryReversalTraceabilityController(reversal.Object, trace.Object);

        var result = await controller.Trace(7, 9, "LOT-A", null, "StockTransfer", 42, 50, 0);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result.Result).Value);
        trace.Verify(x => x.TraceAsync(7, 9, "LOT-A", null, "StockTransfer", 42, 50,
            It.IsAny<CancellationToken>(), 0, 0, null), Times.Once);
        trace.VerifyNoOtherCalls();
        reversal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Trace_ForwardsIndependentLedgerOffsetAndBucketOffset()
    {
        var reversal = new Mock<IInventoryReversalService>(MockBehavior.Strict);
        var trace = new Mock<IInventoryTraceabilityQueryService>(MockBehavior.Strict);
        var response = new InventoryTraceabilityResultDto();
        trace.Setup(x => x.TraceAsync(7, 9, null, null, null, null, 50,
            It.IsAny<CancellationToken>(), 500, 100, 777)).ReturnsAsync(response);
        var controller = new InventoryReversalTraceabilityController(reversal.Object, trace.Object);

        var result = await controller.Trace(7, 9, null, null, null, null, 50, 500, 100, 777);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result.Result).Value);
        trace.Verify(x => x.TraceAsync(7, 9, null, null, null, null, 50,
            It.IsAny<CancellationToken>(), 500, 100, 777), Times.Once);
        trace.VerifyNoOtherCalls();
        reversal.VerifyNoOtherCalls();
    }


    [Fact]
    public void TraceResponse_ExposesReadOnlyShipmentImpactInVietnameseUiContract()
    {
        var response = new InventoryTraceabilityResultDto
        {
            EventAnchorId = 123,
            ShipmentExposures =
            [
                new InventoryTraceabilityShipmentExposureDto
                {
                    ShipmentId = 42, WarehouseId = 7, ShipmentCode = "SHIP-42",
                    ShipmentStatus = "Dispatched", DispatchedQuantity = 10,
                    LedgerEventCount = 2, LastTransactionId = 123
                }
            ],
            ShipmentExposuresTruncated = true,
            ShipmentPickingEvidence = [new InventoryTraceabilityShipmentPickingEvidenceDto
            {
                ShipmentId = 42, ShipmentCode = "SHIP-42", WarehouseId = 7,
                PackingSessionId = 15, PackingSessionCode = "PACK-15",
                PickingTaskId = 16, PickingTaskCode = "PICK-16",
                PickingTaskLineId = 17, AllocationId = 18,
                SourceLocationCode = "A-01", PickedQuantity = 10m
            }],
            ShipmentPickingEvidenceTruncated = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(
            response, new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(123, root.GetProperty("eventAnchorId").GetInt32());
        Assert.True(root.GetProperty("shipmentExposuresTruncated").GetBoolean());
        var shipment = Assert.Single(root.GetProperty("shipmentExposures").EnumerateArray());
        Assert.Equal("SHIP-42", shipment.GetProperty("shipmentCode").GetString());
        Assert.Equal(10m, shipment.GetProperty("dispatchedQuantity").GetDecimal());
        Assert.Equal(2, shipment.GetProperty("ledgerEventCount").GetInt32());
        Assert.True(root.GetProperty("shipmentPickingEvidenceTruncated").GetBoolean());
        var pick = Assert.Single(root.GetProperty("shipmentPickingEvidence").EnumerateArray());
        Assert.Equal(42, pick.GetProperty("shipmentId").GetInt32());
        Assert.Equal("PICK-16", pick.GetProperty("pickingTaskCode").GetString());
        Assert.Equal("PACK-15", pick.GetProperty("packingSessionCode").GetString());
        Assert.Equal(17, pick.GetProperty("pickingTaskLineId").GetInt32());
        Assert.Equal(18, pick.GetProperty("allocationId").GetInt32());
        Assert.Equal(10m, pick.GetProperty("pickedQuantity").GetDecimal());
    }

    [Fact]
    public void TraceResponse_ExposesPostedReceiptEvidenceInApiContract()
    {
        var response = new InventoryTraceabilityResultDto
        {
            EventAnchorId = 89,
            ReceiptExposures = [new InventoryTraceabilityReceiptExposureDto
            {
                ReceiptId = 21, WarehouseId = 7, ReceiptCode = "PN-21",
                PostedQuantity = 5m, LastPostedAt = new DateTime(2026, 10, 9, 8, 0, 0, DateTimeKind.Utc),
                LedgerEventCount = 2, LastTransactionId = 88
            }],
            ReceiptExposuresTruncated = true
        };
        var json = System.Text.Json.JsonSerializer.Serialize(
            response, new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("receiptExposuresTruncated").GetBoolean());
        var receipt = Assert.Single(root.GetProperty("receiptExposures").EnumerateArray());
        Assert.Equal("PN-21", receipt.GetProperty("receiptCode").GetString());
        Assert.Equal(5m, receipt.GetProperty("postedQuantity").GetDecimal());
        Assert.Equal(2, receipt.GetProperty("ledgerEventCount").GetInt32());
        Assert.Equal(88, receipt.GetProperty("lastTransactionId").GetInt32());
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
