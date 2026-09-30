using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/users/{userId:int}/warehouse-access")]
[Authorize]
public sealed class UserWarehouseAccessController(IUserWarehouseAccessService service) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.UserWarehouseRead)]
    public async Task<IActionResult> Get(int userId, CancellationToken cancellationToken) =>
        Ok(await service.GetForUserAsync(userId, cancellationToken));

    [HttpPost, IdempotentCommand("UserWarehouse.Grant"), PermissionAuthorize(AppPermissions.UserWarehouseManage)]
    public async Task<IActionResult> Grant(int userId, GrantWarehouseAccessDto request, CancellationToken cancellationToken)
    {
        await service.GrantAsync(userId, request.WarehouseId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{warehouseId:int}"), IdempotentCommand("UserWarehouse.Revoke"), PermissionAuthorize(AppPermissions.UserWarehouseManage)]
    public async Task<IActionResult> Revoke(int userId, int warehouseId, CancellationToken cancellationToken)
    {
        await service.RevokeAsync(userId, warehouseId, cancellationToken);
        return NoContent();
    }
}
