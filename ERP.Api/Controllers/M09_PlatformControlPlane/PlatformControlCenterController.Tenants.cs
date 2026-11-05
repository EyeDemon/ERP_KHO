using System.Data;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Domain.Entities;
using ERP.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    [HttpGet("tenants")]
    [PlatformAuthorize(AppPermissions.PlatformTenantRead)]
    public async Task<IActionResult> GetTenants(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var saasTenants = await db.SaasTenants.AsNoTracking().OrderBy(t => t.TenantId).ToListAsync(ct);
        var subs = await db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        var subByTenant = subs.GroupBy(s => s.TenantId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First());
        var warehousesByTenant = await db.Warehouses.IgnoreQueryFilters().AsNoTracking()
            .Where(w => w.IsActive)
            .GroupBy(w => w.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);
        var usersByTenant = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.IsActive && (u.SecurityDomain == null || u.SecurityDomain != "PLATFORM"))
            .GroupBy(u => u.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count, ct);

        var latestStatusAuditByTenant = (await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Action == "PLATFORM_TENANT_STATUS_CHANGED")
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Take(200).ToListAsync(ct))
            .GroupBy(a => a.EntityId is > 0 ? a.EntityId.Value : a.TenantId)
            .ToDictionary(g => g.Key, g => g.First());

        var latestQuotaAuditByTenant = (await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Action == "PLATFORM_TENANT_QUOTA_UPDATED")
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Take(200).ToListAsync(ct))
            .GroupBy(a => a.EntityId is > 0 ? a.EntityId.Value : a.TenantId)
            .ToDictionary(g => g.Key, g => g.First());

        var allTenantIds = saasTenants.Select(t => t.TenantId).Union(subByTenant.Keys).Union([1]).Distinct().OrderBy(id => id).ToList();

        var items = allTenantIds.Select(tid =>
        {
            var tenant = saasTenants.FirstOrDefault(t => t.TenantId == tid);
            subByTenant.TryGetValue(tid, out var sub);
            var spec = SaasBillingService.ResolvePlanSpec(sub?.PlanCode);
            var rawCode = tenant?.TenantCode ?? $"TENANT-{tid:D3}";
            var displayCode = string.Equals(rawCode, "DEFAULT", StringComparison.OrdinalIgnoreCase) ? $"TENANT-{tid:D3}" : rawCode;
            var maxWarehouses = sub?.MaxWarehouses is > 0 ? sub.MaxWarehouses : spec.MaxWarehouses;
            var maxUsers = sub?.MaxUsers is > 0 ? sub.MaxUsers : spec.MaxUsers;
            var monthlyOrderLimit = sub?.MonthlyOrderQuota is > 0 ? sub.MonthlyOrderQuota : spec.MonthlyOrderQuota;
            var usedWarehouses = warehousesByTenant.TryGetValue(tid, out var whCnt) && whCnt > 0 ? whCnt : (tid == 1 ? 1 : 0);
            var usedUsers = usersByTenant.GetValueOrDefault(tid, 0);
            var currentMonthOrders = sub?.CurrentMonthOrderCount ?? 0;

            var warehousePct = maxWarehouses > 0 ? Math.Round((usedWarehouses * 100m) / maxWarehouses, 1) : 0m;
            var userPct = maxUsers > 0 ? Math.Round((usedUsers * 100m) / maxUsers, 1) : 0m;
            var orderPct = monthlyOrderLimit > 0 ? Math.Round((currentMonthOrders * 100m) / monthlyOrderLimit, 1) : 0m;
            var effectiveStatus = tenant?.LifecycleStatus ?? sub?.Status ?? "ACTIVE";
            var isSuspended = string.Equals(effectiveStatus, "SUSPENDED", StringComparison.OrdinalIgnoreCase);

            latestStatusAuditByTenant.TryGetValue(tid, out var statusAudit);
            var parsedStatusMeta = ExtractSuspensionAuditMetadata(statusAudit);
            var suspendedAtUtc = isSuspended
                ? (parsedStatusMeta.SuspendedAtUtc ?? statusAudit?.Timestamp ?? tenant?.UpdatedAtUtc ?? now)
                : parsedStatusMeta.SuspendedAtUtc;
            var normalizedCompanyName = SaasBillingService.NormalizeTenantLegalName(tid, tenant?.CompanyName, tenant?.TenantName);

            latestQuotaAuditByTenant.TryGetValue(tid, out var quotaAudit);
            var parsedQuotaMeta = ExtractQuotaAuditMetadata(quotaAudit);
            var contractAddendumCode = parsedQuotaMeta.ContractReference ?? (tid == 1 ? "PLHD-2026-001" : string.Empty);
            var effectiveUntilUtc = parsedQuotaMeta.EffectiveUntilDate ?? string.Empty;

            return new
            {
                tenantId = tid,
                tenantCode = displayCode,
                rawTenantCode = rawCode,
                companyName = normalizedCompanyName,
                companyLegalName = normalizedCompanyName,
                taxCode = SaasBillingService.NormalizeTenantTaxCode(tid, tenant?.TaxCode, tenant?.CompanyName, tenant?.TenantName),
                taxpayerType = tenant?.TaxpayerType ?? "ENTERPRISE",
                ownerEmail = tenant?.OwnerEmail ?? $"owner.t{tid}@erpkho.local",
                status = effectiveStatus,
                lifecycleStatus = effectiveStatus,
                suspensionReason = tenant?.SuspensionReason,
                reasonCategory = isSuspended ? (parsedStatusMeta.ReasonCategory ?? "OTHER") : parsedStatusMeta.ReasonCategory,
                suspendedBy = isSuspended ? (parsedStatusMeta.SuspendedBy ?? "platform_operator") : parsedStatusMeta.SuspendedBy,
                suspendedAtUtc,
                suspendedAtVi = isSuspended ? FormatSuspendedAtVi(suspendedAtUtc, now) : null,
                allowSupportAccess = tenant?.AllowSupportAccess ?? true,
                planCode = sub?.PlanCode ?? spec.PlanCode,
                planName = spec.PlanName,
                usedWarehouses,
                maxWarehouses,
                usedUsers,
                maxUsers,
                currentMonthOrders,
                currentMonthOrderCount = currentMonthOrders,
                monthlyOrderLimit,
                monthlyOrderQuota = monthlyOrderLimit,
                warehouseUtilizationPct = warehousePct,
                userUtilizationPct = userPct,
                orderUtilizationPct = orderPct,
                maxResourceUtilizationPct = Math.Max(warehousePct, Math.Max(userPct, orderPct)),
                contractAddendumCode,
                contractReference = contractAddendumCode,
                effectiveUntilDate = effectiveUntilUtc,
                effectiveUntilUtc,
                updatedAtUtc = tenant?.UpdatedAtUtc ?? sub?.UpdatedAtUtc ?? now
            };
        }).ToArray();

        return Ok(new { totalCount = items.Length, items, quotas = items });
    }

    [HttpGet("tenants/{tenantId:int}/status")]
    [PlatformAuthorize(AppPermissions.PlatformTenantRead)]
    public async Task<IActionResult> GetTenantLifecycleStatus(int tenantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var tenant = await db.SaasTenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        var sub = await db.TenantSubscriptions.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var effectiveStatus = tenant?.LifecycleStatus ?? sub?.Status ?? "ACTIVE";
        var isSuspended = string.Equals(effectiveStatus, "SUSPENDED", StringComparison.OrdinalIgnoreCase);
        var latestStatusAudit = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.Action == "PLATFORM_TENANT_STATUS_CHANGED" && (a.EntityId == tenantId || a.TenantId == tenantId))
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);
        var parsedMeta = ExtractSuspensionAuditMetadata(latestStatusAudit);
        var suspendedAtUtc = isSuspended ? (parsedMeta.SuspendedAtUtc ?? latestStatusAudit?.Timestamp ?? tenant?.UpdatedAtUtc ?? now) : parsedMeta.SuspendedAtUtc;
        var normalizedCompanyName = SaasBillingService.NormalizeTenantLegalName(tenantId, tenant?.CompanyName, tenant?.TenantName);

        return Ok(new
        {
            tenantId,
            tenantCode = tenant?.TenantCode is { Length: > 0 } tc && tc != "DEFAULT" ? tc : $"TENANT-{tenantId:D3}",
            companyName = normalizedCompanyName,
            companyLegalName = normalizedCompanyName,
            status = effectiveStatus,
            lifecycleStatus = effectiveStatus,
            suspensionReason = tenant?.SuspensionReason,
            reasonCategory = isSuspended ? (parsedMeta.ReasonCategory ?? "OTHER") : parsedMeta.ReasonCategory,
            suspendedBy = isSuspended ? (parsedMeta.SuspendedBy ?? "platform_operator") : parsedMeta.SuspendedBy,
            suspendedAtUtc,
            suspendedAtVi = isSuspended ? FormatSuspendedAtVi(suspendedAtUtc, now) : null,
            allowSupportAccess = tenant?.AllowSupportAccess ?? true,
            updatedAtUtc = tenant?.UpdatedAtUtc ?? sub?.UpdatedAtUtc ?? now
        });
    }

    [NonAction]
    public Task<IActionResult> UpdateTenantLifecycle(int tenantId, UpdateTenantLifecycleRequest request, CancellationToken ct = default) =>
        UpdateTenantStatus(tenantId, request, ct);

    [HttpPost("tenants/{tenantId:int}/status")]
    [HttpPut("tenants/{tenantId:int}/status")]
    [PlatformAuthorize(AppPermissions.PlatformTenantSuspend)]
    public async Task<IActionResult> UpdateTenantStatus(int tenantId, [FromBody] UpdateTenantLifecycleRequest request, CancellationToken ct = default)
    {
        if (EnsureCallerHasPlatformPermission(AppPermissions.PlatformTenantSuspend, "platform.tenant.write") is { } forbidResult)
            return forbidResult;

        if (tenantId <= 0)
            return BadRequest(new { code = "INVALID_TENANT_ID", errorCode = "INVALID_TENANT_ID", message = "TenantId không hợp lệ." });

        var rawStatus = !string.IsNullOrWhiteSpace(request?.LifecycleStatus)
            ? request.LifecycleStatus.Trim().ToUpperInvariant()
            : (string.IsNullOrWhiteSpace(request?.Status) ? "ACTIVE" : request.Status.Trim().ToUpperInvariant());

        if (rawStatus is not ("ACTIVE" or "TRIAL" or "GRACE" or "SUSPENDED" or "READ_ONLY" or "ARCHIVED"))
        {
            return UnprocessableEntity(new
            {
                code = "INVALID_TENANT_LIFECYCLE_STATUS",
                errorCode = "INVALID_TENANT_LIFECYCLE_STATUS",
                message = $"Trạng thái vòng đời '{rawStatus}' không hợp lệ (chỉ chấp nhận ACTIVE, TRIAL, GRACE, SUSPENDED, READ_ONLY, ARCHIVED)."
            });
        }

        var effectiveReason = !string.IsNullOrWhiteSpace(request?.SuspensionReason) ? request.SuspensionReason.Trim() : request?.Reason?.Trim();
        if (rawStatus == "SUSPENDED" && (string.IsNullOrWhiteSpace(effectiveReason) || effectiveReason.Length < 10))
        {
            return UnprocessableEntity(new
            {
                code = "PLATFORM_SUSPENSION_REASON_REQUIRED",
                errorCode = "PLATFORM_SUSPENSION_REASON_REQUIRED",
                message = "Khi tạm khóa Doanh nghiệp (SUSPENDED), lý do tạm khóa (SuspensionReason) bắt buộc phải có tối thiểu 10 ký tự để phục vụ hậu kiểm."
            });
        }

        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;

        var now = DateTime.UtcNow;
        var tenant = await db.SaasTenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
        var previousStatus = tenant?.LifecycleStatus ?? "ACTIVE";
        if (tenant is null)
        {
            tenant = new SaasTenant
            {
                TenantId = tenantId,
                TenantCode = $"TENANT-{tenantId:D3}",
                CompanyName = SaasBillingService.NormalizeTenantLegalName(tenantId, null),
                TenantName = SaasBillingService.NormalizeTenantLegalName(tenantId, null),
                TaxCode = SaasBillingService.NormalizeTenantTaxCode(tenantId, null),
                TaxpayerType = "ENTERPRISE",
                LifecycleStatus = rawStatus,
                SuspensionReason = effectiveReason,
                AllowSupportAccess = request?.AllowSupportAccess ?? true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.SaasTenants.Add(tenant);
        }
        else
        {
            tenant.CompanyName = SaasBillingService.NormalizeTenantLegalName(tenantId, tenant.CompanyName, tenant.TenantName);
            tenant.TaxCode = SaasBillingService.NormalizeTenantTaxCode(tenantId, tenant.TaxCode, tenant.CompanyName, tenant.TenantName);
            tenant.LifecycleStatus = rawStatus;
            tenant.SuspensionReason = effectiveReason;
            if (request?.AllowSupportAccess.HasValue == true)
                tenant.AllowSupportAccess = request.AllowSupportAccess.Value;
            tenant.UpdatedAtUtc = now;
        }

        var sub = await db.TenantSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (sub is not null)
        {
            sub.Status = rawStatus;
            sub.UpdatedAtUtc = now;
        }

        var reasonCategory = !string.IsNullOrWhiteSpace(request?.ReasonCategory) ? request.ReasonCategory.Trim().ToUpperInvariant() : "OTHER";
        var actorUsername = ResolveActorUsername();
        var suspendedAtVi = rawStatus == "SUSPENDED" ? "Vừa xong" : null;
        var normalizedCompanyName = SaasBillingService.NormalizeTenantLegalName(tenantId, tenant.CompanyName, tenant.TenantName);
        var (revokedCustomerSessionsCount, revokedImpersonationSessionsCount) = rawStatus == "SUSPENDED"
            ? await RevokeTenantSessionsOnSuspensionAsync(tenantId, actorUsername, now, ct)
            : (0, 0);

        db.AuditLogs.Add(new AuditLog
        {
            UserId = GetActorUserId(),
            TenantId = tenantId,
            SecurityDomain = "PLATFORM",
            Action = "PLATFORM_TENANT_STATUS_CHANGED",
            EntityName = "SaasTenant",
            EntityId = tenantId,
            OldValues = JsonSerializer.Serialize(new { tenantId, tenantCode = tenant.TenantCode, companyName = normalizedCompanyName, status = previousStatus }),
            NewValues = JsonSerializer.Serialize(new { tenantId, tenantCode = tenant.TenantCode, companyName = normalizedCompanyName, status = rawStatus, reason = effectiveReason, reasonCategory, actorUsername, suspendedBy = actorUsername, suspendedAtUtc = now, suspendedAtVi, revokedCustomerSessionsCount, revokedImpersonationSessionsCount }),
            Reason = effectiveReason ?? $"Cập nhật trạng thái thuê bao #{tenantId} sang {rawStatus}",
            Result = "Success",
            CorrelationId = ResolveCorrelationId(),
            Timestamp = now
        });

        await db.SaveChangesAsync(ct);
        if (tx is not null)
        {
            await tx.CommitAsync(ct);
        }

        return Ok(new
        {
            tenantId,
            tenantCode = tenant.TenantCode,
            companyName = normalizedCompanyName,
            companyLegalName = normalizedCompanyName,
            previousStatus,
            status = tenant.LifecycleStatus,
            lifecycleStatus = tenant.LifecycleStatus,
            suspensionReason = tenant.SuspensionReason,
            reasonCategory,
            suspendedBy = actorUsername,
            suspendedAtUtc = now,
            suspendedAtVi,
            allowSupportAccess = tenant.AllowSupportAccess,
            revokedCustomerSessionsCount,
            revokedImpersonationSessionsCount,
            auditAction = "PLATFORM_TENANT_STATUS_CHANGED",
            updatedAtUtc = now
        });
    }
}
