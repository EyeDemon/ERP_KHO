using System.Security.Claims;
using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public record SyncBiSnapshotRequestBase(int? WarehouseId = null);
public record ExportGlJournalRequestBase(int? WarehouseId = null);
public record ComplianceEvidenceBundleRequestBase(int? WarehouseId = null);

public record CodSettlementItemRequestBase(
    string? TrackingNumber = null,
    string? CarrierCode = null,
    int? ExportReceiptId = null,
    decimal ExpectedCodAmount = 0m,
    decimal ActualCodCollected = 0m,
    decimal EstimatedShippingFee = 0m,
    decimal ActualShippingFee = 0m,
    decimal PlatformCommissionFee = 0m,
    decimal ReturnFee = 0m);

public record AcknowledgeOperationalExceptionDtoBase(
    string ExceptionCode,
    int? WarehouseId,
    string? ResolutionNote,
    string? ExceptionType = null);

public record CreateRecallCampaignDtoBase(
    string? CampaignCode = null,
    int? ProductId = null,
    string? LotNumber = null,
    int? WarehouseId = null,
    string? Reason = null);

public record ExecuteRecallIsolationDtoBase(
    string? CampaignCode = null,
    int? WarehouseId = null,
    string? QuarantineLocationCode = null,
    string? Note = null);

public record CreateCustomReportTemplateDtoBase(
    string? TemplateCode = null,
    string? TemplateName = null,
    string? DatasetType = null,
    int? WarehouseId = null);

public record ExecuteCustomReportRunDtoBase(
    string? TemplateCode = null,
    int? WarehouseId = null,
    string? OutputFormat = null);

public record UpsertLaborStandardDtoBase(
    string? TaskType = null,
    decimal TargetUnitsPerHour = 0m,
    int? WarehouseId = null);

public record UpsertThreePlContractRuleDtoBase(
    int? OwnerId = null,
    int? WarehouseId = null,
    decimal DailyPalletRateVnd = 18000m,
    decimal DailyCbmRateVnd = 15000m,
    decimal DailyBinRateVnd = 5000m);

public record GenerateThreePlInvoiceDtoBase(
    int? OwnerId = null,
    int? WarehouseId = null,
    string? BillingPeriod = null);

public record ReconcileCodRemittanceBatchDtoBase(
    int? WarehouseId = null,
    string? CarrierCode = null,
    string? BatchCode = null);

public record ConfirmInterWarehouseReceiptDtoBase(
    int? TransferId = null,
    int? DestinationWarehouseId = null,
    string? Note = null);

