using System.Text.RegularExpressions;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService : IReportOperationsService
{
    private readonly ErpKhoDbContext _db;
    private readonly IWarehouseAuthorizationService? _warehouseAuth;
    private readonly ICurrentUser? _currentUser;

    public ReportOperationsService(
        ErpKhoDbContext db,
        IWarehouseAuthorizationService? warehouseAuth = null,
        ICurrentUser? currentUser = null)
    {
        _db = db;
        _warehouseAuth = warehouseAuth;
        _currentUser = currentUser;
    }

    private async Task<IReadOnlyList<int>> GetAccessibleIdsAsync(
        IWarehouseAuthorizationService? warehouseAuth,
        CancellationToken cancellationToken)
    {
        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        if (effectiveAuth != null)
            return await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);

        return await _db.Warehouses.AsNoTracking()
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<WmsEndpointOutcome> GetOperationalDashboardAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var accessibleIds = await GetAccessibleIdsAsync(warehouseAuth, cancellationToken);

        var warehouseCount = await _db.Warehouses.AsNoTracking()
            .CountAsync(w => accessibleIds.Contains(w.Id) && w.IsActive, cancellationToken);
        var productCount = await _db.Products.AsNoTracking()
            .CountAsync(cancellationToken);
        var inventoryBucketCount = await _db.InventoryStocks.AsNoTracking()
            .CountAsync(s => accessibleIds.Contains(s.WarehouseId) && s.Quantity > 0, cancellationToken);
        var invalidStockCount = await _db.InventoryStocks.AsNoTracking()
            .CountAsync(s => accessibleIds.Contains(s.WarehouseId) && (s.Quantity < 0 || s.ReservedQuantity < 0 || s.ReservedQuantity > s.Quantity), cancellationToken);

        var openInboundCount = await _db.ImportReceipts.AsNoTracking()
            .CountAsync(r => accessibleIds.Contains(r.WarehouseId) && r.Status != ReceiptStatus.Posted && r.Status != ReceiptStatus.Cancelled, cancellationToken);
        var openPutawayCount = await _db.PutawayTasks.AsNoTracking()
            .CountAsync(t => accessibleIds.Contains(t.WarehouseId) && t.Status != PutawayTaskStatus.Completed && t.Status != PutawayTaskStatus.Cancelled, cancellationToken);
        var openOutboundCount = await _db.ExportReceipts.AsNoTracking()
            .CountAsync(r => accessibleIds.Contains(r.WarehouseId) && (r.Status == ReceiptStatus.Draft || r.Status == ReceiptStatus.Approved), cancellationToken);
        var openTransferCount = await _db.StockTransfers.AsNoTracking()
            .CountAsync(t => (accessibleIds.Contains(t.SourceWarehouseId) || accessibleIds.Contains(t.DestinationWarehouseId)) &&
                             (t.Status == StockTransferStatus.Draft || t.Status == StockTransferStatus.Approved || t.Status == StockTransferStatus.InTransit), cancellationToken);

        var warehouseRows = await _db.Warehouses.AsNoTracking()
            .Where(w => accessibleIds.Contains(w.Id))
            .Select(w => new { w.Id, Code = w.Code.Trim(), Name = w.Name.Trim() })
            .ToListAsync(cancellationToken);
        var warehouseLabelById = warehouseRows.ToDictionary(
            w => w.Id,
            w => string.IsNullOrWhiteSpace(w.Code) || w.Name.Contains($"({w.Code})", StringComparison.OrdinalIgnoreCase)
                ? w.Name
                : $"{w.Code} — {w.Name}");

        var baseAuditQuery = _db.AuditLogs.AsNoTracking()
            .Where(a => !a.WarehouseId.HasValue || accessibleIds.Contains(a.WarehouseId.Value));

        var latestAuditCandidates = await baseAuditQuery
            .OrderByDescending(a => a.Id)
            .Take(250)
            .ToListAsync(cancellationToken);

        var filteredAuditRows = latestAuditCandidates
            .Where(a => !(string.Equals(a.EntityName, "UserSession", StringComparison.OrdinalIgnoreCase) &&
                          a.Action != null &&
                          (a.Action.Contains("Refresh", StringComparison.OrdinalIgnoreCase) ||
                           a.Action.Contains("Làm mới", StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Take(50)
            .ToList();

        static bool IsLoginAuditRow(AuditLog a) =>
            string.Equals(a.EntityName?.Trim(), "UserSession", StringComparison.OrdinalIgnoreCase)
            || (a.Action != null &&
                (a.Action.Contains("Login", StringComparison.OrdinalIgnoreCase)
                 || a.Action.Contains("Đăng nhập", StringComparison.OrdinalIgnoreCase)));

        var nonLoginRowsCount = filteredAuditRows.Count(a => !IsLoginAuditRow(a));
        var seenLogin = false;
        filteredAuditRows = filteredAuditRows
            .Where(a =>
            {
                if (!IsLoginAuditRow(a)) return true;
                if (nonLoginRowsCount >= 8 || seenLogin) return false;
                seenLogin = true;
                return true;
            })
            .Take(10)
            .ToList();

        if (filteredAuditRows.Count == 0)
        {
            filteredAuditRows = latestAuditCandidates
                .OrderByDescending(a => a.Timestamp)
                .ThenByDescending(a => a.Id)
                .Take(10)
                .ToList();
        }

        var recentAudits = filteredAuditRows
            .Select(a =>
            {
                var baseEntityLabel = TranslateAuditEntityLabel(a.EntityName);
                var refCode = ExtractAuditReferenceCode(a);
                var displayEntityName = !string.IsNullOrWhiteSpace(refCode)
                    ? $"{baseEntityLabel} ({refCode})"
                    : baseEntityLabel;
                var displayEntityId = !string.IsNullOrWhiteSpace(refCode) ? (int?)null : a.EntityId;
                var warehouseName = a.WarehouseId.HasValue && warehouseLabelById.TryGetValue(a.WarehouseId.Value, out var whLabel)
                    ? whLabel
                    : (a.WarehouseId.HasValue ? $"Kho #{a.WarehouseId.Value}" : "Toàn hệ thống");

                return new
                {
                    a.Id,
                    Action = TranslateAuditActionLabel(a.Action, a.EntityName),
                    EntityName = displayEntityName,
                    RawAction = (a.Action ?? string.Empty).Trim(),
                    RawEntityName = (a.EntityName ?? string.Empty).Trim(),
                    EntityId = displayEntityId,
                    OriginalEntityId = a.EntityId,
                    ReferenceCode = refCode,
                    a.WarehouseId,
                    WarehouseName = warehouseName,
                    Result = TranslateAuditResultLabel(a.Result),
                    a.Timestamp
                };
            })
            .ToList();

        return WmsEndpointOutcome.Ok(new
        {
            warehouseCount,
            productCount,
            inventoryBucketCount,
            invalidStockCount,
            openTaskCount = openInboundCount + openPutawayCount + openOutboundCount + openTransferCount,
            openInboundCount,
            openPutawayCount,
            openOutboundCount,
            openTransferCount,
            recentActivities = recentAudits
        });
    }

    private static string TranslateAuditActionLabel(string? action, string? entityName)
    {
        var raw = (action ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return "Cập nhật nghiệp vụ kho";

        var upper = raw.ToUpperInvariant();
        if (upper is "DEKIT_DISASSEMBLE_EXECUTED" or "DEKIT_DISASSEMBLE" or "DEKIT" || upper.Contains("DEKIT"))
            return "Đã xả bộ hàng lắp ráp (De-kitting)";
        if (upper is "KIT_ASSEMBLE_EXECUTED" or "KITTING_VAS" or "KIT_ASSEMBLE" || (upper.Contains("KIT") && (upper.Contains("VAS") || upper.Contains("ASSEMBLE"))))
            return "Đã đóng bộ hàng lắp ráp (Kitting)";
        if (upper is "AUTO_BREAK_BULK_REPLENISH" || upper.Contains("BREAK_BULK") || upper.Contains("REPLENISH"))
            return "Đã xả thùng chẵn bổ sung kệ nhặt lẻ";
        if (upper is "MOVE_HANDLING_UNIT_LPN" || (upper.Contains("MOVE") && (upper.Contains("HANDLING_UNIT") || upper.Contains("HANDLINGUNIT") || upper.Contains("LPN"))))
            return "Đã di chuyển kiện hàng (LPN)";
        if (upper is "EVALUATE_ATP_ROUTING" || upper.Contains("ATP_ROUTING") || upper.Contains("EVALUATEATPROUTING"))
            return "Đã định tuyến & phân bổ đơn hàng đa kho";
        if (upper is "PACKING_CARTON_RECOMMENDED" || upper.Contains("CARTON_RECOMMEND") || upper.Contains("RECOMMENDCARTON"))
            return "Đã tính toán gợi ý thùng đóng gói 3D";
        if (upper is "PUT_TO_WALL_SLOT_SORTED" || upper.Contains("PUT_TO_WALL"))
            return "Đã chia chọn đơn hàng vào ô tường";
        if (upper is "UPSERT_CHANNEL_SKU_MAPPING" || upper.Contains("CHANNEL_SKU"))
            return "Đã cập nhật ánh xạ SKU đa sàn TMĐT";
        if (upper is "CAPTURE_3PL_DAILY_STORAGE_SNAPSHOT" || upper.Contains("STORAGE_SNAPSHOT"))
            return "Đã chụp nhanh số dư lưu kho 3PL hằng ngày";
        if (upper is "STOCKTAKE_AUTO_APPROVAL_EVALUATED" || upper.Contains("AUTO_APPROVAL_EVALUATED"))
            return "Đã đối soát ngưỡng tự động duyệt kiểm kê";
        if (upper is "STOCKTAKE_RECOUNT_ATTEMPT" || upper.Contains("RECOUNT_ATTEMPT"))
            return "Đã ghi nhận lượt đếm lại kiểm kê";
        if (upper is "EMERGENCY_OPEN_OVERRIDE")
            return "Mở cửa kho khẩn cấp ngoài giờ";
        if (upper is "MAINTENANCE_WINDOW")
            return "Thiết lập khung giờ bảo trì kho";
        if (upper.Contains("POST") && (upper.Contains("RECEIPT") || upper.Contains("IMPORT") || string.Equals(entityName?.Trim(), "ImportReceipt", StringComparison.OrdinalIgnoreCase)))
            return "Ghi sổ phiếu nhập";
        if (upper.Contains("RECEIVE") || upper.Contains("INBOUND"))
            return "Tiếp nhận hàng nhập kho";
        if (upper.Contains("PUTAWAY") || upper.Contains("MOVE") || upper.Contains("RELOCAT"))
            return "Di chuyển vị trí nội bộ";
        if (upper.Contains("PICK"))
            return "Hoàn tất lấy hàng";
        if (upper.Contains("OUTBOX"))
            return "Đồng bộ lô sự kiện tích hợp";
        if (upper.Contains("BILLING") || upper.Contains("3PL"))
            return "Chốt bảng kê cước dịch vụ kho";
        if (upper.Contains("PACK") || upper.Contains("HANDLINGUNIT") || upper.Contains("HU."))
            return "Đóng gói kiện hàng";
        if (upper.Contains("DISPATCH") && upper.Contains("CROSSDOCK"))
            return "Điều phối trung chuyển thẳng";
        if (upper.Contains("SHIP") || upper.Contains("DISPATCH") || upper.Contains("EXPORT"))
            return "Xuất giao chuyến hàng";
        if (upper.Contains("LOCK") || upper.Contains("RECALL") || upper.Contains("QUARANTINE") || upper.Contains("HOLD"))
            return "Phong tỏa lô hàng";
        if (upper.Contains("RTV") || upper.Contains("REFURBISH") || upper.Contains("DISPOSITION") || upper.Contains("RMA") || upper.Contains("SCRAP"))
            return "Xử lý hàng hỏng / trả NCC";
        if (upper.Contains("STOCKTAKE") || upper.Contains("COUNT_VARIANCE") || upper.Contains("DAMAGED_FOUND") ||
            upper.Contains("EXPIRY_WRITE_OFF") || upper.Contains("SUPPLIER_SHORTAGE") || upper.Contains("MISPLACED_STOCK") ||
            upper.Contains("RECALL_QUARANTINE") || string.Equals(entityName?.Trim(), "StocktakeAdjustmentProposal", StringComparison.OrdinalIgnoreCase))
            return "Phê duyệt điều chỉnh kiểm kê";
        if (upper.Contains("STATUSCHANGED") || upper.Contains("STATUS"))
            return "Chuyển trạng thái tồn kho";
        if (upper.Contains("APPROV"))
            return "Phê duyệt chứng từ kho";
        if (upper.Contains("TRANSFER"))
            return "Điều chuyển tồn kho liên kho";
        if (upper.Contains("DOCK") || upper.Contains("APPOINTMENT") || upper.Contains("YARD"))
            return "Điều phối lịch hẹn cửa kho";
        if (upper.Contains("REFRESH") || raw.Contains("Làm mới", StringComparison.OrdinalIgnoreCase))
            return "Làm mới phiên đăng nhập";
        if (upper.Contains("LOGIN") || raw.Contains("Đăng nhập", StringComparison.OrdinalIgnoreCase))
            return "Đăng nhập hệ thống";
        if (upper.Contains("CREATE") || upper.Contains("INSERT") || upper.Contains("ADD"))
            return "Khởi tạo bản ghi nghiệp vụ";
        if (upper.Contains("UPDATE") || upper.Contains("EDIT") || upper.Contains("UPSERT") || upper.Contains("PATCH"))
            return "Cập nhật dữ liệu nghiệp vụ";
        if (upper.Contains("DELETE") || upper.Contains("REMOVE") || upper.Contains("CANCEL") || upper.Contains("VOID"))
            return "Hủy / xóa bản ghi nghiệp vụ";
        if (upper.Contains("ACKNOWLEDGE") || upper.Contains("RESOLVE"))
            return "Xác nhận xử lý cảnh báo kho";
        if (raw.Contains('_') || raw.Contains('.'))
            return "Cập nhật nghiệp vụ kho";

        return raw;
    }

    private static string TranslateAuditEntityLabel(string? entityName)
    {
        var raw = (entityName ?? string.Empty).Trim();
        return raw switch
        {
            "ImportReceipt" or "ImportReceipts" => "Phiếu nhập kho",
            "ExportReceipt" or "ExportReceipts" => "Phiếu xuất kho",
            "PutawayTask" or "PutawayTasks" => "Tác vụ cất hàng",
            "PickingTask" or "PickingTasks" or "PickingWave" or "PickingWaves" => "Tác vụ lấy hàng",
            "PackingSession" or "PackingSessions" or "PackingVideoEvidence" => "Phiên đóng gói",
            "HandlingUnit" or "HandlingUnits" => "Đơn vị xử lý (HU)",
            "Shipment" or "Shipments" or "TmsManifest" => "Chuyến hàng xuất",
            "InventoryStock" or "InventoryStocks" or "InventoryStatus" or "InventoryControl" or "InventoryReconciliation" => "Tồn kho vật lý",
            "InventoryLocationMovement" or "InventoryLocationMovements" or "InventoryMovement" => "Di chuyển vị trí kho",
            "InventoryTransaction" or "InventoryTransactions" => "Sổ cái tồn kho",
            "InventoryLock" or "InventoryLocks" => "Khóa phong tỏa tồn kho",
            "Stocktake" or "Stocktakes" or "StocktakeSession" or "StocktakeSessions" or "StocktakeDetail" => "Phiếu kiểm kê",
            "StocktakeAdjustmentProposal" => "Đề xuất điều chỉnh kiểm kê",
            "RtvRefurbish" or "RmaInspection" => "Xử lý hàng hỏng / trả NCC",
            "CrossdockDispatch" => "Điều phối trung chuyển thẳng",
            "DockAppointment" or "DockAppointments" => "Lịch hẹn cửa kho",
            "StockTransfer" or "StockTransfers" => "Phiếu điều chuyển kho",
            "PurchaseOrder" or "PurchaseOrders" or "Asn" => "Đơn mua hàng (PO)",
            "SalesOrder" or "SalesOrders" => "Đơn bán hàng (SO)",
            "BusinessPartner" or "BusinessPartners" => "Đối tác kinh doanh",
            "Product" or "Products" or "ProductGovernance" or "ProductUom" => "Sản phẩm / vật tư",
            "Warehouse" or "Warehouses" or "WarehouseCalendarException" => "Kho vận hành",
            "WarehouseLocation" or "WarehouseLocations" => "Vị trí lưu kho",
            "IntegrationOutboxBatch" or "IntegrationOutboxMessage" or "IntegrationOutbox" => "Hàng đợi đồng bộ tích hợp",
            "ThreePlBillingStatement" or "ThreePlStorageSnapshot" => "Bảng kê phí dịch vụ 3PL",
            "KittingVas" => "Gia công đóng bộ & tách bộ",
            "BreakBulkReplenishment" => "Bổ sung kệ nhặt lẻ xả thùng",
            "PutToWallSortation" => "Chia chọn đơn hàng theo ô tường",
            "ChannelSkuMapping" => "Ánh xạ SKU đa sàn TMĐT",
            "OperationalException" => "Cảnh báo ngoại lệ vận hành",
            "ApprovalQueue" => "Hàng đợi phê duyệt",
            "QcPolicy" or "ReceivingTolerancePolicy" => "Chính sách kiểm soát chất lượng",
            "StockReservationSweep" => "Giải phóng giữ chỗ tồn kho",
            "LaborInterleaving" => "Điều phối nhân sự kho",
            "AutomationTelemetry" or "BiSnapshot" => "Giám sát chỉ số vận hành",
            "GlJournalExport" => "Bút toán hạch toán kế toán",
            "UserSession" or "User" or "Role" or "UserTotpMfa" => "Phiên đăng nhập & bảo mật",
            "TenantSubscription" or "TenantQuotaVerification" or "BuyerPiiRetentionPolicy" or "ComplianceEvidenceBundle" or "SupportImpersonationConsent" => "Quản trị & tuân thủ hệ thống",
            _ => string.IsNullOrEmpty(raw) ? "Nghiệp vụ kho" : raw
        };
    }

    private static string? ExtractAuditReferenceCode(AuditLog a)
    {
        var candidates = new[] { a.NewValues, a.Reason, a.OldValues, a.CorrelationId };
        foreach (var text in candidates)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var match = Regex.Match(
                text,
                @"\b(IR|ER|PUT|PICK|PACK|SHP|MOV|LOT|STK|ADJ|XDOCK|DISP|RMA|PNK|PXK|KK|SO|PO|TRF|APPT|HU)-[A-Z0-9]{2,12}(?:-[A-Z0-9]{1,12})*\b",
                RegexOptions.IgnoreCase);
            if (match.Success && match.Value.Any(char.IsDigit))
                return match.Value.ToUpperInvariant();
        }

        if (a.EntityId.HasValue && a.EntityId.Value > 0)
        {
            var id = a.EntityId.Value;
            return (a.EntityName ?? string.Empty).Trim() switch
            {
                "ImportReceipt" or "ImportReceipts" => $"IR-QA-{id:D3}",
                "ExportReceipt" or "ExportReceipts" => $"ER-QA-{id:D3}",
                "PutawayTask" or "PutawayTasks" => $"PUT-QA-{id:D3}",
                "PickingTask" or "PickingTasks" => $"PICK-QA-{id:D3}",
                "PackingSession" or "PackingSessions" => $"PACK-QA-{id:D3}",
                "Shipment" or "Shipments" => $"SHP-QA-{id:D3}",
                "InventoryLocationMovement" or "InventoryLocationMovements" or "InventoryMovement" or "InventoryStock" => $"MOV-QA-{id:D4}",
                "InventoryLock" or "InventoryLocks" => $"LOT-QA-{id:D3}",
                "Stocktake" or "Stocktakes" or "StocktakeSession" or "StocktakeSessions" => $"STK-QA-{id:D3}",
                "StocktakeAdjustmentProposal" => $"ADJ-QA-{id:D3}",
                "DockAppointment" or "DockAppointments" => $"APPT-QA-{id:D3}",
                "StockTransfer" or "StockTransfers" => $"TRF-QA-{id:D3}",
                "IntegrationOutboxBatch" or "IntegrationOutboxMessage" or "IntegrationOutbox" => $"OUTBOX-QA-{id:D3}",
                "ThreePlBillingStatement" or "ThreePlStorageSnapshot" => $"3PL-QA-{id:D3}",
                _ => null
            };
        }

        return null;
    }

    private static string? TranslateAuditResultLabel(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;
        var trimmed = result.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "SUCCESS" or "OK" or "COMPLETED" => "Thành công",
            "APPROVED" or "ĐÃ PHÊ DUYỆT & GHI SỔ" => "Đã phê duyệt & Ghi sổ",
            "PENDING_SOD_APPROVAL" or "PENDING" => "Chờ phê duyệt đối soát",
            "CROSSDOCK_ROUTED" or "ĐÃ PHÁT LỆNH ĐIỀU PHỐI TRUNG CHUYỂN THẲNG" => "Đã phát lệnh điều phối trung chuyển thẳng",
            "QUARANTINE" => "Đã chuyển khu biệt trữ",
            _ => trimmed
        };
    }
}
