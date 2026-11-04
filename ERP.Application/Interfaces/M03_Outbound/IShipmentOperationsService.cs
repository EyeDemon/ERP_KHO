using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public record NormalizeVnAddressInput(
    string? RawAddress = null,
    string? Province = null,
    string? District = null,
    string? Ward = null,
    string? CarrierCode = null);

public record SyncTmsManifestInput(int? WarehouseId, string? CarrierCode);

public record AssignFleetVehicleInput(
    int? ShipmentId = null,
    int? ManifestId = null,
    int? WarehouseId = null,
    string? CarrierCode = null,
    string? VehiclePlate = null,
    string? TrailerPlate = null,
    string? DriverName = null,
    string? DriverPhone = null,
    string? SealNumber = null);

public record CompleteShipmentRtsReturnInput(
    int? QuarantineLocationId = null,
    int? ProductId = null,
    decimal? Quantity = null,
    string? Note = null,
    int? TargetLocationId = null,
    bool? IsSealedIntact = null);

public sealed record VerifyAndSyncGhnInput(
    int? WarehouseId = null,
    string? ApiToken = null,
    string? Environment = null,
    string? ShopId = null,
    string? CarrierCode = null);

public sealed record CalculateCarrierFeeInput(
    int? WarehouseId = null,
    int? ShipmentId = null,
    int? ExportReceiptId = null,
    string? CarrierCode = "GHN",
    int? FromDistrictId = null,
    string? FromWardCode = null,
    int? ToDistrictId = null,
    string? ToWardCode = null,
    int? WeightGrams = null,
    int? LengthCm = null,
    int? WidthCm = null,
    int? HeightCm = null,
    decimal? InsuranceValueVnd = null,
    int? ServiceTypeId = 2,
    bool? CompareAllCarriers = null);

public sealed record SyncGhnOrderDetailInput(
    string? OrderCode = null,
    int? WarehouseId = null,
    string? CarrierCode = null);

public sealed record CancelGhnWaybillInput(
    string? OrderCode = null,
    string? Reason = null,
    int? WarehouseId = null,
    string? CarrierCode = null);

public interface IShipmentOperationsService
{
    Task<WmsEndpointOutcome> VerifyAndSyncGhnConfigAsync(
        VerifyAndSyncGhnInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> VerifyAndSyncCarrierConfigAsync(
        string carrierCode,
        VerifyAndSyncGhnInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default) =>
        VerifyAndSyncGhnConfigAsync(request, actorUserId, warehouseAuth, cancellationToken);

    Task<WmsEndpointOutcome> SyncCarrierWaybillDetailAsync(
        int shipmentId,
        SyncGhnOrderDetailInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> CancelCarrierWaybillAsync(
        int shipmentId,
        CancelGhnWaybillInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> CalculateCarrierFeeAsync(
        CalculateCarrierFeeInput? request = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> SyncTmsManifestAsync(
        SyncTmsManifestInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetFleetAssignmentsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> AssignFleetVehicleAsync(
        AssignFleetVehicleInput? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> CompleteRtsReturnAsync(
        int id,
        CompleteShipmentRtsReturnInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetVnAdministrativeUnitsAsync(
        string? provinceCode = null,
        string? districtCode = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> NormalizeVietnameseAddressAsync(
        NormalizeVnAddressInput? request = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetCarrierConfigsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> UpsertCarrierConfigAsync(
        UpsertCarrierConfigInput? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> BookCarrierWaybillAsync(
        CarrierWaybillBookingRequest? request,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> IngestCarrierWebhookAsync(
        string carrierCode,
        CarrierWebhookEventInput? request,
        string? rawBody = null,
        string? signatureHeader = null,
        int? actorUserId = null,
        CancellationToken cancellationToken = default);
}
