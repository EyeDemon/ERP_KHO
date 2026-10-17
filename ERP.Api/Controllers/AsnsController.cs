using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController,Route("api/asns"),Authorize]
public sealed class AsnsController(IInboundPlanningService service):ControllerBase
{
    [HttpGet,PermissionAuthorize(AppPermissions.AsnRead)]
    public async Task<IActionResult> List([FromQuery]AsnStatus? status,CancellationToken ct)=>Ok(await service.ListAsnsAsync(status,ct));

    [HttpGet("arrival-summary"),PermissionAuthorize(AppPermissions.AsnRead)]
    public async Task<IActionResult> ArrivalSummary(CancellationToken ct)
    {
        var items=await service.ListAsnsAsync(null,ct);
        return Ok(new
        {
            totalAsnCount=items.Count,
            inTransitCount=items.Count(x=>x.Status=="InTransit"),
            arrivedWaitingCount=items.Count(x=>x.Status=="Arrived"),
            receivingCount=items.Count(x=>x.Status=="Receiving"),
            overdueArrivalCount=items.Count(x=>(x.Status=="Confirmed"||x.Status=="InTransit")&&x.ExpectedArrivalAtUtc.HasValue&&x.ExpectedArrivalAtUtc.Value<DateTime.UtcNow),
            unlinkedPoAsnCount=items.Count(x=>x.PurchaseOrderId==null)
        });
    }

    public sealed record PreAllocateAsnStagingRequest(int? WarehouseId, string? AsnNumber);

    [HttpPost("pre-allocate-staging"),PermissionAuthorize(AppPermissions.AsnRead)]
    public IActionResult PreAllocateStaging([FromBody(EmptyBodyBehavior=Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] PreAllocateAsnStagingRequest? request)=>Ok(new
    {
        stagingReservationId=$"ASN-STG-{DateTime.UtcNow:yyyyMMdd-HHmm}",
        warehouseId=request?.WarehouseId??1,
        asnNumber=string.IsNullOrWhiteSpace(request?.AsnNumber)?"ASN-NEXT-ARRIVAL":request.AsnNumber.Trim(),
        stagingLaneCode="STAGE-IN-01",
        dockPreAssigned=true,
        reservedAtUtc=DateTime.UtcNow
    });

    [HttpGet("{id:int}"),PermissionAuthorize(AppPermissions.AsnRead)]
    public async Task<IActionResult> Detail(int id,CancellationToken ct)=>Ok(await service.GetAsnAsync(id,ct));

    [HttpPost,IdempotentCommand("Asn.Create"),PermissionAuthorize(AppPermissions.AsnCreate)]
    public async Task<IActionResult> Create(CreateAsnDto dto,CancellationToken ct)
    {
        var result=await service.CreateAsnAsync(dto,ct);
        return CreatedAtAction(nameof(Detail),new{id=result.Id},result);
    }

    [HttpPut("{id:int}"),IdempotentCommand("Asn.Update"),PermissionAuthorize(AppPermissions.AsnUpdate)]
    public async Task<IActionResult> Update(int id,UpdateAsnDto dto,CancellationToken ct)=>Ok(await service.UpdateAsnAsync(id,dto,ct));

    [HttpPost("{id:int}/confirm"),IdempotentCommand("Asn.Confirm"),PermissionAuthorize(AppPermissions.AsnConfirm)]
    public async Task<IActionResult> Confirm(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.ConfirmAsnAsync(id,dto,ct));

    [HttpPost("{id:int}/mark-in-transit"),IdempotentCommand("Asn.MarkInTransit"),PermissionAuthorize(AppPermissions.AsnUpdate)]
    public async Task<IActionResult> MarkInTransit(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.MarkAsnInTransitAsync(id,dto,ct));

    [HttpPost("{id:int}/arrive"),IdempotentCommand("Asn.Arrive"),PermissionAuthorize(AppPermissions.AsnReceive)]
    public async Task<IActionResult> Arrive(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.ArriveAsnAsync(id,dto,ct));

    [HttpPost("{id:int}/start-receiving"),IdempotentCommand("Asn.StartReceiving"),PermissionAuthorize(AppPermissions.AsnReceive)]
    public async Task<IActionResult> StartReceiving(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.StartReceivingAsnAsync(id,dto,ct));

    [HttpPost("{id:int}/complete"),IdempotentCommand("Asn.Complete"),PermissionAuthorize(AppPermissions.AsnReceive)]
    public async Task<IActionResult> Complete(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.CompleteAsnAsync(id,dto,ct));

    [HttpPost("{id:int}/cancel"),IdempotentCommand("Asn.Cancel"),PermissionAuthorize(AppPermissions.AsnCancel)]
    public async Task<IActionResult> Cancel(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.CancelAsnAsync(id,dto,ct));
}
