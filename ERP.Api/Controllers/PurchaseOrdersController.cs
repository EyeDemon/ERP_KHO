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
