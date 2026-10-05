using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/handling-units")]
[Authorize]
[PermissionAuthorize(AppPermissions.HandlingUnitRead)]
public sealed class HandlingUnitsController(IPackingService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<HandlingUnitDto>>> List(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default) =>
        Ok(await service.ListHandlingUnitsAsync(warehouseId, cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HandlingUnitDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetHandlingUnitAsync(id, cancellationToken));
}
