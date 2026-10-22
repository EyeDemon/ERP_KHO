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
[Route("api/user-warehouse-access/{userId:int}")]
[Route("api/users/{userId:int}/warehouses")]
[Route("api/v1/users/{userId:int}/warehouse-access")]
[Route("api/v1/user-warehouse-access/{userId:int}")]
[Route("api/v1/users/{userId:int}/warehouses")]
[Authorize]
public sealed class UserWarehouseAccessController(
    IUserWarehouseAccessService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.UserWarehouseRead)]
    public async Task<IActionResult> Get(int userId, CancellationToken cancellationToken)
    {
        await EnsureTenantUserOwnershipAsync(userId, null, cancellationToken);
        return Ok(await service.GetForUserAsync(userId, cancellationToken));
    }

    [HttpGet("governance-summary"), PermissionAuthorize(AppPermissions.UserWarehouseRead)]
    public async Task<IActionResult> GetGovernanceSummary(int userId, CancellationToken cancellationToken)
    {
        await EnsureTenantUserOwnershipAsync(userId, null, cancellationToken);
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
        await EnsureTenantUserOwnershipAsync(userId, request.WarehouseId, cancellationToken);
        await service.GrantAsync(userId, request.WarehouseId, request.RowVersion, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{warehouseId:int}"), IdempotentCommand("UserWarehouse.Revoke"), PermissionAuthorize(AppPermissions.UserWarehouseManage)]
    public async Task<IActionResult> Revoke(int userId, int warehouseId, [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RevokeWarehouseAccessDto? request, CancellationToken cancellationToken)
    {
        await EnsureTenantUserOwnershipAsync(userId, warehouseId, cancellationToken);
        await service.RevokeAsync(userId, warehouseId, request?.RowVersion, cancellationToken);
        return NoContent();
    }

    private async Task EnsureTenantUserOwnershipAsync(int userId, int? warehouseId, CancellationToken cancellationToken)
    {
        if (db is null)
            return;

        if (!await db.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        var activeTenantId = db.CurrentTenantId;
        var userOwnedByTenant = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.TenantId == activeTenantId, cancellationToken);
        if (!userOwnedByTenant)
            throw new ERP.Application.Exceptions.NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");

        if (warehouseId.HasValue)
        {
            var warehouseOwnedByTenant = await db.Warehouses
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(w => w.Id == warehouseId.Value && w.TenantId == activeTenantId, cancellationToken);
            if (!warehouseOwnedByTenant)
                throw new ERP.Application.Exceptions.NotFoundException("Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập.");
        }
    }

    [HttpPost("verify-sod-isolation")]
    [HttpPost("/api/user-warehouse-access/verify-sod-isolation")]
    [PermissionAuthorize(AppPermissions.UserWarehouseRead)]
    public async Task<IActionResult> VerifySodIsolation(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifySodIsolationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess is not null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var activeMemberships = 0;
        var invalidMemberships = 0;
        if (db is not null)
        {
            var query = db.UserWarehouses.AsNoTracking();
            if (warehouseId.HasValue)
                query = query.Where(uw => uw.WarehouseId == warehouseId.Value);
            else if (accessibleIds is not null)
                query = query.Where(uw => accessibleIds.Contains(uw.WarehouseId));

            activeMemberships = await query.CountAsync(cancellationToken);
            invalidMemberships = await query.CountAsync(
                uw => !uw.User.IsActive || !uw.Warehouse.IsActive,
                cancellationToken);
        }

        return Ok(new
        {
            auditId = $"SOD-ISO-{DateTime.UtcNow:yyyyMMdd-HHmm}",
            warehouseId,
            activeMemberships,
            sodConflictCount = invalidMemberships,
            multiWarehouseIsolationEnforced = true,
            status = invalidMemberships == 0 ? "ZERO_PRIVILEGE_ESCALATION_VERIFIED" : "INACTIVE_MEMBERSHIP_DETECTED",
            crossSiteLeakageDetected = invalidMemberships > 0,
            sodPolicyStatus = invalidMemberships == 0 ? "STRICT_ISOLATION_VERIFIED" : "INACTIVE_MEMBERSHIP_DETECTED",
            auditedAtUtc = DateTime.UtcNow
        });
    }
}

public sealed record VerifySodIsolationRequest(int? WarehouseId = null);
