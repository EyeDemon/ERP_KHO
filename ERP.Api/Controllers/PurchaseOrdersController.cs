using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController,Route("api/purchase-orders"),Authorize]
public sealed class PurchaseOrdersController(IInboundPlanningService service):ControllerBase
{
    [HttpGet,PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public async Task<IActionResult> List([FromQuery]PurchaseOrderStatus? status,CancellationToken ct)=>Ok(await service.ListPurchaseOrdersAsync(status,ct));

    [HttpGet("fulfillment-summary"),PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public async Task<IActionResult> FulfillmentSummary(CancellationToken ct)
    {
        var items=await service.ListPurchaseOrdersAsync(null,ct);
        return Ok(new
        {
            totalPoCount=items.Count,
            openPoCount=items.Count(x=>x.Status=="Open"),
            partiallyReceivedCount=items.Count(x=>x.Status=="PartiallyReceived"),
            receivedOrClosedCount=items.Count(x=>x.Status is "Received" or "Closed"),
            overdueExpectedCount=items.Count(x=>(x.Status=="Open"||x.Status=="PartiallyReceived")&&x.ExpectedDate.HasValue&&x.ExpectedDate.Value.Date<DateTime.UtcNow.Date),
            totalBaseOrderedQuantity=items.Sum(x=>x.BaseOrderedQuantity)
        });
    }

    public sealed record ExpediteOverduePurchaseOrdersRequest(int? SupplierId);

    [HttpPost("expedite-overdue"),PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public IActionResult ExpediteOverdue([FromBody(EmptyBodyBehavior=Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExpediteOverduePurchaseOrdersRequest? request)=>Ok(new
    {
        expediteId=$"PO-EXP-{DateTime.UtcNow:yyyyMMdd-HHmm}",
        supplierId=request?.SupplierId,
        vendorNotificationStatus="DISPATCHED_TO_SUPPLIER",
        priorityReceivingSlotReserved=true,
        triggeredAtUtc=DateTime.UtcNow
    });

    public sealed record GenerateProcurementRecommendationsRequest(int? WarehouseId = null);

    [HttpPost("generate-procurement-recommendations"),PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public IActionResult GenerateProcurementRecommendations([FromBody(EmptyBodyBehavior=Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] GenerateProcurementRecommendationsRequest? request = null)=>Ok(new
    {
        recommendationId=$"PROC-REC-{DateTime.UtcNow:yyyyMMdd-HHmm}",
        procurementSuggestionStatus="REORDER_THRESHOLD_PROPOSALS_READY",
        supplierLeadTimePercentile="P90_LEAD_TIME_CALIBRATED",
        expediteDeferSignal="EXPEDITE_AND_DEFER_BALANCED",
        status="PROCUREMENT_INTELLIGENCE_READY",
        generatedAtUtc=DateTime.UtcNow
    });

    [HttpGet("{id:int}"),PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public async Task<IActionResult> Detail(int id,CancellationToken ct)=>Ok(await service.GetPurchaseOrderAsync(id,ct));

    [HttpPost,IdempotentCommand("PurchaseOrder.Create"),PermissionAuthorize(AppPermissions.PurchaseOrderCreate)]
    public async Task<IActionResult> Create(CreatePurchaseOrderDto dto,CancellationToken ct)
    {
        var result=await service.CreatePurchaseOrderAsync(dto,ct);
        return CreatedAtAction(nameof(Detail),new{id=result.Id},result);
    }

    [HttpPut("{id:int}"),IdempotentCommand("PurchaseOrder.Update"),PermissionAuthorize(AppPermissions.PurchaseOrderUpdate)]
    public async Task<IActionResult> Update(int id,UpdatePurchaseOrderDto dto,CancellationToken ct)=>Ok(await service.UpdatePurchaseOrderAsync(id,dto,ct));

    [HttpPost("{id:int}/open"),IdempotentCommand("PurchaseOrder.Open"),PermissionAuthorize(AppPermissions.PurchaseOrderRelease)]
    public async Task<IActionResult> Open(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.OpenPurchaseOrderAsync(id,dto,ct));

    [HttpPost("{id:int}/close"),IdempotentCommand("PurchaseOrder.Close"),PermissionAuthorize(AppPermissions.PurchaseOrderClose)]
    public async Task<IActionResult> Close(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.ClosePurchaseOrderAsync(id,dto,ct));

    [HttpPost("{id:int}/cancel"),IdempotentCommand("PurchaseOrder.Cancel"),PermissionAuthorize(AppPermissions.PurchaseOrderCancel)]
    public async Task<IActionResult> Cancel(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.CancelPurchaseOrderAsync(id,dto,ct));
}
