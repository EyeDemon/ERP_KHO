using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService
{
    public async Task<WmsEndpointOutcome> ImportMarketplaceOrdersExcelAsync(
        ImportMarketplaceOrdersExcelInput? request,
        IReadOnlyList<MarketplaceOrderExcelRowInput> parsedFileRows,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        int warehouseId;
        if (request?.WarehouseId.HasValue == true)
        {
            warehouseId = request.WarehouseId.Value;
            if (effectiveWhAccess != null)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
            }
        }
        else if (effectiveWhAccess != null && (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin)))
        {
            var accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
            if (accessibleIds.Count == 0)
            {
                return WmsEndpointOutcome.Forbid();
            }
            warehouseId = await _db.Warehouses.AsNoTracking()
                .Where(w => w.IsActive && accessibleIds.Contains(w.Id))
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (warehouseId <= 0)
            {
                warehouseId = accessibleIds.First();
            }
        }
        else
        {
            warehouseId = await _db.Warehouses.AsNoTracking()
                .Where(w => w.IsActive)
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (warehouseId <= 0) warehouseId = 1;
        }

        var tenantId = _db.CurrentTenantId;
        var tenantSub = await _db.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        if (tenantSub != null)
        {
            var quota = tenantSub.MonthlyOrderQuota > 0 ? tenantSub.MonthlyOrderQuota : 1500;
            var maxAllowed = (int)Math.Ceiling(quota * 1.15m);
            var isExpiredPastDue = string.Equals(tenantSub.Status, "PAST_DUE", StringComparison.OrdinalIgnoreCase) &&
                                   tenantSub.CurrentPeriodEndUtc.AddHours(72) < now;
            if ((tenantSub.MonthlyOrderQuota > 0 && tenantSub.CurrentMonthOrderCount >= maxAllowed) || isExpiredPastDue)
            {
                return WmsEndpointOutcome.Json(403, new
                {
                    code = "SUBSCRIPTION_QUOTA_EXCEEDED",
                    errorCode = "SUBSCRIPTION_QUOTA_EXCEEDED",
                    message = $"Thuê bao đã vượt ngưỡng cứng 115% hạn ngạch đơn hàng tháng ({tenantSub.CurrentMonthOrderCount}/{quota} đơn). Vui lòng nâng cấp gói cước."
                });
            }
        }

        var rows = new List<MarketplaceOrderExcelRowInput>();
        if (parsedFileRows is { Count: > 0 })
        {
            rows.AddRange(parsedFileRows);
        }
        else if (request?.Rows is { Count: > 0 })
        {
            rows.AddRange(request.Rows);
        }

        if (rows.Count == 0)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "MARKETPLACE_EXCEL_FILE_REQUIRED",
                errorCode = "MARKETPLACE_EXCEL_FILE_REQUIRED",
                message = "Vui lòng tải lên tệp Excel (.xlsx) hoặc danh sách dòng đơn hàng sàn hợp lệ."
            });
        }

        var defaultCustomer = await _db.BusinessPartners.AsNoTracking()
            .Where(c => c.IsCustomer && c.IsActive)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var defaultProduct = await _db.Products.AsNoTracking()
            .Include(p => p.Unit)
            .Where(p => p.IsActive)
            .OrderBy(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var activeGiftRules = await _db.PromotionGiftRules.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var importedOrders = new List<object>();
        var skippedDuplicates = 0;

        var groupedByExtOrder = rows
            .GroupBy(r => string.IsNullOrWhiteSpace(r.ExternalOrderId)
                ? $"MP-XLS-{Guid.NewGuid():N}"[..20].ToUpperInvariant()
                : r.ExternalOrderId.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var orderGroup in groupedByExtOrder)
        {
            var extOrderId = orderGroup.Key;

            if (tenantSub != null && tenantSub.MonthlyOrderQuota > 0)
            {
                var maxAllowed = (int)Math.Ceiling(tenantSub.MonthlyOrderQuota * 1.15m);
                if (tenantSub.CurrentMonthOrderCount >= maxAllowed)
                {
                    return WmsEndpointOutcome.Json(403, new
                    {
                        code = "SUBSCRIPTION_QUOTA_EXCEEDED",
                        errorCode = "SUBSCRIPTION_QUOTA_EXCEEDED",
                        message = $"Thuê bao đã vượt ngưỡng cứng 115% hạn ngạch đơn hàng tháng ({tenantSub.CurrentMonthOrderCount}/{tenantSub.MonthlyOrderQuota} đơn)."
                    });
                }
            }

            if (await _db.SalesOrders.AsNoTracking().AnyAsync(o => o.ExternalOrderId == extOrderId, cancellationToken))
            {
                skippedDuplicates++;
                continue;
            }

            var firstRow = orderGroup.First();
            var so = new SalesOrder
            {
                OrderCode = $"SO-XLS-{now:yyMMddHHmmss}-{importedOrders.Count + 1:D2}",
                ExternalOrderId = extOrderId,
                CustomerId = defaultCustomer?.Id ?? 1,
                WarehouseId = warehouseId,
                RequestedShipDate = now.Date.AddDays(1),
                Priority = 2,
                ShippingMethod = firstRow.ChannelCode ?? request?.ChannelCode ?? "SHOPEE_EXPRESS",
                Currency = "VND",
                Status = SalesOrderStatus.Draft,
                CreatedAt = now,
                CreatedBy = _currentUser?.UserId ?? 1
            };

            var lineSeq = 1;
            foreach (var row in orderGroup)
            {
                var product = !string.IsNullOrWhiteSpace(row.SkuCode)
                    ? await _db.Products.AsNoTracking().Include(p => p.Unit).FirstOrDefaultAsync(p => p.Code == row.SkuCode.Trim() && p.IsActive, cancellationToken) ?? defaultProduct
                    : defaultProduct;

                if (product == null)
                    continue;

                var orderedQty = Math.Max(1m, row.Quantity);
                var unitPrice = Math.Max(0m, row.UnitPrice);

                var bundleComponents = await _db.ProductBundleComponents
                    .AsNoTracking()
                    .Include(b => b.ComponentProduct)
                        .ThenInclude(cp => cp!.Unit)
                    .Where(b => b.ParentProductId == product.Id && b.IsActive)
                    .OrderBy(b => b.Id)
                    .ToListAsync(cancellationToken);

                if (bundleComponents.Count > 0)
                {
                    foreach (var comp in bundleComponents)
                    {
                        var compQty = orderedQty * (comp.ComponentQuantity > 0m ? comp.ComponentQuantity : 1m);
                        var ratio = 1m / bundleComponents.Count;
                        var compUnitPrice = comp.ComponentQuantity > 0m
                            ? decimal.Round((unitPrice * ratio) / comp.ComponentQuantity, 4)
                            : decimal.Round(unitPrice * ratio, 4);

                        so.Lines.Add(new SalesOrderLine
                        {
                            ExternalLineId = $"{extOrderId}-L{lineSeq++}",
                            ProductId = comp.ComponentProductId,
                            ParentBundleProductId = product.Id,
                            OrderedQuantity = compQty,
                            UnitPrice = compUnitPrice,
                            TaxPercent = 8m,
                            UomCode = comp.ComponentProduct?.Unit?.Code ?? "CÁI"
                        });
                    }
                }
                else
                {
                    so.Lines.Add(new SalesOrderLine
                    {
                        ExternalLineId = $"{extOrderId}-L{lineSeq++}",
                        ProductId = product.Id,
                        OrderedQuantity = orderedQty,
                        UnitPrice = unitPrice,
                        TaxPercent = 8m,
                        UomCode = product.Unit?.Code ?? "THÙNG"
                    });
                }
            }

            if (so.Lines.Count == 0)
                continue;

            await AttachEligiblePromotionGiftsAsync(so, extOrderId, warehouseId, activeGiftRules, cancellationToken);

            _db.SalesOrders.Add(so);
            if (tenantSub != null)
            {
                tenantSub.CurrentMonthOrderCount += 1;
                tenantSub.UpdatedAtUtc = now;
            }
            await _db.SaveChangesAsync(cancellationToken);

            importedOrders.Add(new
            {
                salesOrderId = so.Id,
                orderCode = so.OrderCode,
                externalOrderId = so.ExternalOrderId,
                channelCode = firstRow.ChannelCode ?? request?.ChannelCode ?? "SHOPEE",
                lineCount = so.Lines.Count,
                bundleComponentLinesCount = so.Lines.Count(l => l.ParentBundleProductId.HasValue),
                freeGiftLinesCount = so.Lines.Count(l => l.IsFreeGift),
                lines = so.Lines.Select(l => new
                {
                    l.Id,
                    l.ProductId,
                    l.ParentBundleProductId,
                    l.OrderedQuantity,
                    l.UnitPrice,
                    l.IsFreeGift
                })
            });
        }

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId,
            importedCount = importedOrders.Count,
            skippedDuplicatesCount = skippedDuplicates,
            orders = importedOrders,
            importedAtUtc = now,
            statusLabel = $"Đã nhập {importedOrders.Count} đơn hàng sàn từ Excel (Bỏ qua {skippedDuplicates} đơn trùng mã ExternalOrderId)"
        });
    }

    private async Task AttachEligiblePromotionGiftsAsync(
        SalesOrder so,
        string extOrderId,
        int warehouseId,
        IReadOnlyList<PromotionGiftRule> activeGiftRules,
        CancellationToken cancellationToken)
    {
        if (activeGiftRules.Count == 0)
            return;

        var orderTotal = so.Lines.Where(l => !l.IsFreeGift).Sum(l => l.OrderedQuantity * l.UnitPrice);
        var totalMainQty = so.Lines.Where(l => !l.IsFreeGift).Sum(l => l.OrderedQuantity);

        foreach (var rule in activeGiftRules)
        {
            if (orderTotal < rule.MinOrderAmount)
                continue;

            if (rule.TriggerProductId.HasValue)
            {
                var triggerQty = so.Lines
                    .Where(l => !l.IsFreeGift && (l.ProductId == rule.TriggerProductId.Value || l.ParentBundleProductId == rule.TriggerProductId.Value))
                    .Sum(l => l.OrderedQuantity);
                if (triggerQty < rule.MinTriggerQuantity)
                    continue;
            }
            else if (totalMainQty < rule.MinTriggerQuantity)
            {
                continue;
            }

            var giftStocks = await _db.InventoryStocks
                .AsNoTracking()
                .Include(s => s.Location)
                .Where(s => s.WarehouseId == warehouseId &&
                            s.ProductId == rule.GiftProductId &&
                            s.Status == InventoryStatus.Available &&
                            (s.Location == null || s.Location.LocationType != WarehouseLocationType.Receiving))
                .ToListAsync(cancellationToken);
            var giftAtp = giftStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
            if (giftAtp < rule.GiftQuantity && rule.AutoSkipWhenOutOfStock)
                continue;

            so.Lines.Add(new SalesOrderLine
            {
                ExternalLineId = $"{extOrderId}-GIFT-{rule.Id}",
                ProductId = rule.GiftProductId,
                OrderedQuantity = rule.GiftQuantity,
                UnitPrice = 0m,
                TaxPercent = 0m,
                IsFreeGift = true,
                UomCode = "CÁI"
            });
        }
    }
}
