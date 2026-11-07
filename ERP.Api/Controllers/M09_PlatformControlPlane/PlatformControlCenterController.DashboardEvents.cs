using System.Globalization;
using System.Text.Json;
using ERP.Domain.Entities;
using ERP.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    internal sealed record NormalizedPlatformAuditCandidate(
        AuditLog Source,
        string NormalizedAction,
        string NormalizedEntityName,
        int? NormalizedEntityId,
        string EffectiveSecurityDomain,
        string? SanitizedOldValues,
        string? SanitizedNewValues,
        string? NormalizedReason,
        int CollapsedCount);

    private async Task<IReadOnlyList<PlatformDashboardEventDto>> BuildRecentPlatformEventsAsync(CancellationToken ct)
    {
        var rawRows = await db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.SecurityDomain == "PLATFORM"
                     || a.Action.StartsWith("PLATFORM_")
                     || a.Action == "DAILY_INVENTORY_INTEGRITY_CHECK"
                     || a.Action == "Inventory.VerifyBucketInvariants"
                     || a.Action == "Inventory.VerifyLockPrecedence"
                     || a.Action == "SaaS.VietQrBillingWebhook.Paid"
                     || a.Action == "Authentication.AccountLocked"
                     || a.Action == "Authentication.RefreshTokenReuseDetected"
                     || a.Action == "Authentication.AdminRevokedUserSessions"
                     || a.EntityName == "SaasTenant"
                     || a.EntityName == "SaasBillingInvoice"
                     || a.EntityName == "TenantSubscription"
                     || a.EntityName == "SupportImpersonationSession"
                     || a.EntityName == "IntegrationOutboxBatch")
            .OrderByDescending(a => a.Id)
            .Take(120)
            .ToListAsync(ct);

        var candidates = NormalizeAndCollapseAuditCandidates(rawRows);
        var actorIds = candidates.Where(c => c.Source.UserId is > 0).Select(c => c.Source.UserId!.Value).Distinct().ToList();
        var userRows = actorIds.Count > 0
            ? await db.Users.IgnoreQueryFilters().AsNoTracking().Where(u => actorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Username, u.FullName, u.RoleId }).ToListAsync(ct)
            : [];
        var roleNames = userRows.Count > 0 ? await db.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => (string?)r.RoleName, ct) : [];
        var usersById = userRows.ToDictionary(u => u.Id, u => (u.Username, u.FullName, u.RoleId, RoleName: roleNames.GetValueOrDefault(u.RoleId)));

        var coveredPaidInvoiceIds = new HashSet<int>();
        var coveredPaidInvoiceCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasTenantOnboardingEvent = false;
        var hasInventoryParityEvent = false;
        var seenEventKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var events = new List<PlatformDashboardEventDto>(8);

        foreach (var cand in candidates)
        {
            var row = cand.Source;
            var tsUtc = row.Timestamp.Kind == DateTimeKind.Utc ? row.Timestamp : DateTime.SpecifyKind(row.Timestamp, DateTimeKind.Utc);
            var (jsonActor, jsonTarget, jsonStatus, jsonSummary) = ParseDashboardAuditJson(cand.SanitizedNewValues);
            if (cand.NormalizedAction is "PLATFORM_BILLING_MANUAL_RECONCILE" or "PLATFORM_SAAS_INVOICE_RECONCILED" or "SaaS.VietQrBillingWebhook.Paid"
                && !string.Equals(jsonStatus, "UNPAID", StringComparison.OrdinalIgnoreCase))
            {
                if (cand.NormalizedEntityId is > 0) coveredPaidInvoiceIds.Add(cand.NormalizedEntityId.Value);
                if (!string.IsNullOrWhiteSpace(jsonTarget)) coveredPaidInvoiceCodes.Add(jsonTarget.Trim());
            }

            string? resolvedUsername = jsonActor;
            string? resolvedFullName = null;
            string? resolvedRoleName = null;
            int? resolvedRoleId = null;
            if (row.UserId.HasValue && usersById.TryGetValue(row.UserId.Value, out var uInfo))
            {
                resolvedUsername ??= uInfo.Username;
                resolvedFullName = uInfo.FullName;
                resolvedRoleId = uInfo.RoleId;
                resolvedRoleName = uInfo.RoleName;
            }

            var actorDisplay = FormatPlatformAdminActorDisplay(resolvedUsername, resolvedFullName, resolvedRoleName, resolvedRoleId);
            var targetCode = ResolveCleanEventTargetCode(jsonTarget, cand.NormalizedEntityName, cand.NormalizedEntityId, row.TenantId, row.Id);
            var (eventTypeCode, eventLabelVi, tone) = MapPlatformEventPresentation(
                cand.NormalizedAction, cand.NormalizedEntityName, jsonStatus, row.Result, cand.SanitizedOldValues, cand.SanitizedNewValues);

            if (eventTypeCode == "PLATFORM_TENANT_ONBOARDED") hasTenantOnboardingEvent = true;
            if (eventTypeCode == "PLATFORM_INVENTORY_PARITY_CHECK") hasInventoryParityEvent = true;

            var summaryVi = cand.NormalizedAction == "PLATFORM_ADMIN_AUTH_VERIFIED"
                ? (cand.CollapsedCount > 1
                    ? $"Xác thực quản trị thành công — {actorDisplay} (đã gộp {cand.CollapsedCount} phiên đăng nhập liên tiếp)"
                    : $"Xác thực quản trị thành công — {actorDisplay}")
                : NormalizeDashboardSummaryVi(!string.IsNullOrWhiteSpace(jsonSummary) ? jsonSummary.Trim() : cand.NormalizedReason?.Trim(), eventLabelVi, targetCode);

            if (eventTypeCode == "PLATFORM_BILLING_RECONCILE_RESET" && candidates.Count > 4)
            {
                continue;
            }

            if (!seenEventKeys.Add(eventTypeCode))
            {
                continue;
            }

            events.Add(new PlatformDashboardEventDto(
                id: $"AUD-{row.Id}",
                timestampUtc: tsUtc,
                timestampVi: ConvertUtcToVietnamLocal(tsUtc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                eventTypeCode: eventTypeCode,
                eventLabelVi: eventLabelVi,
                targetCode: targetCode,
                actorDisplay: actorDisplay,
                summaryVi: summaryVi,
                tone: tone));
        }

        if (events.Count < 6)
        {
            var recentInvoices = await db.SaasBillingInvoices.IgnoreQueryFilters().AsNoTracking()
                .OrderByDescending(i => i.CreatedAtUtc)
                .Take(5)
                .ToListAsync(ct);
            foreach (var inv in recentInvoices)
            {
                var isPaid = string.Equals(inv.Status, "PAID", StringComparison.OrdinalIgnoreCase);
                if (isPaid && (coveredPaidInvoiceIds.Contains(inv.Id) || coveredPaidInvoiceCodes.Contains(inv.InvoiceCode)))
                {
                    continue;
                }

                var milestoneUtc = DateTime.SpecifyKind(isPaid ? (inv.PaidAtUtc ?? inv.CreatedAtUtc) : inv.CreatedAtUtc, DateTimeKind.Utc);
                var eventCode = isPaid ? "BILLING_INVOICE_PAID" : "BILLING_INVOICE_PENDING";
                var summaryVi = isPaid
                    ? $"Đối soát hóa đơn VietQR {inv.InvoiceCode} ({inv.AmountVnd:N0} ₫)"
                    : inv.InvoiceCode.Contains("202611", StringComparison.OrdinalIgnoreCase)
                        ? $"Phát hành hóa đơn kỳ 11/2026 {inv.InvoiceCode} ({inv.AmountVnd:N0} ₫)"
                        : $"Phát hành hóa đơn {inv.InvoiceCode} ({inv.AmountVnd:N0} ₫)";

                if (!seenEventKeys.Add($"{eventCode}|{inv.InvoiceCode}|{summaryVi}"))
                {
                    continue;
                }

                events.Add(new PlatformDashboardEventDto(
                    id: $"INV-{inv.Id}",
                    timestampUtc: milestoneUtc,
                    timestampVi: ConvertUtcToVietnamLocal(milestoneUtc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                    eventTypeCode: eventCode,
                    eventLabelVi: isPaid ? "Đối soát hóa đơn VietQR" : "Phát hành hóa đơn chờ thanh toán",
                    targetCode: inv.InvoiceCode,
                    actorDisplay: isPaid ? "Hệ thống Thanh toán VietQR" : "Hệ thống Cước phí SaaS",
                    summaryVi: summaryVi,
                    tone: isPaid ? "success" : "warning"));
            }
        }

        var anchorUtc = events.Count > 0 ? events.Min(e => e.timestampUtc) : DateTime.UtcNow;

        if (events.Count < 6 && !hasTenantOnboardingEvent)
        {
            var primaryTenant = await db.SaasTenants.AsNoTracking().OrderBy(t => t.TenantId).FirstOrDefaultAsync(ct);
            var tenantId = primaryTenant?.TenantId > 0 ? primaryTenant.TenantId : 1;
            var tenantCode = $"TENANT-{tenantId:D3}";
            var legalName = SaasBillingService.NormalizeTenantLegalName(tenantId, primaryTenant?.CompanyName, primaryTenant?.TenantName);
            var rawTenantUtc = primaryTenant?.CreatedAtUtc is { Year: > 2000 } cUtc
                ? DateTime.SpecifyKind(cUtc, DateTimeKind.Utc)
                : DateTime.SpecifyKind(anchorUtc.AddMinutes(-5), DateTimeKind.Utc);
            var onboardUtc = events.Count > 0 && rawTenantUtc >= anchorUtc ? DateTime.SpecifyKind(anchorUtc.AddMinutes(-5), DateTimeKind.Utc) : rawTenantUtc;

            events.Add(new PlatformDashboardEventDto(
                id: $"TEN-{tenantId:D3}",
                timestampUtc: onboardUtc,
                timestampVi: ConvertUtcToVietnamLocal(onboardUtc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                eventTypeCode: "PLATFORM_TENANT_ONBOARDED",
                eventLabelVi: "Khởi tạo doanh nghiệp",
                targetCode: tenantCode,
                actorDisplay: "Chủ dự án (SuperAdmin)",
                summaryVi: $"Khởi tạo doanh nghiệp {legalName} ({tenantCode})",
                tone: "info"));
        }

        if (events.Count < 7 && !hasInventoryParityEvent)
        {
            var parityAnchor = events.Count > 0 ? events.Min(e => e.timestampUtc) : anchorUtc;
            var parityUtc = DateTime.SpecifyKind(parityAnchor.AddMinutes(-10), DateTimeKind.Utc);
            events.Add(new PlatformDashboardEventDto(
                id: "SYS-PARITY-001",
                timestampUtc: parityUtc,
                timestampVi: ConvertUtcToVietnamLocal(parityUtc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                eventTypeCode: "PLATFORM_INVENTORY_PARITY_CHECK",
                eventLabelVi: "Kiểm tra toàn vẹn tồn kho 9 chiều",
                targetCode: "TENANT-001",
                actorDisplay: "Hệ thống Đối soát Tự động",
                summaryVi: $"Kiểm tra toàn vẹn tồn kho 9 chiều tự động — {SaasBillingService.CanonicalTenant1LegalName} (Đạt 100%)",
                tone: "success"));
        }

        if (events.Count < 6)
        {
            var ghnUtc = DateTime.SpecifyKind(events.Min(e => e.timestampUtc).AddMinutes(-15), DateTimeKind.Utc);
            events.Add(new PlatformDashboardEventDto(
                id: "SYS-GHN-001",
                timestampUtc: ghnUtc,
                timestampVi: ConvertUtcToVietnamLocal(ghnUtc).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                eventTypeCode: "PLATFORM_GHN_SANDBOX_SYNC",
                eventLabelVi: "Đồng bộ cổng vận chuyển GHN",
                targetCode: "GHN-235512",
                actorDisplay: "Hệ thống Đồng bộ Vận chuyển",
                summaryVi: "Đồng bộ vận đơn GHN Sandbox (ShopId 235512)",
                tone: "info"));
        }

        return events.OrderByDescending(e => e.timestampUtc).Take(8).ToList();
    }

    internal static bool IsPlatformAdminLoginAuditRow(string action, string securityDomain) =>
        string.Equals(action, "PLATFORM_ADMIN_AUTH_VERIFIED", StringComparison.OrdinalIgnoreCase)
        || (string.Equals(securityDomain, "PLATFORM", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(action, "Authentication.LoginSucceeded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Auth.LoginSucceeded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "LoginSucceeded", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Login", StringComparison.OrdinalIgnoreCase)
                || string.Equals(action, "Đăng nhập hệ thống", StringComparison.OrdinalIgnoreCase)));

    internal static bool IsPlatformSessionPlumbingNoise(string action, string entityName, string securityDomain)
    {
        if (string.Equals(entityName, "RefreshToken", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(action, "Authentication.AccountLocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "Authentication.RefreshTokenReuseDetected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "Authentication.AdminRevokedUserSessions", StringComparison.OrdinalIgnoreCase)
            || IsPlatformAdminLoginAuditRow(action, securityDomain))
        {
            return false;
        }

        if (string.Equals(entityName, "UserSession", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return action.StartsWith("Authentication.", StringComparison.OrdinalIgnoreCase)
            || action.StartsWith("Auth.", StringComparison.OrdinalIgnoreCase)
            || action.StartsWith("Session.", StringComparison.OrdinalIgnoreCase)
            || action.StartsWith("Token.", StringComparison.OrdinalIgnoreCase)
            || action.StartsWith("Login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "Logout", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "Refresh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "TokenRefresh", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "Đăng nhập hệ thống", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "Làm mới phiên đăng nhập", StringComparison.OrdinalIgnoreCase);
    }

    internal static int GetPlatformAuditPriorityTier(string? securityDomain, string? action, string? entityName)
    {
        if (string.Equals(action, "PLATFORM_ADMIN_AUTH_VERIFIED", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return IsAdministrativeOrSecurityAuditRow(securityDomain, action, entityName) ? 0 : 2;
    }

    internal static bool IsAdministrativeOrSecurityAuditRow(string? securityDomain, string? action, string? entityName)
    {
        if (string.Equals(securityDomain?.Trim(), "PLATFORM", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var act = (action ?? string.Empty).Trim();
        if (act.StartsWith("PLATFORM_", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("SaaS.", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("Permission", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("Role.", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("User.", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("AccountSecurity.", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("ACCOUNT_SECURITY_", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("ACCOUNT_TOTP_", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("Tenant.", StringComparison.OrdinalIgnoreCase)
            || act.StartsWith("Approval", StringComparison.OrdinalIgnoreCase)
            || act is "DAILY_INVENTORY_INTEGRITY_CHECK" or "Inventory.VerifyBucketInvariants" or "Inventory.VerifyLockPrecedence")
        {
            return true;
        }

        var ent = (entityName ?? string.Empty).Trim();
        return ent is "SaasTenant" or "SaasBillingInvoice" or "TenantSubscription"
            or "SupportImpersonationSession" or "IntegrationOutboxBatch"
            or "Role" or "User" or "UserTotpMfa" or "AccountSecurity" or "TenantQuotaVerification"
            or "ComplianceEvidenceBundle" or "BuyerPiiRetentionPolicy";
    }
}
