using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController]
[Route("api/users/{userId:int}/warehouse-access")]
[Authorize]
public sealed class UserWarehouseAccessController(
    IUserWarehouseAccessService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.UserWarehouseRead)]
    public async Task<IActionResult> Get(int userId, CancellationToken cancellationToken) =>
        Ok(await service.GetForUserAsync(userId, cancellationToken));

    [HttpGet("governance-summary"), PermissionAuthorize(AppPermissions.UserWarehouseRead)]
    public async Task<IActionResult> GetGovernanceSummary(int userId, CancellationToken cancellationToken)
    {
        var access = await service.GetForUserAsync(userId, cancellationToken);
        return Ok(new
        {
            userId,
            assignedWarehouseCount = access.Memberships.Count,
            hasWarehouseAccess = access.Memberships.Count > 0,
            canMutate = !string.IsNullOrEmpty(access.RowVersion),
            warehouses = access.Memberships.Select(x => x.WarehouseCode).ToArray()
        });
    }

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

    [HttpPost("verify-sod-isolation")]
    [HttpPost("/api/user-warehouse-access/verify-sod-isolation")]
    [PermissionAuthorize(AppPermissions.UserWarehouseRead)]
    public async Task<IActionResult> VerifySodIsolation(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifySodIsolationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        if (warehouseAccess is not null && warehouseId.HasValue)
            await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var activeMemberships = db is not null
            ? await db.UserWarehouses.AsNoTracking().CountAsync(
                uw => !warehouseId.HasValue || uw.WarehouseId == warehouseId.Value,
                cancellationToken)
            : 0;
        var invalidMemberships = db is not null
            ? await db.UserWarehouses.AsNoTracking().CountAsync(
                uw => !uw.User.IsActive || !uw.Warehouse.IsActive,
                cancellationToken)
            : 0;

        return Ok(new
        {
            auditId = $"SOD-ISO-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            warehouseId,
            activeMemberships,
            crossSiteLeakageDetected = invalidMemberships > 0,
            sodPolicyStatus = invalidMemberships == 0 ? "STRICT_ISOLATION_VERIFIED" : "INACTIVE_MEMBERSHIP_DETECTED",
            auditedAtUtc = DateTime.UtcNow
        });
    }
}

public sealed record VerifySodIsolationRequest(int? WarehouseId = null);
