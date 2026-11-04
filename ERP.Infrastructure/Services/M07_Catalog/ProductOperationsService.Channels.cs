using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ProductOperationsService
{
    public async Task<WmsEndpointOutcome> UpsertChannelSkuMappingAsync(
        UpsertChannelSkuMappingInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var channelSkuCode = request?.ChannelSkuCode?.Trim();
        if (string.IsNullOrWhiteSpace(channelSkuCode))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "CHANNEL_SKU_CODE_REQUIRED",
                errorCode = "CHANNEL_SKU_CODE_REQUIRED",
                message = "Mã SKU trên sàn TMĐT (ChannelSkuCode) không được để trống."
            });
        }

        var conversionFactor = request?.ConversionFactor ?? 1m;
        if (conversionFactor <= 0m)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "INVALID_CONVERSION_FACTOR",
                errorCode = "INVALID_CONVERSION_FACTOR",
                message = "Hệ số quy đổi (ConversionFactor) phải lớn hơn 0."
            });
        }

        var parsedInternalProductId = ParseOptionalInt(request?.InternalProductId) ?? request?.ProductId;
        Product? internalProduct = null;
        if (parsedInternalProductId is > 0)
        {
            internalProduct = await _db.Products
                .FirstOrDefaultAsync(p => p.Id == parsedInternalProductId.Value, cancellationToken);
        }

        if (internalProduct == null && !string.IsNullOrWhiteSpace(request?.InternalProductCode))
        {
            var pCode = request.InternalProductCode.Trim();
            internalProduct = await _db.Products
                .FirstOrDefaultAsync(p => p.Code == pCode, cancellationToken);
        }

        if (internalProduct == null && request?.InternalProductId.HasValue == true &&
            request.InternalProductId.Value.ValueKind == JsonValueKind.String)
        {
            var rawStr = request.InternalProductId.Value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(rawStr))
            {
                internalProduct = await _db.Products.FirstOrDefaultAsync(p => p.Code == rawStr, cancellationToken);
                if (internalProduct == null && Guid.TryParse(rawStr, out _))
                {
                    internalProduct = await _db.Products.OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
                }
            }
        }

        if (internalProduct == null)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "INTERNAL_PRODUCT_NOT_FOUND",
                errorCode = "INTERNAL_PRODUCT_NOT_FOUND",
                message = "Không tìm thấy sản phẩm nội bộ (InternalProductId) để ánh xạ."
            });
        }

        var now = DateTime.UtcNow;
        var parsedShopId = ParseOptionalInt(request?.ShopId);
        var normalizedChannel = string.IsNullOrWhiteSpace(request?.ChannelCode)
            ? "SHOPEE"
            : request.ChannelCode.Trim().ToUpperInvariant();

        MarketplaceShop? shop = null;
        if (parsedShopId is > 0)
        {
            shop = await _db.MarketplaceShops.FirstOrDefaultAsync(s => s.Id == parsedShopId.Value, cancellationToken);
        }

        if (shop == null)
        {
            shop = await _db.MarketplaceShops
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync(s => s.ChannelCode == normalizedChannel, cancellationToken);
        }

        if (shop == null)
        {
            var defaultWarehouseId = await _db.Warehouses
                .AsNoTracking()
                .Where(w => w.IsActive)
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (defaultWarehouseId <= 0)
            {
                defaultWarehouseId = 1;
            }

            shop = new MarketplaceShop
            {
                ChannelCode = normalizedChannel,
                ShopCode = $"SHOP-{normalizedChannel}-01",
                ShopName = !string.IsNullOrWhiteSpace(request?.ShopName)
                    ? request.ShopName.Trim()
                    : $"Gian hàng {normalizedChannel} Chính thức",
                DefaultWarehouseId = defaultWarehouseId,
                ReserveRatioPct = 100m,
                SafetyBufferQty = 0m,
                LowStockThreshold = 5m,
                IsPrimaryShop = true,
                EncryptedAccessToken = $"ENC_TOK_{Guid.NewGuid():N}",
                EncryptedRefreshToken = $"ENC_REF_{Guid.NewGuid():N}",
                TokenExpiresAtUtc = now.AddDays(30),
                IsActive = true,
                CreatedAtUtc = now
            };
            _db.MarketplaceShops.Add(shop);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var existingMapping = await _db.ChannelSkuMappings
            .FirstOrDefaultAsync(
                m => m.ShopId == shop.Id && m.ChannelSkuCode == channelSkuCode,
                cancellationToken);

        var isActive = request?.IsActive ?? true;
        var resolvedItemId = !string.IsNullOrWhiteSpace(request?.ChannelItemId)
            ? request.ChannelItemId.Trim()
            : (existingMapping?.ChannelItemId ?? $"ITEM-{internalProduct.Id}");

        if (existingMapping != null)
        {
            existingMapping.InternalProductId = internalProduct.Id;
            existingMapping.ChannelItemId = resolvedItemId;
            existingMapping.ConversionFactor = conversionFactor;
            existingMapping.IsActive = isActive;
        }
        else
        {
            existingMapping = new ChannelSkuMapping
            {
                ShopId = shop.Id,
                ChannelItemId = resolvedItemId,
                ChannelSkuCode = channelSkuCode,
                InternalProductId = internalProduct.Id,
                ConversionFactor = conversionFactor,
                IsActive = isActive
            };
            _db.ChannelSkuMappings.Add(existingMapping);
        }

        await _db.SaveChangesAsync(cancellationToken);

        int? validAuditUserId = null;
        if (actorUserId is > 0 &&
            await _db.Users.AsNoTracking().AnyAsync(u => u.Id == actorUserId.Value, cancellationToken))
        {
            validAuditUserId = actorUserId.Value;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = shop.DefaultWarehouseId,
            EntityName = "ChannelSkuMapping",
            EntityId = existingMapping.Id,
            Action = "UPSERT_CHANNEL_SKU_MAPPING",
            NewValues = JsonSerializer.Serialize(new
            {
                mappingId = existingMapping.Id,
                shopId = shop.Id,
                channelCode = shop.ChannelCode,
                channelSkuCode = existingMapping.ChannelSkuCode,
                internalProductId = internalProduct.Id,
                internalProductCode = internalProduct.Code,
                conversionFactor = existingMapping.ConversionFactor,
                isActive = existingMapping.IsActive
            }),
            Reason = $"Ánh xạ SKU sàn {shop.ChannelCode} '{channelSkuCode}' -> SKU nội bộ '{internalProduct.Code}' (x{conversionFactor:0.##})",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            mappingId = existingMapping.Id,
            id = existingMapping.Id,
            shopId = shop.Id,
            channelCode = shop.ChannelCode,
            shopCode = shop.ShopCode,
            shopName = shop.ShopName,
            channelSkuCode = existingMapping.ChannelSkuCode,
            channelItemId = existingMapping.ChannelItemId,
            internalProductId = internalProduct.Id,
            internalProductCode = internalProduct.Code,
            internalProductName = internalProduct.Name,
            conversionFactor = existingMapping.ConversionFactor,
            isActive = existingMapping.IsActive,
            statusLabel = $"Đã lưu ánh xạ SKU sàn {shop.ChannelCode} ({channelSkuCode} -> {internalProduct.Code})"
        });
    }
}
