using ERP.Api.Authorization;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/users/{userId:int}/warehouse-access")]
[Authorize(Roles = AppRoles.Admin)]
public sealed class UserWarehouseAccessController(IUserWarehouseAccessService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(int userId, CancellationToken cancellationToken) =>
        Ok(await service.GetForUserAsync(userId, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Grant(int userId, GrantWarehouseAccessDto request, CancellationToken cancellationToken)
    {
        await service.GrantAsync(userId, request.WarehouseId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{warehouseId:int}")]
    public async Task<IActionResult> Revoke(int userId, int warehouseId, CancellationToken cancellationToken)
    {
        await service.RevokeAsync(userId, warehouseId, cancellationToken);
        return NoContent();
    }
}
