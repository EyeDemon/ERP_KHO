using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public record VerifyPackagingProfileInput(
    int? ProductId = null,
    string? CartonType = null,
    int? WarehouseId = null);

public record ExecuteKittingVasInput(
    int? WarehouseId = null,
    string? KitSkuCode = null,
    string? VasServiceType = null,
    string? VasServices = null,
    int? ComponentProductId = null,
    string? ComponentProductCode = null,
    int? KitProductId = null,
    int? ParentProductId = null,
    string? KitProductCode = null,
    int? LocationId = null,
    string? LocationCode = null,
    decimal? KitQuantity = null,
    decimal? Quantity = null,
    decimal? ComponentRatio = null,
    string? OperationMode = null,
    string? VasMode = null,
    string? Note = null,
    bool ExecuteStockMutation = false);

public record RecordPackingVideoEvidenceInput(
    int? PackingSessionId = null,
    int? ExportReceiptId = null,
    string? TrackingNumber = null,
    string? OrderCode = null,
    string? CameraStationId = null,
    DateTime? StartedAtUtc = null,
    DateTime? EndedAtUtc = null,
    string? VideoHashSha256 = null,
    string? StorageUrl = null,
    long? FileSizeBytes = null);

public record RecommendCartonInput(
    decimal? DimDivisor = 6000m,
    string? ServiceType = "ROAD_STANDARD",
    decimal? FragileBubbleWrapAllowancePct = 15m,
    string? PreferredCartonCode = null);

public interface IPackingSessionOperationsService
{
    object GetPackagingProfilesPayload();

    Task<WmsEndpointOutcome> VerifyPackagingProfileAsync(
        VerifyPackagingProfileInput? request,
        IWarehouseAuthorizationService? warehouseAccess = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ExecuteKittingVasAsync(
        ExecuteKittingVasInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccess = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> RecordPackingVideoEvidenceAsync(
        RecordPackingVideoEvidenceInput? request,
        int? sessionId,
        Stream? videoStream,
        long? uploadedFileLength,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ListPackingVideoEvidencesAsync(
        string? trackingNumber = null,
        string? orderCode = null,
        int? sessionId = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetPackingVideoEvidenceAsync(
        string trackingNumber,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> BuildCartonRecommendationAsync(
        string? id,
        RecommendCartonInput? request,
        IWarehouseAuthorizationService? warehouseAccess = null,
        CancellationToken cancellationToken = default);
}
