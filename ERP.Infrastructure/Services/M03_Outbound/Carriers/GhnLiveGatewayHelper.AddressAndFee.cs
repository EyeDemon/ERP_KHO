using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ERP.Infrastructure.Services.Carriers;

public static partial class GhnLiveGatewayHelper
{
    public static async Task<GhnVerificationSyncResult> VerifyAndFetchMasterDataAsync(
        HttpClient http,
        string apiToken,
        string? preferredEnvironment,
        CancellationToken ct)
    {
        var tryProdFirst = string.Equals(preferredEnvironment?.Trim(), "PRODUCTION", StringComparison.OrdinalIgnoreCase);
        var endpoints = tryProdFirst
            ? new[] { (Env: "PRODUCTION", Url: ProductionBaseUrl), (Env: "DEV_SANDBOX", Url: DevSandboxBaseUrl) }
            : new[] { (Env: "DEV_SANDBOX", Url: DevSandboxBaseUrl), (Env: "PRODUCTION", Url: ProductionBaseUrl) };

        string? verifiedEnv = null;
        string? verifiedBaseUrl = null;
        var shopId = 235512;
        var shopName = "0397354273";
        var shopPhone = "0397354273";
        string? lastError = null;

        foreach (var candidate in endpoints)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{candidate.Url}/v2/shop/all");
                req.Headers.TryAddWithoutValidation("Token", apiToken);
                req.Content = new StringContent("{\"offset\":0,\"limit\":50,\"client_phone\":\"\"}", Encoding.UTF8, "application/json");

                using var resp = await http.SendAsync(req, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)resp.StatusCode}: {body}";
                    continue;
                }

                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.GetInt32() == 200)
                {
                    verifiedEnv = candidate.Env;
                    verifiedBaseUrl = candidate.Url;
                    if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                        dataEl.TryGetProperty("shops", out var shopsEl) &&
                        shopsEl.ValueKind == JsonValueKind.Array &&
                        shopsEl.GetArrayLength() > 0)
                    {
                        var firstShop = shopsEl[0];
                        if (firstShop.TryGetProperty("_id", out var idEl) && idEl.TryGetInt32(out var parsedId) && parsedId > 0)
                            shopId = parsedId;
                        if (firstShop.TryGetProperty("name", out var nameEl) && !string.IsNullOrWhiteSpace(nameEl.GetString()))
                            shopName = nameEl.GetString()!.Trim();
                        if (firstShop.TryGetProperty("phone", out var phoneEl) && !string.IsNullOrWhiteSpace(phoneEl.GetString()))
                            shopPhone = phoneEl.GetString()!.Trim();
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        if (verifiedBaseUrl == null || verifiedEnv == null)
        {
            return new GhnVerificationSyncResult(
                Succeeded: false,
                Environment: "DEV_SANDBOX",
                GatewayBaseUrl: DevSandboxBaseUrl,
                ShopId: 0,
                ShopName: string.Empty,
                ShopPhone: string.Empty,
                FromProvinceId: 202,
                FromProvinceName: "Hồ Chí Minh",
                FromDistrictId: 1454,
                FromDistrictName: "Quận 12",
                FromWardCode: "21211",
                FromWardName: "Phường Tân Thới Nhất",
                DefaultToDistrictId: 1442,
                DefaultToWardCode: "20101",
                DefaultToWardName: "Phường Bến Nghé",
                SyncedProvincesCount: 0,
                SyncedDistrictsCount: 0,
                SyncedWardsCount: 0,
                ErrorMessage: lastError ?? "Không thể xác thực Token với cổng GHN API.");
        }

        var syncedProvincesCount = 0;
        var syncedDistrictsCount = 0;
        var syncedWardsCount = 0;
        var fromWardCode = "21211";
        var fromWardName = "Phường Tân Thới Nhất";
        var toWardCode = "20101";
        var toWardName = "Phường Bến Nghé";

        // 1. Fetch Provinces
        try
        {
            using var provReq = new HttpRequestMessage(HttpMethod.Get, $"{verifiedBaseUrl}/master-data/province");
            provReq.Headers.TryAddWithoutValidation("Token", apiToken);
            using var provResp = await http.SendAsync(provReq, ct);
            if (provResp.IsSuccessStatusCode)
            {
                using var provDoc = JsonDocument.Parse(await provResp.Content.ReadAsStringAsync(ct));
                if (provDoc.RootElement.TryGetProperty("data", out var provData) && provData.ValueKind == JsonValueKind.Array)
                {
                    syncedProvincesCount = provData.GetArrayLength();
                }
            }
        }
        catch
        {
            // Keep default count if master-data call times out
        }

        // 2. Fetch Districts for TP. Hồ Chí Minh (province_id = 202)
        try
        {
            using var distReq = new HttpRequestMessage(HttpMethod.Post, $"{verifiedBaseUrl}/master-data/district");
            distReq.Headers.TryAddWithoutValidation("Token", apiToken);
            distReq.Content = new StringContent("{\"province_id\":202}", Encoding.UTF8, "application/json");
            using var distResp = await http.SendAsync(distReq, ct);
            if (distResp.IsSuccessStatusCode)
            {
                using var distDoc = JsonDocument.Parse(await distResp.Content.ReadAsStringAsync(ct));
                if (distDoc.RootElement.TryGetProperty("data", out var distData) && distData.ValueKind == JsonValueKind.Array)
                {
                    syncedDistrictsCount = distData.GetArrayLength();
                }
            }
        }
        catch
        {
            // Keep default count
        }

        // 3. Fetch Wards for Quận 12 (district_id = 1454)
        var (w1454Code, w1454Name, w1454Count) = await FetchFirstWardAsync(http, verifiedBaseUrl, apiToken, 1454, ct);
        if (!string.IsNullOrWhiteSpace(w1454Code))
        {
            fromWardCode = w1454Code;
            fromWardName = w1454Name;
            syncedWardsCount += w1454Count;
        }

        // 4. Fetch Wards for Quận 1 (district_id = 1442)
        var (w1442Code, w1442Name, w1442Count) = await FetchFirstWardAsync(http, verifiedBaseUrl, apiToken, 1442, ct);
        if (!string.IsNullOrWhiteSpace(w1442Code))
        {
            toWardCode = w1442Code;
            toWardName = w1442Name;
            syncedWardsCount += w1442Count;
        }

        return new GhnVerificationSyncResult(
            Succeeded: true,
            Environment: verifiedEnv,
            GatewayBaseUrl: verifiedBaseUrl,
            ShopId: shopId,
            ShopName: shopName,
            ShopPhone: shopPhone,
            FromProvinceId: 202,
            FromProvinceName: "Hồ Chí Minh",
            FromDistrictId: 1454,
            FromDistrictName: "Quận 12",
            FromWardCode: fromWardCode,
            FromWardName: fromWardName,
            DefaultToDistrictId: 1442,
            DefaultToWardCode: toWardCode,
            DefaultToWardName: toWardName,
            SyncedProvincesCount: Math.Max(1, syncedProvincesCount),
            SyncedDistrictsCount: Math.Max(1, syncedDistrictsCount),
            SyncedWardsCount: Math.Max(1, syncedWardsCount));
    }

    public static async Task<(string WardCode, string WardName, int Count)> FetchFirstWardAsync(
        HttpClient http,
        string baseUrl,
        string apiToken,
        int districtId,
        CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/master-data/ward?district_id={districtId}");
            req.Headers.TryAddWithoutValidation("Token", apiToken);
            req.Content = new StringContent(JsonSerializer.Serialize(new { district_id = districtId }), Encoding.UTF8, "application/json");
            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return (string.Empty, string.Empty, 0);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                dataEl.ValueKind == JsonValueKind.Array &&
                dataEl.GetArrayLength() > 0)
            {
                var count = dataEl.GetArrayLength();
                var first = dataEl[0];
                var code = first.TryGetProperty("WardCode", out var wc) ? (wc.GetString() ?? string.Empty).Trim() : string.Empty;
                var name = first.TryGetProperty("WardName", out var wn) ? (wn.GetString() ?? string.Empty).Trim() : string.Empty;
                return (code, name, count);
            }
        }
        catch
        {
            // Fallback handled by caller
        }

        return (string.Empty, string.Empty, 0);
    }

    public static async Task<GhnFeeCalculationResult> CalculateShippingFeeAsync(
        HttpClient http,
        string apiToken,
        string shopId,
        string baseUrl,
        int fromDistrictId,
        string? fromWardCode,
        int toDistrictId,
        string? toWardCode,
        int weightGrams,
        int lengthCm,
        int widthCm,
        int heightCm,
        int insuranceValue,
        int serviceTypeId,
        CancellationToken ct)
    {
        var effFromDistrict = fromDistrictId > 0 ? fromDistrictId : 1454;
        var effToDistrict = toDistrictId > 0 ? toDistrictId : 1442;
        var effFromWard = fromWardCode?.Trim();
        if (string.IsNullOrWhiteSpace(effFromWard))
        {
            var (wc, _, _) = await FetchFirstWardAsync(http, baseUrl, apiToken, effFromDistrict, ct);
            effFromWard = !string.IsNullOrWhiteSpace(wc) ? wc : "21211";
        }

        var effToWard = toWardCode?.Trim();
        if (string.IsNullOrWhiteSpace(effToWard))
        {
            var (wc, _, _) = await FetchFirstWardAsync(http, baseUrl, apiToken, effToDistrict, ct);
            effToWard = !string.IsNullOrWhiteSpace(wc) ? wc : "20101";
        }

        var effWeight = Math.Clamp(weightGrams > 0 ? weightGrams : 1000, 100, 30000);
        var effLength = Math.Clamp(lengthCm > 0 ? lengthCm : 20, 1, 150);
        var effWidth = Math.Clamp(widthCm > 0 ? widthCm : 15, 1, 150);
        var effHeight = Math.Clamp(heightCm > 0 ? heightCm : 10, 1, 150);
        var effInsurance = Math.Clamp(insuranceValue, 0, 5_000_000);
        var effServiceType = serviceTypeId > 0 ? serviceTypeId : 2;

        var payload = new
        {
            from_district_id = effFromDistrict,
            from_ward_code = effFromWard,
            service_type_id = effServiceType,
            to_district_id = effToDistrict,
            to_ward_code = effToWard,
            height = effHeight,
            length = effLength,
            weight = effWeight,
            width = effWidth,
            insurance_value = effInsurance,
            cod_failed_amount = 0,
            coupon = (string?)null
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v2/shipping-order/fee");
            req.Headers.TryAddWithoutValidation("Token", apiToken);
            req.Headers.TryAddWithoutValidation("ShopId", string.IsNullOrWhiteSpace(shopId) ? "235512" : shopId.Trim());
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var resp = await http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new GhnFeeCalculationResult(
                    false, 0m, 0m, 0m, effFromDistrict, effFromWard, effToDistrict, effToWard, effWeight, baseUrl,
                    $"GHN HTTP {(int)resp.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Object)
            {
                var total = dataEl.TryGetProperty("total", out var tEl) ? tEl.GetDecimal() : 0m;
                var serviceFee = dataEl.TryGetProperty("service_fee", out var sfEl) ? sfEl.GetDecimal() : total;
                var insFee = dataEl.TryGetProperty("insurance_fee", out var ifEl) ? ifEl.GetDecimal() : 0m;

                return new GhnFeeCalculationResult(
                    true, total, serviceFee, insFee, effFromDistrict, effFromWard, effToDistrict, effToWard, effWeight, baseUrl);
            }
        }
        catch (Exception ex)
        {
            return new GhnFeeCalculationResult(
                false, 0m, 0m, 0m, effFromDistrict, effFromWard, effToDistrict, effToWard, effWeight, baseUrl, ex.Message);
        }

        return new GhnFeeCalculationResult(
            false, 0m, 0m, 0m, effFromDistrict, effFromWard, effToDistrict, effToWard, effWeight, baseUrl,
            "Không đọc được phản hồi tính phí từ GHN.");
    }
}
