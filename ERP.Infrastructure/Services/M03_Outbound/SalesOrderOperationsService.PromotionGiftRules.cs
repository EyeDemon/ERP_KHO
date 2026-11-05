using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService
{
    public async Task<WmsEndpointOutcome> GetPromotionGiftRulesAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId.HasValue && _warehouseAccess != null)
        {
            await _warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }

        var dbRules = await _db.PromotionGiftRules.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);
        var giftProdIds = dbRules.Select(r => r.GiftProductId).Distinct().ToList();
        var giftProds = giftProdIds.Count > 0
            ? await _db.Products.AsNoTracking()
                .Where(p => giftProdIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (p.Code, p.Name), cancellationToken)
            : new Dictionary<int, (string Code, string Name)>();

        var mapped = dbRules.Select(r => new
        {
            id = r.Id,
            ruleCode = r.RuleCode,
            ruleName = r.RuleName,
            minOrderAmount = r.MinOrderAmount,
            minOrderTotalAmount = r.MinOrderAmount,
            minOrderValue = r.MinOrderAmount,
            minOrderValueVnd = r.MinOrderAmount,
            triggerProductId = r.TriggerProductId,
            minTriggerQuantity = r.MinTriggerQuantity,
            giftProductId = r.GiftProductId,
            giftProductCode = giftProds.TryGetValue(r.GiftProductId, out var gp) ? gp.Code : $"SKU-{r.GiftProductId}",
            giftProductName = giftProds.TryGetValue(r.GiftProductId, out var gpName) ? gpName.Name : $"Quà tặng #{r.GiftProductId}",
            giftQuantity = r.GiftQuantity,
            giftUnitPrice = r.GiftUnitPrice,
            autoSkipWhenOutOfStock = r.AutoSkipWhenOutOfStock,
            isActive = r.IsActive
        }).ToList();

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId,
            totalCount = mapped.Count,
            items = mapped,
            rules = mapped,
            statusLabel = "Đã tải quy tắc tự động đính kèm quà tặng khuyến mãi (0 ₫)"
        });
    }

    public async Task<WmsEndpointOutcome> CreatePromotionGiftRuleAsync(
        CreatePromotionGiftRuleInput? request,
        CancellationToken cancellationToken = default)
    {
        var giftProductId = request?.GiftProductId ?? 0;
        if (giftProductId <= 0)
        {
            giftProductId = await _db.Products.AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (giftProductId <= 0)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "GIFT_PRODUCT_REQUIRED",
                errorCode = "GIFT_PRODUCT_REQUIRED",
                message = "Phải chỉ định sản phẩm quà tặng hợp lệ (GiftProductId)."
            });
        }

        var now = DateTime.UtcNow;
        var minOrderAmount = request?.MinOrderTotalAmount ?? request?.MinOrderAmount ?? request?.MinOrderValue ?? 0m;
        var giftQty = request?.GiftQuantity is > 0m ? request.GiftQuantity.Value : 1m;
        var ruleCode = !string.IsNullOrWhiteSpace(request?.RuleCode)
            ? request.RuleCode.Trim().ToUpperInvariant()
            : $"GIFT-{now:yyMMddHHmmss}";
        var ruleName = !string.IsNullOrWhiteSpace(request?.RuleName)
            ? request.RuleName.Trim()
            : $"Quà tặng khuyến mãi 0 ₫ ({ruleCode})";

        var rule = new PromotionGiftRule
        {
            RuleCode = ruleCode,
            RuleName = ruleName,
            MinOrderAmount = Math.Max(0m, minOrderAmount),
            TriggerProductId = request?.TriggerProductId,
            MinTriggerQuantity = Math.Max(1m, request?.MinTriggerQuantity ?? 1m),
            GiftProductId = giftProductId,
            GiftQuantity = giftQty,
            GiftUnitPrice = 0m,
            AutoSkipWhenOutOfStock = request?.AutoSkipWhenOutOfStock ?? true,
            IsActive = request?.IsActive ?? true,
            CreatedAtUtc = now
        };

        _db.PromotionGiftRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken);

        var giftProd = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == giftProductId, cancellationToken);
        return WmsEndpointOutcome.Ok(new
        {
            id = rule.Id,
            ruleCode = rule.RuleCode,
            ruleName = rule.RuleName,
            minOrderAmount = rule.MinOrderAmount,
            minOrderTotalAmount = rule.MinOrderAmount,
            minOrderValue = rule.MinOrderAmount,
            minOrderValueVnd = rule.MinOrderAmount,
            triggerProductId = rule.TriggerProductId,
            minTriggerQuantity = rule.MinTriggerQuantity,
            giftProductId = rule.GiftProductId,
            giftProductCode = giftProd?.Code ?? $"SKU-{rule.GiftProductId}",
            giftProductName = giftProd?.Name ?? $"Quà tặng #{rule.GiftProductId}",
            giftQuantity = rule.GiftQuantity,
            giftUnitPrice = rule.GiftUnitPrice,
            autoSkipWhenOutOfStock = rule.AutoSkipWhenOutOfStock,
            isActive = rule.IsActive
        });
    }

    public static async Task EnsurePromotionGiftRulesSeededAsync(ErpKhoDbContext dbContext, CancellationToken ct)
    {
        if (await dbContext.PromotionGiftRules.AnyAsync(ct))
            return;

        var defaultGiftProductId = await dbContext.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.Id)
            .Select(p => p.Id)
            .FirstOrDefaultAsync(ct);
        if (defaultGiftProductId <= 0)
            return;

        var now = DateTime.UtcNow;
        dbContext.PromotionGiftRules.AddRange(
            new PromotionGiftRule
            {
                RuleCode = "GIFT-ORDER-500K",
                RuleName = "Đơn từ 500.000 ₫ tặng 1 Túi Canvas / Sample Mini",
                MinOrderAmount = 500000m,
                GiftProductId = defaultGiftProductId,
                GiftQuantity = 1m,
                GiftUnitPrice = 0m,
                AutoSkipWhenOutOfStock = true,
                IsActive = true,
                CreatedAtUtc = now
            },
            new PromotionGiftRule
            {
                RuleCode = "GIFT-BUY2-GET1",
                RuleName = "Mua từ 2 sản phẩm chính tặng 1 quà kèm theo",
                MinOrderAmount = 0m,
                MinTriggerQuantity = 2m,
                GiftProductId = defaultGiftProductId,
                GiftQuantity = 1m,
                GiftUnitPrice = 0m,
                AutoSkipWhenOutOfStock = true,
                IsActive = true,
                CreatedAtUtc = now
            });
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<WmsEndpointOutcome> EvaluatePromotionGiftRulesAsync(
        EvaluateGiftRulesInput? request,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId ?? 1;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (effectiveWhAccess != null)
        {
            if (request?.WarehouseId.HasValue == true)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
            }
            else if (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin))
            {
                var accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                {
                    return WmsEndpointOutcome.Forbid();
                }
                warehouseId = accessibleIds.First();
            }
        }

        var giftQty = request?.GiftQuantity is > 0m ? request.GiftQuantity.Value : 1m;
        var giftProductId = request?.GiftProductId ?? 0;
        decimal availableGiftQty = 0m;

        if (giftProductId <= 0)
        {
            var firstRule = await _db.PromotionGiftRules.AsNoTracking()
                .Where(r => r.IsActive)
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (firstRule != null)
            {
                giftProductId = firstRule.GiftProductId;
                if (request?.GiftQuantity is null or <= 0m)
                {
                    giftQty = firstRule.GiftQuantity;
                }
            }
        }

        if (giftProductId <= 0)
        {
            giftProductId = await _db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Id).Select(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        }

        if (giftProductId > 0)
        {
            var giftStocks = await _db.InventoryStocks
                .AsNoTracking()
                .Where(s => s.WarehouseId == warehouseId &&
                            s.ProductId == giftProductId &&
                            s.Status == InventoryStatus.Available)
                .ToListAsync(cancellationToken);
            availableGiftQty = giftStocks.Sum(x => Math.Max(0m, x.Quantity - Math.Max(x.ReservedQuantity, x.AllocatedQuantity) - x.LockedQuantity));
        }

        var giftAttached = availableGiftQty >= giftQty && giftProductId > 0;
        var giftSkippedDueToZeroStock = !giftAttached;

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId,
            salesOrderId = request?.SalesOrderId,
            giftProductId,
            requestedGiftQuantity = giftQty,
            availableGiftQty,
            giftAttached,
            giftSkippedDueToZeroStock,
            mainOrderBlocked = false,
            giftLine = giftAttached
                ? new
                {
                    productId = giftProductId,
                    quantity = giftQty,
                    unitPrice = 0m,
                    isPromotionalGift = true,
                    note = "Quà tặng khuyến mãi 0 ₫ (Tự động đính kèm)"
                }
                : null,
            statusLabel = giftAttached
                ? "Đã tự động đính kèm dòng quà tặng 0 ₫ vào đơn hàng"
                : "Tồn kho quà tặng đã hết (0) — Tự động bỏ qua quà tặng để không làm kẹt đơn hàng chính"
        });
    }
}
