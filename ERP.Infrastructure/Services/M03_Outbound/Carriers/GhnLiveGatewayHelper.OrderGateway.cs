using System.Net.Http;
using System.Text;
using System.Text.Json;
using ERP.Application.Interfaces;

namespace ERP.Infrastructure.Services.Carriers;

public static partial class GhnLiveGatewayHelper
{
    public static async Task<GhnCreateOrderResult> CreateShippingOrderAsync(
        HttpClient http,
        string apiToken,
        string shopId,
        string baseUrl,
        CarrierWaybillBookingRequest request,
        string? fromWardCode,
        string? fromWardName,
        int toDistrictId,
        string? toWardCode,
        string normalizedAddress,
        CancellationToken ct)
    {
        var effFromWard = fromWardCode?.Trim();
        var effFromWardName = fromWardName?.Trim();
        if (string.IsNullOrWhiteSpace(effFromWard))
        {
            var (wc, wn, _) = await FetchFirstWardAsync(http, baseUrl, apiToken, 1454, ct);
            effFromWard = !string.IsNullOrWhiteSpace(wc) ? wc : "21211";
            effFromWardName = !string.IsNullOrWhiteSpace(wn) ? wn : "Phường Tân Thới Nhất";
        }
        if (string.IsNullOrWhiteSpace(effFromWardName))
            effFromWardName = "Phường Tân Thới Nhất";

        var effToDistrict = toDistrictId > 0 ? toDistrictId : 1442;
        var effToWard = toWardCode?.Trim();
        if (string.IsNullOrWhiteSpace(effToWard) || !effToWard.All(char.IsDigit))
        {
            var (wc, _, _) = await FetchFirstWardAsync(http, baseUrl, apiToken, effToDistrict, ct);
            effToWard = !string.IsNullOrWhiteSpace(wc) ? wc : "20101";
        }

        var rawPhone = new string((request.RecipientPhone ?? string.Empty).Where(char.IsDigit).ToArray());
        var validToPhone = rawPhone.Length == 10 && rawPhone.StartsWith('0')
            ? rawPhone
            : "0901234567";

        var effWeight = (int)Math.Clamp(Math.Round(request.WeightGrams <= 0m ? 1000m : request.WeightGrams), 100m, 30000m);
        var effCod = (int)Math.Clamp(Math.Round(request.CodAmountVnd), 0m, 50_000_000m);
        var effInsurance = (int)Math.Clamp(Math.Round(request.OrderValueVnd), 0m, 5_000_000m);
        var effToName = string.IsNullOrWhiteSpace(request.RecipientName)
            ? "Khách Hàng Nhận Kiện ERP_KHO"
            : request.RecipientName.Trim();
        var effToAddress = string.IsNullOrWhiteSpace(normalizedAddress)
            ? "123 Lê Lợi, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh"
            : normalizedAddress;
        var resolvedServiceTypeId = request.ServiceTypeId is > 0
            ? request.ServiceTypeId.Value
            : await FetchAvailableServiceTypeIdAsync(
                http,
                apiToken,
                shopId,
                baseUrl,
                1454,
                effToDistrict,
                effWeight,
                ct);
        var clientOrderCode = !string.IsNullOrWhiteSpace(request.ClientOrderCode)
            ? request.ClientOrderCode.Trim()
            : null;

        var payload = new
        {
            payment_type_id = 1,
            note = "Đơn hàng xuất kho ERP_KHO",
            required_note = "KHONGCHOXEMHANG",
            client_order_code = clientOrderCode,
            from_name = "Kho Trung tâm (WH01)",
            from_phone = "0397354273",
            from_address = "123 Quốc Lộ 1A, Phường Tân Thới Nhất, Quận 12, TP. Hồ Chí Minh",
            from_ward_code = effFromWard,
            from_district_id = 1454,
            from_ward_name = effFromWardName,
            from_district_name = "Quận 12",
            from_province_name = "Hồ Chí Minh",
            to_name = effToName,
            to_phone = validToPhone,
            to_address = effToAddress,
            to_ward_code = effToWard,
            to_district_id = effToDistrict,
            cod_amount = effCod,
            content = "Kiện hàng xuất kho ERP_KHO",
            weight = effWeight,
            length = 20,
            width = 15,
            height = 10,
            insurance_value = effInsurance,
            service_type_id = resolvedServiceTypeId,
            items = new[]
            {
                new
                {
                    name = "Kiện hàng xuất kho ERP_KHO",
                    code = "SKU-WMS-01",
                    quantity = 1,
                    price = 100000,
                    length = 20,
                    width = 15,
                    height = 10,
                    weight = effWeight
                }
            }
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/shipping-order/create");
            req.Headers.TryAddWithoutValidation("Token", apiToken);
            req.Headers.TryAddWithoutValidation("ShopId", string.IsNullOrWhiteSpace(shopId) ? "235512" : shopId.Trim());
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new GhnCreateOrderResult(false, string.Empty, 0m, null, null, body, $"GHN HTTP {(int)resp.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                var orderCode = dataEl.TryGetProperty("order_code", out var ocEl) ? (ocEl.GetString() ?? string.Empty).Trim() : string.Empty;
                var totalFee = dataEl.TryGetProperty("total_fee", out var tfEl) ? tfEl.GetDecimal() : 0m;
                var sortCode = dataEl.TryGetProperty("sort_code", out var scEl) ? scEl.GetString() : null;
                var expectedDelivery = dataEl.TryGetProperty("expected_delivery_time", out var edEl) ? edEl.GetString() : null;

                if (!string.IsNullOrWhiteSpace(orderCode))
                {
                    return new GhnCreateOrderResult(true, orderCode, totalFee, sortCode, expectedDelivery, body);
                }
            }

            return new GhnCreateOrderResult(false, string.Empty, 0m, null, null, body, "Phản hồi GHN không chứa order_code.");
        }
        catch (Exception ex)
        {
            return new GhnCreateOrderResult(false, string.Empty, 0m, null, null, "{}", ex.Message);
        }
    }

