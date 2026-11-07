using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Application.Interfaces;

namespace ERP.Infrastructure.Services.Carriers;

public abstract class VietnamCarrierAdapterBase : ICarrierIntegrationAdapter
{
    public abstract string CarrierCode { get; }
    public abstract string CarrierDisplayName { get; }

    public abstract CarrierWaybillBookingResult BookWaybill(
        CarrierWaybillBookingRequest request,
        string sortingHubCode,
        string routeCode,
        string normalizedAddress,
        decimal baseFeeVnd,
        bool isSandbox = true);

    public bool VerifyWebhookHmacSignature(
        string rawPayload,
        string? providedSignature,
        string webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(providedSignature) || string.IsNullOrWhiteSpace(webhookSecret))
            return false;

        var cleanSignature = providedSignature.Trim();
        if (cleanSignature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            cleanSignature = cleanSignature[7..].Trim();

        if (cleanSignature.Length == 0)
            return false;

        var expectedHex = ComputeHmacSha256Hex(rawPayload ?? string.Empty, webhookSecret);
        var expectedBytes = Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant());
        var actualBytes = Encoding.ASCII.GetBytes(cleanSignature.ToLowerInvariant());

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public static string ComputeHmacSha256Hex(string rawPayload, string webhookSecret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(webhookSecret ?? string.Empty);
        var payloadBytes = Encoding.UTF8.GetBytes(rawPayload ?? string.Empty);
        var hashBytes = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    protected static int ResolveReferenceId(CarrierWaybillBookingRequest request)
    {
        if (request.ShipmentId is > 0)
            return request.ShipmentId.Value;
        if (request.ExportReceiptId is > 0)
            return request.ExportReceiptId.Value;

        var seed = $"{request.WarehouseId}:{request.RecipientPhone}:{request.RawAddress}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var val = BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF;
        return (val % 90000) + 10000;
    }

    protected static decimal CalculateTieredShippingFee(
        decimal baseFeeVnd,
        decimal weightGrams,
        decimal codAmountVnd,
        decimal orderValueVnd,
        decimal extraStepFeePer500gVnd,
        decimal insuranceRate)
    {
        var effectiveBase = baseFeeVnd > 0m ? baseFeeVnd : 22000m;
        var effectiveWeight = Math.Max(100m, weightGrams);
        var extraGrams = Math.Max(0m, effectiveWeight - 2000m);
        var extraSteps = Math.Ceiling(extraGrams / 500m);
        var weightSurcharge = extraSteps * extraStepFeePer500gVnd;

        var codFee = codAmountVnd > 3000000m ? Math.Round(codAmountVnd * 0.005m, 4) : 0m;
        var insuranceFee = orderValueVnd > 1000000m ? Math.Round(orderValueVnd * insuranceRate, 4) : 0m;

        return Math.Round(effectiveBase + weightSurcharge + codFee + insuranceFee, 4);
    }
}

public sealed class GhnCarrierAdapter : VietnamCarrierAdapterBase
{
    public override string CarrierCode => "GHN";
    public override string CarrierDisplayName => "Giao Hàng Nhanh (GHN)";

    public override CarrierWaybillBookingResult BookWaybill(
        CarrierWaybillBookingRequest request,
        string sortingHubCode,
        string routeCode,
        string normalizedAddress,
        decimal baseFeeVnd,
        bool isSandbox = true)
    {
        var warehouseId = Math.Max(1, request.WarehouseId);
        var refId = ResolveReferenceId(request);
        var awb = $"GHN-{warehouseId:D2}-{DateTime.UtcNow:yyMMdd}-{refId:D5}";
        var fee = CalculateTieredShippingFee(
            baseFeeVnd > 0m ? baseFeeVnd : 22000m,
            request.WeightGrams,
            request.CodAmountVnd,
            request.OrderValueVnd,
            extraStepFeePer500gVnd: 3500m,
            insuranceRate: 0.005m);

        var payloadJson = JsonSerializer.Serialize(new
        {
            provider = "GHN_API_V2",
            endpoint = "/shiip/public-api/v2/shipping-order/create",
            isSandbox,
            payment_type_id = request.CodAmountVnd > 0m ? 2 : 1,
            service_type_id = 2,
            required_note = "KHONGCHOXEMHANG",
            client_order_code = awb,
            to_name = request.RecipientName,
            to_phone = request.RecipientPhone,
            to_address = normalizedAddress,
            to_ward_name = request.Ward,
            to_province_name = request.Province,
            administrative_model = "VN_2_TIER_2025_2026",
            sorting_hub_code = sortingHubCode,
            route_code = routeCode,
            weight = (int)Math.Ceiling(Math.Max(100m, request.WeightGrams)),
            cod_amount = Math.Round(request.CodAmountVnd, 4),
            insurance_value = Math.Round(request.OrderValueVnd, 4),
            estimated_fee_vnd = fee
        });

        return new CarrierWaybillBookingResult(
            CarrierCode: CarrierCode,
            AwbTrackingCode: awb,
            SortingHubCode: sortingHubCode,
            RouteCode: routeCode,
            EstimatedShippingFeeVnd: fee,
            AdministrativeModel: "VN_2_TIER_2025_2026",
            NormalizedAddress: normalizedAddress,
            ProviderPayloadJson: payloadJson,
            IsSandbox: isSandbox);
    }
}

public sealed class GhtkCarrierAdapter : VietnamCarrierAdapterBase
{
    public override string CarrierCode => "GHTK";
    public override string CarrierDisplayName => "Giao Hàng Tiết Kiệm (GHTK)";

