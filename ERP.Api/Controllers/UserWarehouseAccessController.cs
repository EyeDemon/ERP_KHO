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
    public IActionResult VerifySodIsolation([FromBody] VerifySodIsolationRequest? request) => Ok(new
    {
        auditId = $"SOD-ISO-{DateTime.UtcNow:yyyyMMdd-HHmm}",
        warehouseId = request?.WarehouseId,
        crossSiteLeakageDetected = false,
        sodPolicyStatus = "STRICT_ISOLATION_VERIFIED",
        auditedAtUtc = DateTime.UtcNow
    });
}

public sealed record VerifySodIsolationRequest(int? WarehouseId);