    public static async Task<int> FetchAvailableServiceTypeIdAsync(
        HttpClient http,
        string apiToken,
        string shopId,
        string baseUrl,
        int fromDistrictId,
        int toDistrictId,
        int weightGrams,
        CancellationToken ct)
    {
        var defaultServiceTypeId = weightGrams >= 20000 ? 5 : 2;
        try
        {
            var numericShopId = int.TryParse(shopId?.Trim(), out var parsedShop) && parsedShop > 0 ? parsedShop : 235512;
            var payload = new
            {
                shop_id = numericShopId,
                from_district = fromDistrictId > 0 ? fromDistrictId : 1454,
                to_district = toDistrictId > 0 ? toDistrictId : 1442
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/shipping-order/available-services");
            req.Headers.TryAddWithoutValidation("Token", apiToken);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return defaultServiceTypeId;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.ValueKind == JsonValueKind.Array &&
                dataEl.GetArrayLength() > 0)
            {
                var availableTypes = new List<int>();
                foreach (var item in dataEl.EnumerateArray())
                {
                    if (item.TryGetProperty("service_type_id", out var stEl) && stEl.TryGetInt32(out var stId) && stId > 0)
                    {
                        availableTypes.Add(stId);
                    }
                }

                if (availableTypes.Contains(defaultServiceTypeId))
                    return defaultServiceTypeId;
                if (availableTypes.Count > 0)
                    return availableTypes[0];
            }
        }
        catch
        {
            // Fallback to deterministic service_type_id
        }

        return defaultServiceTypeId;
    }

