using System.Text.Json;
using System.Text.RegularExpressions;
using ERP.Infrastructure.Services;

namespace ERP.Api.Controllers;

public sealed partial class PlatformControlCenterController
{
    internal static string ResolveCleanEventTargetCode(string? jsonTarget, string entityName, int? entityId, int tenantId, int rowId) =>
        ResolveAuditTargetCodeVi(jsonTarget, entityName, entityId, tenantId, rowId, null);

    internal static string ResolveAuditTargetCodeVi(
        string? jsonTarget,
        string entityName,
        int? entityId,
        int tenantId,
        int rowId,
        string? warehouseCode,
        params string?[] rawPayloads)
    {
        if (!string.IsNullOrWhiteSpace(jsonTarget))
        {
            var trimmedTarget = jsonTarget.Trim();
            if (trimmedTarget.StartsWith("IMP-", StringComparison.OrdinalIgnoreCase) && trimmedTarget.Length > 12)
            {
                return $"IMP-****{trimmedTarget[^4..]}";
            }

            return trimmedTarget.Replace("ERP_KHO", "TENANT-001", StringComparison.OrdinalIgnoreCase);
        }

        foreach (var text in rawPayloads)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var invOrTicket = Regex.Match(text, @"\b(INV-SAAS-[A-Z0-9-]+|SUP-[A-Z0-9-]+|TENANT-\d{3})\b", RegexOptions.IgnoreCase);
            if (invOrTicket.Success)
            {
                return invOrTicket.Value.ToUpperInvariant();
            }
        }

        switch (entityName)
        {
            case "SaasTenant":
            case "TenantSubscription":
            case "TenantQuotaVerification":
                return $"TENANT-{(entityId is > 0 ? entityId.Value : Math.Max(1, tenantId)):D3}";
            case "SaasBillingInvoice":
                return $"INV-SAAS-{(entityId ?? rowId):D3}";
            case "SupportImpersonationSession":
            case "SupportImpersonationConsent":
                return $"SUP-VN-2026-{(entityId ?? rowId):D3}";
            case "IntegrationOutboxBatch":
            case "IntegrationOutboxMessage":
            case "IntegrationOutbox":
                return "Đồng bộ hệ thống";
            case "Role":
            case "Roles":
            case "RolePermission":
                return entityId is > 0 ? $"Vai trò #{entityId.Value}" : "Phân quyền vai trò";
            case "Warehouse":
            case "Warehouses":
            case "WarehouseLocation":
            case "WarehouseLocations":
            case "WarehouseCalendarException":
                return !string.IsNullOrWhiteSpace(warehouseCode)
                    ? $"Kho {warehouseCode.Trim()}"
                    : (entityId is > 0 ? $"Kho #{entityId.Value}" : "Kho vận hành");
            case "User":
            case "Users":
            case "UserTotpMfa":
            case "AccountSecurity":
            case "PlatformAdminAccess":
                return entityId is > 0 ? $"Tài khoản #{entityId.Value}" : "Cổng quản trị nền tảng";
            case "InventoryStock":
            case "InventoryStatus":
            case "InventoryLedger":
                return "Sổ cái tồn kho";
            case "BuyerPiiRetentionPolicy":
            case "ComplianceEvidenceBundle":
                return "Tuân thủ & bảo mật";
        }

