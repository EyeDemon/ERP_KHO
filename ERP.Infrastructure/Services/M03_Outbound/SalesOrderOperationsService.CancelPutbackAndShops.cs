using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService
{
    public async Task<WmsEndpointOutcome> ConfirmCancelPutbackAsync(
        int id,
        ConfirmCancelPutbackInput? request,
        int? actorUserId,
        ISalesOrderBackorderService backorderService,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var order = await _db.SalesOrders
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order == null)
        {
            return WmsEndpointOutcome.Json(404, new { code = "NOT_FOUND", message = $"Không tìm thấy đơn bán hàng #{id}." });
        }

        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(order.WarehouseId, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var resolvedActorId = actorUserId is > 0 ? actorUserId.Value : 1;

        var activeAllocations = await _db.StockAllocations
            .Include(a => a.Reservation)
            .Where(a => a.Reservation != null &&
                        a.Reservation.SourceType == "SalesOrder" &&
                        a.Reservation.SourceId == order.Id &&
                        (a.Status == StockAllocationStatus.Active ||
                         a.Status == StockAllocationStatus.Picked))
            .ToListAsync(cancellationToken);

        foreach (var alloc in activeAllocations)
        {
            var remaining = alloc.Quantity;
            if (remaining > 0m)
            {
                var bucket = await _db.InventoryStocks.FirstOrDefaultAsync(
                    s => s.WarehouseId == alloc.WarehouseId &&
                         s.ProductId == alloc.ProductId &&
                         s.LocationId == alloc.LocationId &&
                         s.LotId == alloc.LotId &&
                         s.SerialId == alloc.SerialId &&
                         s.Status == alloc.InventoryStatus,
                    cancellationToken);
                if (bucket != null)
                {
                    bucket.AllocatedQuantity = Math.Max(0m, bucket.AllocatedQuantity - remaining);
                    bucket.ReservedQuantity = Math.Max(bucket.AllocatedQuantity, bucket.ReservedQuantity - remaining);
                    bucket.LastUpdated = now;
                }
            }
            alloc.Status = StockAllocationStatus.Released;
            alloc.ReleasedBy = resolvedActorId;
            alloc.ReleasedAt = now;
            alloc.ReleaseReason = request?.Note ?? "Hoàn tất cất trả hàng đã soạn khi hủy đơn bán hàng";
            if (alloc.Reservation != null)
            {
                alloc.Reservation.Status = StockReservationStatus.Released;
                alloc.Reservation.ReleasedAt = now;
                alloc.Reservation.ReleasedBy = resolvedActorId;
                alloc.Reservation.ReleaseReason = alloc.ReleaseReason;
            }
        }

        order.Status = SalesOrderStatus.Cancelled;
        order.CancellationReason ??= request?.Note ?? "Hoàn tất cất trả hàng đã soạn (Cancel Putback)";
        order.CancelledAt ??= now;
        foreach (var line in order.Lines)
        {
            line.CancelledQuantity = line.OrderedQuantity;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = resolvedActorId,
            WarehouseId = order.WarehouseId,
            Action = "SalesOrder.ConfirmCancelPutback",
            EntityName = "SalesOrder",
            EntityId = order.Id,
            NewValues = order.Status.ToString(),
            Reason = request?.Note ?? "Xác nhận hoàn tất cất trả hàng đã soạn về kệ lưu trữ và hủy đơn bán hàng",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        return WmsEndpointOutcome.Ok(await backorderService.GetSalesOrderAsync(id, cancellationToken));
    }

    public async Task<WmsEndpointOutcome> GetMarketplaceShopsAsync(
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else if (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin))
            {
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        var query = _db.MarketplaceShops
            .AsNoTracking()
            .Include(s => s.SkuMappings)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(s => s.DefaultWarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            query = query.Where(s => accessibleIds.Contains(s.DefaultWarehouseId));
        }

        var shops = await query.OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return WmsEndpointOutcome.Ok(new
        {
            totalCount = shops.Count,
            items = shops.Select(s => new
            {
                s.Id,
                s.ChannelCode,
                s.ShopCode,
                s.ShopName,
                s.DefaultWarehouseId,
                s.ReserveRatioPct,
                safetyStockBufferPct = Math.Max(0m, 100m - s.ReserveRatioPct),
                s.SafetyBufferQty,
                s.LowStockThreshold,
                s.IsPrimaryShop,
                maskedAccessToken = MaskMarketplaceSecret(s.EncryptedAccessToken),
                s.TokenExpiresAtUtc,
                s.IsActive,
                skuMappingCount = s.SkuMappings.Count,
                skuMappings = s.SkuMappings.Select(m => new
                {
                    m.Id,
                    m.ChannelItemId,
                    m.ChannelVariationId,
                    m.ChannelSkuCode,
                    m.InternalProductId,
                    m.ConversionFactor,
                    m.LastPushedQty,
                    m.LastPushedAtUtc
                })
            }),
            statusLabel = "Đã tải danh sách gian hàng đa sàn & cấu hình tỷ lệ đẩy tồn chống bán lố"
        });
    }

    public async Task<WmsEndpointOutcome> ConnectMarketplaceShopAsync(
        ConnectMarketplaceShopInput? request,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var channelCode = string.IsNullOrWhiteSpace(request?.ChannelCode) ? "SHOPEE" : request.ChannelCode.Trim().ToUpperInvariant();
        var shopCode = string.IsNullOrWhiteSpace(request?.ShopCode) ? $"SHOP-{channelCode}-01" : request.ShopCode.Trim().ToUpperInvariant();
        var shopName = string.IsNullOrWhiteSpace(request?.ShopName) ? $"Gian hàng {channelCode} ({shopCode})" : request.ShopName.Trim();
        var warehouseId = request?.DefaultWarehouseId ?? 1;

        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
        }

        var reserveRatioPct = request?.ReserveRatioPct
            ?? (request?.SafetyStockBufferPct.HasValue == true ? Math.Clamp(100m - request.SafetyStockBufferPct.Value, 0m, 100m) : 100m);
        var safetyBufferQty = request?.SafetyBufferQty ?? 0m;
        var lowStockThreshold = request?.LowStockThreshold ?? 5m;
        var isPrimaryShop = request?.IsPrimaryShop ?? true;
        var now = DateTime.UtcNow;

        var shop = await _db.MarketplaceShops
            .Include(s => s.SkuMappings)
            .FirstOrDefaultAsync(s => s.ChannelCode == channelCode && s.ShopCode == shopCode, cancellationToken);

        if (shop == null)
        {
            var rawAccessToken = !string.IsNullOrWhiteSpace(request?.AccessToken)
                ? request.AccessToken.Trim()
                : $"TOK_{Guid.NewGuid():N}";
            var rawRefreshToken = !string.IsNullOrWhiteSpace(request?.RefreshToken)
                ? request.RefreshToken.Trim()
                : $"REF_{Guid.NewGuid():N}";

            shop = new MarketplaceShop
            {
                ChannelCode = channelCode,
                ShopCode = shopCode,
                ShopName = shopName,
                DefaultWarehouseId = warehouseId,
                ReserveRatioPct = reserveRatioPct,
                SafetyBufferQty = safetyBufferQty,
                LowStockThreshold = lowStockThreshold,
                IsPrimaryShop = isPrimaryShop,
                EncryptedAccessToken = EncryptMarketplaceSecret(rawAccessToken),
                EncryptedRefreshToken = EncryptMarketplaceSecret(rawRefreshToken),
                TokenExpiresAtUtc = now.AddDays(30),
                WebhookSecret = null,
                IsActive = true,
                CreatedAtUtc = now
            };
            _db.MarketplaceShops.Add(shop);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            shop.ShopName = shopName;
            shop.DefaultWarehouseId = warehouseId;
            shop.ReserveRatioPct = reserveRatioPct;
            shop.SafetyBufferQty = safetyBufferQty;
            shop.LowStockThreshold = lowStockThreshold;
            shop.IsPrimaryShop = isPrimaryShop;
            if (!string.IsNullOrWhiteSpace(request?.AccessToken))
            {
                shop.EncryptedAccessToken = EncryptMarketplaceSecret(request.AccessToken.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(shop.EncryptedAccessToken))
            {
                shop.EncryptedAccessToken = EncryptMarketplaceSecret(shop.EncryptedAccessToken);
            }

            if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
            {
                shop.EncryptedRefreshToken = EncryptMarketplaceSecret(request.RefreshToken.Trim());
            }
            else if (!string.IsNullOrWhiteSpace(shop.EncryptedRefreshToken))
            {
                shop.EncryptedRefreshToken = EncryptMarketplaceSecret(shop.EncryptedRefreshToken);
            }

            shop.TokenExpiresAtUtc = now.AddDays(30);
            shop.IsActive = true;
        }

        if (request?.InternalProductId is > 0)
        {
            var skuCode = string.IsNullOrWhiteSpace(request.ChannelSkuCode)
                ? $"CH-SKU-{request.InternalProductId.Value}"
                : request.ChannelSkuCode.Trim();
            var existingMap = shop.SkuMappings.FirstOrDefault(m => m.ChannelSkuCode == skuCode);
            if (existingMap == null)
            {
                _db.ChannelSkuMappings.Add(new ChannelSkuMapping
                {
                    ShopId = shop.Id,
                    ChannelItemId = $"ITEM-{request.InternalProductId.Value}",
                    ChannelSkuCode = skuCode,
                    InternalProductId = request.InternalProductId.Value,
                    ConversionFactor = request.ConversionFactor is > 0m ? request.ConversionFactor.Value : 1m,
                    IsActive = true
                });
            }
            else
            {
                existingMap.InternalProductId = request.InternalProductId.Value;
                existingMap.ConversionFactor = request.ConversionFactor is > 0m ? request.ConversionFactor.Value : 1m;
                existingMap.IsActive = true;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return WmsEndpointOutcome.Ok(new
        {
            shop.Id,
            shop.ChannelCode,
            shop.ShopCode,
            shop.ShopName,
            shop.DefaultWarehouseId,
            shop.ReserveRatioPct,
            safetyStockBufferPct = Math.Max(0m, 100m - shop.ReserveRatioPct),
            shop.SafetyBufferQty,
            shop.LowStockThreshold,
            shop.IsPrimaryShop,
            maskedAccessToken = MaskMarketplaceSecret(shop.EncryptedAccessToken),
            encryptedAccessToken = MaskMarketplaceSecret(shop.EncryptedAccessToken),
            encryptedRefreshToken = MaskMarketplaceSecret(shop.EncryptedRefreshToken),
            shop.TokenExpiresAtUtc,
            statusLabel = $"Đã kết nối & cấu hình gian hàng {shop.ChannelCode} ({shop.ShopCode})"
        });
    }
}
