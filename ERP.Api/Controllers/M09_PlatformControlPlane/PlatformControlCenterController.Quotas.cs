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
    [HttpGet("quotas")]
    [PlatformAuthorize(AppPermissions.PlatformTenantRead)]
    public Task<IActionResult> GetQuotas(CancellationToken ct = default) => GetTenants(ct);

    [NonAction]
    public Task<IActionResult> OverrideTenantQuotas(
        int tenantId,
        OverrideTenantQuotaRequest request,
        CancellationToken ct = default) => OverrideTenantQuota(tenantId, request, ct);

    [HttpPut("quotas/{tenantId:int}")]
    [HttpPut("tenants/{tenantId:int}/quotas")]
    [PlatformAuthorize(AppPermissions.PlatformQuotaOverride)]
    public async Task<IActionResult> OverrideTenantQuota(
        int tenantId,
        [FromBody] OverrideTenantQuotaRequest request,
        CancellationToken ct = default)
    {
        if (EnsureCallerHasPlatformPermission(AppPermissions.PlatformQuotaOverride, "platform.quota.write") is { } forbidResult)
        {
            return forbidResult;
        }

        if (tenantId <= 0)
        {
            return BadRequest(new { code = "INVALID_TENANT_ID", errorCode = "INVALID_TENANT_ID", message = "TenantId không hợp lệ." });
        }

        await using var tx = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct)
            : null;

        var now = DateTime.UtcNow;
        var sub = await db.TenantSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);
        var spec = SaasBillingService.ResolvePlanSpec(request?.PlanCode ?? sub?.PlanCode);
        var oldPlanCode = sub?.PlanCode ?? spec.PlanCode;
        var oldQuota = sub?.MonthlyOrderQuota ?? spec.MonthlyOrderQuota;
        var oldMaxWarehouses = sub?.MaxWarehouses ?? spec.MaxWarehouses;
        var oldMaxUsers = sub?.MaxUsers ?? spec.MaxUsers;

        var rawUsedWarehouses = await db.Warehouses.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(w => w.TenantId == tenantId && w.IsActive, ct);
        var usedWarehouses = rawUsedWarehouses > 0 ? rawUsedWarehouses : (tenantId == 1 ? 1 : 0);

        var usedUsers = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(u => u.TenantId == tenantId && u.IsActive && (u.SecurityDomain == null || u.SecurityDomain != "PLATFORM"), ct);

        var currentMonthOrders = sub?.CurrentMonthOrderCount ?? 0;
        var requestedQuota = request?.MonthlyOrderQuota ?? request?.MonthlyOrderLimit;

        if ((request?.MaxWarehouses.HasValue == true && (request.MaxWarehouses.Value <= 0 || request.MaxWarehouses.Value < usedWarehouses))
            || (request?.MaxUsers.HasValue == true && (request.MaxUsers.Value <= 0 || request.MaxUsers.Value < usedUsers))
            || (requestedQuota.HasValue && (requestedQuota.Value <= 0 || requestedQuota.Value < currentMonthOrders)))
        {
            return UnprocessableEntity(new
            {
                code = "QUOTA_BELOW_CURRENT_USAGE",
                errorCode = "QUOTA_BELOW_CURRENT_USAGE",
                message = "Hạn mức mới không được thấp hơn mức đang sử dụng thực tế của doanh nghiệp.",
                usedWarehouses,
                usedUsers,
                currentMonthOrders
            });
        }

        var targetQuota = requestedQuota ?? sub?.MonthlyOrderQuota ?? spec.MonthlyOrderQuota;
        var targetMaxWarehouses = request?.MaxWarehouses is > 0 ? request.MaxWarehouses.Value : (sub?.MaxWarehouses ?? spec.MaxWarehouses);
        var targetMaxUsers = request?.MaxUsers is > 0 ? request.MaxUsers.Value : (sub?.MaxUsers ?? spec.MaxUsers);
        var contractReference = !string.IsNullOrWhiteSpace(request?.ContractReference)
            ? request.ContractReference.Trim()
            : (!string.IsNullOrWhiteSpace(request?.ContractAddendumCode) ? request.ContractAddendumCode.Trim() : null);
        var effectiveUntilDate = !string.IsNullOrWhiteSpace(request?.EffectiveUntilDate)
            ? request.EffectiveUntilDate.Trim()
            : (!string.IsNullOrWhiteSpace(request?.EffectiveUntilUtc) ? request.EffectiveUntilUtc.Trim() : null);

        var exceedsStandardPlanCeiling = targetMaxWarehouses > spec.MaxWarehouses
            || targetMaxUsers > spec.MaxUsers
            || targetQuota > spec.MonthlyOrderQuota;
        if (exceedsStandardPlanCeiling)
        {
            if (string.IsNullOrWhiteSpace(contractReference))
            {
                return UnprocessableEntity(new
                {
                    code = "QUOTA_CONTRACT_ADDENDUM_REQUIRED",
                    errorCode = "QUOTA_CONTRACT_ADDENDUM_REQUIRED",
                    message = "Bắt buộc nhập Mã phụ lục hợp đồng B2B khi cấp hạn mức vượt trần gói cước tiêu chuẩn."
                });
            }

            if (string.IsNullOrWhiteSpace(effectiveUntilDate))
            {
                return UnprocessableEntity(new
                {
                    code = "QUOTA_EFFECTIVE_UNTIL_REQUIRED",
                    errorCode = "QUOTA_EFFECTIVE_UNTIL_REQUIRED",
                    message = "Bắt buộc chọn ngày Hiệu lực đến khi cấp hạn mức vượt trần gói cước tiêu chuẩn."
                });
            }
        }

        if (sub is null)
        {
            sub = new TenantSubscription
            {
                TenantId = tenantId,
                PlanCode = request?.PlanCode?.Trim().ToUpperInvariant() ?? spec.PlanCode,
                Status = "ACTIVE",
                MonthlyOrderQuota = requestedQuota ?? spec.MonthlyOrderQuota,
                CurrentMonthOrderCount = request?.CurrentMonthOrderCount ?? 0,
                MaxWarehouses = request?.MaxWarehouses is > 0 ? request.MaxWarehouses.Value : spec.MaxWarehouses,
                MaxUsers = request?.MaxUsers is > 0 ? request.MaxUsers.Value : spec.MaxUsers,
                CurrentPeriodStartUtc = now,
                CurrentPeriodEndUtc = now.AddDays(30),
                UpdatedAtUtc = now
            };
            db.TenantSubscriptions.Add(sub);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request?.PlanCode))
                sub.PlanCode = request.PlanCode.Trim().ToUpperInvariant();
            if (requestedQuota.HasValue)
                sub.MonthlyOrderQuota = requestedQuota.Value;
            if (request?.MaxWarehouses is > 0)
                sub.MaxWarehouses = request.MaxWarehouses.Value;
            if (request?.MaxUsers is > 0)
                sub.MaxUsers = request.MaxUsers.Value;
            if (request?.CurrentMonthOrderCount.HasValue == true)
                sub.CurrentMonthOrderCount = request.CurrentMonthOrderCount.Value;
            sub.UpdatedAtUtc = now;
        }

        var effectiveReason = !string.IsNullOrWhiteSpace(request?.Reason)
            ? request.Reason.Trim()
            : "Quản trị viên Nền tảng điều chỉnh hạn ngạch thuê bao";

        db.AuditLogs.Add(new AuditLog
        {
            UserId = GetActorUserId(),
            TenantId = tenantId,
            SecurityDomain = "PLATFORM",
            Action = "PLATFORM_TENANT_QUOTA_UPDATED",
            EntityName = "TenantSubscription",
            EntityId = tenantId,
            OldValues = JsonSerializer.Serialize(new
            {
                planCode = oldPlanCode,
                maxWarehouses = oldMaxWarehouses,
                maxUsers = oldMaxUsers,
                monthlyOrderQuota = oldQuota,
                monthlyOrderLimit = oldQuota
            }),
            NewValues = JsonSerializer.Serialize(new
            {
                planCode = sub.PlanCode,
                maxWarehouses = sub.MaxWarehouses,
                maxUsers = sub.MaxUsers,
                monthlyOrderQuota = sub.MonthlyOrderQuota,
                monthlyOrderLimit = sub.MonthlyOrderQuota,
                actorUsername = ResolveActorUsername(),
                reason = effectiveReason,
                contractReference,
                effectiveUntilDate
            }),
            Reason = effectiveReason,
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
            planCode = sub.PlanCode,
            usedWarehouses,
            maxWarehouses = sub.MaxWarehouses,
            usedUsers,
            maxUsers = sub.MaxUsers,
            currentMonthOrders = sub.CurrentMonthOrderCount,
            currentMonthOrderCount = sub.CurrentMonthOrderCount,
            monthlyOrderQuota = sub.MonthlyOrderQuota,
            monthlyOrderLimit = sub.MonthlyOrderQuota,
            contractReference,
            effectiveUntilDate,
            auditAction = "PLATFORM_TENANT_QUOTA_UPDATED",
            updatedAtUtc = now
        });
    }
}
