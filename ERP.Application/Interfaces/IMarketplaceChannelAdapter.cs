namespace ERP.Application.Interfaces;

public record MarketplaceOAuthExchangeInput(
    string? ChannelCode = null,
    string? ShopCode = null,
    string? ShopName = null,
    string? AuthCode = null,
    int? DefaultWarehouseId = null,
    string? WebhookSecret = null);

public record MarketplaceOAuthTokenResult(
    string ChannelCode,
    string ShopCode,
    string EncryptedAccessToken,
    string EncryptedRefreshToken,
    DateTime TokenExpiresAtUtc,
    string OpenPlatformPartnerId,
    string Scope);

public record MarketplaceInventoryPushEnvelope(
    string ChannelCode,
    string ShopCode,
    string ChannelItemId,
    string ChannelSkuCode,
    int InternalProductId,
    decimal AtpBaseQty,
    decimal PublishedQty,
    decimal SafetyBufferQty,
    decimal ChannelRatioPct,
    string PayloadJson);

public interface IMarketplaceChannelAdapter
{
    string ChannelCode { get; }
    string ChannelDisplayName { get; }

    MarketplaceOAuthTokenResult ExchangeAuthCode(string shopCode, string authCode, DateTime nowUtc);

    MarketplaceOAuthTokenResult RefreshAccessToken(string shopCode, string currentRefreshToken, DateTime nowUtc);

    MarketplaceInventoryPushEnvelope BuildInventoryPushEnvelope(
        int shopId,
        string shopCode,
        string channelItemId,
        string channelSkuCode,
        int internalProductId,
        decimal atpBaseQty,
        decimal publishedQty,
        decimal safetyBufferQty,
        decimal reserveRatioPct,
        DateTime nowUtc);

    bool VerifyWebhookHmacSignature(string rawPayload, string? providedSignature, string webhookSecret);
}

public interface IMarketplaceChannelRegistry
{
    IMarketplaceChannelAdapter ResolveAdapter(string? channelCode);
    IReadOnlyList<IMarketplaceChannelAdapter> GetAllAdapters();
}