public interface IReportOperationsService
{
    Task<WmsEndpointOutcome> GetOperationalDashboardAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetMultiWarehouseControlTowerAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetProductivityAndBiSnapshotAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> SyncBiSnapshotAsync(
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ExportGlJournalAsync(
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ComplianceEvidenceBundleAsync(
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? actorUserId = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetCodAndSettlementReconciliationAsync(
        int? warehouseId = null,
        string? carrierCode = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> VerifyCodAndSettlementReconciliationAsync(
        int? warehouseId,
        string? carrierCode,
        string? reconcileMode,
        IReadOnlyList<CodSettlementItemRequestBase>? items,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetOwnerGrossProfitAsync(
        int? warehouseId,
        bool canReadCost,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetOrCaptureThreePlStorageSnapshotAsync(
        int? warehouseId,
        int? ownerId,
        bool isPost,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetOnboardingWizardStatusAsync(
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetWarehouseTasksAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetOperationalExceptionsAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        bool includeAcknowledged = false,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> AcknowledgeOperationalExceptionAsync(
        AcknowledgeOperationalExceptionDtoBase? request,
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? userId = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GlobalSearchAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        string? q = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetAnalyticsSummaryAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        int days = 30,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetExpiryAgingReportAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        int? productId = null,
        int nearExpiryDays = 30,
        bool onlyLots = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryInOutReportDto>> ComputeInventoryInOutStockAsync(
        IWarehouseAuthorizationService? warehouseAuth,
        DateTime? fromDate,
        DateTime? toDate,
        int? warehouseId,
        int? productId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetStockCardS12DnAsync(
        int? warehouseId = null,
        int? productId = null,
        int? lotId = null,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);
}

public sealed record StockCardS12DnRowDto(
    int LineNumber,
    int TransactionId,
    DateTime EntryDate,
    DateTime VoucherDate,
    string InboundVoucherCode,
    string OutboundVoucherCode,
    string VoucherCode,
    string ReferenceType,
    string ReferenceTypeLabel,
    string Description,
    string? LocationCode,
    string? LotNumber,
    string InventoryStatus,
    decimal InboundQty,
    decimal OutboundQty,
    decimal RunningBalance,
    string SignatureConfirmation)
{
    public decimal InQuantity => InboundQty;
    public decimal OutQuantity => OutboundQty;
    public decimal BalanceQuantity => RunningBalance;
}

public sealed record StockCardS12DnReportDto(
    string FormCode,
    string CircularReference,
    string ReportTitle,
    int WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    int ProductId,
    string ProductCode,
    string ProductName,
    string UnitName,
    int? LotId,
    string? LotNumber,
    DateTime? FromDate,
    DateTime? ToDate,
    decimal OpeningBalance,
    decimal TotalInbound,
    decimal TotalOutbound,
    decimal ClosingBalance,
    decimal CurrentOnHand,
    bool ReconciledWithStock,
    IReadOnlyList<StockCardS12DnRowDto> Rows)
{
    public decimal OpeningQuantity => OpeningBalance;
    public decimal TotalInQuantity => TotalInbound;
    public decimal TotalOutQuantity => TotalOutbound;
    public decimal ClosingQuantity => ClosingBalance;
    public IReadOnlyList<StockCardS12DnRowDto> Items => Rows;
}

public static class ReportOperationsTextAndFallbackHelper
{
    public static string? NormalizeActorDisplayName(string? value, int? fallbackUserId = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallbackUserId.HasValue ? $"Người dùng #{fallbackUserId.Value}" : value?.Trim();

        var trimmed = value.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "QA MANAGER WH01" or "QA_MANAGER_WH01" => "Quản lý Kho WH01",
            "QA STAFF WH01" or "QA_STAFF_WH01" => "Nhân viên Kho WH01",
            "QA SUPERVISOR WH01" or "QA_SUPERVISOR_WH01" or "QA VIEWER WH01" or "QA_VIEWER_WH01" => "Giám sát Kho WH01",
            "QA SYSTEM ADMIN" or "QA ADMIN" or "QA_ADMIN" => "Quản trị viên Hệ thống",
            "QA_ADMIN_LOCAL" or "QA ADMIN LOCAL" => "Quản trị viên Nội bộ",
            _ => trimmed
        };
    }

    public static string TranslateInventoryStatusLabel(string? status) =>
        (status ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "AVAILABLE" => "Khả dụng",
            "QUARANTINE" => "Cách ly",
            "DAMAGED" => "Hư hỏng",
            "REJECTED" => "Từ chối",
            "EXPIRED" => "Đã hết hạn",
            "QCHOLD" or "QC_HOLD" => "Tạm giữ chờ kiểm định",
            "RECALLBLOCKED" or "RECALL_BLOCKED" => "Phong tỏa thu hồi",
            "BLOCKED" => "Phong tỏa",
            _ => (status ?? string.Empty).Trim()
        };

    public static string? TranslateReferenceTypeLabel(string? referenceType)
    {
        if (string.IsNullOrWhiteSpace(referenceType)) return referenceType?.Trim();
        var trimmed = referenceType.Trim();
        return trimmed.ToUpperInvariant() switch
        {
            "INTERNALMOVE" or "INVENTORYMOVE" => "Di chuyển nội bộ",
            "QASEEDRECONCILE" or "RECONCILIATION" => "Đối soát sổ cái",
            "IMPORTRECEIPT" or "GOODSRECEIPT" or "RECEIPT" => "Phiếu nhập kho",
            "EXPORTRECEIPT" => "Phiếu xuất kho",
            "STOCKTRANSFER" => "Phiếu điều chuyển",
            "STOCKTRANSFERDAMAGE" => "Hư hỏng điều chuyển",
            "STOCKTAKE" or "STOCKTAKEADJUSTMENT" => "Phiếu kiểm kê",
            "SHIPMENT" => "Chuyến giao hàng",
            "INVENTORYREVERSAL" => "Phiếu đảo giao dịch",
            "INVENTORYADJUSTMENT" => "Phiếu điều chỉnh",
            "PUTAWAYTASK" => "Tác vụ cất hàng",
            "PICKTASK" or "PICKINGTASK" => "Tác vụ lấy hàng",
            "PACKINGTASK" or "PACKINGSESSION" => "Tác vụ đóng gói",
            "INVENTORYSTATUSCHANGE" => "Đổi trạng thái tồn kho",
            "QUALITYDISPOSITIONRELEASE" => "Giải tỏa kiểm định chất lượng",
            "QUALITYDISPOSITIONSCRAP" => "Xuất hủy hàng lỗi",
            "QUALITYDISPOSITIONRTV" => "Hoàn trả nhà cung cấp",
            "RECALLCAMPAIGNBLOCK" => "Phong tỏa thu hồi lô hàng",
            "RETURN" => "Phiếu trả hàng",
            "DEBT_PAYMENT" or "DEBTPAYMENT" => "Thanh toán công nợ",
            _ => trimmed
        };
    }

    public static string? NormalizeTransactionNote(string? note, string? transactionType = null) =>
        note?.Trim()
            .Replace("(100% Match)", "(Khớp 100%)", StringComparison.OrdinalIgnoreCase)
            .Replace("chờ QC", "chờ kiểm định chất lượng", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeUom(string? uom, string fallback = "THÙNG")
    {
        var t = uom?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(t) ||
               string.Equals(t, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(t, "QA-PCS", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(t, "QA-BOX", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(t, "QA_BOX", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(t, "ĐVT", StringComparison.OrdinalIgnoreCase)
            ? fallback
            : t;
    }

    public static bool CanReadInventoryCost(ICurrentUser? currentUser, ClaimsPrincipal? user)
    {
        if (currentUser is { IsAuthenticated: true })
        {
            if (currentUser.IsGlobalAdmin ||
                string.Equals(currentUser.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentUser.Role, "Manager", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentUser.Role, "WarehouseManager", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentUser.Role, "Accountant", StringComparison.OrdinalIgnoreCase))
                return true;
            return currentUser.HasPermission("inventory.cost.read");
        }

        if (user?.Identity?.IsAuthenticated == true)
        {
            if (user.IsInRole("Admin") || user.IsInRole("Manager") || user.IsInRole("WarehouseManager") || user.IsInRole("Accountant"))
                return true;
            return user.Claims.Any(c =>
                (c.Type == "permission" || c.Type == "permissions") &&
                (string.Equals(c.Value, "inventory.cost.read", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.Value, "inventory.read_cost", StringComparison.OrdinalIgnoreCase)));
        }

        return true;
    }

    public static int? GetActorUserId(ClaimsPrincipal? user, ICurrentUser? currentUser = null)
    {
        if (int.TryParse(user?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user?.FindFirst("Id")?.Value, out var uid))
            return uid;
        return currentUser?.UserId;
    }
}