        foreach (var text in rawPayloads)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var docMatch = Regex.Match(
                text,
                @"\b(IR|ER|PUT|PICK|PACK|SHP|MOV|LOT|STK|ADJ|RMA|PNK|PXK|KK|SO|PO|TRF|APPT|HU)-[A-Z0-9]{2,12}(?:-[A-Z0-9]{1,12})*\b",
                RegexOptions.IgnoreCase);
            if (docMatch.Success && docMatch.Value.Any(char.IsDigit))
            {
                return docMatch.Value.ToUpperInvariant();
            }
        }

        return entityName switch
        {
            "Product" or "Products" or "ProductUom" or "ProductGovernance" or "ReceivingTolerancePolicy" or "QcPolicy" or "ChannelSkuMapping"
                => entityId is > 0 ? $"Sản phẩm #{entityId.Value}" : "Danh mục sản phẩm",
            "ImportReceipt" or "ImportReceipts" => entityId is > 0 ? $"Phiếu nhập #{entityId.Value}" : "Phiếu nhập kho",
            "ExportReceipt" or "ExportReceipts" => entityId is > 0 ? $"Phiếu xuất #{entityId.Value}" : "Phiếu xuất kho",
            "SalesOrder" or "SalesOrders" => entityId is > 0 ? $"Đơn hàng #{entityId.Value}" : "Đơn bán hàng",
            "PurchaseOrder" or "PurchaseOrders" or "Asn" => entityId is > 0 ? $"Đơn mua #{entityId.Value}" : "Đơn mua hàng",
            "Shipment" or "Shipments" or "HandoverManifest" or "TmsManifest" => entityId is > 0 ? $"Vận đơn #{entityId.Value}" : "Chuyến hàng xuất",
            "Stocktake" or "Stocktakes" or "StocktakeSession" or "StocktakeAdjustmentProposal" => entityId is > 0 ? $"Kiểm kê #{entityId.Value}" : "Phiếu kiểm kê",
            "StockTransfer" or "StockTransfers" => entityId is > 0 ? $"Điều chuyển #{entityId.Value}" : "Phiếu điều chuyển",
            "CustomerReturn" or "CustomerReturns" or "RmaInspection" or "RtvRefurbish" => entityId is > 0 ? $"Đổi trả #{entityId.Value}" : "Phiếu hàng hoàn",
            "ApprovalQueue" => entityId is > 0 ? $"Phê duyệt #{entityId.Value}" : "Hàng đợi phê duyệt",
            _ when !string.IsNullOrWhiteSpace(warehouseCode) => $"Kho {warehouseCode.Trim()}",
            _ => $"HT-{(entityId ?? rowId):D3}"
        };
    }

    internal static string? SanitizePlatformAuditReasonVi(string? rawReason)
    {
        if (string.IsNullOrWhiteSpace(rawReason))
        {
            return rawReason;
        }

        var cleaned = Regex.Replace(rawReason.Trim(), @"\s*sau kiểm định(?:\s+đối kháng)?\s+WAVE-[\w.-]+", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s*\(?WAVE-[\w.-]+\)?", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"trạng thái\s+(?:chuẩn\s+)?ACTIVE\b", "trạng thái Đang hoạt động", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bACTIVE\b", "Đang hoạt động", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bSUSPENDED\b", "Tạm ngưng", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bUNPAID\b", "Chờ thanh toán", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bPARTIALLY_PAID\b", "Thanh toán một phần", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bPAID\b", "Đã thanh toán", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bgói\s+GROWTH\b", "gói Tăng trưởng", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bgói\s+STARTER\b", "gói Khởi đầu", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bgói\s+ENTERPRISE\b", "gói Doanh nghiệp", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bWebhook thanh toán VietQR\b", "Hệ thống thanh toán VietQR", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\b(?:qa_admin|platform_operator)\b", "Chủ dự án (SuperAdmin)", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bqa_billing_admin\b", "Quản trị Cước phí SaaS", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bqa_support_admin\b", "Chuyên viên Hỗ trợ Kỹ thuật", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\bqa_security_auditor\b", "Kiểm toán An toàn Thông tin", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\btenant_admin\b", "Quản trị viên Đại Phát", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"(\d)\s*đ(?=$|[\s.,;•)])", "$1 ₫", RegexOptions.None);
        cleaned = Regex.Replace(cleaned, @"\bsổ cái 9 chiều\b", "tồn kho 9 chiều", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"\s*\(Guided Impersonation\)", string.Empty, RegexOptions.IgnoreCase);
        return cleaned
            .Replace("ERP_KHO", "Đại Phát", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    internal static string NormalizeDashboardSummaryVi(string? rawSummary, string eventLabelVi, string targetCode)
    {
        if (string.IsNullOrWhiteSpace(rawSummary)
            || rawSummary.TrimStart().StartsWith('{')
            || rawSummary.Contains("TaxCode=", StringComparison.OrdinalIgnoreCase)
            || rawSummary.Contains("SessionId=", StringComparison.OrdinalIgnoreCase))
        {
            return $"{eventLabelVi} ({targetCode})";
        }

        var cleaned = SanitizePlatformAuditReasonVi(rawSummary) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return $"{eventLabelVi} ({targetCode})";
        }

        if (string.Equals(cleaned, "Khôi phục trạng thái Đang hoạt động", StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetCode, "TENANT-001", StringComparison.OrdinalIgnoreCase))
        {
            return $"Khôi phục trạng thái Đang hoạt động cho {SaasBillingService.CanonicalTenant1LegalName} (TENANT-001)";
        }

        return cleaned;
    }

    internal static (string? Actor, string? Target, string? Status, string? Summary) ParseDashboardAuditJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{'))
        {
            return (null, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var actor = root.TryGetProperty("actorUsername", out var a1) ? a1.GetString()
                : root.TryGetProperty("ReconciledByUsername", out var a4) ? a4.GetString()
                : root.TryGetProperty("startedByUsername", out var a2) ? a2.GetString()
                : root.TryGetProperty("suspendedBy", out var a3) ? a3.GetString() : null;
            var target = root.TryGetProperty("invoiceCode", out var t1) ? t1.GetString()
                : root.TryGetProperty("ticketCode", out var t4) ? t4.GetString()
                : root.TryGetProperty("tenantCode", out var t2) ? t2.GetString()
                : root.TryGetProperty("sessionId", out var t3) ? t3.GetString() : null;
            var status = root.TryGetProperty("status", out var s1) ? s1.GetString()
                : root.TryGetProperty("TargetStatus", out var s2) ? s2.GetString() : null;
            var summary = root.TryGetProperty("ownerAuditStampVi", out var m1) ? m1.GetString()
                : root.TryGetProperty("OwnerAuditStampVi", out var m3) ? m3.GetString()
                : root.TryGetProperty("settlementSummaryVi", out var m2) ? m2.GetString() : null;
            return (actor, target, status, summary);
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }

    internal static (string Code, string LabelVi, string Tone) MapPlatformEventPresentation(
        string action,
        string entityName,
        string? jsonStatus,
        string? result,
        string? oldValues = null,
        string? newValues = null)
    {
        var toneFallback = string.Equals(result, "Failed", StringComparison.OrdinalIgnoreCase) ? "danger" : "info";
        return action switch
        {
            "PLATFORM_BILLING_MANUAL_RECONCILE" => ("PLATFORM_BILLING_MANUAL_RECONCILE", "Đối soát thanh toán thủ công", "success"),
            "PLATFORM_SAAS_INVOICE_RECONCILED" => string.Equals(jsonStatus, "UNPAID", StringComparison.OrdinalIgnoreCase)
                ? ("PLATFORM_BILLING_RECONCILE_RESET", "Đặt lại trạng thái đối soát", "warning")
                : ("PLATFORM_SAAS_INVOICE_RECONCILED", "Đối soát hóa đơn VietQR", "success"),
            "PLATFORM_BILLING_RECONCILE_RESET" => ("PLATFORM_BILLING_RECONCILE_RESET", "Đặt lại trạng thái đối soát", "warning"),
            "PLATFORM_WAREHOUSE_QUOTA_UPDATED" or "Warehouse.QuotaUpdated" => ("PLATFORM_WAREHOUSE_QUOTA_UPDATED", "Cập nhật hạn mức kho", "info"),
            "PLATFORM_TENANT_QUOTA_UPDATED" => (
                "PLATFORM_TENANT_QUOTA_UPDATED",
                HasWarehouseQuotaChanged(oldValues, newValues) ? "Cập nhật hạn mức kho" : "Cập nhật hạn ngạch doanh nghiệp",
                "info"),
            "PLATFORM_TENANT_CREATED" or "PLATFORM_TENANT_ONBOARDED" or "SaaS.TenantCreated" or "Tenant.Created" or "Tenant.Onboarded"
                => ("PLATFORM_TENANT_ONBOARDED", "Khởi tạo doanh nghiệp", "info"),
            "PLATFORM_TENANT_STATUS_CHANGED" => (
                "PLATFORM_TENANT_STATUS_CHANGED",
                string.Equals(jsonStatus, "SUSPENDED", StringComparison.OrdinalIgnoreCase) ? "Tạm ngưng doanh nghiệp" : "Cập nhật trạng thái doanh nghiệp",
                string.Equals(jsonStatus, "SUSPENDED", StringComparison.OrdinalIgnoreCase) ? "danger" : "success"),
            "PLATFORM_SUPPORT_IMPERSONATION_STARTED" => ("PLATFORM_SUPPORT_IMPERSONATION_STARTED", "Khởi tạo phiên hỗ trợ kỹ thuật", "warning"),
            "PLATFORM_SUPPORT_IMPERSONATION_STOPPED" => ("PLATFORM_SUPPORT_IMPERSONATION_STOPPED", "Kết thúc phiên hỗ trợ kỹ thuật", "info"),
            "PLATFORM_ADMIN_AUTH_VERIFIED" => ("PLATFORM_ADMIN_AUTH_VERIFIED", "Xác thực quản trị", "info"),
            "ACCOUNT_SECURITY_LOCKED" => ("ACCOUNT_SECURITY_LOCKED", "Tạm khóa tài khoản do sai mật khẩu", "danger"),
            "ACCOUNT_SECURITY_TOKEN_REUSE_BLOCKED" => ("ACCOUNT_SECURITY_TOKEN_REUSE_BLOCKED", "Chặn nghi vấn tái sử dụng mã phiên", "danger"),
            "ACCOUNT_SECURITY_SESSIONS_REVOKED" => ("ACCOUNT_SECURITY_SESSIONS_REVOKED", "Thu hồi toàn bộ phiên đăng nhập", "warning"),
            "SaaS.UpdateTaxProfile" => ("SAAS_TAX_PROFILE_UPDATED", "Cập nhật hồ sơ thuế doanh nghiệp", "info"),
            "SaaS.VietQrBillingWebhook.Paid" => ("SAAS_VIETQR_WEBHOOK_PAID", "Đối soát hóa đơn VietQR", "success"),
            "Permission.Grant" => ("PERMISSION_GRANTED", "Cấp quyền truy cập vai trò", "info"),
            "Permission.Revoke" => ("PERMISSION_REVOKED", "Thu hồi quyền truy cập vai trò", "warning"),
            "Permissions.VerifyTenantQuotas" => ("PERMISSIONS_QUOTA_VERIFIED", "Đối soát hạn ngạch quyền thuê bao", "info"),
            "User.PasswordReset" => ("USER_PASSWORD_RESET", "Đặt lại mật khẩu tài khoản", "warning"),
            "AccountSecurity.GrantSupportImpersonation" => ("SUPPORT_IMPERSONATION_GRANTED", "Cấp quyền phiên hỗ trợ kỹ thuật", "warning"),
            "AccountSecurity.RevokeSupportImpersonation" => ("SUPPORT_IMPERSONATION_REVOKED", "Thu hồi phiên hỗ trợ kỹ thuật", "info"),
            "AccountSecurity.AnonymizeExpiredBuyerPii" or "BuyerPiiRetentionAnonymizerJob.Sweep" => ("PII_RETENTION_ANONYMIZED", "Ẩn danh dữ liệu cá nhân hết hạn lưu trữ", "info"),
            "ACCOUNT_TOTP_VERIFY_FAILED" or "ACCOUNT_TOTP_LOGIN_FAILED" => ("TOTP_VERIFY_FAILED", "Xác thực bảo mật 2 lớp (2FA) thất bại", "danger"),
            "ApprovalRejected" => ("APPROVAL_REJECTED", "Từ chối phê duyệt chứng từ", "warning"),
            "Approval.SlaEscalated" or "Approval.EscalateOverdue" => ("APPROVAL_SLA_ESCALATED", "Cảnh báo quá hạn phê duyệt chứng từ", "warning"),
            "DAILY_INVENTORY_INTEGRITY_CHECK" or "Inventory.VerifyBucketInvariants" or "Inventory.VerifyLockPrecedence" => ("PLATFORM_INVENTORY_PARITY_CHECK", "Kiểm tra toàn vẹn tồn kho 9 chiều", "success"),
            "InventoryReconciliation.DispatchOutbox" => ("INVENTORY_OUTBOX_DISPATCHED", "Đồng bộ đối soát tồn kho tích hợp", "success"),
            "MAINTENANCE_WINDOW" => ("WAREHOUSE_MAINTENANCE_WINDOW", "Cấu hình khung giờ bảo trì kho", "warning"),
            "EMERGENCY_OPEN_OVERRIDE" => ("WAREHOUSE_EMERGENCY_OVERRIDE", "Mở kho khẩn cấp ngoài giờ", "warning"),
            "WarehouseLocation.LayoutUpdated" => ("WAREHOUSE_LAYOUT_UPDATED", "Cập nhật sơ đồ vị trí kho", "info"),
            "WarehouseCalendar.OptimizeLaborInterleaving" => ("WAREHOUSE_LABOR_OPTIMIZED", "Tối ưu điều phối nhân sự kho", "info"),
            _ when entityName is "IntegrationOutboxBatch" or "IntegrationOutboxMessage" or "IntegrationOutbox" => ("PLATFORM_OUTBOX_BATCH_SYNC", "Đồng bộ vận đơn & hóa đơn đối tác", "success"),
            var a when a.StartsWith("Permission.", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Permissions.", StringComparison.OrdinalIgnoreCase) => ("PERMISSION_UPDATED", "Cập nhật phân quyền hệ thống", toneFallback),
            var a when a.StartsWith("Role.", StringComparison.OrdinalIgnoreCase) || entityName is "Role" or "Roles" or "RolePermission" => ("ROLE_UPDATED", "Cập nhật vai trò bảo mật", toneFallback),
            var a when a.StartsWith("User.", StringComparison.OrdinalIgnoreCase) || a.StartsWith("UserWarehouse", StringComparison.OrdinalIgnoreCase) => ("USER_ACCESS_UPDATED", "Cập nhật tài khoản & phân quyền kho", toneFallback),
            var a when a.StartsWith("AccountSecurity.Totp", StringComparison.OrdinalIgnoreCase) || a.StartsWith("ACCOUNT_TOTP_", StringComparison.OrdinalIgnoreCase) => ("TOTP_SECURITY_UPDATED", "Cập nhật xác thực bảo mật 2 lớp (2FA)", toneFallback),
            var a when a.StartsWith("AccountSecurity.", StringComparison.OrdinalIgnoreCase) => ("ACCOUNT_SECURITY_UPDATED", "Cập nhật chính sách bảo mật tài khoản", toneFallback),
            var a when a.StartsWith("Tenant.", StringComparison.OrdinalIgnoreCase) || a.StartsWith("SaaS.", StringComparison.OrdinalIgnoreCase) => ("SAAS_TENANT_UPDATED", "Cập nhật cấu hình thuê bao SaaS", toneFallback),
            var a when a.StartsWith("Warehouse", StringComparison.OrdinalIgnoreCase) || entityName is "Warehouse" or "Warehouses" or "WarehouseCalendarException" => ("WAREHOUSE_CONFIG_UPDATED", "Cập nhật cấu hình kho vận hành", toneFallback),
            var a when a.StartsWith("Approval", StringComparison.OrdinalIgnoreCase) || a.EndsWith(":APPROVED", StringComparison.OrdinalIgnoreCase) => ("APPROVAL_COMPLETED", "Phê duyệt chứng từ kiểm soát", "success"),
            var a when a.StartsWith("Stocktake", StringComparison.OrdinalIgnoreCase) || a.StartsWith("STOCKTAKE_", StringComparison.OrdinalIgnoreCase) || entityName is "Stocktake" or "StocktakeSession" or "StocktakeAdjustmentProposal" => ("STOCKTAKE_GOVERNANCE", "Kiểm soát & điều chỉnh kiểm kê kho", toneFallback),
            var a when a.StartsWith("Reports.", StringComparison.OrdinalIgnoreCase) || a == "CAPTURE_3PL_DAILY_STORAGE_SNAPSHOT" => ("COMPLIANCE_REPORT_EXPORT", "Kết xuất hồ sơ đối soát & tuân thủ", toneFallback),
            var a when a.StartsWith("BusinessPartner.", StringComparison.OrdinalIgnoreCase) || a.StartsWith("ThreePlDepositorPortal.", StringComparison.OrdinalIgnoreCase) || a.StartsWith("PartnerDebt.", StringComparison.OrdinalIgnoreCase) => ("FINANCE_3PL_SETTLEMENT", "Đối soát công nợ & bảng kê 3PL", toneFallback),
            var a when a.StartsWith("ImportReceipt", StringComparison.OrdinalIgnoreCase) || a.StartsWith("PurchaseOrder", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Asn.", StringComparison.OrdinalIgnoreCase) || a.StartsWith("DockYard.", StringComparison.OrdinalIgnoreCase) || a.StartsWith("CustomerReturn.", StringComparison.OrdinalIgnoreCase) => ("INBOUND_GOVERNANCE", "Kiểm soát tiếp nhận & nhập kho", toneFallback),
            var a when a.StartsWith("ExportReceipt", StringComparison.OrdinalIgnoreCase) || a.StartsWith("SalesOrder", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Shipment", StringComparison.OrdinalIgnoreCase) || a.StartsWith("HandoverManifest", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Picking", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Packing", StringComparison.OrdinalIgnoreCase) => ("OUTBOUND_GOVERNANCE", "Kiểm soát xuất kho & vận đơn", toneFallback),
            var a when a.StartsWith("Marketplace", StringComparison.OrdinalIgnoreCase) || a.StartsWith("CarrierWebhook", StringComparison.OrdinalIgnoreCase) => ("INTEGRATION_WEBHOOK_SYNC", "Đồng bộ sự kiện sàn TMĐT & vận chuyển", toneFallback),
            var a when a.StartsWith("Product.", StringComparison.OrdinalIgnoreCase) || a.Contains("KIT", StringComparison.OrdinalIgnoreCase) || a.Contains("CHANNEL_SKU", StringComparison.OrdinalIgnoreCase) => ("CATALOG_POLICY_UPDATED", "Cập nhật danh mục & chính sách sản phẩm", toneFallback),
            var a when a.StartsWith("Inventory", StringComparison.OrdinalIgnoreCase) || a.StartsWith("Stock", StringComparison.OrdinalIgnoreCase) || a.Contains("REPLENISH", StringComparison.OrdinalIgnoreCase) || a.Contains("LPN", StringComparison.OrdinalIgnoreCase) => ("INVENTORY_OPERATION", "Kiểm soát biến động tồn kho", toneFallback),
            var a when a.StartsWith("PLATFORM_", StringComparison.OrdinalIgnoreCase) => ("PLATFORM_ADMIN_ACTION", "Thao tác quản trị nền tảng", toneFallback),
            var a when !string.IsNullOrWhiteSpace(a) && !a.Contains('.') && !a.Contains('_') && a.Any(ch => ch > 127) => ("PLATFORM_AUDIT_EVENT", SanitizePlatformAuditReasonVi(a) ?? "Sự kiện quản trị nền tảng", toneFallback),
            _ => ("PLATFORM_AUDIT_EVENT", "Sự kiện quản trị nền tảng", toneFallback)
        };
    }

    internal static string FormatPlatformAdminActorDisplay(string? username, string? fullName, string? roleName = null, int? roleId = null)
    {
        var cleanUser = (username ?? string.Empty).Trim();
        var cleanName = (fullName ?? string.Empty).Trim();

        switch (cleanUser.ToLowerInvariant())
        {
            case "qa_admin":
            case "platform_operator":
                return "Chủ dự án (SuperAdmin)";
            case "qa_billing_admin":
                return "Quản trị Cước phí SaaS";
            case "qa_support_admin":
                return "Chuyên viên Hỗ trợ Kỹ thuật";
            case "qa_security_auditor":
                return "Kiểm toán An toàn Thông tin";
            case "tenant_admin":
                return "Quản trị viên Đại Phát";
            case "qa_staff_wh01":
                return "Nhân viên kho WH01";
            case "qa_manager_wh01":
                return "Quản lý kho WH01";
            case "qa_supervisor_wh01":
                return "Giám sát & QC WH01";
            case "qa_accountant":
                return "Kế toán kho Đại Phát";
            case "qa_viewer_wh01":
                return "Kiểm toán nội bộ WH01";
            case "system_webhook":
                return "Hệ thống Thanh toán VietQR";
        }

        return roleName switch
        {
            "SuperAdmin" or "PlatformSuperAdmin" or "PlatformOwner" => "Chủ dự án (SuperAdmin)",
            "BillingAdmin" or "PlatformBillingAdmin" => "Quản trị Cước phí SaaS",
            "SupportAdmin" or "PlatformSupportAgent" => "Chuyên viên Hỗ trợ Kỹ thuật",
            "SecurityAuditor" or "PlatformSecurityAuditor" => "Kiểm toán An toàn Thông tin",
            _ when roleId == 1 && (string.IsNullOrWhiteSpace(roleName) || cleanName.Contains("Quản trị", StringComparison.OrdinalIgnoreCase) || cleanName.Contains("SuperAdmin", StringComparison.OrdinalIgnoreCase))
                => "Chủ dự án (SuperAdmin)",
            _ when !string.IsNullOrWhiteSpace(cleanName) && !string.Equals(cleanName, cleanUser, StringComparison.OrdinalIgnoreCase)
                => SanitizePlatformAuditReasonVi(cleanName) ?? cleanName,
            _ when !string.IsNullOrWhiteSpace(cleanUser)
                => "Quản trị viên nền tảng",
            _ => "Hệ thống Quản trị Nền tảng"
        };
    }
}
