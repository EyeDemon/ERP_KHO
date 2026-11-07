using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    [HttpGet("audit-logs")]
    [PlatformAuthorize(AppPermissions.PlatformAuditRead)]
    public async Task<IActionResult> GetPlatformAuditLogs(CancellationToken ct = default)
    {
        if (EnsureCallerHasPlatformPermission(AppPermissions.PlatformAuditRead) is { } forbidResult)
        {
            return forbidResult;
        }

        var baseQuery = db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.EntityName != "RefreshToken"
                     && !a.Action.StartsWith("Session.")
                     && !a.Action.StartsWith("Token.")
                     && a.Action != "Auth.TokenRefreshed"
                     && a.Action != "Authentication.RefreshRotated"
                     && a.Action != "Logout"
                     && a.Action != "Refresh"
                     && a.Action != "TokenRefresh");

        var recentCandidates = await baseQuery
            .OrderByDescending(a => a.Id)
            .Take(250)
            .ToListAsync(ct);

        var adminCandidates = await baseQuery
            .Where(a => a.SecurityDomain == "PLATFORM"
                     || a.Action.StartsWith("PLATFORM_")
                     || a.Action.StartsWith("SaaS.")
                     || a.Action.StartsWith("Permission")
                     || a.Action.StartsWith("Role.")
                     || a.Action.StartsWith("User.")
                     || a.Action.StartsWith("AccountSecurity.")
                     || a.Action == "Authentication.AccountLocked"
                     || a.Action == "Authentication.RefreshTokenReuseDetected"
                     || a.Action == "Authentication.AdminRevokedUserSessions"
                     || a.Action == "DAILY_INVENTORY_INTEGRITY_CHECK"
                     || a.Action == "Inventory.VerifyBucketInvariants"
                     || a.Action == "Inventory.VerifyLockPrecedence"
                     || a.EntityName == "SaasTenant"
                     || a.EntityName == "SaasBillingInvoice"
                     || a.EntityName == "TenantSubscription"
                     || a.EntityName == "SupportImpersonationSession"
                     || a.EntityName == "IntegrationOutboxBatch"
                     || a.EntityName == "Role")
            .OrderByDescending(a => a.Id)
            .Take(150)
            .ToListAsync(ct);

        var orderedRaw = adminCandidates
            .Concat(recentCandidates)
            .DistinctBy(a => a.Id)
            .OrderByDescending(a => a.Id)
            .ToList();

        var candidateLogs = NormalizeAndCollapseAuditCandidates(orderedRaw)
            .OrderBy(c => GetPlatformAuditPriorityTier(c.EffectiveSecurityDomain, c.NormalizedAction, c.NormalizedEntityName))
            .ThenByDescending(c => c.Source.Id)
            .Take(250)
            .ToList();

        var actorIds = candidateLogs
            .Where(c => c.Source.UserId is > 0)
            .Select(c => c.Source.UserId!.Value)
            .Distinct()
            .ToList();
        var userRows = actorIds.Count > 0
            ? await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.FullName, u.RoleId })
                .ToListAsync(ct)
            : [];
        var roleNames = userRows.Count > 0
            ? await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => (string?)r.RoleName, ct)
            : [];
        var usersById = userRows.ToDictionary(
            u => u.Id,
            u => (u.Username, u.FullName, u.RoleId, RoleName: roleNames.GetValueOrDefault(u.RoleId)));

        var warehouseIds = candidateLogs
            .Select(c => c.Source.WarehouseId ?? (c.NormalizedEntityName is "Warehouse" or "Warehouses" or "WarehouseCalendarException" ? c.NormalizedEntityId : null))
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var warehouseCodesById = warehouseIds.Count > 0
            ? await db.Warehouses.IgnoreQueryFilters().AsNoTracking()
                .Where(w => warehouseIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Code, ct)
            : [];

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var logs = new List<object>(50);

        foreach (var cand in candidateLogs)
        {
            var a = cand.Source;
            var action = cand.NormalizedAction;
            var entityName = cand.NormalizedEntityName;
            var securityDomain = cand.EffectiveSecurityDomain;
            var isPlatformDomain = string.Equals(securityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase);
            var securityDomainLabelVi = isPlatformDomain ? "Quản trị nền tảng" : "Doanh nghiệp thuê bao";

            var (jsonActor, jsonTarget, jsonStatus, jsonSummary) = ParseDashboardAuditJson(cand.SanitizedNewValues);

            string? resolvedUsername = null;
            string? resolvedFullName = null;
            string? resolvedRoleName = null;
            int? resolvedRoleId = null;
            if (a.UserId is > 0 && usersById.TryGetValue(a.UserId.Value, out var uInfo))
            {
                resolvedUsername = !string.IsNullOrWhiteSpace(uInfo.Username) ? uInfo.Username.Trim() : jsonActor;
                resolvedFullName = uInfo.FullName;
                resolvedRoleId = uInfo.RoleId;
                resolvedRoleName = uInfo.RoleName;
            }
            else if (!string.IsNullOrWhiteSpace(jsonActor))
            {
                resolvedUsername = jsonActor.Trim();
            }
            else if (action.Contains("VietQr", StringComparison.OrdinalIgnoreCase) || action == "PLATFORM_SAAS_INVOICE_RECONCILED")
            {
                resolvedUsername = "system_webhook";
            }
            else if (isPlatformDomain)
            {
                resolvedUsername = "platform_operator";
            }

            var actorDisplayVi = FormatPlatformAdminActorDisplay(resolvedUsername, resolvedFullName, resolvedRoleName, resolvedRoleId);
            var username = !string.IsNullOrWhiteSpace(resolvedUsername)
                ? resolvedUsername
                : (isPlatformDomain ? "platform_operator" : "system_operator");

            var (_, actionLabelVi, _) = MapPlatformEventPresentation(
                action, entityName, jsonStatus, a.Result, cand.SanitizedOldValues, cand.SanitizedNewValues);
            var whLookupId = a.WarehouseId ?? (entityName is "Warehouse" or "Warehouses" or "WarehouseCalendarException" ? cand.NormalizedEntityId : null);
            var whCode = whLookupId is > 0 ? warehouseCodesById.GetValueOrDefault(whLookupId.Value) : null;
            var targetCodeVi = ResolveAuditTargetCodeVi(
                jsonTarget, entityName, cand.NormalizedEntityId, a.TenantId, a.Id, whCode,
                cand.SanitizedNewValues, cand.NormalizedReason, cand.SanitizedOldValues);
            var stateChangeVi = BuildAuditStateChangeVi(action, targetCodeVi, cand.SanitizedOldValues, cand.SanitizedNewValues, actionLabelVi);
            var sanitizedReason = cand.NormalizedAction == "PLATFORM_ADMIN_AUTH_VERIFIED" && cand.CollapsedCount > 1
                ? $"Xác thực đăng nhập quản trị nền tảng thành công (đã gộp {cand.CollapsedCount} phiên liên tiếp)"
                : SanitizePlatformAuditReasonVi(cand.NormalizedReason);
            var summaryVi = cand.NormalizedAction == "PLATFORM_ADMIN_AUTH_VERIFIED"
                ? (cand.CollapsedCount > 1
                    ? $"Xác thực quản trị thành công — {actorDisplayVi} (đã gộp {cand.CollapsedCount} phiên đăng nhập liên tiếp)"
                    : $"Xác thực quản trị thành công — {actorDisplayVi}")
                : NormalizeDashboardSummaryVi(!string.IsNullOrWhiteSpace(jsonSummary) ? jsonSummary.Trim() : cand.NormalizedReason?.Trim(), actionLabelVi, targetCodeVi);
            var tsUtc = a.Timestamp.Kind == DateTimeKind.Utc ? a.Timestamp : DateTime.SpecifyKind(a.Timestamp, DateTimeKind.Utc);

            var dedupKey = $"{action}|{entityName}|{targetCodeVi}|{stateChangeVi}|{summaryVi}";
            if (!seenKeys.Add(dedupKey))
            {
                continue;
            }

            logs.Add(new
            {
                a.Id,
                a.TenantId,
                a.UserId,
                username,
                actorDisplayVi,
                a.ImpersonatedBySupportUserId,
                a.ImpersonatedByUserId,
                securityDomain,
                securityDomainLabelVi,
                Action = action,
                actionLabelVi,
                EntityName = entityName,
                EntityId = cand.NormalizedEntityId,
                targetCodeVi,
                OldValues = cand.SanitizedOldValues,
                NewValues = cand.SanitizedNewValues,
                stateChangeVi,
                Reason = sanitizedReason,
                summaryVi,
                a.Result,
                a.CorrelationId,
                collapsedCount = cand.CollapsedCount,
                Timestamp = tsUtc
            });

            if (logs.Count >= 50)
            {
                break;
            }
        }

        return Ok(new { totalCount = logs.Count, items = logs });
    }

    internal static IReadOnlyList<NormalizedPlatformAuditCandidate> NormalizeAndCollapseAuditCandidates(IReadOnlyList<AuditLog> orderedRows)
    {
        var result = new List<NormalizedPlatformAuditCandidate>(orderedRows.Count);
        var authIndexByActor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in orderedRows)
        {
            var act = (row.Action ?? string.Empty).Trim();
            var ent = (row.EntityName ?? string.Empty).Trim();
            var dom = (row.SecurityDomain ?? string.Empty).Trim();

            if (IsPlatformSessionPlumbingNoise(act, ent, dom))
                continue;

            if (IsPlatformAdminLoginAuditRow(act, dom))
            {
                var actorKey = row.UserId is > 0 ? $"U:{row.UserId.Value}" : "U:PLATFORM_ADMIN";
                if (authIndexByActor.TryGetValue(actorKey, out var existingIdx))
                {
                    var prev = result[existingIdx];
                    var newCount = prev.CollapsedCount + 1;
                    result[existingIdx] = prev with
                    {
                        CollapsedCount = newCount,
                        SanitizedNewValues = JsonSerializer.Serialize(new { accessPortal = "PLATFORM", mfaVerified = true, collapsedSessions = newCount })
                    };
                }
                else
                {
                    authIndexByActor[actorKey] = result.Count;
                    result.Add(new NormalizedPlatformAuditCandidate(
                        row, "PLATFORM_ADMIN_AUTH_VERIFIED", "AccountSecurity", null, "PLATFORM",
                        null, JsonSerializer.Serialize(new { accessPortal = "PLATFORM", mfaVerified = true, collapsedSessions = 1 }),
                        "Xác thực đăng nhập quản trị nền tảng thành công", 1));
                }
                continue;
            }

            if (string.Equals(act, "Authentication.AccountLocked", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new NormalizedPlatformAuditCandidate(
                    row, "ACCOUNT_SECURITY_LOCKED", "AccountSecurity", row.EntityId ?? row.UserId,
                    string.Equals(dom, "PLATFORM", StringComparison.OrdinalIgnoreCase) ? "PLATFORM" : "CUSTOMER_TENANT",
                    null, null, "Hệ thống tự động tạm khóa tài khoản sau nhiều lần xác thực thất bại", 1));
                continue;
            }

            if (string.Equals(act, "Authentication.RefreshTokenReuseDetected", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new NormalizedPlatformAuditCandidate(
                    row, "ACCOUNT_SECURITY_TOKEN_REUSE_BLOCKED", "AccountSecurity", null, "PLATFORM",
                    null, null, "Hệ thống phát hiện và chặn mã phiên đăng nhập có dấu hiệu tái sử dụng bất thường", 1));
                continue;
            }

            if (string.Equals(act, "Authentication.AdminRevokedUserSessions", StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new NormalizedPlatformAuditCandidate(
                    row, "ACCOUNT_SECURITY_SESSIONS_REVOKED", "AccountSecurity", row.EntityId,
                    string.Equals(dom, "PLATFORM", StringComparison.OrdinalIgnoreCase) ? "PLATFORM" : "CUSTOMER_TENANT",
                    null, null, "Quản trị viên thu hồi toàn bộ phiên đăng nhập của tài khoản", 1));
                continue;
            }

            var isPlatform = string.Equals(dom, "PLATFORM", StringComparison.OrdinalIgnoreCase)
                || act.StartsWith("PLATFORM_", StringComparison.OrdinalIgnoreCase);
            result.Add(new NormalizedPlatformAuditCandidate(
                row, act, ent, row.EntityId,
                isPlatform ? "PLATFORM" : (string.IsNullOrWhiteSpace(dom) ? "CUSTOMER_TENANT" : dom),
                row.OldValues, row.NewValues, row.Reason, 1));
        }

        return result;
    }
}
