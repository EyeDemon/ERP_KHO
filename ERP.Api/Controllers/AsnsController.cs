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
