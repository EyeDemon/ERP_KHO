using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ERP.Application.Interfaces;

namespace ERP.Infrastructure.Services.Marketplaces;

public static class MarketplaceWebhookSignatureVerifier
{
    public static string ComputeHmacSha256Hex(string rawPayload, string webhookSecret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(webhookSecret ?? string.Empty);
        var payloadBytes = Encoding.UTF8.GetBytes(rawPayload ?? string.Empty);
        var hashBytes = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static bool VerifyHmacSha256(string rawPayload, string? providedSignature, string webhookSecret)
    {
        if (string.IsNullOrWhiteSpace(providedSignature) || string.IsNullOrWhiteSpace(webhookSecret))
        {
            return false;
        }

        var cleanedSig = providedSignature.Trim();
        if (cleanedSig.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            cleanedSig = cleanedSig["sha256=".Length..].Trim();
        }

        var keyBytes = Encoding.UTF8.GetBytes(webhookSecret);
        var payloadBytes = Encoding.UTF8.GetBytes(rawPayload ?? string.Empty);
        var computedHashBytes = HMACSHA256.HashData(keyBytes, payloadBytes);

        if (cleanedSig.Length == computedHashBytes.Length * 2)
        {
            try
            {
                var providedHashBytes = Convert.FromHexString(cleanedSig);
                return CryptographicOperations.FixedTimeEquals(computedHashBytes, providedHashBytes);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        var computedHexBytes = Encoding.UTF8.GetBytes(Convert.ToHexString(computedHashBytes).ToLowerInvariant());
        var providedHexBytes = Encoding.UTF8.GetBytes(cleanedSig.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(computedHexBytes, providedHexBytes);
    }

    internal static string ComputeTokenDigest(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash)[..24];
    }
}

public sealed class ShopeeChannelAdapter : IMarketplaceChannelAdapter
{
    public string ChannelCode => "SHOPEE";
    public string ChannelDisplayName => "Shopee Open Platform v2";

    public MarketplaceOAuthTokenResult ExchangeAuthCode(string shopCode, string authCode, DateTime nowUtc)
    {
        var normShop = string.IsNullOrWhiteSpace(shopCode) ? "SHOPEE-DEFAULT" : shopCode.Trim().ToUpperInvariant();
        var normCode = string.IsNullOrWhiteSpace(authCode) ? "AUTH-CODE-SHOPEE" : authCode.Trim();
        var accessDigest = MarketplaceWebhookSignatureVerifier.ComputeTokenDigest($"SHOPEE:AT:{normShop}:{normCode}:{nowUtc.Ticks}");
        var refreshDigest = MarketplaceWebhookSignatureVerifier.ComputeTokenDigest($"SHOPEE:RT:{normShop}:{normCode}:{nowUtc.Ticks}");

        return new MarketplaceOAuthTokenResult(
            ChannelCode: ChannelCode,
            ShopCode: normShop,
            EncryptedAccessToken: $"ENC_SHOPEE_AT_{normShop}_{accessDigest}",
            EncryptedRefreshToken: $"ENC_SHOPEE_RT_{normShop}_{refreshDigest}",
            TokenExpiresAtUtc: nowUtc.AddHours(4),
            OpenPlatformPartnerId: "SHOPEE_OPEN_V2_PARTNER_2026",
            Scope: "shop.inventory.write,shop.order.read,shop.fulfillment.write");
    }

    public MarketplaceOAuthTokenResult RefreshAccessToken(string shopCode, string currentRefreshToken, DateTime nowUtc)
    {
        var normShop = string.IsNullOrWhiteSpace(shopCode) ? "SHOPEE-DEFAULT" : shopCode.Trim().ToUpperInvariant();
        var normRefresh = string.IsNullOrWhiteSpace(currentRefreshToken) ? "REFRESH-SHOPEE" : currentRefreshToken.Trim();
        var accessDigest = MarketplaceWebhookSignatureVerifier.ComputeTokenDigest($"SHOPEE:REFRESH_AT:{normShop}:{normRefresh}:{nowUtc.Ticks}");
        var refreshDigest = MarketplaceWebhookSignatureVerifier.ComputeTokenDigest($"SHOPEE:REFRESH_RT:{normShop}:{normRefresh}:{nowUtc.Ticks}");

        return new MarketplaceOAuthTokenResult(
            ChannelCode: ChannelCode,
            ShopCode: normShop,
            EncryptedAccessToken: $"ENC_SHOPEE_AT_{normShop}_{accessDigest}",
            EncryptedRefreshToken: $"ENC_SHOPEE_RT_{normShop}_{refreshDigest}",
            TokenExpiresAtUtc: nowUtc.AddHours(4),
            OpenPlatformPartnerId: "SHOPEE_OPEN_V2_PARTNER_2026",
            Scope: "shop.inventory.write,shop.order.read,shop.fulfillment.write");
    }

    public MarketplaceInventoryPushEnvelope BuildInventoryPushEnvelope(
        int shopId,
        string shopCode,
        string channelItemId,
        string channelSkuCode,
        int internalProductId,
        decimal atpBaseQty,
        decimal publishedQty,
        decimal safetyBufferQty,
        decimal reserveRatioPct,
        DateTime nowUtc)
    {
        var normItemId = string.IsNullOrWhiteSpace(channelItemId) ? $"SP-ITEM-{internalProductId}" : channelItemId.Trim();
        var normSku = string.IsNullOrWhiteSpace(channelSkuCode) ? $"SKU-{internalProductId}" : channelSkuCode.Trim();
        var sellerStockQty = (int)Math.Max(0m, Math.Floor(publishedQty));

        var payloadJson = JsonSerializer.Serialize(new
        {
            shopId,
            ChannelCode,
            ShopCode = shopCode,
            ChannelItemId = normItemId,
            ChannelSkuCode = normSku,
            InternalProductId = internalProductId,
            atpBaseQty,
            pushedQuantity = publishedQty,
            ReserveRatioPct = reserveRatioPct,
            SafetyBufferQty = safetyBufferQty,
            openPlatformEndpoint = "/api/v2/product/update_stock",
            shopeeStockPayload = new
            {
                item_id = normItemId,
                stock_list = new[]
                {
                    new
                    {
                        model_sku = normSku,
                        seller_stock = new[]
                        {
                            new { location_id = "VN-WH-MAIN", stock = sellerStockQty }
                        }
                    }
                }
            },
            syncedAtUtc = nowUtc
        });

        return new MarketplaceInventoryPushEnvelope(
            ChannelCode: ChannelCode,
            ShopCode: shopCode,
            ChannelItemId: normItemId,
            ChannelSkuCode: normSku,
            InternalProductId: internalProductId,
            AtpBaseQty: atpBaseQty,
            PublishedQty: publishedQty,
            SafetyBufferQty: safetyBufferQty,
            ChannelRatioPct: reserveRatioPct,
            PayloadJson: payloadJson);
    }

    public bool VerifyWebhookHmacSignature(string rawPayload, string? providedSignature, string webhookSecret) =>
        MarketplaceWebhookSignatureVerifier.VerifyHmacSha256(rawPayload, providedSignature, webhookSecret);
}

public sealed class TikTokShopChannelAdapter : IMarketplaceChannelAdapter
{
    public string ChannelCode => "TIKTOK_SHOP";
    public string ChannelDisplayName => "TikTok Shop Partner API v2025";

    public MarketplaceOAuthTokenResult ExchangeAuthCode(string shopCode, string authCode, DateTime nowUtc)
    {
        var normShop = string.IsNullOrWhiteSpace(shopCode) ? "TIKTOK-DEFAULT" : shopCode.Trim().ToUpperInvariant();
        var normCode = string.IsNullOrWhiteSpace(authCode) ? "AUTH-CODE-TTS" : authCode.Trim();
        var accessDigest = MarketplaceWebhookSignatureVerifier.ComputeTokenDigest($"TTS:AT:{normShop}:{normCode}:{nowUtc.Ticks}");
        var refreshDigest = MarketplaceWebhookSignatureVerifier.ComputeTokenDigest($"TTS:RT:{normShop}:{normCode}:{nowUtc.Ticks}");

        return new MarketplaceOAuthTokenResult(
            ChannelCode: ChannelCode,
            ShopCode: normShop,
            EncryptedAccessToken: $"ENC_TTS_AT_{normShop}_{accessDigest}",
            EncryptedRefreshToken: $"ENC_TTS_RT_{normShop}_{refreshDigest}",
            TokenExpiresAtUtc: nowUtc.AddDays(7),
            OpenPlatformPartnerId: "TTS_PARTNER_V2025_APP",
            Scope: "product.inventory.write,order.info.read,fulfillment.package.write");
    }

    public MarketplaceOAuthTokenResult RefreshAccessToken(string shopCode, string currentRefreshToken, DateTime nowUtc)
    {
        var normShop = string.IsNullOrWhiteSpace(shopCode) ? "TIKTOK-DEFAULT" : shopCode.Trim().ToUpperInvariant();
        var normRefresh = string.IsNullOrWhiteSpace(currentRefreshToken) ? "REFRESH-TTS" : currentRefreshToken.Trim();
        var accessDigest = MarketplaceWebhookSignatureVerifier.ComputeTokenDigest($"TTS:REFRESH_AT:{normShop}:{normRefresh}:{nowUtc.Ticks}");
        var refreshDigest = MarketplaceWebhookSignatureVerifier.ComputeTokenDigest($"TTS:REFRESH_RT:{normShop}:{normRefresh}:{nowUtc.Ticks}");

        return new MarketplaceOAuthTokenResult(
            ChannelCode: ChannelCode,
            ShopCode: normShop,
            EncryptedAccessToken: $"ENC_TTS_AT_{normShop}_{accessDigest}",
            EncryptedRefreshToken: $"ENC_TTS_RT_{normShop}_{refreshDigest}",
            TokenExpiresAtUtc: nowUtc.AddDays(7),
            OpenPlatformPartnerId: "TTS_PARTNER_V2025_APP",
            Scope: "product.inventory.write,order.info.read,fulfillment.package.write");
    }

    public MarketplaceInventoryPushEnvelope BuildInventoryPushEnvelope(
        int shopId,
        string shopCode,
        string channelItemId,
        string channelSkuCode,
        int internalProductId,
        decimal atpBaseQty,
        decimal publishedQty,
        decimal safetyBufferQty,
        decimal reserveRatioPct,
        DateTime nowUtc)
    {
        var normItemId = string.IsNullOrWhiteSpace(channelItemId) ? $"TTS-ITEM-{internalProductId}" : channelItemId.Trim();
        var normSku = string.IsNullOrWhiteSpace(channelSkuCode) ? $"SKU-{internalProductId}" : channelSkuCode.Trim();
        var availableStockQty = (int)Math.Max(0m, Math.Floor(publishedQty));

        var payloadJson = JsonSerializer.Serialize(new
        {
            shopId,
            ChannelCode,
            ShopCode = shopCode,
            ChannelItemId = normItemId,
            ChannelSkuCode = normSku,
            InternalProductId = internalProductId,
            atpBaseQty,
            pushedQuantity = publishedQty,
            ReserveRatioPct = reserveRatioPct,
            SafetyBufferQty = safetyBufferQty,
            openPlatformEndpoint = "/product/202502/inventory/update",
            tikTokInventoryPayload = new
            {
                product_id = normItemId,
                skus = new[]
                {
                    new
                    {
                        seller_sku = normSku,
                        inventory = new[]
                        {
                            new { warehouse_id = "TTS-VN-WH01", available_stock = availableStockQty }
                        }
                    }
                }
            },
            syncedAtUtc = nowUtc
        });

        return new MarketplaceInventoryPushEnvelope(
            ChannelCode: ChannelCode,
            ShopCode: shopCode,
            ChannelItemId: normItemId,
            ChannelSkuCode: normSku,
            InternalProductId: internalProductId,
            AtpBaseQty: atpBaseQty,
            PublishedQty: publishedQty,
            SafetyBufferQty: safetyBufferQty,
            ChannelRatioPct: reserveRatioPct,
            PayloadJson: payloadJson);
    }

    public bool VerifyWebhookHmacSignature(string rawPayload, string? providedSignature, string webhookSecret) =>
        MarketplaceWebhookSignatureVerifier.VerifyHmacSha256(rawPayload, providedSignature, webhookSecret);
}

public sealed class MarketplaceChannelRegistry : IMarketplaceChannelRegistry
{
    public static readonly MarketplaceChannelRegistry Default = new();

    private readonly ShopeeChannelAdapter _shopee = new();
    private readonly TikTokShopChannelAdapter _tikTok = new();

    public IMarketplaceChannelAdapter ResolveAdapter(string? channelCode)
    {
        var normalized = channelCode?.Trim().ToUpperInvariant();
        return normalized is "TIKTOK" or "TIKTOK_SHOP" or "TTS"
            ? _tikTok
            : _shopee;
    }

    public IReadOnlyList<IMarketplaceChannelAdapter> GetAllAdapters() => [_shopee, _tikTok];
}
