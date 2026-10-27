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

public interface IImportReceiptOperationsService
{
    Task<WmsEndpointOutcome> RmaIntakeCheckAsync(
        RmaIntakeCheckInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetCustomerReturnsAsync(
        int? warehouseId = null,
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
