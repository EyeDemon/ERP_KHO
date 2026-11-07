using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public record RmaIntakeCheckInput(
    string? ReturnReference = null,
    string? ReasonCode = null,
    int? WarehouseId = null,
    string? RmaReferenceCode = null,
    int? ProductId = null,
    string? ProductCode = null,
    int? LocationId = null,
    string? LocationCode = null,
    string? LotNumber = null,
    decimal? Quantity = null);

public record ExecuteRtvRefurbishInput(
    int? WarehouseId = null,
    string? DispositionType = null,
    int? ProductId = null,
    int? LocationId = null,
    string? LotNumber = null,
    bool ExecuteStockMutation = false,
    string? ActionType = null,
    decimal Quantity = 1m,
    string? ProductCode = null,
    string? LocationCode = null,
    string? ReasonCode = null,
    string? Note = null,
    int? CustomerReturnId = null,
    string? ReturnCode = null,
    string? TrackingNumber = null,
    string? Status = null);

public record CustomerReturnIntakeLineInput(
    int? ProductId = null,
    string? ProductCode = null,
    decimal Quantity = 1m,
    decimal? UnitCost = null,
    int? LotId = null,
    string? LotNumber = null,
    int? SerialId = null,
    string? SerialNumber = null,
    DateTime? ExpiryDate = null,
    int? QuarantineLocationId = null,
    string? QuarantineLocationCode = null,
    string? Condition = null,
    string? BaseUomCode = null,
    string? TransactionUomCode = null,
    decimal? ConversionRateSnapshot = null);

public record CustomerReturnIntakeInput(
    int? WarehouseId = null,
    string? ReturnCode = null,
    string? TrackingNumber = null,
    string? ExportReceiptCode = null,
    string? SalesOrderCode = null,
    string? ReturnReference = null,
    int? ExportReceiptId = null,
    int? SalesOrderId = null,
    int? ShipmentId = null,
    int? QuarantineLocationId = null,
    string? QuarantineLocationCode = null,
    string? ReturnReason = null,
    string? ReasonCode = null,
    int? ProductId = null,
    string? ProductCode = null,
    decimal? Quantity = null,
    decimal? UnitCost = null,
    int? LotId = null,
    string? LotNumber = null,
    int? SerialId = null,
    string? SerialNumber = null,
    string? Condition = null,
    IReadOnlyList<CustomerReturnIntakeLineInput>? Lines = null);

public record CustomerReturnInspectLineInput(
    int? LineId = null,
    int? ProductId = null,
    string? Condition = null,
    string? Disposition = null,
    int? TargetLocationId = null,
    int? RestockLocationId = null,
    int? PackagingProductId = null,
    decimal? PackagingQuantity = null);

public record CustomerReturnInspectInput(
    string? Condition = null,
    string? Disposition = null,
    string? Note = null,
    string? RowVersion = null,
    IReadOnlyList<CustomerReturnInspectLineInput>? Lines = null);

public record CustomerReturnDispositionSplitInput(
    int? LineId = null,
    int? ProductId = null,
    decimal Quantity = 0m,
    string? Disposition = null,
    string? Condition = null,
    int? RestockLocationId = null,
    string? RestockLocationCode = null,
    int? TargetLocationId = null,
    int? PackagingProductId = null,
    decimal? PackagingQuantity = null,
    int? LotId = null,
    int? SerialId = null);

public record CustomerReturnDisposeInput(
    string? Disposition = null,
    int? RestockLocationId = null,
    string? RestockLocationCode = null,
    int? TargetLocationId = null,
    int? PackagingProductId = null,
    decimal? PackagingQuantity = null,
    string? ReasonCode = null,
    string? Note = null,
    string? RowVersion = null,
    IReadOnlyList<CustomerReturnDispositionSplitInput>? Splits = null,
    IReadOnlyList<CustomerReturnDispositionSplitInput>? Lines = null);

public record CustomerReturnQueryFilter(
    int? WarehouseId = null,
    string? Status = null,
    string? Condition = null,
    string? Disposition = null,
    string? Keyword = null,
    int PageNumber = 1,
    int PageSize = 20,
    bool CanReadCost = true);

public record ExpressPostImportReceiptInput(
    int? TargetLocationId = null,
    string? TargetLocationCode = null,
    string? Note = null);

public interface IImportReceiptOperationsService
{
    Task<WmsEndpointOutcome> GetPipelineSummaryAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetQcAndRmaSummaryAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ExpressPostAsync(
        int id,
        ExpressPostImportReceiptInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> RmaIntakeCheckAsync(
        RmaIntakeCheckInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetCustomerReturnsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> QueryCustomerReturnsAsync(
        CustomerReturnQueryFilter filter,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetCustomerReturnByIdAsync(
        int id,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> IntakeCustomerReturnAsync(
        CustomerReturnIntakeInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> InspectCustomerReturnAsync(
        int id,
        CustomerReturnInspectInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> DisposeCustomerReturnAsync(
        int id,
        CustomerReturnDisposeInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ExecuteRtvRefurbishAsync(
        ExecuteRtvRefurbishInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetMacPreviewAsync(
        int id,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);
}
