using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController, Route("api/locations"), Authorize]
public sealed class LocationsController(IPutawayService service) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.LocationRead)]
    public async Task<IActionResult> List([FromQuery] int warehouseId, CancellationToken ct) =>
        Ok(await service.ListLocationsAsync(warehouseId, ct));

    [HttpGet("{id:int}"), PermissionAuthorize(AppPermissions.LocationRead)]
    public async Task<IActionResult> Detail(int id, CancellationToken ct) =>
        Ok(await service.GetLocationAsync(id, ct));

    [HttpGet("barcode/{barcode}"), PermissionAuthorize(AppPermissions.LocationRead)]
    public async Task<IActionResult> Barcode(string barcode, [FromQuery] int? warehouseId, CancellationToken ct) =>
        Ok(await service.ResolveLocationBarcodeAsync(barcode, warehouseId, ct));

    [HttpPost, PermissionAuthorize(AppPermissions.LocationManage)]
    public async Task<IActionResult> Create(CreateWarehouseLocationDto dto, CancellationToken ct) =>
        Ok(await service.CreateLocationAsync(dto, ct));

    [HttpPatch("{id:int}"), PermissionAuthorize(AppPermissions.LocationManage)]
    public async Task<IActionResult> Update(int id, UpdateWarehouseLocationDto dto, CancellationToken ct) =>
        Ok(await service.UpdateLocationAsync(id, dto, ct));
}
