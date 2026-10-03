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
    public async Task<IActionResult> Grant(int userId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] GrantWarehouseAccessDto? request, CancellationToken cancellationToken)
    {
        if (request is null) throw new ERP.Domain.Exceptions.ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
        await service.GrantAsync(userId, request.WarehouseId, request.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{warehouseId:int}"), IdempotentCommand("UserWarehouse.Revoke"), PermissionAuthorize(AppPermissions.UserWarehouseManage)]
    public async Task<IActionResult> Revoke(int userId, int warehouseId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RevokeWarehouseAccessDto? request, CancellationToken cancellationToken)
    {
        await service.RevokeAsync(userId, warehouseId, request?.RowVersion, cancellationToken);
        return NoContent();
    }
}