    public override CarrierWaybillBookingResult BookWaybill(
        CarrierWaybillBookingRequest request,
        string sortingHubCode,
        string routeCode,
        string normalizedAddress,
        decimal baseFeeVnd,
        bool isSandbox = true)
    {
        var refId = ResolveReferenceId(request);
        var awb = $"S2188.GHTK.{DateTime.UtcNow:yyMMdd}.{refId:D5}";
        var fee = CalculateTieredShippingFee(
            baseFeeVnd > 0m ? baseFeeVnd : 20000m,
            request.WeightGrams,
            request.CodAmountVnd,
            request.OrderValueVnd,
            extraStepFeePer500gVnd: 3000m,
            insuranceRate: 0.005m);

        var payloadJson = JsonSerializer.Serialize(new
        {
            provider = "GHTK_OPEN_API",
            endpoint = "/services/shipment/order",
            isSandbox,
            order = new
            {
                id = awb,
                name = request.RecipientName,
                tel = request.RecipientPhone,
                address = normalizedAddress,
                province = request.Province,
                ward = request.Ward,
                administrative_model = "VN_2_TIER_2025_2026",
                sorting_hub_code = sortingHubCode,
                route_code = routeCode,
                weight = Math.Round(Math.Max(100m, request.WeightGrams) / 1000m, 4),
                pick_money = Math.Round(request.CodAmountVnd, 4),
                value = Math.Round(request.OrderValueVnd, 4),
                transport = "road",
                estimated_fee_vnd = fee
            }
        });

        return new CarrierWaybillBookingResult(
            CarrierCode: CarrierCode,
            AwbTrackingCode: awb,
            SortingHubCode: sortingHubCode,
            RouteCode: routeCode,
            EstimatedShippingFeeVnd: fee,
            AdministrativeModel: "VN_2_TIER_2025_2026",
            NormalizedAddress: normalizedAddress,
            ProviderPayloadJson: payloadJson,
            IsSandbox: isSandbox);
    }
}

public sealed class ViettelPostCarrierAdapter : VietnamCarrierAdapterBase
{
    public override string CarrierCode => "VIETTELPOST";
    public override string CarrierDisplayName => "Viettel Post (VTP)";

    public override CarrierWaybillBookingResult BookWaybill(
        CarrierWaybillBookingRequest request,
        string sortingHubCode,
        string routeCode,
        string normalizedAddress,
        decimal baseFeeVnd,
        bool isSandbox = true)
    {
        var refId = ResolveReferenceId(request);
        var awb = $"VTP{DateTime.UtcNow:yyMMdd}{refId:D6}";
        var fee = CalculateTieredShippingFee(
            baseFeeVnd > 0m ? baseFeeVnd : 21000m,
            request.WeightGrams,
            request.CodAmountVnd,
            request.OrderValueVnd,
            extraStepFeePer500gVnd: 3200m,
            insuranceRate: 0.004m);

        var payloadJson = JsonSerializer.Serialize(new
        {
            provider = "VIETTELPOST_V2",
            endpoint = "/v2/order/createOrder",
            isSandbox,
            ORDER_NUMBER = awb,
            RECEIVER_FULLNAME = request.RecipientName,
            RECEIVER_PHONE = request.RecipientPhone,
            RECEIVER_ADDRESS = normalizedAddress,
            RECEIVER_PROVINCE = request.Province,
            RECEIVER_WARD = request.Ward,
            ADMINISTRATIVE_MODEL = "VN_2_TIER_2025_2026",
            SORTING_HUB_CODE = sortingHubCode,
            ROUTE_CODE = routeCode,
            PRODUCT_WEIGHT = Math.Round(Math.Max(100m, request.WeightGrams), 4),
            MONEY_COLLECTION = Math.Round(request.CodAmountVnd, 4),
            PRODUCT_PRICE = Math.Round(request.OrderValueVnd, 4),
            ORDER_SERVICE = "VCN",
            MONEY_TOTAL = fee
        });

        return new CarrierWaybillBookingResult(
            CarrierCode: CarrierCode,
            AwbTrackingCode: awb,
            SortingHubCode: sortingHubCode,
            RouteCode: routeCode,
            EstimatedShippingFeeVnd: fee,
            AdministrativeModel: "VN_2_TIER_2025_2026",
            NormalizedAddress: normalizedAddress,
            ProviderPayloadJson: payloadJson,
            IsSandbox: isSandbox);
    }
}

public sealed class JtExpressCarrierAdapter : VietnamCarrierAdapterBase
{
    public override string CarrierCode => "JT_EXPRESS";
    public override string CarrierDisplayName => "J&T Express Vietnam";