    public static async Task<GhnOrderDetailResult> GetShippingOrderDetailAsync(
        HttpClient http,
        string apiToken,
        string baseUrl,
        string orderCode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orderCode))
        {
            return new GhnOrderDetailResult(false, string.Empty, string.Empty, 0m, 0m, null, null, "{}", "Thiếu mã vận đơn GHN (order_code).");
        }

        var trimmedCode = orderCode.Trim();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/shipping-order/detail");
            req.Headers.TryAddWithoutValidation("Token", apiToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new { order_code = trimmedCode }), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new GhnOrderDetailResult(false, trimmedCode, string.Empty, 0m, 0m, null, null, body, $"GHN HTTP {(int)resp.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                var status = dataEl.TryGetProperty("status", out var stEl) ? (stEl.GetString() ?? "ready_to_pick").Trim() : "ready_to_pick";
                var codAmount = dataEl.TryGetProperty("cod_amount", out var codEl) && codEl.TryGetDecimal(out var parsedCod) ? parsedCod : 0m;
                var totalFee = dataEl.TryGetProperty("total_fee", out var feeEl) && feeEl.TryGetDecimal(out var parsedFee) ? parsedFee : 0m;
                var leadtime = dataEl.TryGetProperty("leadtime", out var ltEl) ? ltEl.GetString() : null;
                var updatedDate = dataEl.TryGetProperty("updated_date", out var udEl) ? udEl.GetString() : null;

                return new GhnOrderDetailResult(
                    Succeeded: true,
                    OrderCode: trimmedCode,
                    RawStatus: status,
                    TotalFeeVnd: totalFee,
                    CodAmountVnd: codAmount,
                    LeadtimeIso: leadtime,
                    UpdatedDateIso: updatedDate,
                    RawResponseJson: body);
            }

            return new GhnOrderDetailResult(false, trimmedCode, string.Empty, 0m, 0m, null, null, body, "Phản hồi GHN detail không chứa trường data.");
        }
        catch (Exception ex)
        {
            return new GhnOrderDetailResult(false, trimmedCode, string.Empty, 0m, 0m, null, null, "{}", ex.Message);
        }
    }

    public static async Task<GhnCancelOrderResult> CancelShippingOrdersAsync(
        HttpClient http,
        string apiToken,
        string shopId,
        string baseUrl,
        IReadOnlyList<string> orderCodes,
        CancellationToken ct)
    {
        var cleanCodes = orderCodes
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (cleanCodes.Length == 0)
        {
            return new GhnCancelOrderResult(false, string.Empty, false, null, "{}", "Thiếu danh sách mã vận đơn cần hủy (order_codes).");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/switch-status/cancel");
            req.Headers.TryAddWithoutValidation("Token", apiToken);
            req.Headers.TryAddWithoutValidation("ShopId", string.IsNullOrWhiteSpace(shopId) ? "235512" : shopId.Trim());
            req.Content = new StringContent(JsonSerializer.Serialize(new { order_codes = cleanCodes }), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new GhnCancelOrderResult(false, cleanCodes[0], false, null, body, $"GHN HTTP {(int)resp.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.ValueKind == JsonValueKind.Array &&
                dataEl.GetArrayLength() > 0)
            {
                var first = dataEl[0];
                var resultFlag = !first.TryGetProperty("result", out var resEl) ||
                                 (resEl.ValueKind == JsonValueKind.True || (resEl.ValueKind == JsonValueKind.False ? false : true));
                var msg = first.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : "Đã gửi lệnh hủy vận đơn GHN";
                return new GhnCancelOrderResult(true, cleanCodes[0], resultFlag, msg, body);
            }

            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var codeVal) && codeVal == 200)
            {
                return new GhnCancelOrderResult(true, cleanCodes[0], true, "Đã hủy vận đơn GHN thành công", body);
            }

            return new GhnCancelOrderResult(false, cleanCodes[0], false, null, body, "Phản hồi hủy vận đơn GHN không hợp lệ.");
        }
        catch (Exception ex)
        {
            return new GhnCancelOrderResult(false, cleanCodes[0], false, null, "{}", ex.Message);
        }
    }
}
