using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public record VerifyDispatchCutoffInput(int? WarehouseId = null);

public record BatchPrintAwbInput(
    int? WarehouseId = null,
    List<int>? ReceiptIds = null,
    List<int>? ExportReceiptIds = null,
    bool EnforceReprintReason = false,
    string? ReprintReason = null);

public record NormalizeCarrierAddressInput(
    string? Province = null,
    string? District = null,
    string? Ward = null);

public record OpeningBalanceItemInput(
    int? ProductId = null,
    string? ProductCode = null,
    int? LocationId = null,
    string? LocationCode = null,
    decimal Quantity = 0m,
    decimal UnitCost = 0m,
    string? LotNumber = null,
    DateTime? ExpiryDate = null);

public record ImportOpeningBalanceInput(
    int? WarehouseId = null,
    int? ProductId = null,
    int? LocationId = null,
    decimal? Quantity = null,
    decimal? UnitCost = null,
    List<OpeningBalanceItemInput>? Items = null);

public record ExportMisaEinvoiceInput(
    int? WarehouseId = null,
    string? Standard = null,
    string? Format = null);

public record IssueEinvoiceNd70DraftInput(
    int? ExportReceiptId = null,
    int? WarehouseId = null,
    string? BuyerTaxCode = null,
    string? BuyerName = null,
    string? DecreeStandard = null);

public sealed record MisaExcelRowData(
    string SoChungTu,
    string NgayHachToan,
    string MaHang,
    string TenHang,
    string TkNoGiaVon,
    string TkCoKho,
    decimal SoLuong,
    decimal? UnitCost,
    decimal? DonGiaVonMac,
    decimal? CostAmount,
    decimal? ThanhTienGiaVon,
    string TkNoCongNo,
    string TkCoDoanhThu,
    decimal DonGiaBan,
    decimal DoanhThu,
    string EInvoiceStatus,
    string EInvoiceProvider);

public sealed record MisaExcelExportData(
    bool CanReadCost,
    decimal TotalCostAmountValue,
    decimal TotalRevenueAmountValue,
    IReadOnlyList<MisaExcelRowData> Rows);

public interface IExportReceiptOperationsService
{
    Task<WmsEndpointOutcome> GetSlaQueueAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome?> CheckSubscriptionQuotaForCreateAsync(
        CancellationToken cancellationToken = default);

    Task IncrementSubscriptionOrderCountAsync(
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> VerifyDispatchCutoffAsync(
        VerifyDispatchCutoffInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> BatchPrintAwbAsync(
        BatchPrintAwbInput? request,
        bool canReprintAwb,
        int? actorUserId,
        bool isAuthenticated,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default);

    Task<HandoverManifestOperationResult> GetHandoverManifestsAsync(
        int? warehouseId,
        string? carrierCode,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default);

    Task<HandoverManifestOperationResult> ScanHandoverAwbAsync(
        int? manifestIdRoute,
        ScanHandoverAwbInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default);

    Task<HandoverManifestOperationResult> SealHandoverManifestAsync(
        int id,
        SealHandoverManifestInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default);

    Task<HandoverManifestOperationResult> ConfirmCarrierHandoverAsync(
        ConfirmCarrierHandoverInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        IExportReceiptService? exportReceiptServiceOverride = null,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetCarrierAddressMappingsAsync(
        string? province,
        string? district,
        string? ward,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ImportOpeningBalanceAsync(
        ImportOpeningBalanceInput? request,
        IReadOnlyList<OpeningBalanceItemInput> parsedFileItems,
        int? actorUserId,
        bool isAuthenticated,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default);

    Task<MisaExcelExportData> GetMisaExcelExportDataAsync(
        int? warehouseId,
        bool canReadCost,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> IssueEinvoiceNd70DraftAsync(
        IssueEinvoiceNd70DraftInput? request,
        int? actorUserId,
        bool isAuthenticated,
        ICurrentUser? currentUserOverride = null,
        CancellationToken cancellationToken = default);
}
