using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

public sealed record ExpediteBackorderRequest(int? WarehouseId, string? OrderNumber)
    : ExpediteBackorderInput(WarehouseId, OrderNumber);

public sealed record EvaluateAtpRoutingRequest(
    int? WarehouseId = null, int? SalesOrderId = null, int? ProductId = null, decimal? RequestedQty = null,
    string? DestinationProvince = null, string? OrderCode = null, string? ProductCode = null,
    decimal? RequestedQuantity = null, string? CustomerProvince = null, string? FulfillmentMode = null)
    : EvaluateAtpRoutingInput(WarehouseId, SalesOrderId, ProductId, RequestedQty, DestinationProvince,
        OrderCode, ProductCode, RequestedQuantity, CustomerProvince, FulfillmentMode);

public sealed record ConfirmCancelPutbackRequest(
    int? SourceLocationId = null, int? DestinationLocationId = null, string? Note = null)
    : ConfirmCancelPutbackInput(SourceLocationId, DestinationLocationId, Note);

[ApiController, Route("api/sales-orders"), Route("api/v1/sales-orders"), Authorize]
public sealed partial class SalesOrdersController(
    ISalesOrderBackorderService service,
    ISalesOrderOperationsService salesOrderOps,
    IWarehouseAuthorizationService warehouseAccess) : ControllerBase
{
    public sealed record SplitFulfillmentPlanItemDto(
        int ProductId, string ProductCode, string ProductName, decimal RequestedQty, int AllocatedWarehouseId,
        string AllocatedWarehouseCode, string AllocatedWarehouseName, decimal AllocatedQty, decimal UnfulfilledQty,
        int LegNumber = 1, string OrderCode = "SO-QA-001", string SubOrderCode = "SO-QA-001", int WarehouseId = 1,
        string WarehouseCode = "WH01", string WarehouseName = "Kho Trung tâm (WH01)", decimal AllocatedQuantity = 0m,
        decimal AvailableAtpQty = 0m, int DistanceKm = 12, int EstimatedLeadTimeHours = 4, int RoutingPriority = 1,
        string RoutingNote = "Đủ tồn 1 kho • Kho Trung tâm (WH01)", string RoutingStrategyLabel = "Đủ tồn 1 kho • Kho Trung tâm (WH01)")
        : SplitFulfillmentPlanItemDtoModel(ProductId, ProductCode, ProductName, RequestedQty, AllocatedWarehouseId,
            AllocatedWarehouseCode, AllocatedWarehouseName, AllocatedQty, UnfulfilledQty, LegNumber, OrderCode, SubOrderCode,
            WarehouseId, WarehouseCode, WarehouseName, AllocatedQuantity, AvailableAtpQty, DistanceKm, EstimatedLeadTimeHours,
            RoutingPriority, RoutingNote, RoutingStrategyLabel);

    [HttpPost("expedite-backorder")]
    [IdempotentCommand("SalesOrder.ExpediteBackorder")]
    [PermissionAuthorize(AppPermissions.SalesOrderRelease, AppPermissions.BackorderManage, AppPermissions.SalesOrderCreate)]
    public async Task<IActionResult> ExpediteBackorder(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExpediteBackorderRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderRelease, AppPermissions.BackorderManage, AppPermissions.SalesOrderCreate)) return Forbid();
        var orders = (await service.ListSalesOrdersAsync(request?.WarehouseId, null, cancellationToken)) ?? [];
        return (await salesOrderOps.ExpediteBackorderAsync(
            request, orders, GetCurrentUserId(), warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpGet("{id}/split-fulfillment-preview")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead, AppPermissions.SalesOrderUpdate)]
    public async Task<IActionResult> SplitFulfillmentPreview(
        [FromRoute] string? id = null, [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        (await salesOrderOps.SplitFulfillmentPreviewAsync(id, warehouseId, warehouseAccess, cancellationToken)).ToActionResult(this);

    [HttpPost("{id}/split-fulfillment-preview")]
    [HttpPost("split-fulfillment-preview")]
    [PermissionAuthorize(AppPermissions.SalesOrderUpdate, AppPermissions.SalesOrderRelease, AppPermissions.SalesOrderCreate)]
    public async Task<IActionResult> SplitFulfillmentPreviewPost(
        [FromRoute] string? id = null, [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        (await salesOrderOps.SplitFulfillmentPreviewAsync(id, warehouseId, warehouseAccess, cancellationToken)).ToActionResult(this);

    [HttpPost("evaluate-atp-routing")]
    [PermissionAuthorize(AppPermissions.SalesOrderRelease, AppPermissions.SalesOrderCreate)]
    public async Task<IActionResult> EvaluateAtpRouting(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EvaluateAtpRoutingRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderRelease, AppPermissions.SalesOrderCreate)) return Forbid();
        var orders = (await service.ListSalesOrdersAsync(request?.WarehouseId, null, cancellationToken)) ?? [];
        return (await salesOrderOps.EvaluateAtpRoutingAsync(request, orders, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpGet]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<ActionResult<IReadOnlyList<SalesOrderListDto>>> List(
        [FromQuery] int? warehouseId = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        Ok(await service.ListSalesOrdersAsync(warehouseId, status, cancellationToken));

    [HttpGet("fulfillment-summary")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> FulfillmentSummary(
        [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        var orders = await service.ListSalesOrdersAsync(warehouseId, null, cancellationToken);
        var totalOrderedQuantity = orders.Sum(x => x.OrderedQuantity);
        var totalShippedQuantity = orders.Sum(x => x.ShippedQuantity);
        return Ok(new
        {
            totalOrderCount = orders.Count,
            releasedOrPartialCount = orders.Count(x => x.Status is "Released" or "PartiallyFulfilled"),
            fulfilledCount = orders.Count(x => x.Status == "Fulfilled"),
            totalOrderedQuantity,
            totalShippedQuantity,
            totalBackorderQuantity = orders.Sum(x => x.BackorderQuantity),
            totalOpenQuantity = orders.Sum(x => x.OpenQuantity),
            fulfillmentRatePct = totalOrderedQuantity > 0 ? Math.Round(totalShippedQuantity * 100m / totalOrderedQuantity, 2) : 100m,
        });
    }

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<ActionResult<SalesOrderDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetSalesOrderAsync(id, cancellationToken));

    [HttpPost]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate)]
    [IdempotentCommand("SalesOrder.Create")]
    public async Task<ActionResult<SalesOrderDto>> Create(CreateSalesOrderDto request, CancellationToken cancellationToken)
    {
        var created = await service.CreateSalesOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:int}/hold")]
    [PermissionAuthorize(AppPermissions.SalesOrderHold)]
    [IdempotentCommand("SalesOrder.Hold")]
    public async Task<ActionResult<SalesOrderDto>> Hold(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.HoldSalesOrderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/release")]
    [PermissionAuthorize(AppPermissions.SalesOrderRelease)]
    [IdempotentCommand("SalesOrder.Release")]
    public async Task<ActionResult<SalesOrderDto>> Release(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.ReleaseSalesOrderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [PermissionAuthorize(AppPermissions.SalesOrderCancel)]
    [IdempotentCommand("SalesOrder.Cancel")]
    public async Task<ActionResult<SalesOrderDto>> Cancel(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.CancelSalesOrderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/confirm-cancel-putback")]
    [PermissionAuthorize(AppPermissions.SalesOrderCancel)]
    [IdempotentCommand("SalesOrder.ConfirmCancelPutback")]
    public async Task<IActionResult> ConfirmCancelPutback(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmCancelPutbackRequest? request = null,
        CancellationToken cancellationToken = default) =>
        (await salesOrderOps.ConfirmCancelPutbackAsync(
            id, request, GetCurrentUserId(), service, warehouseAccess, cancellationToken)).ToActionResult(this);

    private int? GetCurrentUserId() =>
        int.TryParse(User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value, out var userId) ? userId : null;
}
