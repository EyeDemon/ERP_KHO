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

    [HttpGet("summary")]
    [PermissionAuthorize(AppPermissions.HandlingUnitRead)]
    public async Task<IActionResult> Summary(
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var units = await service.ListHandlingUnitsAsync(warehouseId, cancellationToken);
        return Ok(new
        {
            totalHandlingUnits = units.Count,
            openOrInUseCount = units.Count(x => x.Status is "Open" or "InUse" or "Packing"),
            sealedCount = units.Count(x => x.Status is "Closed" or "Sealed" || x.SealedAt != null),
            stagedOrLoadedCount = units.Count(x => x.Status is "Staged" or "Loaded"),
            shippedCount = units.Count(x => x.Status == "Shipped"),
            nestedHuCount = units.Count(x => x.ParentHandlingUnitId != null),
            ssccAssignedCount = units.Count(x => !string.IsNullOrWhiteSpace(x.Sscc)),
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<HandlingUnitDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetHandlingUnitAsync(id, cancellationToken));
}
