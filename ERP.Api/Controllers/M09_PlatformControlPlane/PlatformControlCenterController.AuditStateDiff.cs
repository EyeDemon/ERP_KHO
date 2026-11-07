using System.Globalization;
using System.Text.Json;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    internal static string BuildAuditStateChangeVi(
        string action,
        string targetCodeVi,
        string? oldValues,
        string? newValues,
        string actionLabelVi)
    {
        var oldDoc = TryParseAuditJsonElement(oldValues);
        var newDoc = TryParseAuditJsonElement(newValues);

        switch (action)
        {
            case "PLATFORM_BILLING_MANUAL_RECONCILE":
            {
                var statusRaw = GetAuditJsonString(newDoc, "status", "TargetStatus");
                var afterVi = string.Equals(statusRaw, "PARTIALLY_PAID", StringComparison.OrdinalIgnoreCase)
                    ? "Thanh toán một phần"
                    : "Đã thanh toán";
                return $"Trạng thái: Chờ thanh toán → {afterVi} ({targetCodeVi})";
            }
            case "PLATFORM_BILLING_RECONCILE_RESET":
                return $"Trạng thái: Đã thanh toán → Chờ thanh toán ({targetCodeVi})";

            case "PLATFORM_SAAS_INVOICE_RECONCILED":
            {
                var statusRaw = GetAuditJsonString(newDoc, "status", "TargetStatus");
                return string.Equals(statusRaw, "UNPAID", StringComparison.OrdinalIgnoreCase)
                    ? $"Trạng thái: Đã thanh toán → Chờ thanh toán ({targetCodeVi})"
                    : $"Trạng thái: Chờ thanh toán → Đã thanh toán VietQR ({targetCodeVi})";
            }

            case "PLATFORM_TENANT_CREATED":
            case "PLATFORM_TENANT_ONBOARDED":
            case "SaaS.TenantCreated":
            case "Tenant.Created":
            case "Tenant.Onboarded":
            {
                var planVi = TranslatePlanCodeVi(GetAuditJsonString(newDoc, "planCode", "Plan"));
                return $"Khởi tạo thuê bao mới • {planVi} ({targetCodeVi})";
            }

            case "PLATFORM_ADMIN_AUTH_VERIFIED":
            {
                var collapsed = GetAuditJsonInt(newDoc, "collapsedSessions", "collapsedLoginCount") ?? 1;
                return collapsed > 1
                    ? $"Phiên quản trị: Đã xác thực hợp lệ (gộp {collapsed} lần đăng nhập liên tiếp)"
                    : "Phiên quản trị: Đã xác thực hợp lệ";
            }

            case "ACCOUNT_SECURITY_LOCKED":
                return $"Trạng thái bảo mật: Tạm khóa tự động do nhập sai mật khẩu ({targetCodeVi})";

            case "ACCOUNT_SECURITY_TOKEN_REUSE_BLOCKED":
                return "Bảo mật phiên: Đã thu hồi chuỗi phiên nghi vấn tái sử dụng";

            case "ACCOUNT_SECURITY_SESSIONS_REVOKED":
            {
                var revoked = GetAuditJsonInt(newDoc, "revokedCount") ?? 1;
                return $"Đã thu hồi {revoked} phiên đăng nhập đang hoạt động ({targetCodeVi})";
            }

            case "DAILY_INVENTORY_INTEGRITY_CHECK":
            case "Inventory.VerifyBucketInvariants":
            case "Inventory.VerifyLockPrecedence":
            {
                var kv = ParseAuditKeyValues(newValues);
                if (int.TryParse(kv.GetValueOrDefault("CheckedBuckets"), out var buckets))
                {
                    var drift = int.TryParse(kv.GetValueOrDefault("DriftCount"), out var d) ? d : 0;
                    return drift == 0
                        ? $"Đối soát {buckets} phân vùng tồn kho 9 chiều: Khớp 100% (0 sai lệch)"
                        : $"Đã rà soát {buckets} phân vùng tồn kho • Ghi nhận {drift} cảnh báo lịch sử";
                }
                return $"Đối soát toàn vẹn tồn kho 9 chiều ({targetCodeVi})";
            }

            case "PLATFORM_TENANT_STATUS_CHANGED":
            {
                var oldSt = TranslateAuditTokenVi(GetAuditJsonString(oldDoc, "status", "lifecycleStatus"));
                var newSt = TranslateAuditTokenVi(GetAuditJsonString(newDoc, "status", "lifecycleStatus")) ?? "Đang hoạt động";
                return !string.IsNullOrWhiteSpace(oldSt) && !string.Equals(oldSt, newSt, StringComparison.OrdinalIgnoreCase)
                    ? $"Trạng thái doanh nghiệp: {oldSt} → {newSt} ({targetCodeVi})"
                    : $"Trạng thái doanh nghiệp: {newSt} ({targetCodeVi})";
            }
            case "PLATFORM_TENANT_QUOTA_UPDATED":
            {
                var viCulture = CultureInfo.GetCultureInfo("vi-VN");
                var parts = new List<string>(3);
                var oldOrders = GetAuditJsonInt(oldDoc, "monthlyOrderQuota", "monthlyOrderLimit");
                var newOrders = GetAuditJsonInt(newDoc, "monthlyOrderQuota", "monthlyOrderLimit");
                if (oldOrders.HasValue && newOrders.HasValue && oldOrders.Value != newOrders.Value)
                    parts.Add($"Đơn/tháng: {oldOrders.Value.ToString("N0", viCulture)} → {newOrders.Value.ToString("N0", viCulture)}");
                else if (newOrders.HasValue)
                    parts.Add($"Hạn mức: {newOrders.Value.ToString("N0", viCulture)} đơn/tháng");

                var oldWh = GetAuditJsonInt(oldDoc, "maxWarehouses");
                var newWh = GetAuditJsonInt(newDoc, "maxWarehouses");
                if (oldWh.HasValue && newWh.HasValue && oldWh.Value != newWh.Value)
                    parts.Add($"Kho tối đa: {oldWh.Value} → {newWh.Value}");

                var oldUsers = GetAuditJsonInt(oldDoc, "maxUsers");
                var newUsers = GetAuditJsonInt(newDoc, "maxUsers");
                if (oldUsers.HasValue && newUsers.HasValue && oldUsers.Value != newUsers.Value)
                    parts.Add($"Tài khoản: {oldUsers.Value} → {newUsers.Value}");

                return parts.Count > 0
                    ? $"{string.Join(" • ", parts)} ({targetCodeVi})"
                    : $"Cập nhật hạn ngạch thuê bao ({targetCodeVi})";
            }
            case "PLATFORM_SUPPORT_IMPERSONATION_STARTED":
            {
                var ttl = GetAuditJsonInt(newDoc, "ttlMinutes") ?? 30;
                return $"Phiên hỗ trợ: Chưa kích hoạt → Đang hoạt động chỉ đọc ({ttl} phút • {targetCodeVi})";
            }
            case "PLATFORM_SUPPORT_IMPERSONATION_STOPPED":
                return $"Phiên hỗ trợ: Đang hoạt động → Đã kết thúc ({targetCodeVi})";

            case "Permission.Grant":
            case "Permission.Revoke":
            {
                var isGrant = string.Equals(action, "Permission.Grant", StringComparison.OrdinalIgnoreCase);
                var permCode = GetAuditJsonString(newDoc, "permissionCode") ?? GetAuditJsonString(oldDoc, "permissionCode");
                var permVi = TranslatePermissionCodeVi(permCode);
                return isGrant
                    ? $"Phân quyền ({targetCodeVi}): Chưa cấp → Đã cấp ({permVi})"
                    : $"Phân quyền ({targetCodeVi}): Đang cấp → Đã thu hồi ({permVi})";
            }
            case "SaaS.UpdateTaxProfile":
            {
                var kv = ParseAuditKeyValues(newValues);
                var taxCode = kv.GetValueOrDefault("TaxCode") ?? GetAuditJsonString(newDoc, "taxCode", "TaxCode");
                var compName = SanitizePlatformAuditReasonVi(kv.GetValueOrDefault("CompanyName") ?? GetAuditJsonString(newDoc, "companyName", "CompanyName"));
                return !string.IsNullOrWhiteSpace(taxCode) && !string.IsNullOrWhiteSpace(compName)
                    ? $"Hồ sơ thuế: MST {taxCode} • {compName}"
                    : $"Cập nhật hồ sơ thuế doanh nghiệp ({targetCodeVi})";
            }
            case "SaaS.VietQrBillingWebhook.Paid":
            {
                var kv = ParseAuditKeyValues(newValues);
                var planVi = TranslatePlanCodeVi(kv.GetValueOrDefault("Plan") ?? GetAuditJsonString(newDoc, "planCode", "Plan"));
                return $"Trạng thái gói cước: Đang hoạt động • {planVi} ({targetCodeVi})";
            }
        }

        if (oldDoc.HasValue || newDoc.HasValue)
        {
            var oldStVi = TranslateAuditTokenVi(GetAuditJsonString(oldDoc, "status", "Status", "state", "State", "lifecycleStatus"));
            var newStVi = TranslateAuditTokenVi(GetAuditJsonString(newDoc, "status", "Status", "state", "State", "lifecycleStatus"));
            if (!string.IsNullOrWhiteSpace(oldStVi) && !string.IsNullOrWhiteSpace(newStVi) && !string.Equals(oldStVi, newStVi, StringComparison.OrdinalIgnoreCase))
                return $"Trạng thái: {oldStVi} → {newStVi} ({targetCodeVi})";
            if (!string.IsNullOrWhiteSpace(newStVi))
                return $"Trạng thái: {newStVi} ({targetCodeVi})";
            return $"Đã ghi nhận: {actionLabelVi} ({targetCodeVi})";
        }

        var oldTokVi = TranslateAuditTokenVi(oldValues);
        var newTokVi = TranslateAuditTokenVi(newValues);
        if (!string.IsNullOrWhiteSpace(oldTokVi) && !string.IsNullOrWhiteSpace(newTokVi) && !string.Equals(oldTokVi, newTokVi, StringComparison.OrdinalIgnoreCase))
            return $"Trạng thái: {oldTokVi} → {newTokVi} ({targetCodeVi})";
        if (!string.IsNullOrWhiteSpace(newTokVi))
            return $"Trạng thái: {newTokVi} ({targetCodeVi})";

        if (!string.IsNullOrWhiteSpace(newValues) && !newValues.Contains('=') && (newValues.Contains(' ') || newValues.Any(ch => ch > 127)))
        {
            var cleanedNew = SanitizePlatformAuditReasonVi(newValues) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(cleanedNew))
            {
                if (!string.IsNullOrWhiteSpace(oldValues) && !oldValues.Contains('=') && (oldValues.Contains(' ') || oldValues.Any(ch => ch > 127)))
                {
                    var cleanedOld = SanitizePlatformAuditReasonVi(oldValues) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(cleanedOld) && !string.Equals(cleanedOld, cleanedNew, StringComparison.OrdinalIgnoreCase))
                        return $"{cleanedOld} → {cleanedNew}";
                }
                return cleanedNew;
            }
        }

        return $"Đã ghi nhận: {actionLabelVi} ({targetCodeVi})";
    }

    private static JsonElement? TryParseAuditJsonElement(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !raw.TrimStart().StartsWith('{')) return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetAuditJsonString(JsonElement? element, params string[] keys)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
            if (element.Value.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        return null;
    }

    private static int? GetAuditJsonInt(JsonElement? element, params string[] keys)
    {
        if (!element.HasValue || element.Value.ValueKind != JsonValueKind.Object) return null;
        foreach (var key in keys)
            if (element.Value.TryGetProperty(key, out var prop) && prop.TryGetInt32(out var val))
                return val;
        return null;
    }

    private static Dictionary<string, string> ParseAuditKeyValues(string? raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return dict;
        }

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var idx = part.IndexOf('=');
            if (idx > 0 && idx < part.Length - 1)
            {
                dict[part[..idx].Trim()] = part[(idx + 1)..].Trim();
            }
        }

        return dict;
    }

    private static string? TranslateAuditTokenVi(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        return rawToken.Trim().ToUpperInvariant() switch
        {
            "ACTIVE" => "Đang hoạt động",
            "SUSPENDED" => "Tạm ngưng",
            "TRIAL" => "Dùng thử",
            "GRACE" => "Gia hạn tạm thời",
            "READ_ONLY" => "Chỉ đọc",
            "ARCHIVED" => "Lưu trữ",
            "UNPAID" => "Chờ thanh toán",
            "PAID" => "Đã thanh toán",
            "PARTIALLY_PAID" => "Thanh toán một phần",
            "DRAFT" => "Nháp",
            "PENDING" or "PENDING_APPROVAL" => "Chờ phê duyệt",
            "APPROVED" => "Đã phê duyệt",
            "POSTED" => "Đã ghi sổ",
            "DISPATCHED" => "Đã xuất giao",
            "COMPLETED" => "Hoàn tất",
            "CANCELLED" => "Đã hủy",
            "REJECTED" => "Đã từ chối",
            "REVOKED" => "Đã thu hồi",
            "LOCKED" => "Đã khóa",
            "UNLOCKED" => "Đã mở khóa",
            "RELEASED" => "Đã giải phóng",
            "IN_TRANSIT" or "INTRANSIT" => "Đang vận chuyển",
            "QUARANTINE" or "RECEIVED_IN_QUARANTINE" => "Biệt trữ kiểm định",
            _ => null
        };
    }

    private static string TranslatePlanCodeVi(string? planCode) => (planCode ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "STARTER" => "Gói Khởi đầu",
        "GROWTH" => "Gói Tăng trưởng",
        "ENTERPRISE" => "Gói Doanh nghiệp",
        _ => "Gói dịch vụ SaaS"
    };

    private static string TranslatePermissionCodeVi(string? permCode)
    {
        var normalized = (permCode ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "inventory.cost.read" => "Xem giá vốn tồn kho",
            "inventory.read" => "Tra cứu tồn kho",
            "inventory.adjust" or "inventory.write" => "Điều chỉnh tồn kho",
            "user.manage" => "Quản trị tài khoản",
            "role.manage" or "permission.manage" => "Quản trị phân quyền vai trò",
            "warehouse.manage" => "Quản trị kho vận hành",
            "import.approve" => "Phê duyệt phiếu nhập kho",
            "export.approve" => "Phê duyệt phiếu xuất kho",
            "stocktake.approve" => "Phê duyệt kiểm kê kho",
            _ when normalized.StartsWith("inventory.", StringComparison.Ordinal) => "Nghiệp vụ tồn kho",
            _ when normalized.StartsWith("import.", StringComparison.Ordinal) => "Nghiệp vụ nhập kho",
            _ when normalized.StartsWith("export.", StringComparison.Ordinal) => "Nghiệp vụ xuất kho",
            _ when normalized.StartsWith("platform.", StringComparison.Ordinal) => "Quản trị nền tảng",
            _ => "Quyền nghiệp vụ hệ thống"
        };
    }

    internal static bool HasWarehouseQuotaChanged(string? oldValues, string? newValues)
    {
        var oldWh = GetAuditJsonInt(TryParseAuditJsonElement(oldValues), "maxWarehouses");
        var newWh = GetAuditJsonInt(TryParseAuditJsonElement(newValues), "maxWarehouses");
        return oldWh.HasValue && newWh.HasValue && oldWh.Value != newWh.Value;
    }
}
