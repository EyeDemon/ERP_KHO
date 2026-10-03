using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController, Route("api/warehouses/{warehouseId:int}"), Authorize]
public sealed class WarehouseStructureController(
    IWarehouseStructureService structure,
    IPutawayService locations) : ControllerBase
{
    [HttpGet("structure"), PermissionAuthorize(AppPermissions.LocationRead)]
    public async Task<IActionResult> Structure(int warehouseId, CancellationToken ct) =>
        Ok(await structure.GetAsync(warehouseId, ct));

    [HttpGet("zones"), PermissionAuthorize(AppPermissions.LocationRead)]
    public async Task<IActionResult> Zones(int warehouseId, CancellationToken ct) =>
        Ok((await structure.GetAsync(warehouseId, ct)).Zones);

    [HttpPost("zones"), PermissionAuthorize(AppPermissions.WarehouseZoneManage)]
    public async Task<IActionResult> CreateZone(int warehouseId, CreateWarehouseZoneDto dto, CancellationToken ct) =>
        Ok(await structure.CreateZoneAsync(warehouseId, dto, ct));

    [HttpPut("zones/{zoneId:int}"), PermissionAuthorize(AppPermissions.WarehouseZoneManage)]
    public async Task<IActionResult> UpdateZone(int warehouseId, int zoneId, UpdateWarehouseZoneDto dto, CancellationToken ct) =>
        Ok(await structure.UpdateZoneAsync(warehouseId, zoneId, dto, ct));

    [HttpPost("zones/{zoneId:int}/aisles"), PermissionAuthorize(AppPermissions.WarehouseZoneManage)]
    public async Task<IActionResult> CreateAisle(int warehouseId, int zoneId, CreateWarehouseAisleDto dto, CancellationToken ct) =>
        Ok(await structure.CreateAisleAsync(warehouseId, zoneId, dto, ct));

    [HttpPut("zones/{zoneId:int}/aisles/{aisleId:int}"), PermissionAuthorize(AppPermissions.WarehouseZoneManage)]
    public async Task<IActionResult> UpdateAisle(int warehouseId, int zoneId, int aisleId, UpdateWarehouseAisleDto dto, CancellationToken ct) =>
        Ok(await structure.UpdateAisleAsync(warehouseId, zoneId, aisleId, dto, ct));

    [HttpPost("aisles/{aisleId:int}/racks"), PermissionAuthorize(AppPermissions.WarehouseZoneManage)]
    public async Task<IActionResult> CreateRack(int warehouseId, int aisleId, CreateWarehouseRackDto dto, CancellationToken ct) =>
        Ok(await structure.CreateRackAsync(warehouseId, aisleId, dto, ct));

    [HttpPut("aisles/{aisleId:int}/racks/{rackId:int}"), PermissionAuthorize(AppPermissions.WarehouseZoneManage)]
    public async Task<IActionResult> UpdateRack(int warehouseId, int aisleId, int rackId, UpdateWarehouseRackDto dto, CancellationToken ct) =>
        Ok(await structure.UpdateRackAsync(warehouseId, aisleId, rackId, dto, ct));

    [HttpPost("racks/{rackId:int}/levels"), PermissionAuthorize(AppPermissions.WarehouseZoneManage)]
    public async Task<IActionResult> CreateLevel(int warehouseId, int rackId, CreateWarehouseRackLevelDto dto, CancellationToken ct) =>
        Ok(await structure.CreateLevelAsync(warehouseId, rackId, dto, ct));

    [HttpGet("locations"), PermissionAuthorize(AppPermissions.LocationRead)]
    public async Task<IActionResult> Locations(int warehouseId, CancellationToken ct) =>
        Ok(await locations.ListLocationsAsync(warehouseId, ct));
}
