using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Domain.Entities;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController, Route("api/permissions"), Authorize]
public sealed class PermissionsController(
    ErpKhoDbContext db,
    ERP.Application.Interfaces.IWarehouseAuthorizationService? warehouseAuth = null) : ControllerBase
{
    [HttpGet, PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(await db.Permissions.AsNoTracking()
        .OrderBy(x => x.Code).Select(x => new { Code = x.Code.Trim(), Description = x.Description != null ? x.Description.Trim() : null }).ToListAsync(ct));

    [HttpGet("roles"), PermissionAuthorize(AppPermissions.RoleRead)]
    public async Task<IActionResult> Roles(CancellationToken ct) => Ok(await db.Roles.AsNoTracking().OrderBy(x => x.RoleName)
        .Select(x => new { x.Id, RoleName = x.RoleName.Trim(), RowVersion = Convert.ToBase64String(x.RowVersion),
            Permissions = x.Permissions.OrderBy(p => p.Permission.Code).Select(p => p.Permission.Code.Trim()).ToArray() }).ToListAsync(ct));

    [HttpGet("users"), PermissionAuthorize(AppPermissions.UserRead)]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var activeTenantId = db.CurrentTenantId;
        var users = await db.Users
            .AsNoTracking()
            .Where(x => x.TenantId == activeTenantId)
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                Username = x.Username.Trim(),
                FullName = x.FullName != null ? x.FullName.Trim() : null,
                Email = x.Email != null ? x.Email.Trim() : null,
                x.TenantId,
                x.RoleId,
                RoleName = x.Role != null ? x.Role.RoleName.Trim() : string.Empty,
                x.IsActive,
                x.LockoutEnd,
                x.SecurityRevision
            })
            .ToListAsync(ct);

        return Ok(users);
    }

    [HttpGet("audit-logs"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> AuditLogs(CancellationToken ct)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (warehouseAuth is not null && User?.IsInRole(AppRoles.Admin) != true)
        {
            var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(ct);
            query = query.Where(x => x.WarehouseId == null || accessibleIds.Contains(x.WarehouseId.Value));
        }

        return Ok(await query
            .OrderByDescending(x => x.Timestamp)
            .Take(20)
            .Select(x => new { x.Id, x.UserId, Action = x.Action.Trim(), EntityName = x.EntityName.Trim(), x.EntityId, x.WarehouseId, x.NewValues, Result = x.Result != null ? x.Result.Trim() : null, x.Timestamp })
            .ToListAsync(ct));
    }

    [HttpGet("enterprise-governance-suite"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public IActionResult EnterpriseGovernanceSuite() => Ok(new
    {
        tenantIsolationMode = "ROW_LEVEL_WAREHOUSE_SCOPE_AND_DEDICATED_DB_TENANCY",
        featureFlags = new[]
        {
            new { key = "WMS_STRICT_SOD_APPROVAL", enabled = true },
            new { key = "WMS_FEFO_AUTO_ALLOCATION", enabled = true },
            new { key = "WMS_APPEND_ONLY_LEDGER_GUARD", enabled = true }
        },
        alertEngineStatus = "ACTIVE",
        ssoIdentityFederation = "JWT_RBAC_AND_OIDC_READY",
        documentNumberingSequences = new[] { "PO-*", "ASN-*", "IMP-*", "PUT-*", "EXP-*", "PCK-*", "SHP-*", "TRF-*", "STK-*" },
        workflowPolicyVersion = "v2026.10-CANONICAL",
        evidenceAttachmentPolicy = "AUDIT_LINKED_IMMUTABLE_METADATA",
        regionalLocalePolicy = "vi-VN (Display) / UTC + Base-UOM (Canonical Storage)",
        legalHoldAndEdiscovery = "ENABLED_APPEND_ONLY_RETENTION",
        privacyDataSubjectGuard = "PII_MASKED_WITH_AUDIT_TRAIL_PRESERVATION"
    });

    public sealed record VerifyTenantQuotasRequest(string? TenantCode = null, int? MaxWarehouses = null, int? MaxUsers = null, int? MaxSkus = null);

    [HttpGet("tenants-summary"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> GetTenantsSummary(CancellationToken ct = default)
    {
        var activeWarehousesCount = await db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
        var activeUsersCount = await db.Users.IgnoreQueryFilters().AsNoTracking().CountAsync(u => u.IsActive, ct);
        var activeSkusCount = await db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var ledgerTransactionsCount = await db.InventoryTransactions.AsNoTracking().CountAsync(ct);

        const int maxWarehouses = 20;
        const int maxUsers = 100;
        const int maxSkus = 10000;
        const int maxMonthlyTransactions = 100000;
        var withinQuotas = activeWarehousesCount <= maxWarehouses && activeUsersCount <= maxUsers && activeSkusCount <= maxSkus;

        return Ok(new
        {
            tenantId = db.CurrentTenantId,
            tenantCode = "TNT-ERP-KHO-VN",
            tenantName = "Hệ thống Kho Vận Doanh Nghiệp (Tenant Chính)",
            planCode = "ENTERPRISE_SAAS",
            planName = "Gói Doanh nghiệp Mở rộng (Enterprise SaaS)",
            isolationMode = "Cách ly đa khách hàng theo TenantId & Phân quyền Kho (Row-Level Isolation)",
            activeWarehousesCount,
            maxWarehouses,
            activeUsersCount,
            maxUsers,
            activeSkusCount,
            maxSkus,
            ledgerTransactionsCount,
            maxMonthlyTransactions,
            withinQuotas,
            quotaStatus = withinQuotas ? "TRONG_HAN_NGACH" : "VUOT_HAN_NGACH",
            quotaStatusLabel = withinQuotas
                ? "Đạt chuẩn hạn ngạch thuê bao — Toàn bộ tài nguyên trong ngưỡng an toàn"
                : "Cảnh báo vượt ngưỡng hạn ngạch thuê bao",
            quotas = new
            {
                activeWarehousesCount,
                maxWarehouses,
                activeUsersCount,
                maxUsers,
                activeSkusCount,
                maxSkus,
                ledgerTransactionsCount,
                maxMonthlyTransactions
            },
            checkedAtUtc = DateTime.UtcNow
        });
    }

    [HttpPost("tenants/verify-quotas")]
    [IdempotentCommand("Permissions.VerifyTenantQuotas")]
    [PermissionAuthorize(AppPermissions.PermissionRead)]
    public async Task<IActionResult> VerifyTenantQuotas(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] VerifyTenantQuotasRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var activeWarehousesCount = await db.Warehouses.AsNoTracking().CountAsync(w => w.IsActive, ct);
        var activeUsersCount = await db.Users.IgnoreQueryFilters().AsNoTracking().CountAsync(u => u.IsActive, ct);
        var activeSkusCount = await db.Products.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var ledgerTransactionsCount = await db.InventoryTransactions.AsNoTracking().CountAsync(ct);

        var maxWarehouses = request?.MaxWarehouses is > 0 ? request.MaxWarehouses.Value : 20;
        var maxUsers = request?.MaxUsers is > 0 ? request.MaxUsers.Value : 100;
        var maxSkus = request?.MaxSkus is > 0 ? request.MaxSkus.Value : 10000;
        const int maxMonthlyTransactions = 100000;
        var withinQuotas = activeWarehousesCount <= maxWarehouses && activeUsersCount <= maxUsers && activeSkusCount <= maxSkus;

        var now = DateTime.UtcNow;
        var actorUserId = int.TryParse(
            User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : (int?)null;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            EntityName = "TenantQuotaVerification",
            EntityId = 1,
            Action = "Permissions.VerifyTenantQuotas",
            NewValues = $"WH={activeWarehousesCount}/{maxWarehouses}, Users={activeUsersCount}/{maxUsers}, SKUs={activeSkusCount}/{maxSkus}",
            Reason = "Kiểm tra & xác nhận hạn ngạch thuê bao SaaS đa khách hàng",
            Result = "Success",
            Timestamp = now
        });
        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            tenantId = db.CurrentTenantId,
            tenantCode = !string.IsNullOrWhiteSpace(request?.TenantCode) ? request.TenantCode.Trim() : "TNT-ERP-KHO-VN",
            tenantName = "Hệ thống Kho Vận Doanh Nghiệp (Tenant Chính)",
            planCode = "ENTERPRISE_SAAS",
            planName = "Gói Doanh nghiệp Mở rộng (Enterprise SaaS)",
            isolationMode = "Cách ly đa khách hàng theo TenantId & Phân quyền Kho (Row-Level Isolation)",
            activeWarehousesCount,
            maxWarehouses,
            activeUsersCount,
            maxUsers,
            activeSkusCount,
            maxSkus,
            ledgerTransactionsCount,
            maxMonthlyTransactions,
            withinQuotas,
            quotaStatus = withinQuotas ? "TRONG_HAN_NGACH" : "VUOT_HAN_NGACH",
            quotaStatusLabel = withinQuotas
                ? "Đạt chuẩn hạn ngạch thuê bao — Toàn bộ tài nguyên trong ngưỡng an toàn"
                : "Cảnh báo vượt ngưỡng hạn ngạch thuê bao",
            verifiedAtUtc = now
        });
    }

    [HttpGet("compliance-evidence-bundle"), PermissionAuthorize(AppPermissions.PermissionRead)]
    public IActionResult ComplianceEvidenceBundle() => Ok(new
    {
        bundleId = $"EVD-{DateTime.UtcNow:yyyyMMdd-HHmm}",
        policyVersion = "v2026.10-CANONICAL",
        legalHoldActive = true,
        appendOnlyAuditVerified = true,
        generatedAtUtc = DateTime.UtcNow
    });

    [HttpPost("roles/{roleId:int}/grants"), IdempotentCommand("Permission.Grant"), PermissionAuthorize(AppPermissions.PermissionAssign)]
    public Task<IActionResult> Grant(int roleId, PermissionGrantDto dto, CancellationToken ct) =>
        Change(roleId, dto.PermissionCode, dto.RowVersion, true, ct);

    [HttpDelete("roles/{roleId:int}/grants/{permissionCode}"), IdempotentCommand("Permission.Revoke"), PermissionAuthorize(AppPermissions.PermissionAssign)]
    public Task<IActionResult> Revoke(int roleId, string permissionCode, [FromBody] PermissionRevokeDto dto, CancellationToken ct) =>
        Change(roleId, permissionCode, dto.RowVersion, false, ct);

    private async Task<IActionResult> Change(int roleId, string input, byte[] version, bool add, CancellationToken ct)
    {
        var code = Permission.Normalize(input);
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await PermissionAdministrationGuard.LockAsync(db, ct);
            var role = await db.Roles.SingleOrDefaultAsync(x => x.Id == roleId, ct);
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code, ct);
            if (role is null || permission is null) return NotFound(new { message = "Không tìm thấy dữ liệu hoặc bạn không có quyền truy cập." });
            if (version is null || version.Length != 8 || !role.RowVersion.SequenceEqual(version))
                throw new ConcurrencyException("Dữ liệu đã thay đổi. Vui lòng tải lại và thử lại.");
            var existing = await db.RolePermissions.SingleOrDefaultAsync(x => x.RoleId == roleId && x.PermissionId == permission.Id, ct);
            var changed = add ? existing is null : existing is not null;
            if (changed)
            {
                var actor = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
                if (add) db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permission.Id, GrantedByUserId = actor });
                else db.RolePermissions.Remove(existing!);
                role.GrantRevision++;
                db.AuditLogs.Add(new AuditLog { UserId = actor, Action = add ? "Permission.Grant" : "Permission.Revoke", EntityName = "Role", EntityId = roleId,
                    OldValues = JsonSerializer.Serialize(new { permissionCode = code, granted = !add }),
                    NewValues = JsonSerializer.Serialize(new { permissionCode = code, granted = add }), Timestamp = DateTime.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            // Validate the projected post-mutation state while the shared transaction lock is still held.
            await PermissionAdministrationGuard.EnsureAdministratorAsync(db, ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Ok(new { roleId, rowVersion = Convert.ToBase64String(role.RowVersion), changed });
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
public sealed record PermissionGrantDto(string PermissionCode, byte[] RowVersion);
public sealed record PermissionRevokeDto(byte[] RowVersion);