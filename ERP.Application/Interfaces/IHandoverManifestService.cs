namespace ERP.Application.Interfaces;

public sealed record ScanHandoverAwbInputDto(
    int? ManifestId = null,
    int? WarehouseId = null,
    string? CarrierCode = null,
    string? TrackingNumber = null,
    int? ExportReceiptId = null,
    string? VehiclePlate = null,
    string? DriverName = null,
    string? DriverPhone = null);

public sealed record SealHandoverManifestInputDto(
    string? DriverName = null,
    string? DriverPhone = null,
    string? VehiclePlate = null,
    string? DigitalSignatureHash = null);

public sealed record ConfirmCarrierHandoverInputDto(
    int? WarehouseId = null,
    string? CarrierCode = null,
    IReadOnlyList<string>? TrackingNumbers = null,
    string? VehiclePlate = null,
    string? DriverName = null,
    string? DriverPhone = null,
    int? ManifestId = null,
    IReadOnlyList<int>? ExportReceiptIds = null);

public sealed record HandoverManifestOperationResult(
    int StatusCode,
    object Payload)
{
    public static HandoverManifestOperationResult Ok(object payload) => new(200, payload);
    public static HandoverManifestOperationResult Fail(int statusCode, object payload) => new(statusCode, payload);
}

public interface IHandoverManifestService
{
    Task<HandoverManifestOperationResult> GetHandoverManifestsAsync(
        int? warehouseId = null,
        string? carrierCode = null,
        CancellationToken cancellationToken = default);

    Task<HandoverManifestOperationResult> ScanHandoverAwbAsync(
        int? manifestIdRoute,
        ScanHandoverAwbInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        CancellationToken cancellationToken = default);

    Task<HandoverManifestOperationResult> SealHandoverManifestAsync(
        int id,
        SealHandoverManifestInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        CancellationToken cancellationToken = default);

    Task<HandoverManifestOperationResult> ConfirmCarrierHandoverAsync(
        ConfirmCarrierHandoverInputDto? request,
        int? actorUserId,
        bool isAuthenticated,
        CancellationToken cancellationToken = default);
}
