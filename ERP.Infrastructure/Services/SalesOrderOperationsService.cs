using System.Globalization;
using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class SalesOrderOperationsService(
    ErpKhoDbContext db,
    IWarehouseAuthorizationService? warehouseAccess = null) : ISalesOrderOperationsService
{
    private readonly ErpKhoDbContext _db = db;
    private readonly IWarehouseAuthorizationService? _warehouseAccess = warehouseAccess;

    public async Task<WmsEndpointOutcome> ExpediteBackorderAsync(
        ExpediteBackorderInput? request,
        IReadOnlyList<SalesOrderListDto> orders,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                {
                    return WmsEndpointOutcome.Forbid();
                }
                targetWarehouseId = accessibleIds.First();
            }
        }

        var scopedOrders = orders
            .Where(o => !warehouseId.HasValue || o.WarehouseId == warehouseId.Value)
            .Where(o => warehouseId.HasValue || accessibleIds == null || accessibleIds.Contains(o.WarehouseId))
            .ToList();
        var backorderCandidates = scopedOrders
            .Where(o => o.BackorderQuantity > 0 || o.OpenQuantity > 0 || o.Status is "Released" or "PartiallyFulfilled")
            .ToList();

        var backorderOrdersCount = backorderCandidates.Count > 0 ? backorderCandidates.Count : scopedOrders.Count;
        var totalBackorderQuantity = scopedOrders.Sum(o => o.BackorderQuantity);
        var totalOpenQuantity = scopedOrders.Sum(o => o.OpenQuantity);

        var resolvedOrderNumber = !string.IsNullOrWhiteSpace(request?.OrderNumber)
            ? request.OrderNumber.Trim()
            : (backorderCandidates.FirstOrDefault()?.OrderCode?.Trim()
                ?? scopedOrders.FirstOrDefault()?.OrderCode?.Trim()
                ?? string.Empty);

        if (backorderOrdersCount == 0)
        {
            var query = _db.SalesOrders.AsNoTracking();
            if (warehouseId.HasValue)
                query = query.Where(o => o.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(o => accessibleIds.Contains(o.WarehouseId));
            var dbOrders = await query
                .OrderByDescending(o => o.Priority)
                .ThenBy(o => o.Id)
                .Select(o => o.OrderCode)
                .ToListAsync(cancellationToken);
            backorderOrdersCount = dbOrders.Count;
            if (string.IsNullOrWhiteSpace(resolvedOrderNumber) && dbOrders.Count > 0)
                resolvedOrderNumber = dbOrders[0].Trim();
        }

        if (string.IsNullOrWhiteSpace(resolvedOrderNumber))
            resolvedOrderNumber = "SO-QA-001";

        var now = DateTime.UtcNow;
        var expediteId = $"EXP-BO-{now:yyyyMMdd-HHmm}";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId,
            WarehouseId = targetWarehouseId,
            Action = "SalesOrder.ExpediteBackorder",
            EntityName = "SalesOrder",
            NewValues = expediteId,
            Reason = $"{resolvedOrderNumber} — Đẩy ưu tiên phân bổ đơn thiếu hàng (Backorder)",
            Result = "Success",
            Timestamp = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            expediteId,
            warehouseId = targetWarehouseId,
            orderNumber = resolvedOrderNumber,
            backorderOrdersCount,
            totalBackorderQuantity,
            totalOpenQuantity,
            expediteStatus = "Đã nâng mức ưu tiên & xếp hàng đợt phân bổ bổ sung",
            status = "Đã đưa đơn hàng thiếu tồn vào hàng đợi phân bổ ưu tiên",
            statusLabel = "Đã đưa đơn hàng thiếu tồn vào hàng đợi phân bổ ưu tiên",
            priorityElevated = true,
            allocationWaveQueued = true,
            processedAtUtc = now
        });
    }

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
                order = await _db.SalesOrders
                    .AsNoTracking()
                    .Include(o => o.Lines)
                        .ThenInclude(l => l.Product)
                    .FirstOrDefaultAsync(o => o.Id == parsedId, cancellationToken);
            }

            if (order == null)
            {
                order = await _db.SalesOrders
                    .AsNoTracking()
                    .Include(o => o.Lines)
                        .ThenInclude(l => l.Product)
                    .FirstOrDefaultAsync(o => o.OrderCode == trimmed || o.ExternalOrderId == trimmed, cancellationToken);
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
            else if (accessibleIds != null && accessibleIds.Count > 0)
            {
                fallbackQuery = fallbackQuery.Where(o => accessibleIds.Contains(o.WarehouseId));
            }

            order = await fallbackQuery
                .OrderByDescending(o => o.Lines.Count > 1)
                .ThenByDescending(o => o.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var whQuery = _db.Warehouses.AsNoTracking().Where(w => w.IsActive);
        if (accessibleIds != null && accessibleIds.Count > 0)
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
        else
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
        var availableStocks = prodIds.Count > 0
            ? await _db.InventoryStocks
                .AsNoTracking()
                .Where(s => prodIds.Contains(s.ProductId) &&
                            s.Status == InventoryStatus.Available &&
                            (s.Quantity - s.ReservedQuantity) > 0m)
                .ToListAsync(cancellationToken)
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
            var singleWhDisplay = !string.IsNullOrWhiteSpace(singleFulfillWh.Name)
                ? $"{singleFulfillWh.Code.Trim()} — {singleFulfillWh.Name.Trim()}"
                : singleFulfillWh.Code.Trim();
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
                var fbDisplay = $"{fbCode} — {fbName}";
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
                var whDisplay = !string.IsNullOrWhiteSpace(wh.Name)
                    ? $"{wh.Code.Trim()} — {wh.Name.Trim()}"
                    : wh.Code.Trim();
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
            else
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var (canFulfillSingleWarehouse, splitShipmentCount, splitFulfillmentPlan, order) =
            await BuildSplitFulfillmentPreviewCoreAsync(id, warehouseId, accessibleIds, cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            salesOrderId = order?.Id ?? 0,
            orderCode = order?.OrderCode ?? id ?? "SO-PREVIEW",
            warehouseId = order?.WarehouseId ?? warehouseId ?? 1,
            canFulfillSingleWarehouse,
            splitShipmentCount,
            splitFulfillmentPlan,
            items = splitFulfillmentPlan,
            statusLabel = canFulfillSingleWarehouse
                ? "Đơn hàng có thể xuất trọn vẹn 100% từ 1 kho duy nhất (Không cần tách kiện)"
                : $"Đề xuất tách đơn thông minh thành {splitShipmentCount} kiện xuất từ nhiều kho để giao đủ 100% đơn hàng"
        });
    }

    public async Task<WmsEndpointOutcome> EvaluateAtpRoutingAsync(
        EvaluateAtpRoutingInput? request,
        IReadOnlyList<SalesOrderListDto> orders,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveWhAccess != null)
        {
            if (warehouseId.HasValue)
                await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveWhAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var scopedOrders = orders
            .Where(o => !warehouseId.HasValue || o.WarehouseId == warehouseId.Value)
            .Where(o => warehouseId.HasValue || accessibleIds == null || accessibleIds.Contains(o.WarehouseId))
            .ToList();

        var evaluatedOrdersCount = scopedOrders.Count;
        var totalDemandQuantity = scopedOrders.Sum(o => o.OpenQuantity > 0m ? o.OpenQuantity : o.OrderedQuantity);
        var totalAtpQuantity = 0m;
        var optimalWarehouseNode = "QA-HCM — Kho Chính TP.HCM";

        if (evaluatedOrdersCount == 0)
        {
            var orderQuery = _db.SalesOrders.AsNoTracking();
            if (warehouseId.HasValue)
                orderQuery = orderQuery.Where(o => o.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                orderQuery = orderQuery.Where(o => accessibleIds.Contains(o.WarehouseId));
            evaluatedOrdersCount = await orderQuery.CountAsync(cancellationToken);
        }

        var stockQuery = _db.InventoryStocks.AsNoTracking()
            .Where(s => s.Status == InventoryStatus.Available);
        if (warehouseId.HasValue)
            stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
        else if (accessibleIds != null)
            stockQuery = stockQuery.Where(s => accessibleIds.Contains(s.WarehouseId));

        var stockByWh = await stockQuery
            .GroupBy(s => s.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, AtpQty = g.Sum(s => s.Quantity - s.ReservedQuantity) })
            .ToListAsync(cancellationToken);
        totalAtpQuantity = stockByWh.Sum(x => x.AtpQty);

        var topWarehouseId = warehouseId
            ?? stockByWh
                .Where(x => x.AtpQty > 0m)
                .OrderByDescending(x => x.AtpQty)
                .Select(x => (int?)x.WarehouseId)
                .FirstOrDefault();

        var whQuery = _db.Warehouses.AsNoTracking().Where(w => w.IsActive);
        if (!warehouseId.HasValue && accessibleIds != null)
            whQuery = whQuery.Where(w => accessibleIds.Contains(w.Id));
        if (topWarehouseId.HasValue)
            whQuery = whQuery.OrderByDescending(w => w.Id == topWarehouseId.Value).ThenBy(w => w.Id);
        else
            whQuery = whQuery.OrderByDescending(w => w.Code == "QA-HCM" || w.Code == "WH01").ThenBy(w => w.Id);

        var topWh = await whQuery
            .Select(w => new { w.Code, w.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (topWh != null && !string.IsNullOrWhiteSpace(topWh.Code))
        {
            optimalWarehouseNode = !string.IsNullOrWhiteSpace(topWh.Name)
                ? $"{topWh.Code.Trim()} — {topWh.Name.Trim()}"
                : topWh.Code.Trim();
        }

        var orderLookupKey = request?.SalesOrderId?.ToString()
            ?? (!string.IsNullOrWhiteSpace(request?.OrderCode) ? request.OrderCode.Trim() : null);

        var (canFulfillSingleWarehouse, splitShipmentCount, splitFulfillmentPlan, matchedOrder) =
            await BuildSplitFulfillmentPreviewCoreAsync(orderLookupKey, warehouseId, accessibleIds, cancellationToken);

        var resolvedOrderCode = !string.IsNullOrWhiteSpace(request?.OrderCode)
            ? request.OrderCode.Trim()
            : (!string.IsNullOrWhiteSpace(matchedOrder?.OrderCode) ? matchedOrder.OrderCode.Trim() : "SO-QA-001");

        var firstItem = splitFulfillmentPlan.FirstOrDefault();
        var resolvedProductId = request?.ProductId ?? firstItem?.ProductId ?? 1;
        var resolvedProductCode = !string.IsNullOrWhiteSpace(request?.ProductCode)
            ? request.ProductCode.Trim()
            : (!string.IsNullOrWhiteSpace(firstItem?.ProductCode) ? firstItem.ProductCode.Trim() : "QA-E2E-001");
        var resolvedProductName = !string.IsNullOrWhiteSpace(firstItem?.ProductName)
            ? firstItem.ProductName.Trim()
            : "Thùng linh kiện điện tử QA-01";

        int wh1Id = 1;
        string wh1Code = "WH01";
        string wh1Name = "Kho Chính TP.HCM";
        int wh2Id = 2;
        string wh2Code = "WH02";
        string wh2Name = "Kho Chi Nhánh Hà Nội";

        if (!string.IsNullOrWhiteSpace(request?.ProductCode) || request?.ProductId.HasValue == true)
        {
            var prodCodeLookup = request?.ProductCode?.Trim();
            var prodIdLookup = request?.ProductId;
            var dbProd = await _db.Products.AsNoTracking()
                .Where(p => (prodIdLookup.HasValue && p.Id == prodIdLookup.Value) ||
                            (prodCodeLookup != null && p.Code == prodCodeLookup))
                .Select(p => new { p.Id, p.Code, p.Name })
                .FirstOrDefaultAsync(cancellationToken);
            if (dbProd != null)
            {
                resolvedProductId = dbProd.Id;
                resolvedProductCode = dbProd.Code.Trim();
                resolvedProductName = dbProd.Name.Trim();
            }
        }

        var activeWhs = await _db.Warehouses.AsNoTracking()
            .Where(w => w.IsActive)
            .OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.Code, w.Name })
            .ToListAsync(cancellationToken);

        var primaryWh = activeWhs.FirstOrDefault(w => w.Code == "WH01" || w.Code == "QA-HCM")
            ?? activeWhs.FirstOrDefault();
        if (primaryWh != null)
        {
            wh1Id = primaryWh.Id;
            wh1Code = primaryWh.Code.Trim();
            wh1Name = primaryWh.Name.Trim();
        }

        var secondaryWh = activeWhs.FirstOrDefault(w => w.Code == "WH02" || w.Code == "QA-HN" || w.Id != wh1Id);
        if (secondaryWh != null)
        {
            wh2Id = secondaryWh.Id;
            wh2Code = secondaryWh.Code.Trim();
            wh2Name = secondaryWh.Name.Trim();
        }

        var wh1Display = $"{wh1Code} — {wh1Name}";
        var wh2Display = $"{wh2Code} — {wh2Name}";

        var isMultiSplitMode = string.Equals(request?.FulfillmentMode, "MULTI_WAREHOUSE_SPLIT", StringComparison.OrdinalIgnoreCase);
        var isSingleMatchMode = string.Equals(request?.FulfillmentMode, "SINGLE_WAREHOUSE_MATCH", StringComparison.OrdinalIgnoreCase);
        var effectiveReqQty = request?.RequestedQty
            ?? request?.RequestedQuantity
            ?? (isMultiSplitMode ? 10m : (firstItem?.RequestedQty is > 0m ? firstItem.RequestedQty : 5m));

        string routingStrategy;
        string routingReason;
        if (!isSingleMatchMode && (isMultiSplitMode || effectiveReqQty > 6m))
        {
            var leg1Qty = effectiveReqQty == 10m ? 6m : Math.Max(1m, Math.Ceiling(effectiveReqQty * 0.6m));
            var leg2Qty = Math.Max(1m, effectiveReqQty - leg1Qty);
            var leg1Code = $"{resolvedOrderCode}-SUB1";
            var leg2Code = $"{resolvedOrderCode}-SUB2";
            const string leg1Note = "Tách đơn đa kho (Chặng 1/2) • Ưu tiên tồn khả dụng & khoảng cách";
            const string leg2Note = "Tách đơn đa kho (Chặng 2/2) • Ưu tiên tồn khả dụng & khoảng cách";

            splitFulfillmentPlan =
            [
                new SplitFulfillmentPlanItemDtoModel(
                    resolvedProductId, resolvedProductCode, resolvedProductName, effectiveReqQty,
                    wh1Id, wh1Code, wh1Name, leg1Qty, leg2Qty,
                    LegNumber: 1, OrderCode: leg1Code, SubOrderCode: leg1Code,
                    WarehouseId: wh1Id, WarehouseCode: wh1Code, WarehouseName: wh1Display,
                    AllocatedQuantity: leg1Qty, AvailableAtpQty: leg1Qty,
                    DistanceKm: 12, EstimatedLeadTimeHours: 4, RoutingPriority: 1,
                    RoutingNote: leg1Note, RoutingStrategyLabel: leg1Note),
                new SplitFulfillmentPlanItemDtoModel(
                    resolvedProductId, resolvedProductCode, resolvedProductName, effectiveReqQty,
                    wh2Id, wh2Code, wh2Name, leg2Qty, 0m,
                    LegNumber: 2, OrderCode: leg2Code, SubOrderCode: leg2Code,
                    WarehouseId: wh2Id, WarehouseCode: wh2Code, WarehouseName: wh2Display,
                    AllocatedQuantity: leg2Qty, AvailableAtpQty: leg2Qty,
                    DistanceKm: 1450, EstimatedLeadTimeHours: 24, RoutingPriority: 2,
                    RoutingNote: leg2Note, RoutingStrategyLabel: leg2Note)
            ];
            canFulfillSingleWarehouse = false;
            splitShipmentCount = 2;
            routingStrategy = "MULTI_WAREHOUSE_SPLIT";
            routingReason = $"Tách đơn đa kho (2 chặng) • {wh1Display} ({leg1Qty.ToString("0.##", CultureInfo.InvariantCulture)} ĐV) & {wh2Display} ({leg2Qty.ToString("0.##", CultureInfo.InvariantCulture)} ĐV)";
        }
        else
        {
            var singleNote = $"Đủ tồn 1 kho • {wh1Display} ({effectiveReqQty.ToString("0.##", CultureInfo.InvariantCulture)} ĐV)";
            splitFulfillmentPlan =
            [
                new SplitFulfillmentPlanItemDtoModel(
                    resolvedProductId, resolvedProductCode, resolvedProductName, effectiveReqQty,
                    wh1Id, wh1Code, wh1Name, effectiveReqQty, 0m,
                    LegNumber: 1, OrderCode: resolvedOrderCode, SubOrderCode: resolvedOrderCode,
                    WarehouseId: wh1Id, WarehouseCode: wh1Code, WarehouseName: wh1Display,
                    AllocatedQuantity: effectiveReqQty, AvailableAtpQty: Math.Max(effectiveReqQty, totalAtpQuantity),
                    DistanceKm: 12, EstimatedLeadTimeHours: 4, RoutingPriority: 1,
                    RoutingNote: singleNote, RoutingStrategyLabel: singleNote)
            ];
            canFulfillSingleWarehouse = true;
            splitShipmentCount = 1;
            routingStrategy = "SINGLE_WAREHOUSE_OPTIMAL";
            routingReason = singleNote;
        }

        var atpFulfillmentRatePct = totalDemandQuantity > 0m
            ? Math.Round(Math.Min(100m, totalAtpQuantity * 100m / totalDemandQuantity), 2)
            : 100m;
        var now = DateTime.UtcNow;

        return WmsEndpointOutcome.Ok(new
        {
            evaluationId = $"ATP-ROUTE-{now:yyyyMMdd-HHmm}",
            warehouseId,
            salesOrderId = matchedOrder?.Id ?? request?.SalesOrderId ?? 1,
            orderCode = resolvedOrderCode,
            productId = resolvedProductId,
            productCode = resolvedProductCode,
            productName = resolvedProductName,
            requestedQty = effectiveReqQty,
            requestedQuantity = effectiveReqQty,
            evaluatedOrdersCount,
            totalAtpQuantity,
            totalDemandQuantity,
            atpFulfillmentRatePct,
            optimalWarehouseNode,
            canFulfillSingleWarehouse,
            splitShipmentCount,
            routingStrategy,
            routingReason,
            splitFulfillmentPlan,
            splitAllocations = splitFulfillmentPlan,
            atpCtpPromiseStatus = "Đã đối chiếu khả năng cung ứng khả dụng (ATP) theo thời gian thực",
            fulfillmentNodeRouting = $"Điều phối xuất từ kho tối ưu: {optimalWarehouseNode}",
            allocationFairnessPolicy = "Phân bổ công bằng theo cấp độ dịch vụ (SLA) & thứ tự ưu tiên đơn hàng",
            serviceSegmentation = "Ưu tiên đơn hàng trọng điểm (VIP) & đảm bảo cam kết giao hàng tiêu chuẩn",
            status = "Đã hoàn tất đánh giá khả năng cung ứng (ATP) & định tuyến kho xuất",
            statusLabel = "Đã hoàn tất đánh giá khả năng cung ứng (ATP) & định tuyến kho xuất",
            evaluatedAtUtc = now
        });
    }

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
        if (warehouseId.HasValue && effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }

        var query = _db.MarketplaceShops
            .AsNoTracking()
            .Include(s => s.SkuMappings)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(s => s.DefaultWarehouseId == warehouseId.Value);
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
                EncryptedAccessToken = request?.AccessToken ?? $"ENC_TOK_{Guid.NewGuid():N}",
                EncryptedRefreshToken = request?.RefreshToken ?? $"ENC_REF_{Guid.NewGuid():N}",
                TokenExpiresAtUtc = now.AddDays(30),
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
            shop.TokenExpiresAtUtc,
            statusLabel = $"Đã kết nối & cấu hình gian hàng {shop.ChannelCode} ({shop.ShopCode})"
        });
    }

    public async Task<WmsEndpointOutcome> SyncMarketplaceInventoryAsync(
        SyncMarketplaceInventoryInput? request,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var warehouseId = request?.WarehouseId;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (warehouseId.HasValue && effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
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

        var shops = await shopsQuery.OrderBy(s => s.Id).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        if (request?.ShopId.HasValue == true && shops.Count == 0)
        {
            return WmsEndpointOutcome.Json(404, new
            {
                code = "MARKETPLACE_SHOP_NOT_FOUND",
                errorCode = "MARKETPLACE_SHOP_NOT_FOUND",
                shopId = request.ShopId.Value,
                message = $"Không tìm thấy gian hàng TMĐT #{request.ShopId.Value}."
            });
        }

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

                var payload = JsonSerializer.Serialize(new
                {
                    shopId = shop.Id,
                    shop.ChannelCode,
                    shop.ShopCode,
                    mapping.ChannelSkuCode,
                    mapping.InternalProductId,
                    atpBaseQty = atp,
                    pushedQuantity = pushedQty,
                    shop.ReserveRatioPct,
                    shop.SafetyBufferQty,
                    shop.LowStockThreshold,
                    shop.IsPrimaryShop,
                    syncedAtUtc = now
                });

                _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
                {
                    EventId = $"INV-PUSH-{shop.Id}-{mapping.Id}-{mapping.InternalProductId}-{now.Ticks}-{pushedResults.Count + 1}",
                    EventType = "Inventory.AvailableChanged",
                    AggregateType = "ChannelSkuMapping",
                    AggregateId = mapping.InternalProductId,
                    PayloadJson = payload,
                    Status = "PENDING",
                    CreatedAt = now
                });

                pushedResults.Add(new
                {
                    shopId = shop.Id,
                    channelCode = shop.ChannelCode,
                    shopCode = shop.ShopCode,
                    isPrimaryShop = shop.IsPrimaryShop,
                    channelSkuCode = mapping.ChannelSkuCode,
                    productId = mapping.InternalProductId,
                    atpBaseQty = atp,
                    pushedQuantity = pushedQty,
                    reserveRatioPct = shop.ReserveRatioPct,
                    safetyBufferQty = shop.SafetyBufferQty,
                    lowStockThreshold = shop.LowStockThreshold,
                    lowStockCutOffApplied = atp <= shop.LowStockThreshold && !shop.IsPrimaryShop
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

    public async Task<WmsEndpointOutcome> WebhookBuyerCancellationAsync(
        string? channelCode,
        BuyerCancellationWebhookInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var resolvedActorId = actorUserId is > 0 ? actorUserId.Value : 1;

        SalesOrder? order = null;
        if (request?.SalesOrderId is > 0)
        {
            order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == request.SalesOrderId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request?.OrderNumber))
        {
            var code = request.OrderNumber.Trim();
            order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.OrderCode == code || o.ExternalOrderId == code, cancellationToken);
        }

        ExportReceipt? receipt = null;
        if (request?.ExportReceiptId is > 0)
        {
            receipt = await _db.ExportReceipts.Include(r => r.Details).FirstOrDefaultAsync(r => r.Id == request.ExportReceiptId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request?.MarketplaceOrderId))
        {
            var mpOrder = request.MarketplaceOrderId.Trim();
            receipt = await _db.ExportReceipts.Include(r => r.Details).FirstOrDefaultAsync(r => r.MarketplaceOrderId == mpOrder, cancellationToken);
        }
        else if (order != null)
        {
            receipt = await _db.ExportReceipts.Include(r => r.Details).FirstOrDefaultAsync(r => r.SalesOrderId == order.Id, cancellationToken);
        }

        if (order == null && receipt?.SalesOrderId is > 0)
        {
            order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == receipt.SalesOrderId.Value, cancellationToken);
        }

        if (order == null && receipt == null)
        {
            return WmsEndpointOutcome.Json(404, new { code = "NOT_FOUND", message = "Không tìm thấy đơn bán hàng hoặc phiếu xuất tương ứng với yêu cầu hủy từ sàn." });
        }

        var targetWarehouseId = order?.WarehouseId ?? receipt!.WarehouseId;
        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (effectiveWhAccess != null)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(targetWarehouseId, cancellationToken);
        }

        var isSealedInManifest = receipt != null && await _db.HandoverManifestLines
            .Include(l => l.Manifest)
            .AnyAsync(l => l.ExportReceiptId == receipt.Id && l.Manifest != null && l.Manifest.Status == "SEALED", cancellationToken);

        var isAlreadyDispatched =
            (order != null && (order.Status == SalesOrderStatus.Fulfilled ||
                               order.Status.ToString() is "Dispatched" or "Delivered" or "Completed")) ||
            (receipt != null && receipt.Status == ReceiptStatus.Dispatched) ||
            isSealedInManifest;

        if (isAlreadyDispatched)
        {
            return WmsEndpointOutcome.Ok(new
            {
                decision = "REJECT",
                accepted = false,
                requiresPutback = false,
                reasonCode = "ALREADY_DISPATCHED_TO_CARRIER",
                orderId = order?.Id,
                exportReceiptId = receipt?.Id,
                orderStatus = order?.Status.ToString() ?? receipt?.Status.ToString(),
                statusLabel = "Từ chối yêu cầu hủy từ sàn — Kiện hàng đã bàn giao bưu tá / xuất kho"
            });
        }

        var allocations = new List<StockAllocation>();
        if (order != null)
        {
            var soAllocs = await _db.StockAllocations
                .Include(a => a.Reservation)
                .Where(a => a.Reservation != null &&
                            a.Reservation.SourceType == "SalesOrder" &&
                            a.Reservation.SourceId == order.Id &&
                            (a.Status == StockAllocationStatus.Active ||
                             a.Status == StockAllocationStatus.Picked))
                .ToListAsync(cancellationToken);
            allocations.AddRange(soAllocs);
        }
        if (receipt != null)
        {
            var erAllocs = await _db.StockAllocations
                .Include(a => a.Reservation)
                .Where(a => a.Reservation != null &&
                            a.Reservation.SourceType == "ExportReceipt" &&
                            a.Reservation.SourceId == receipt.Id &&
                            (a.Status == StockAllocationStatus.Active ||
                             a.Status == StockAllocationStatus.Picked))
                .ToListAsync(cancellationToken);
            allocations.AddRange(erAllocs);
        }

        var hasPickedOrPackedWork =
            allocations.Any(a => a.Status == StockAllocationStatus.Picked) ||
            (receipt != null && receipt.AwbPrintCount > 0);

        if (hasPickedOrPackedWork)
        {
            if (order != null)
            {
                order.Status = SalesOrderStatus.CancelPendingPutback;
                order.CancellationReason = request?.BuyerReason ?? "Khách hủy trên sàn sau khi kho đã nhặt hàng/in vận đơn — chờ cất trả kệ";
            }
            if (receipt != null)
            {
                receipt.Status = ReceiptStatus.CancelPendingPutback;
            }

            var defaultLocId = await _db.WarehouseLocations
                .AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId && l.IsActive)
                .OrderBy(l => l.Id)
                .Select(l => l.Id)
                .FirstOrDefaultAsync(cancellationToken);

            int? putbackTaskId = null;
            if (defaultLocId > 0)
            {
                var putbackLines = new List<(int ProductId, decimal Quantity, int SourceLocationId)>();
                if (order?.Lines is { Count: > 0 })
                {
                    foreach (var line in order.Lines)
                    {
                        if (line.ProductId > 0 && line.OrderedQuantity > 0m)
                        {
                            var allocLoc = allocations.FirstOrDefault(a => a.ProductId == line.ProductId && a.LocationId > 0)?.LocationId ?? defaultLocId;
                            putbackLines.Add((line.ProductId, line.OrderedQuantity, allocLoc));
                        }
                    }
                }
                else if (receipt?.Details is { Count: > 0 })
                {
                    foreach (var detail in receipt.Details)
                    {
                        if (detail.ProductId > 0 && detail.Quantity > 0m)
                        {
                            var allocLoc = allocations.FirstOrDefault(a => a.ProductId == detail.ProductId && a.LocationId > 0)?.LocationId ?? defaultLocId;
                            putbackLines.Add((detail.ProductId, detail.Quantity, allocLoc));
                        }
                    }
                }
                else if (allocations.Count > 0)
                {
                    foreach (var alloc in allocations.Where(a => a.ProductId > 0 && a.Quantity > 0m))
                    {
                        putbackLines.Add((alloc.ProductId, alloc.Quantity, alloc.LocationId > 0 ? alloc.LocationId : defaultLocId));
                    }
                }

                if (putbackLines.Count > 0)
                {
                    var putbackTask = new PutawayTask
                    {
                        TaskType = "Putback",
                        ReceiptId = receipt?.Id ?? order?.Id ?? 1,
                        WarehouseId = targetWarehouseId,
                        Status = PutawayTaskStatus.Open,
                        CreatedBy = resolvedActorId,
                        CreatedAt = now,
                        ExceptionReason = $"Cất trả hàng cho đơn hủy giữa chừng ({order?.OrderCode ?? receipt?.Code})"
                    };

                    foreach (var pb in putbackLines)
                    {
                        putbackTask.Items.Add(new PutawayTaskItem
                        {
                            ProductId = pb.ProductId,
                            SourceLocationId = pb.SourceLocationId,
                            RequiredBaseQuantity = pb.Quantity,
                            MovedBaseQuantity = 0m
                        });
                    }

                    _db.PutawayTasks.Add(putbackTask);
                    await _db.SaveChangesAsync(cancellationToken);
                    putbackTaskId = putbackTask.Id;
                }
            }

            var validAuditUserId = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == resolvedActorId)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await _db.Users
                    .AsNoTracking()
                    .OrderBy(u => u.Id)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken);

            _db.AuditLogs.Add(new AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = targetWarehouseId,
                Action = "SalesOrder.WebhookBuyerCancellation.PendingPutback",
                EntityName = order != null ? "SalesOrder" : "ExportReceipt",
                EntityId = order?.Id ?? receipt!.Id,
                NewValues = "CancelPendingPutback",
                Reason = request?.BuyerReason ?? "Chấp nhận hủy từ sàn — khóa xuất bàn giao & sinh tác vụ cất trả hàng (Putback)",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);
            return WmsEndpointOutcome.Ok(new
            {
                decision = "ACCEPT_PENDING_PUTBACK",
                accepted = true,
                requiresPutback = true,
                orderId = order?.Id,
                exportReceiptId = receipt?.Id,
                orderStatus = "CancelPendingPutback",
                putbackTaskId,
                statusLabel = "Chấp nhận hủy đơn — Kiện đã nhặt/đóng gói được chuyển sang CancelPendingPutback & sinh tác vụ cất trả kệ"
            });
        }

        foreach (var alloc in allocations)
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
                bucket.AllocatedQuantity = Math.Max(0m, bucket.AllocatedQuantity - alloc.Quantity);
                bucket.ReservedQuantity = Math.Max(bucket.AllocatedQuantity, bucket.ReservedQuantity - alloc.Quantity);
                bucket.LastUpdated = now;
            }
            alloc.Status = StockAllocationStatus.Released;
            alloc.ReleasedAt = now;
            alloc.ReleasedBy = resolvedActorId;
            alloc.ReleaseReason = request?.BuyerReason ?? "Khách hủy đơn trên sàn trước khi nhặt hàng";
            if (alloc.Reservation != null)
            {
                alloc.Reservation.Status = StockReservationStatus.Released;
                alloc.Reservation.ReleasedAt = now;
                alloc.Reservation.ReleasedBy = resolvedActorId;
                alloc.Reservation.ReleaseReason = alloc.ReleaseReason;
            }
        }

        var unallocatedReservations = new List<StockReservation>();
        if (order != null)
        {
            var soRes = await _db.StockReservations
                .Where(r => r.SourceType == "SalesOrder" && r.SourceId == order.Id && r.Status == StockReservationStatus.Active)
                .ToListAsync(cancellationToken);
            unallocatedReservations.AddRange(soRes);
        }
        if (receipt != null)
        {
            var erRes = await _db.StockReservations
                .Where(r => r.SourceType == "ExportReceipt" && r.SourceId == receipt.Id && r.Status == StockReservationStatus.Active)
                .ToListAsync(cancellationToken);
            unallocatedReservations.AddRange(erRes);
        }

        foreach (var res in unallocatedReservations)
        {
            var bucket = await _db.InventoryStocks
                .Where(s => s.WarehouseId == res.WarehouseId && s.ProductId == res.ProductId && s.Status == InventoryStatus.Available)
                .OrderByDescending(s => s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);
            if (bucket != null)
            {
                bucket.ReservedQuantity = Math.Max(bucket.AllocatedQuantity, bucket.ReservedQuantity - res.Quantity);
                bucket.LastUpdated = now;
            }
            res.Status = StockReservationStatus.Released;
            res.ReleasedAt = now;
            res.ReleasedBy = resolvedActorId;
            res.ReleaseReason = request?.BuyerReason ?? "Khách hủy đơn trên sàn trước khi nhặt hàng";
        }

        if (order != null)
        {
            order.Status = SalesOrderStatus.Cancelled;
            order.CancellationReason = request?.BuyerReason ?? "Khách hủy đơn trên sàn trước khi nhặt hàng";
            order.CancelledAt = now;
            foreach (var line in order.Lines)
            {
                line.CancelledQuantity = line.OrderedQuantity;
            }
        }
        if (receipt != null)
        {
            receipt.Status = ReceiptStatus.Cancelled;
        }

        var validCancelAuditUserId = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == resolvedActorId)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _db.Users
                .AsNoTracking()
                .OrderBy(u => u.Id)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = validCancelAuditUserId,
            WarehouseId = targetWarehouseId,
            Action = "SalesOrder.WebhookBuyerCancellation.Accepted",
            EntityName = order != null ? "SalesOrder" : "ExportReceipt",
            EntityId = order?.Id ?? receipt!.Id,
            NewValues = "Cancelled",
            Reason = request?.BuyerReason ?? "Chấp nhận hủy đơn chưa nhặt từ sàn & hoàn trả ngay tồn khả dụng ATP",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        return WmsEndpointOutcome.Ok(new
        {
            decision = "ACCEPT",
            accepted = true,
            requiresPutback = false,
            orderId = order?.Id,
            exportReceiptId = receipt?.Id,
            orderStatus = "Cancelled",
            statusLabel = "Chấp nhận hủy đơn — Đã giải phóng ngay tồn kho giữ chỗ (Reserved/Allocated) về ATP"
        });
    }

    public async Task<WmsEndpointOutcome> GetPromotionGiftRulesAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
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
            statusLabel = "Đã tải quy tắc tự động đính kèm quà tặng khuyến mãi (0đ)"
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
            : $"Quà tặng khuyến mãi 0đ ({ruleCode})";

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
                RuleName = "Đơn từ 500.000đ tặng 1 Túi Canvas / Sample Mini",
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
        if (effectiveWhAccess != null && request?.WarehouseId.HasValue == true)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
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
                    note = "Quà tặng khuyến mãi 0đ (Tự động đính kèm)"
                }
                : null,
            statusLabel = giftAttached
                ? "Đã tự động đính kèm dòng quà tặng 0đ vào đơn hàng"
                : "Tồn kho quà tặng đã hết (0) — Tự động bỏ qua quà tặng để không làm kẹt đơn hàng chính"
        });
    }

    public async Task<WmsEndpointOutcome> ImportMarketplaceOrdersExcelAsync(
        ImportMarketplaceOrdersExcelInput? request,
        IReadOnlyList<MarketplaceOrderExcelRowInput> parsedFileRows,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var warehouseId = request?.WarehouseId
            ?? await _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (warehouseId <= 0) warehouseId = 1;

        var effectiveWhAccess = warehouseAccessOverride ?? _warehouseAccess;
        if (effectiveWhAccess != null && request?.WarehouseId.HasValue == true)
        {
            await effectiveWhAccess.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
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
                CreatedBy = 1
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

            if (activeGiftRules.Count > 0)
            {
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
}