    public override CarrierWaybillBookingResult BookWaybill(
        CarrierWaybillBookingRequest request,
        string sortingHubCode,
        string routeCode,
        string normalizedAddress,
        decimal baseFeeVnd,
        bool isSandbox = true)
    {
        var refId = ResolveReferenceId(request);
        var awb = $"84{DateTime.UtcNow:yyMMdd}{refId:D6}";
        var fee = CalculateTieredShippingFee(
            baseFeeVnd > 0m ? baseFeeVnd : 19500m,
            request.WeightGrams,
            request.CodAmountVnd,
            request.OrderValueVnd,
            extraStepFeePer500gVnd: 2800m,
            insuranceRate: 0.004m);

        var payloadJson = JsonSerializer.Serialize(new
        {
            provider = "JT_EXPRESS_VN_OPENAPI",
            endpoint = "/webopenplatformapi/api/order/addOrder",
            isSandbox,
            txlogisticId = awb,
            billCode = awb,
            sortingCode = sortingHubCode,
            routeCode,
            administrativeModel = "VN_2_TIER_2025_2026",
            receiver = new
            {
                name = request.RecipientName,
                mobile = request.RecipientPhone,
                address = normalizedAddress,
                prov = request.Province,
                area = request.Ward
            },
            weight = Math.Round(Math.Max(100m, request.WeightGrams) / 1000m, 4),
            itemsValue = Math.Round(request.CodAmountVnd, 4),
            goodsValue = Math.Round(request.OrderValueVnd, 4),
            estimatedFeeVnd = fee
        });

        return new CarrierWaybillBookingResult(
            CarrierCode: CarrierCode,
            AwbTrackingCode: awb,
            SortingHubCode: sortingHubCode,
            RouteCode: routeCode,
            EstimatedShippingFeeVnd: fee,
            AdministrativeModel: "VN_2_TIER_2025_2026",
            NormalizedAddress: normalizedAddress,
            ProviderPayloadJson: payloadJson,
            IsSandbox: isSandbox);
    }
}

public sealed class VietnamCarrierIntegrationRegistry : ICarrierIntegrationRegistry
{
    public static VietnamCarrierIntegrationRegistry Default { get; } = new();

    private readonly IReadOnlyList<ICarrierIntegrationAdapter> _adapters =
    [
        new GhnCarrierAdapter(),
        new GhtkCarrierAdapter(),
        new ViettelPostCarrierAdapter(),
        new JtExpressCarrierAdapter()
    ];

    public IReadOnlyList<ICarrierIntegrationAdapter> GetAllAdapters() => _adapters;

    public ICarrierIntegrationAdapter ResolveAdapter(string? carrierCode)
    {
        var normalized = NormalizeCarrierCode(carrierCode);
        return _adapters.FirstOrDefault(a => string.Equals(a.CarrierCode, normalized, StringComparison.OrdinalIgnoreCase))
            ?? _adapters[0];
    }

    public static string NormalizeCarrierCode(string? carrierCode)
    {
        if (string.IsNullOrWhiteSpace(carrierCode))
            return "GHN";

        var raw = carrierCode.Trim().ToUpperInvariant();
        return raw switch
        {
            "GHN" or "GIAOHANGNHANH" => "GHN",
            "GHTK" or "GIAOHANGTIETKIEM" => "GHTK",
            "VIETTELPOST" or "VTP" or "VTPOST" or "VTP-EXPRESS" or "VIETTEL_POST" => "VIETTELPOST",
            "JT_EXPRESS" or "JT" or "J&T" or "JNT" or "J&T EXPRESS" or "JT-EXPRESS" => "JT_EXPRESS",
            _ => raw
        };
    }
}
