namespace ERP.Application.Interfaces;

public record CarrierWaybillBookingRequest(
    int WarehouseId = 1,
    int? ShipmentId = null,
    int? ExportReceiptId = null,
    string CarrierCode = "GHN",
    string RecipientName = "",
    string RecipientPhone = "",
    string RawAddress = "",
    string? Province = null,
    string? District = null,
    string? Ward = null,
    decimal WeightGrams = 500m,
    decimal CodAmountVnd = 0m,
    decimal OrderValueVnd = 0m);

public record CarrierWaybillBookingResult(
    string CarrierCode,
    string AwbTrackingCode,
    string SortingHubCode,
    string RouteCode,
    decimal EstimatedShippingFeeVnd,
    string AdministrativeModel,
    string NormalizedAddress,
    string ProviderPayloadJson,
    bool IsSandbox);

public record CarrierWebhookEventInput(
    string? EventId = null,
    string? TrackingNumber = null,
    int? ShipmentId = null,
    int? ExportReceiptId = null,
    string? CarrierStatus = null,
    decimal? ActualCodCollected = null,
    decimal? ActualShippingFee = null,
    int? ChargedWeightGrams = null,
    string? PodRecipientName = null,
    string? PodNote = null,
    string? PodPhotoUrl = null,
    string? FailureReason = null,
    string? Status = null,
    string? EventType = null);

public record UpsertCarrierConfigInput(
    int? WarehouseId = null,
    string? CarrierCode = null,
    string? ShopId = null,
    string? ApiToken = null,
    bool? IsSandbox = null,
    string? WebhookSecret = null,
    string? SortingHubPrefix = null,
    decimal? BaseShippingFeeVnd = null,
    bool? IsActive = null);

public interface ICarrierIntegrationAdapter
{
    string CarrierCode { get; }
    string CarrierDisplayName { get; }

    CarrierWaybillBookingResult BookWaybill(
        CarrierWaybillBookingRequest request,
        string sortingHubCode,
        string routeCode,
        string normalizedAddress,
        decimal baseFeeVnd,
        bool isSandbox = true);

    bool VerifyWebhookHmacSignature(
        string rawPayload,
        string? providedSignature,
        string webhookSecret);
}

public interface ICarrierIntegrationRegistry
{
    ICarrierIntegrationAdapter ResolveAdapter(string? carrierCode);
    IReadOnlyList<ICarrierIntegrationAdapter> GetAllAdapters();
}
