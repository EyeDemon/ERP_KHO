using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/warehouses/{warehouseId:int}/calendar")]
public sealed class WarehouseCalendarsController(IWarehouseCalendarService service) : ControllerBase
{
    [HttpGet]
    [PermissionAuthorize(AppPermissions.WarehouseRead)]
    public async Task<IActionResult> Get(int warehouseId, CancellationToken token) =>
        Ok(await service.GetAsync(warehouseId, token));

    [HttpPut]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> Update(int warehouseId, UpdateWarehouseCalendarDto dto, CancellationToken token) =>
        Ok(await service.UpdateCalendarAsync(warehouseId, dto, token));

    [HttpPost("shifts")]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> CreateShift(int warehouseId, UpsertWarehouseShiftDto dto, CancellationToken token) =>
        Ok(await service.CreateShiftAsync(warehouseId, dto, token));

    [HttpPut("shifts/{shiftId:int}")]
    [PermissionAuthorize(AppPermissions.WarehouseCalendarManage)]
    public async Task<IActionResult> UpdateShift(int warehouseId, int shiftId, UpsertWarehouseShiftDto dto, CancellationToken token) =>
        Ok(await service.UpdateShiftAsync(warehouseId, shiftId, dto, token));
}
