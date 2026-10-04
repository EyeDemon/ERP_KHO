using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController, Route("api/putaway-tasks"), Authorize]
public sealed class PutawayTasksController(IPutawayService service) : ControllerBase
{
    [HttpGet("location-warehouses"),PermissionAuthorize(AppPermissions.LocationRead)] public async Task<IActionResult> LocationWarehouses(CancellationToken ct)=>Ok(await service.ListLocationWarehousesAsync(ct));
    [HttpGet("locations"),PermissionAuthorize(AppPermissions.LocationRead)] public async Task<IActionResult> Locations([FromQuery]int warehouseId,CancellationToken ct)=>Ok(await service.ListLocationsAsync(warehouseId,ct));
    [HttpGet("location-capacity"),PermissionAuthorize(AppPermissions.LocationRead)] public async Task<IActionResult> LocationCapacity([FromQuery]int warehouseId,CancellationToken ct)=>Ok(await service.ListLocationCapacitiesAsync(warehouseId,ct));
    [HttpPost("locations"),PermissionAuthorize(AppPermissions.LocationManage)] public async Task<IActionResult> CreateLocation(CreateWarehouseLocationDto dto,CancellationToken ct)=>Ok(await service.CreateLocationAsync(dto,ct));
    [HttpPut("locations/{locationId:int}"),PermissionAuthorize(AppPermissions.LocationManage)] public async Task<IActionResult> UpdateLocation(int locationId,UpdateWarehouseLocationDto dto,CancellationToken ct)=>Ok(await service.UpdateLocationAsync(locationId,dto,ct));
    [HttpGet,PermissionAuthorize(AppPermissions.PutawayRead)] public async Task<IActionResult> List(CancellationToken ct)=>Ok(await service.ListAsync(ct));
    [HttpGet("{id:int}"),PermissionAuthorize(AppPermissions.PutawayRead)] public async Task<IActionResult> Detail(int id,CancellationToken ct)=>Ok(await service.GetAsync(id,ct));
    [HttpGet("{id:int}/items/{itemId:int}/destinations"),PermissionAuthorize(AppPermissions.PutawayRead)] public async Task<IActionResult> Destinations(int id,int itemId,CancellationToken ct)=>Ok(await service.GetDestinationsAsync(id,itemId,ct));
    [HttpPost("{id:int}/assign"),IdempotentCommand("Putaway.Assign"),PermissionAuthorize(AppPermissions.PutawayAssign)] public async Task<IActionResult> Assign(int id,PutawayStateCommandDto dto,CancellationToken ct)=>Ok(await service.AssignAsync(id,dto,ct));
    [HttpPost("{id:int}/start"),IdempotentCommand("Putaway.Start"),PermissionAuthorize(AppPermissions.PutawayExecute)] public async Task<IActionResult> Start(int id,PutawayStateCommandDto dto,CancellationToken ct)=>Ok(await service.StartAsync(id,dto,ct));
    [HttpPost("{id:int}/move"),IdempotentCommand("Putaway.Move"),PermissionAuthorize(AppPermissions.PutawayExecute)] public async Task<IActionResult> Move(int id,MovePutawayItemDto dto,CancellationToken ct)=>Ok(await service.MoveAsync(id,dto,ct));
    [HttpPost("{id:int}/exception"),IdempotentCommand("Putaway.Exception"),PermissionAuthorize(AppPermissions.PutawayExecute)] public async Task<IActionResult> Exception(int id,PutawayStateCommandDto dto,CancellationToken ct)=>Ok(await service.OpenExceptionAsync(id,dto,ct));
    [HttpPost("{id:int}/resume"),IdempotentCommand("Putaway.Resume"),PermissionAuthorize(AppPermissions.PutawayExecute)] public async Task<IActionResult> Resume(int id,PutawayStateCommandDto dto,CancellationToken ct)=>Ok(await service.ResumeAsync(id,dto,ct));
    [HttpPost("{id:int}/cancel"),IdempotentCommand("Putaway.Cancel"),PermissionAuthorize(AppPermissions.PutawayCancel)] public async Task<IActionResult> Cancel(int id,PutawayStateCommandDto dto,CancellationToken ct)=>Ok(await service.CancelAsync(id,dto,ct));
}
