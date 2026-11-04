namespace ERP.Application.Interfaces;

public sealed record DataImportOpeningBalanceItemRequest(
    int? ProductId = null,
    string? ProductCode = null,
    int? LocationId = null,
    string? LocationCode = null,
    decimal Quantity = 0m,
    decimal UnitCost = 0m,
    string? LotNumber = null,
    DateTime? ExpiryDate = null);

public sealed record DataImportOpeningBalanceRequest(
    int? WarehouseId = null,
    int? ProductId = null,
    int? LocationId = null,
    decimal? Quantity = null,
    decimal? UnitCost = null,
    string? CutoverReference = null,
    List<DataImportOpeningBalanceItemRequest>? Items = null);

public sealed record DataImportOpeningBalanceResult(
    int StatusCode,
    object Payload);

public interface IDataImportService
{
    object GetTemplatesPayload();

    Task<DataImportOpeningBalanceResult> ImportOpeningBalanceAsync(
        DataImportOpeningBalanceRequest? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);
}
