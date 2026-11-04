using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService
{
    public async Task<WmsEndpointOutcome> SyncMarketplaceInventoryAsync(
        SyncMarketplaceInventoryInput? request,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
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

        if (request?.ShopId.HasValue == true)
        {
            var requestedShop = await _db.MarketplaceShops
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.ShopId.Value && s.IsActive, cancellationToken);
            if (requestedShop == null)
            {
                return WmsEndpointOutcome.Json(404, new
                {
                    code = "MARKETPLACE_SHOP_NOT_FOUND",
                    errorCode = "MARKETPLACE_SHOP_NOT_FOUND",
                    shopId = request.ShopId.Value,
                    message = $"Không tìm thấy gian hàng TMĐT #{request.ShopId.Value}."
                });
            }

            if (effectiveWhAccess != null)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(requestedShop.DefaultWarehouseId, cancellationToken);
            }
        }

        var shopsQuery = _db.MarketplaceShops
            .Include(s => s.SkuMappings)
            .Where(s => s.IsActive);
        if (request?.ShopId.HasValue == true)
        {
            shopsQuery = shopsQuery.Where(s => s.Id == request.ShopId.Value);
        }
        if (warehouseId.HasValue)
        {
            shopsQuery = shopsQuery.Where(s => s.DefaultWarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            shopsQuery = shopsQuery.Where(s => accessibleIds.Contains(s.DefaultWarehouseId));
        }

        var shops = await shopsQuery.OrderBy(s => s.Id).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var today = DateTime.UtcNow.Date;
        var stocksQuery = _db.InventoryStocks
            .AsNoTracking()
            .Include(s => s.Location)
            .Include(s => s.Lot)
            .Where(s => s.Status == InventoryStatus.Available &&
                        (s.Location == null || (s.Location.IsActive && !s.Location.IsBlocked && s.Location.IsPickable && s.Location.LocationType != WarehouseLocationType.Receiving)) &&
                        (s.Lot == null || !s.Lot.ExpiryDate.HasValue || s.Lot.ExpiryDate.Value >= today));
        if (warehouseId.HasValue)
        {
            stocksQuery = stocksQuery.Where(s => s.WarehouseId == warehouseId.Value);
        }
        else if (accessibleIds != null)
        {
            stocksQuery = stocksQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
        }
        if (request?.ProductId.HasValue == true)
        {
            stocksQuery = stocksQuery.Where(s => s.ProductId == request.ProductId.Value);
        }

        var stocks = await stocksQuery.ToListAsync(cancellationToken);
        var atpByWhAndProduct = stocks
            .GroupBy(s => (s.WarehouseId, s.ProductId))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => Math.Max(0m, x.Quantity - Math.Max(x.ReservedQuantity, x.AllocatedQuantity) - x.LockedQuantity)));

        var anyMappingsInDb = await _db.ChannelSkuMappings.AnyAsync(cancellationToken);
        var pushedResults = new List<object>();

        foreach (var shop in shops)
        {
            if (request?.SafetyStockBufferPct.HasValue == true)
            {
                shop.ReserveRatioPct = Math.Clamp(100m - request.SafetyStockBufferPct.Value, 0m, 100m);
            }

            var adapter = _channelRegistry.ResolveAdapter(shop.ChannelCode);
            var mappings = shop.SkuMappings.Where(m => m.IsActive).ToList();
            if (mappings.Count == 0 && !anyMappingsInDb && request?.ShopId.HasValue != true)
            {
                var candidateProducts = await _db.Products
                    .AsNoTracking()
                    .Where(p => p.IsActive && (!request!.ProductId.HasValue || p.Id == request.ProductId.Value))
                    .OrderBy(p => p.Id)
                    .Take(20)
                    .ToListAsync(cancellationToken);
                foreach (var p in candidateProducts)
                {
                    var m = new ChannelSkuMapping
                    {
                        ShopId = shop.Id,
                        ChannelItemId = $"SP-ITEM-{p.Id}",
                        ChannelSkuCode = p.Code,
                        InternalProductId = p.Id,
                        ConversionFactor = 1m,
                        IsActive = true
                    };
                    _db.ChannelSkuMappings.Add(m);
                    mappings.Add(m);
                }
            }

            foreach (var mapping in mappings)
            {
                if (request?.ProductId.HasValue == true && mapping.InternalProductId != request.ProductId.Value)
                {
                    continue;
                }

                var atp = atpByWhAndProduct.TryGetValue((shop.DefaultWarehouseId, mapping.InternalProductId), out var val) ? val : 0m;
                var pushedQty = shop.CalculateChannelPushQty(atp, mapping.ConversionFactor);
                mapping.LastPushedQty = pushedQty;
                mapping.LastPushedAtUtc = now;

                var envelope = adapter.BuildInventoryPushEnvelope(
                    shop.Id,
                    shop.ShopCode,
                    mapping.ChannelItemId,
                    mapping.ChannelSkuCode,
                    mapping.InternalProductId,
                    atp,
                    pushedQty,
                    shop.SafetyBufferQty,
                    shop.ReserveRatioPct,
                    now);

                var outboxEventId = $"INV-PUSH-{shop.Id}-{mapping.Id}-{mapping.InternalProductId}-{now.Ticks}-{pushedResults.Count + 1}";
                _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
                {
                    EventId = outboxEventId,
                    EventType = "Inventory.AvailableChanged",
                    AggregateType = "ChannelSkuMapping",
                    AggregateId = mapping.InternalProductId,
                    PayloadJson = envelope.PayloadJson,
                    Status = "PENDING",
                    CreatedAt = now
                });

                pushedResults.Add(new
                {
                    shopId = shop.Id,
                    channelCode = shop.ChannelCode,
                    channelDisplayName = adapter.ChannelDisplayName,
                    shopCode = shop.ShopCode,
                    isPrimaryShop = shop.IsPrimaryShop,
                    channelItemId = envelope.ChannelItemId,
                    channelSkuCode = mapping.ChannelSkuCode,
                    productId = mapping.InternalProductId,
                    atpBaseQty = atp,
                    pushedQuantity = pushedQty,
                    reserveRatioPct = shop.ReserveRatioPct,
                    safetyBufferQty = shop.SafetyBufferQty,
                    lowStockThreshold = shop.LowStockThreshold,
                    lowStockCutOffApplied = atp <= shop.LowStockThreshold && !shop.IsPrimaryShop,
                    outboxEventId
                });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            shopCount = shops.Count,
            syncedShopCount = shops.Count,
            syncedCount = pushedResults.Count,
            items = pushedResults,
            syncedAtUtc = now,
            statusLabel = $"Đã đồng bộ tồn kho đa sàn ({shops.Count} gian hàng, {pushedResults.Count} SKU) & kích hoạt cơ chế chống bán lố giờ Flash Sale"
        });
    }
}
