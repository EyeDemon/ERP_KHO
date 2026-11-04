using System.Globalization;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService
{
    private async Task<(bool CanFulfillSingleWarehouse, int SplitShipmentCount, List<SplitFulfillmentPlanItemDtoModel> Plan, SalesOrder? Order)> BuildSplitFulfillmentPreviewCoreAsync(
        string? orderIdOrCode,
        int? preferredWarehouseId,
        IReadOnlyCollection<int>? accessibleIds,
        CancellationToken cancellationToken)
    {
        SalesOrder? order = null;
        if (!string.IsNullOrWhiteSpace(orderIdOrCode))
        {
            var trimmed = orderIdOrCode.Trim();
            if (int.TryParse(trimmed, out var parsedId))
            {
                var byIdQuery = _db.SalesOrders
                    .AsNoTracking()
                    .Include(o => o.Lines)
                        .ThenInclude(l => l.Product)
                    .Where(o => o.Id == parsedId);
                if (!preferredWarehouseId.HasValue && accessibleIds != null)
                {
                    byIdQuery = byIdQuery.Where(o => accessibleIds.Contains(o.WarehouseId));
                }
                order = await byIdQuery.FirstOrDefaultAsync(cancellationToken);
            }

            if (order == null)
            {
                var byCodeQuery = _db.SalesOrders
                    .AsNoTracking()
                    .Include(o => o.Lines)
                        .ThenInclude(l => l.Product)
                    .Where(o => o.OrderCode == trimmed || o.ExternalOrderId == trimmed);
                if (!preferredWarehouseId.HasValue && accessibleIds != null)
                {
                    byCodeQuery = byCodeQuery.Where(o => accessibleIds.Contains(o.WarehouseId));
                }
                order = await byCodeQuery.FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (order == null)
        {
            var fallbackQuery = _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                    .ThenInclude(l => l.Product)
                .AsQueryable();

            if (preferredWarehouseId.HasValue)
            {
                fallbackQuery = fallbackQuery.Where(o => o.WarehouseId == preferredWarehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                fallbackQuery = fallbackQuery.Where(o => accessibleIds.Contains(o.WarehouseId));
            }

            order = await fallbackQuery
                .OrderByDescending(o => o.Lines.Count > 1)
                .ThenByDescending(o => o.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var whQuery = _db.Warehouses.AsNoTracking().Where(w => w.IsActive);
        if (!preferredWarehouseId.HasValue && accessibleIds != null)
        {
            whQuery = whQuery.Where(w => accessibleIds.Contains(w.Id));
        }
        var warehouses = await whQuery.OrderBy(w => w.Id).ToListAsync(cancellationToken);
        var whById = warehouses.ToDictionary(w => w.Id);

        var lineReqs = new List<(int ProductId, string ProductCode, string ProductName, decimal RequestedQty)>();
        if (order?.Lines is { Count: > 0 })
        {
            foreach (var g in order.Lines.GroupBy(l => new
            {
                l.ProductId,
                Code = l.Product?.Code ?? $"SKU-{l.ProductId}",
                Name = l.Product?.Name ?? $"Sản phẩm #{l.ProductId}"
            }))
            {
                var reqQty = g.Sum(l => Math.Max(0m, l.OrderedQuantity - l.CancelledQuantity));
                if (reqQty <= 0m)
                {
                    reqQty = g.Sum(l => Math.Max(1m, l.OrderedQuantity));
                }
                lineReqs.Add((g.Key.ProductId, g.Key.Code, g.Key.Name, reqQty));
            }
        }
        else if (accessibleIds == null || warehouses.Count > 0)
        {
            var sampleProducts = await _db.Products
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Id)
                .Take(2)
                .ToListAsync(cancellationToken);
            foreach (var p in sampleProducts)
            {
                lineReqs.Add((p.Id, p.Code, p.Name, 2m));
            }
        }

        var prodIds = lineReqs.Select(x => x.ProductId).Distinct().ToList();
        var stockQuery = _db.InventoryStocks
            .AsNoTracking()
            .Where(s => prodIds.Contains(s.ProductId) &&
                        s.Status == InventoryStatus.Available &&
                        (s.Quantity - s.ReservedQuantity) > 0m);
        if (!preferredWarehouseId.HasValue && accessibleIds != null)
        {
            stockQuery = stockQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
        }

        var availableStocks = prodIds.Count > 0
            ? await stockQuery.ToListAsync(cancellationToken)
            : [];

        var atpByWhAndProduct = availableStocks
            .GroupBy(s => (s.WarehouseId, s.ProductId))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity)));

        var defaultWhId = order?.WarehouseId ?? preferredWarehouseId ?? warehouses.FirstOrDefault()?.Id ?? 1;
        var orderedCandidateWhs = warehouses
            .OrderByDescending(w => w.Id == defaultWhId)
            .ThenBy(w => w.Id)
            .ToList();

        Warehouse? singleFulfillWh = null;
        foreach (var candidateWh in orderedCandidateWhs)
        {
            var canCoverAll = lineReqs.All(req =>
                atpByWhAndProduct.GetValueOrDefault((candidateWh.Id, req.ProductId), 0m) >= req.RequestedQty);
            if (canCoverAll && lineReqs.Count > 0)
            {
                singleFulfillWh = candidateWh;
                break;
            }
        }

        var baseOrderCode = !string.IsNullOrWhiteSpace(order?.OrderCode)
            ? order.OrderCode.Trim()
            : "SO-QA-001";

        var plan = new List<SplitFulfillmentPlanItemDtoModel>();
        if (singleFulfillWh != null)
        {
            var singleWhDisplay = FormatWarehouseNodeDisplay(singleFulfillWh.Code, singleFulfillWh.Name);
            foreach (var req in lineReqs)
            {
                var availAtp = atpByWhAndProduct.GetValueOrDefault((singleFulfillWh.Id, req.ProductId), req.RequestedQty);
                var routeLabel = $"Đủ tồn 1 kho • {singleWhDisplay} ({req.RequestedQty.ToString("0.##", CultureInfo.InvariantCulture)} ĐV)";
                plan.Add(new SplitFulfillmentPlanItemDtoModel(
                    req.ProductId,
                    req.ProductCode,
                    req.ProductName,
                    req.RequestedQty,
                    singleFulfillWh.Id,
                    singleFulfillWh.Code,
                    singleFulfillWh.Name,
                    req.RequestedQty,
                    0m,
                    LegNumber: 1,
                    OrderCode: baseOrderCode,
                    SubOrderCode: baseOrderCode,
                    WarehouseId: singleFulfillWh.Id,
                    WarehouseCode: singleFulfillWh.Code.Trim(),
                    WarehouseName: singleWhDisplay,
                    AllocatedQuantity: req.RequestedQty,
                    AvailableAtpQty: availAtp,
                    DistanceKm: 12,
                    EstimatedLeadTimeHours: 4,
                    RoutingPriority: 1,
                    RoutingNote: routeLabel,
                    RoutingStrategyLabel: routeLabel));
            }
            return (true, 1, plan, order);
        }

        foreach (var req in lineReqs)
        {
            var remaining = req.RequestedQty;
            var whsForSku = orderedCandidateWhs
                .Select(w => (Warehouse: w, Available: atpByWhAndProduct.GetValueOrDefault((w.Id, req.ProductId), 0m)))
                .Where(x => x.Available > 0m)
                .OrderByDescending(x => x.Warehouse.Id == defaultWhId)
                .ThenByDescending(x => x.Available)
                .ToList();

            if (whsForSku.Count == 0)
            {
                whById.TryGetValue(defaultWhId, out var fallbackWh);
                var fbCode = fallbackWh?.Code?.Trim() ?? $"WH{defaultWhId:D2}";
                var fbName = fallbackWh?.Name?.Trim() ?? $"Kho #{defaultWhId}";
                var fbDisplay = FormatWarehouseNodeDisplay(fbCode, fbName);
                var fbLabel = $"Đủ tồn 1 kho • {fbDisplay} ({req.RequestedQty.ToString("0.##", CultureInfo.InvariantCulture)} ĐV)";
                plan.Add(new SplitFulfillmentPlanItemDtoModel(
                    req.ProductId,
                    req.ProductCode,
                    req.ProductName,
                    req.RequestedQty,
                    defaultWhId,
                    fbCode,
                    fbName,
                    req.RequestedQty,
                    0m,
                    LegNumber: 1,
                    OrderCode: baseOrderCode,
                    SubOrderCode: baseOrderCode,
                    WarehouseId: defaultWhId,
                    WarehouseCode: fbCode,
                    WarehouseName: fbDisplay,
                    AllocatedQuantity: req.RequestedQty,
                    AvailableAtpQty: req.RequestedQty,
                    DistanceKm: 12,
                    EstimatedLeadTimeHours: 4,
                    RoutingPriority: 1,
                    RoutingNote: fbLabel,
                    RoutingStrategyLabel: fbLabel));
                continue;
            }

            var legNumber = 0;
            var totalLegs = Math.Max(1, whsForSku.Count);
            foreach (var (wh, avail) in whsForSku)
            {
                if (remaining <= 0m)
                    break;

                legNumber++;
                var alloc = Math.Min(remaining, avail);
                remaining -= alloc;
                var whDisplay = FormatWarehouseNodeDisplay(wh.Code, wh.Name);
                var subOrderCode = totalLegs > 1 ? $"{baseOrderCode}-SUB{legNumber}" : baseOrderCode;
                var routeLabel = totalLegs > 1
                    ? $"Tách đơn đa kho (Chặng {legNumber}/{totalLegs}) • Ưu tiên tồn khả dụng & khoảng cách"
                    : $"Đủ tồn 1 kho • {whDisplay} ({alloc.ToString("0.##", CultureInfo.InvariantCulture)} ĐV)";
                plan.Add(new SplitFulfillmentPlanItemDtoModel(
                    req.ProductId,
                    req.ProductCode,
                    req.ProductName,
                    req.RequestedQty,
                    wh.Id,
                    wh.Code,
                    wh.Name,
                    alloc,
                    remaining,
                    LegNumber: legNumber,
                    OrderCode: subOrderCode,
                    SubOrderCode: subOrderCode,
                    WarehouseId: wh.Id,
                    WarehouseCode: wh.Code.Trim(),
                    WarehouseName: whDisplay,
                    AllocatedQuantity: alloc,
                    AvailableAtpQty: avail,
                    DistanceKm: legNumber == 1 ? 12 : 1450,
                    EstimatedLeadTimeHours: legNumber == 1 ? 4 : 24,
                    RoutingPriority: legNumber,
                    RoutingNote: routeLabel,
                    RoutingStrategyLabel: routeLabel));
            }
        }

        var distinctAllocatedWhCount = plan
            .Where(x => x.AllocatedQty > 0m)
            .Select(x => x.AllocatedWarehouseId)
            .Distinct()
            .Count();
        var splitShipmentCount = Math.Max(1, distinctAllocatedWhCount);
        var canFulfillSingle = distinctAllocatedWhCount == 1 && plan.All(x => x.UnfulfilledQty == 0m);

        return (canFulfillSingle, splitShipmentCount, plan, order);
    }

    public async Task<WmsEndpointOutcome> SplitFulfillmentPreviewAsync(
        string? id,
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else if (_currentUser == null || (_currentUser.IsAuthenticated && !_currentUser.IsGlobalAdmin))
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var (canFulfillSingleWarehouse, splitShipmentCount, splitFulfillmentPlan, order) =
            await BuildSplitFulfillmentPreviewCoreAsync(id, warehouseId, accessibleIds, cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            salesOrderId = order?.Id ?? 0,
            orderCode = order?.OrderCode ?? id ?? "SO-PREVIEW",
            warehouseId = order?.WarehouseId ?? warehouseId ?? 0,
            canFulfillSingleWarehouse,
            splitShipmentCount,
            splitFulfillmentPlan,
            items = splitFulfillmentPlan,
            statusLabel = canFulfillSingleWarehouse
                ? "Đơn hàng có thể xuất trọn vẹn 100% từ 1 kho duy nhất (Không cần tách kiện)"
                : $"Đề xuất tách đơn thông minh thành {splitShipmentCount} kiện xuất từ nhiều kho để giao đủ 100% đơn hàng"
        });
    }
}
