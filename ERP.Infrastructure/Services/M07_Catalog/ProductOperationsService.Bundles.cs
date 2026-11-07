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
    public async Task<WmsEndpointOutcome> GetBundleComponentsAsync(
        int id,
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var components = await _db.ProductBundleComponents
            .AsNoTracking()
            .Include(b => b.ComponentProduct)
            .Where(b => b.ParentProductId == id && b.IsActive)
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var componentIds = components.Select(c => c.ComponentProductId).Distinct().ToList();
        var stockQuery = _db.InventoryStocks
            .AsNoTracking()
            .Include(s => s.Location)
            .Where(s => componentIds.Contains(s.ProductId) &&
                        s.Status == InventoryStatus.Available &&
                        (s.Location == null || s.Location.LocationType != WarehouseLocationType.Receiving));

        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(warehouseId, cancellationToken);
        if (scopedWarehouseIds != null)
        {
            stockQuery = stockQuery.Where(s => scopedWarehouseIds.Contains(s.WarehouseId));
        }

        var stocks = await stockQuery.ToListAsync(cancellationToken);

        var mapped = components.Select(c =>
        {
            var atp = stocks
                .Where(s => s.ProductId == c.ComponentProductId)
                .Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
            var reqQty = c.ComponentQuantity > 0m ? c.ComponentQuantity : 1m;
            var maxBundles = Math.Floor(atp / reqQty);
            return new
            {
                id = c.Id,
                parentProductId = c.ParentProductId,
                componentProductId = c.ComponentProductId,
                childProductId = c.ComponentProductId,
                componentProductCode = c.ComponentProduct?.Code ?? $"SKU-{c.ComponentProductId}",
                childProductCode = c.ComponentProduct?.Code ?? $"SKU-{c.ComponentProductId}",
                componentProductName = c.ComponentProduct?.Name ?? $"Thành phần #{c.ComponentProductId}",
                childProductName = c.ComponentProduct?.Name ?? $"Thành phần #{c.ComponentProductId}",
                componentQuantity = c.ComponentQuantity,
                quantity = c.ComponentQuantity,
                costAllocationRatioPct = 100m,
                componentAvailableQty = atp,
                maxBundlesFromComponent = maxBundles,
                isActive = c.IsActive
            };
        }).ToList();

        var availableBundleQty = mapped.Count > 0
            ? mapped.Min(x => x.maxBundlesFromComponent)
            : 0m;

        return WmsEndpointOutcome.Ok(new
        {
            parentProductId = id,
            availableBundleQty,
            totalComponents = mapped.Count,
            items = mapped,
            components = mapped
        });
    }

    public async Task<WmsEndpointOutcome> UpsertBundleComponentAsync(
        int id,
        UpsertBundleComponentInput? request,
        CancellationToken cancellationToken = default)
    {
        var inputList = new List<(int ComponentProductId, decimal ComponentQuantity, decimal CostAllocationRatioPct)>();
        if (request?.Components is { Count: > 0 })
        {
            foreach (var comp in request.Components)
            {
                var compId = comp.ComponentProductId ?? comp.ChildProductId ?? 0;
                var compQty = comp.ComponentQuantity ?? comp.Quantity ?? 0m;
                var ratio = comp.CostAllocationRatioPct ?? 100m;
                inputList.Add((compId, compQty, ratio));
            }
        }
        else
        {
            var compId = request?.ComponentProductId ?? request?.ChildProductId ?? 0;
            var compQty = request?.ComponentQuantity ?? request?.Quantity ?? 0m;
            var ratio = request?.CostAllocationRatioPct ?? 100m;
            inputList.Add((compId, compQty, ratio));
        }

        foreach (var (compId, compQty, _) in inputList)
        {
            if (compId <= 0)
            {
                return WmsEndpointOutcome.BadRequest(new
                {
                    code = "COMPONENT_PRODUCT_REQUIRED",
                    errorCode = "COMPONENT_PRODUCT_REQUIRED",
                    message = "Phải chỉ định mã sản phẩm thành phần (ComponentProductId)."
                });
            }

            if (compId == id)
            {
                return WmsEndpointOutcome.BadRequest(new
                {
                    code = "BUNDLE_SELF_REFERENCE_INVALID",
                    errorCode = "BUNDLE_SELF_REFERENCE_INVALID",
                    message = "Sản phẩm Combo/Kit không được tự tham chiếu chính nó làm thành phần."
                });
            }

            if (compQty <= 0m)
            {
                return WmsEndpointOutcome.BadRequest(new
                {
                    code = "INVALID_COMPONENT_QUANTITY",
                    errorCode = "INVALID_COMPONENT_QUANTITY",
                    message = "Định lượng thành phần Combo phải lớn hơn 0."
                });
            }
        }

        var now = DateTime.UtcNow;
        ProductBundleComponent? lastSaved = null;
        foreach (var (compId, compQty, _) in inputList)
        {
            var existing = await _db.ProductBundleComponents
                .FirstOrDefaultAsync(b => b.ParentProductId == id && b.ComponentProductId == compId, cancellationToken);

            if (existing != null)
            {
                existing.ComponentQuantity = compQty;
                existing.IsActive = true;
                lastSaved = existing;
            }
            else
            {
                existing = new ProductBundleComponent
                {
                    ParentProductId = id,
                    ComponentProductId = compId,
                    ComponentQuantity = compQty,
                    IsActive = true,
                    CreatedAt = now
                };
                _db.ProductBundleComponents.Add(existing);
                lastSaved = existing;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        var allComponents = await _db.ProductBundleComponents
            .AsNoTracking()
            .Include(b => b.ComponentProduct)
            .Where(b => b.ParentProductId == id && b.IsActive)
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var mapped = allComponents.Select(c => new
        {
            id = c.Id,
            parentProductId = c.ParentProductId,
            componentProductId = c.ComponentProductId,
            childProductId = c.ComponentProductId,
            componentProductCode = c.ComponentProduct?.Code ?? $"SKU-{c.ComponentProductId}",
            childProductCode = c.ComponentProduct?.Code ?? $"SKU-{c.ComponentProductId}",
            componentProductName = c.ComponentProduct?.Name ?? $"Thành phần #{c.ComponentProductId}",
            childProductName = c.ComponentProduct?.Name ?? $"Thành phần #{c.ComponentProductId}",
            componentQuantity = c.ComponentQuantity,
            quantity = c.ComponentQuantity,
            costAllocationRatioPct = 100m,
            isActive = c.IsActive
        }).ToList();

        var primary = mapped.LastOrDefault(m => m.componentProductId == lastSaved?.ComponentProductId) ?? mapped.LastOrDefault();

        return WmsEndpointOutcome.Ok(new
        {
            id = primary?.id ?? lastSaved?.Id ?? 0,
            parentProductId = id,
            componentProductId = primary?.componentProductId ?? lastSaved?.ComponentProductId ?? 0,
            childProductId = primary?.childProductId ?? lastSaved?.ComponentProductId ?? 0,
            componentProductCode = primary?.componentProductCode,
            childProductCode = primary?.childProductCode,
            componentProductName = primary?.componentProductName,
            childProductName = primary?.childProductName,
            componentQuantity = primary?.componentQuantity ?? lastSaved?.ComponentQuantity ?? 1m,
            quantity = primary?.quantity ?? lastSaved?.ComponentQuantity ?? 1m,
            costAllocationRatioPct = primary?.costAllocationRatioPct ?? 100m,
            items = mapped,
            components = mapped,
            statusLabel = "Đã lưu định mức cấu phần Combo / Kit"
        });
    }

    public async Task<WmsEndpointOutcome> GetChannelSkuMappingsAsync(
        int? productId,
        int? shopId,
        string? channelCode,
        CancellationToken cancellationToken = default)
    {
        var mappingsQuery = _db.ChannelSkuMappings
            .AsNoTracking()
            .Include(m => m.Shop)
            .Include(m => m.InternalProduct)
            .AsQueryable();

        if (productId.HasValue && productId.Value > 0)
        {
            mappingsQuery = mappingsQuery.Where(m => m.InternalProductId == productId.Value);
        }

        if (shopId.HasValue && shopId.Value > 0)
        {
            mappingsQuery = mappingsQuery.Where(m => m.ShopId == shopId.Value);
        }

        if (!string.IsNullOrWhiteSpace(channelCode))
        {
            var normChannel = channelCode.Trim().ToUpperInvariant();
            mappingsQuery = mappingsQuery.Where(m => m.Shop != null && m.Shop.ChannelCode == normChannel);
        }

        var rawMappings = await mappingsQuery
            .OrderByDescending(m => m.Id)
            .ToListAsync(cancellationToken);

        var shops = await _db.MarketplaceShops
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(null, cancellationToken);
        var productIds = rawMappings.Select(m => m.InternalProductId).Distinct().ToList();
        var stocksQuery = _db.InventoryStocks
            .AsNoTracking()
            .Include(s => s.Location)
            .Where(s => productIds.Contains(s.ProductId) &&
                        s.Status == InventoryStatus.Available &&
                        (s.Location == null || s.Location.LocationType != WarehouseLocationType.Receiving));
        if (scopedWarehouseIds != null)
        {
            stocksQuery = stocksQuery.Where(s => scopedWarehouseIds.Contains(s.WarehouseId));
        }
        var stocks = productIds.Count > 0
            ? await stocksQuery.ToListAsync(cancellationToken)
            : [];

        var atpByProduct = stocks
            .GroupBy(s => s.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)));

        var items = rawMappings.Select(m =>
        {
            var atp = atpByProduct.TryGetValue(m.InternalProductId, out var qty) ? qty : 0m;
            var factor = m.ConversionFactor > 0m ? m.ConversionFactor : 1m;
            var maxSellable = Math.Floor(atp / factor);
            return new
            {
                mappingId = m.Id,
                id = m.Id,
                shopId = m.ShopId,
                channelCode = m.Shop?.ChannelCode ?? "SHOPEE",
                shopCode = m.Shop?.ShopCode ?? $"SHOP-{m.ShopId}",
                shopName = m.Shop?.ShopName ?? $"Gian hàng #{m.ShopId}",
                channelSkuCode = m.ChannelSkuCode,
                channelItemId = m.ChannelItemId,
                internalProductId = m.InternalProductId,
                internalProductCode = m.InternalProduct?.Code ?? $"SKU-{m.InternalProductId}",
                internalProductName = m.InternalProduct?.Name ?? $"Sản phẩm #{m.InternalProductId}",
                conversionFactor = factor,
                internalAvailableQty = atp,
                maxChannelSellableQty = maxSellable,
                lastPushedQty = m.LastPushedQty,
                lastPushedAtUtc = m.LastPushedAtUtc,
                isActive = m.IsActive
            };
        }).ToList();

        var storeList = shops.Select(s => new
        {
            shopId = s.Id,
            s.ChannelCode,
            s.ShopCode,
            s.ShopName,
            s.DefaultWarehouseId,
            s.ReserveRatioPct,
            s.SafetyBufferQty,
            s.IsActive
        }).ToList();

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = items.Count,
            items,
            mappings = items,
            stores = storeList
        });
    }
}
