using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace ERP.Infrastructure.Services.Carriers;

public sealed record GhnVerificationSyncResult(
    bool Succeeded,
    string Environment,
    string GatewayBaseUrl,
    int ShopId,
    string ShopName,
    string ShopPhone,
    int FromProvinceId,
    string FromProvinceName,
    int FromDistrictId,
    string FromDistrictName,
    string FromWardCode,
    string FromWardName,
    int DefaultToDistrictId,
    string DefaultToWardCode,
    string DefaultToWardName,
    int SyncedProvincesCount,
    int SyncedDistrictsCount,
    int SyncedWardsCount,
    string? ErrorMessage = null);

public sealed record GhnFeeCalculationResult(
    bool Succeeded,
    decimal TotalFeeVnd,
    decimal ServiceFeeVnd,
    decimal InsuranceFeeVnd,
    int FromDistrictId,
    string FromWardCode,
    int ToDistrictId,
    string ToWardCode,
    int WeightGrams,
    string GatewayBaseUrl,
    string? ErrorMessage = null);

public sealed record GhnCreateOrderResult(
    bool Succeeded,
    string OrderCode,
    decimal TotalFeeVnd,
    string? SortCode,
    string? ExpectedDeliveryTime,
    string RawResponseJson,
    string? ErrorMessage = null);

public sealed record GhnOrderDetailResult(
    bool Succeeded,
    string OrderCode,
    string RawStatus,
    decimal TotalFeeVnd,
    decimal CodAmountVnd,
    string? LeadtimeIso,
    string? UpdatedDateIso,
    string RawResponseJson,
    string? ErrorMessage = null);

public sealed record GhnCancelOrderResult(
    bool Succeeded,
    string OrderCode,
    bool Cancelled,
    string? Message,
    string RawResponseJson,
    string? ErrorMessage = null);

public static partial class GhnLiveGatewayHelper
{
    public const string DevSandboxBaseUrl = "https://dev-online-gateway.ghn.vn/shiip/public-api";
    public const string ProductionBaseUrl = "https://online-gateway.ghn.vn/shiip/public-api";

    private static byte[]? TryResolveEncryptionKeyBytes(IConfiguration? configuration)
    {
        var keyMaterial = configuration?["Carriers:TokenEncryptionKey"]
            ?? configuration?["JwtSettings:Secret"]
            ?? configuration?["Jwt:Key"]
            ?? Environment.GetEnvironmentVariable("ERP_CARRIER_TOKEN_KEY");

        if (string.IsNullOrWhiteSpace(keyMaterial))
            return null;

        return SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial.Trim()));
    }

    private static byte[] ResolveEncryptionKeyBytes(IConfiguration? configuration)
    {
        return TryResolveEncryptionKeyBytes(configuration)
            ?? throw new InvalidOperationException("Carrier token encryption key is not configured.");
    }

    public static string EncryptToken(string? rawToken, IConfiguration? configuration = null)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return string.Empty;

        var trimmed = rawToken.Trim();
        if (trimmed.StartsWith("ENC:v1:", StringComparison.Ordinal) ||
            trimmed.StartsWith("SANDBOX_TOKEN_", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var keyBytes = ResolveEncryptionKeyBytes(configuration);
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(trimmed);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var combined = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);
        return $"ENC:v1:{Convert.ToBase64String(combined)}";
    }

    public static string DecryptToken(string? stored, IConfiguration? configuration = null)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return string.Empty;

        var trimmed = stored.Trim();
        if (!trimmed.StartsWith("ENC:v1:", StringComparison.Ordinal))
            return trimmed;

        var keyBytes = ResolveEncryptionKeyBytes(configuration);
        try
        {
            var base64 = trimmed["ENC:v1:".Length..];
            var combined = Convert.FromBase64String(base64);
            if (combined.Length <= 16)
                return string.Empty;

            using var aes = Aes.Create();
            aes.Key = keyBytes;
            var iv = new byte[16];
            Buffer.BlockCopy(combined, 0, iv, 0, 16);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            var plainBytes = decryptor.TransformFinalBlock(combined, 16, combined.Length - 16);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return string.Empty;
        }
    }

    public static string? MaskApiToken(string? rawOrEncryptedToken, IConfiguration? configuration = null)
    {
        if (string.IsNullOrWhiteSpace(rawOrEncryptedToken))
            return null;

        var trimmed = rawOrEncryptedToken.Trim();
        if (trimmed.StartsWith("SANDBOX_TOKEN_", StringComparison.OrdinalIgnoreCase))
            return null;

        if (trimmed.StartsWith("ENC:v1:", StringComparison.Ordinal))
        {
            if (TryResolveEncryptionKeyBytes(configuration) == null)
                return "ENC:****";

            var decrypted = DecryptToken(trimmed, configuration);
            if (string.IsNullOrWhiteSpace(decrypted))
                return "ENC:****";

            if (decrypted.StartsWith("SANDBOX_TOKEN_", StringComparison.OrdinalIgnoreCase))
                return null;

            return decrypted.Length < 8 ? "****" : $"{decrypted[..4]}****{decrypted[^4..]}";
        }

        return trimmed.Length < 8 ? "****" : $"{trimmed[..4]}****{trimmed[^4..]}";
    }

    public static string? ResolveConfiguredGhnToken(
        string? requestToken,
        string? dbEncryptedToken,
        IConfiguration? configuration)
    {
        static string? TryUnwrapToken(string? input, IConfiguration? cfg)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            var trimmed = input.Trim();
            string candidate;
            if (trimmed.StartsWith("ENC:v1:", StringComparison.Ordinal))
            {
                if (TryResolveEncryptionKeyBytes(cfg) == null)
                    return null;
                candidate = DecryptToken(trimmed, cfg);
            }
            else
            {
                candidate = trimmed;
            }

            if (string.IsNullOrWhiteSpace(candidate) ||
                candidate.Contains("****", StringComparison.Ordinal) ||
                candidate.StartsWith("SANDBOX_TOKEN_", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return candidate;
        }

        var fromRequest = TryUnwrapToken(requestToken, configuration);
        if (!string.IsNullOrWhiteSpace(fromRequest))
            return fromRequest;

        var configToken = configuration?["Carriers:GHN:ApiToken"]
            ?? Environment.GetEnvironmentVariable("GHN_API_TOKEN");
        var fromConfig = TryUnwrapToken(configToken, configuration);
        if (!string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig;

        return TryUnwrapToken(dbEncryptedToken, configuration);
    }

    public static HttpClient CreateHttpClient(IHttpClientFactory? httpClientFactory)
    {
        var client = httpClientFactory != null ? httpClientFactory.CreateClient("GHN") : new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(12);
        return client;
    }
}
