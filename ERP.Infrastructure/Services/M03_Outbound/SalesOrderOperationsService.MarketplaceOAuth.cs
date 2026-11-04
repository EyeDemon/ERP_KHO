using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService
{
    public async Task<WmsEndpointOutcome> ExchangeMarketplaceOAuthAsync(
        MarketplaceOAuthExchangeInput? request,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var rawChannel = string.IsNullOrWhiteSpace(request?.ChannelCode) ? "SHOPEE" : request.ChannelCode.Trim().ToUpperInvariant();
        var adapter = _channelRegistry.ResolveAdapter(rawChannel);
        var channelCode = rawChannel;
        var shopCode = string.IsNullOrWhiteSpace(request?.ShopCode)
            ? $"SHOP-{channelCode}-01"
            : request.ShopCode.Trim().ToUpperInvariant();
        var shopName = string.IsNullOrWhiteSpace(request?.ShopName)
            ? $"Gian hàng {adapter.ChannelDisplayName} ({shopCode})"
            : request.ShopName.Trim();
        var authCode = string.IsNullOrWhiteSpace(request?.AuthCode)
            ? $"OAUTH-CODE-{channelCode}-{Guid.NewGuid():N}"[..28]
            : request.AuthCode.Trim();
        var warehouseId = request?.DefaultWarehouseId ?? 1;

        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var tokenResult = adapter.ExchangeAuthCode(shopCode, authCode, now);
        var encryptedAccess = EncryptMarketplaceSecret(tokenResult.EncryptedAccessToken);
        var encryptedRefresh = EncryptMarketplaceSecret(tokenResult.EncryptedRefreshToken);
        var webhookSecret = !string.IsNullOrWhiteSpace(request?.WebhookSecret)
            ? request.WebhookSecret.Trim()
            : null;

        var shop = await _db.MarketplaceShops
            .FirstOrDefaultAsync(
                s => (s.ChannelCode == channelCode || s.ChannelCode == adapter.ChannelCode) && s.ShopCode == shopCode,
                cancellationToken);

        if (shop == null)
        {
            shop = new MarketplaceShop
            {
                ChannelCode = channelCode,
                ShopCode = shopCode,
                ShopName = shopName,
                DefaultWarehouseId = warehouseId,
                ReserveRatioPct = 100m,
                SafetyBufferQty = 0m,
                LowStockThreshold = 5m,
                IsPrimaryShop = true,
                EncryptedAccessToken = encryptedAccess,
                EncryptedRefreshToken = encryptedRefresh,
                TokenExpiresAtUtc = tokenResult.TokenExpiresAtUtc,
                WebhookSecret = webhookSecret,
                IsActive = true,
                CreatedAtUtc = now
            };
            _db.MarketplaceShops.Add(shop);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request?.ShopName))
            {
                shop.ShopName = shopName;
            }
            if (request?.DefaultWarehouseId.HasValue == true)
            {
                shop.DefaultWarehouseId = warehouseId;
            }
            shop.EncryptedAccessToken = encryptedAccess;
            shop.EncryptedRefreshToken = encryptedRefresh;
            shop.TokenExpiresAtUtc = tokenResult.TokenExpiresAtUtc;
            if (!string.IsNullOrWhiteSpace(webhookSecret))
            {
                shop.WebhookSecret = webhookSecret;
            }
            shop.IsActive = true;
        }

        _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
        {
            EventId = $"MP-OAUTH-{shop.Id}-{now.Ticks}",
            EventType = "MARKETPLACE_OAUTH_CONNECTED",
            AggregateType = "MarketplaceShop",
            AggregateId = shop.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                shopId = shop.Id,
                shop.ChannelCode,
                adapterChannelCode = adapter.ChannelCode,
                adapter.ChannelDisplayName,
                shop.ShopCode,
                shop.ShopName,
                shop.DefaultWarehouseId,
                tokenResult.OpenPlatformPartnerId,
                tokenResult.Scope,
                tokenResult.TokenExpiresAtUtc,
                connectedAtUtc = now
            }),
            Status = "PENDING",
            CreatedAt = now
        });

        _db.AuditLogs.Add(new AuditLog
        {
            WarehouseId = shop.DefaultWarehouseId,
            Action = "MarketplaceShop.OAuthExchange",
            EntityName = "MarketplaceShop",
            EntityId = shop.Id,
            NewValues = tokenResult.OpenPlatformPartnerId,
            Reason = $"Trao đổi mã ủy quyền OAuth 2.0 thành công cho gian hàng {adapter.ChannelDisplayName} ({shop.ShopCode})",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            id = shop.Id,
            shopId = shop.Id,
            channelCode = shop.ChannelCode,
            adapterChannelCode = adapter.ChannelCode,
            channelDisplayName = adapter.ChannelDisplayName,
            shopCode = shop.ShopCode,
            shopName = shop.ShopName,
            defaultWarehouseId = shop.DefaultWarehouseId,
            maskedAccessToken = MaskMarketplaceSecret(shop.EncryptedAccessToken),
            encryptedAccessToken = MaskMarketplaceSecret(shop.EncryptedAccessToken),
            encryptedRefreshToken = MaskMarketplaceSecret(shop.EncryptedRefreshToken),
            tokenExpiresAtUtc = shop.TokenExpiresAtUtc,
            openPlatformPartnerId = tokenResult.OpenPlatformPartnerId,
            scope = tokenResult.Scope,
            webhookHmacEnabled = !string.IsNullOrWhiteSpace(shop.WebhookSecret)
                || HasConfiguredMarketplaceWebhookSecret(shop.ChannelCode, adapter.ChannelCode),
            oauthConnected = true,
            outboxEventType = "MARKETPLACE_OAUTH_CONNECTED",
            statusLabel = $"Đã xác thực OAuth 2.0 & lưu khóa bảo mật gian hàng {adapter.ChannelDisplayName} ({shop.ShopCode})"
        });
    }

    public async Task<WmsEndpointOutcome> RefreshMarketplaceOAuthTokenAsync(
        int shopId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var shop = await _db.MarketplaceShops.FirstOrDefaultAsync(s => s.Id == shopId, cancellationToken);
        if (shop == null)
        {
            return WmsEndpointOutcome.Json(404, new
            {
                code = "MARKETPLACE_SHOP_NOT_FOUND",
                errorCode = "MARKETPLACE_SHOP_NOT_FOUND",
                shopId,
                message = $"Không tìm thấy gian hàng TMĐT #{shopId}."
            });
        }

        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(shop.DefaultWarehouseId, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var adapter = _channelRegistry.ResolveAdapter(shop.ChannelCode);
        var previousExpiresAtUtc = shop.TokenExpiresAtUtc;
        var currentRefreshToken = DecryptMarketplaceSecret(shop.EncryptedRefreshToken);
        var refreshed = adapter.RefreshAccessToken(shop.ShopCode, currentRefreshToken, now);

        shop.EncryptedAccessToken = EncryptMarketplaceSecret(refreshed.EncryptedAccessToken);
        shop.EncryptedRefreshToken = EncryptMarketplaceSecret(refreshed.EncryptedRefreshToken);
        shop.TokenExpiresAtUtc = refreshed.TokenExpiresAtUtc;
        shop.IsActive = true;

        _db.AuditLogs.Add(new AuditLog
        {
            WarehouseId = shop.DefaultWarehouseId,
            Action = "MarketplaceShop.OAuthRefresh",
            EntityName = "MarketplaceShop",
            EntityId = shop.Id,
            OldValues = previousExpiresAtUtc?.ToString("O"),
            NewValues = refreshed.TokenExpiresAtUtc.ToString("O"),
            Reason = $"Gia hạn tự động OAuth 2.0 Access/Refresh Token cho gian hàng {adapter.ChannelDisplayName} ({shop.ShopCode})",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            id = shop.Id,
            shopId = shop.Id,
            channelCode = shop.ChannelCode,
            adapterChannelCode = adapter.ChannelCode,
            channelDisplayName = adapter.ChannelDisplayName,
            shopCode = shop.ShopCode,
            shopName = shop.ShopName,
            defaultWarehouseId = shop.DefaultWarehouseId,
            maskedAccessToken = MaskMarketplaceSecret(shop.EncryptedAccessToken),
            encryptedAccessToken = MaskMarketplaceSecret(shop.EncryptedAccessToken),
            encryptedRefreshToken = MaskMarketplaceSecret(shop.EncryptedRefreshToken),
            previousExpiresAtUtc,
            tokenExpiresAtUtc = shop.TokenExpiresAtUtc,
            openPlatformPartnerId = refreshed.OpenPlatformPartnerId,
            scope = refreshed.Scope,
            tokenRotated = true,
            statusLabel = $"Đã làm mới (Refresh) cặp khóa OAuth 2.0 cho gian hàng {adapter.ChannelDisplayName} ({shop.ShopCode})"
        });
    }
}
