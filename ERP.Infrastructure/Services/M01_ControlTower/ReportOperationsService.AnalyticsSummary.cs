using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> GetAnalyticsSummaryAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        var accessibleIds = await GetAccessibleIdsAsync(effectiveAuth, cancellationToken);
        if (warehouseId.HasValue)
        {
            if (effectiveAuth != null)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            if (!accessibleIds.Contains(warehouseId.Value))
                return WmsEndpointOutcome.Forbid();
        }

        var scopedIds = warehouseId.HasValue ? new[] { warehouseId.Value } : accessibleIds.ToArray();
        days = Math.Clamp(days, 1, 365);
        var since = DateTime.UtcNow.AddDays(-days);

        var inboundPostedCount = await _db.ImportReceipts.AsNoTracking()
            .CountAsync(r => scopedIds.Contains(r.WarehouseId) && r.Status == ReceiptStatus.Posted && r.CreatedAt >= since, cancellationToken);
        var putawayCompletedCount = await _db.PutawayTasks.AsNoTracking()
            .CountAsync(t => scopedIds.Contains(t.WarehouseId) && t.Status == PutawayTaskStatus.Completed && t.CreatedAt >= since, cancellationToken);
        var putawayOpenCount = await _db.PutawayTasks.AsNoTracking()
            .CountAsync(t => scopedIds.Contains(t.WarehouseId) && t.Status != PutawayTaskStatus.Completed && t.Status != PutawayTaskStatus.Cancelled && t.CreatedAt >= since, cancellationToken);
        var outboundPostedExportsWithoutShipment = await _db.ExportReceipts.AsNoTracking()
            .CountAsync(r => scopedIds.Contains(r.WarehouseId) &&
                             r.Status == ReceiptStatus.Posted &&
                             r.CreatedAt >= since &&
                             !_db.Shipments.Any(s =>
                                 (s.SourceId == r.Id || s.SourceCode == r.Code) &&
                                 (s.Status == ShipmentStatus.Delivered || s.Status == ShipmentStatus.Completed || s.Status == ShipmentStatus.Dispatched || s.Status == ShipmentStatus.InTransit)),
                        cancellationToken);
        var deliveredShipments = await _db.Shipments.AsNoTracking()
            .CountAsync(s => scopedIds.Contains(s.WarehouseId) &&
                             s.CreatedAt >= since &&
                             (s.Status == ShipmentStatus.Delivered || s.Status == ShipmentStatus.Completed || s.Status == ShipmentStatus.Dispatched || s.Status == ShipmentStatus.InTransit), cancellationToken);
        var failedShipments = await _db.Shipments.AsNoTracking()
            .CountAsync(s => scopedIds.Contains(s.WarehouseId) &&
                             s.CreatedAt >= since &&
                             (s.Status == ShipmentStatus.DeliveryFailed || s.Status == ShipmentStatus.ReturnToWarehouse), cancellationToken);
        var totalTrackedShipments = deliveredShipments + failedShipments;
        var deliverySuccessRatePct = totalTrackedShipments > 0
            ? Math.Round(deliveredShipments * 100m / totalTrackedShipments, 1)
            : 100m;

        var stocktakeTotalCount = await _db.Stocktakes.AsNoTracking()
            .CountAsync(s => scopedIds.Contains(s.WarehouseId) && s.CreatedAt >= since, cancellationToken);
        var stocktakeApprovedCount = await _db.Stocktakes.AsNoTracking()
            .CountAsync(
                s => scopedIds.Contains(s.WarehouseId) &&
                     (s.Status == StocktakeStatus.Approved ||
                      s.Status == StocktakeStatus.Completed ||
                      s.Status == StocktakeStatus.Posted) &&
                     s.CreatedAt >= since,
                cancellationToken);
        var stocktakeDetailsQuery = _db.StocktakeDetails.AsNoTracking()
            .Where(d => scopedIds.Contains(d.Stocktake.WarehouseId) && d.Stocktake.CreatedAt >= since);
        var totalStocktakeLines = await stocktakeDetailsQuery.CountAsync(cancellationToken);
        var countedLineCount = await stocktakeDetailsQuery.CountAsync(d => d.ActualQuantity.HasValue, cancellationToken);
        var stocktakeLineCount = countedLineCount > 0 ? countedLineCount : totalStocktakeLines;
        var stocktakeAccurateLineCount = countedLineCount > 0
            ? await stocktakeDetailsQuery.CountAsync(d => d.ActualQuantity.HasValue && d.DifferenceQuantity == 0, cancellationToken)
            : totalStocktakeLines;
        var stocktakeAccuracyRatePct = stocktakeLineCount > 0
            ? Math.Round(stocktakeAccurateLineCount * 100m / stocktakeLineCount, 1)
            : 100m;

        var onHandByProduct = await _db.InventoryStocks.AsNoTracking()
            .Where(s => scopedIds.Contains(s.WarehouseId) && s.Quantity > 0)
            .GroupBy(s => new { s.ProductId, s.Product.Code, s.Product.Name, UnitName = s.Product.Unit != null ? s.Product.Unit.Name : null })
            .Select(g => new
            {
                g.Key.ProductId,
                ProductCode = g.Key.Code,
                ProductName = g.Key.Name,
                g.Key.UnitName,
                CurrentOnHand = g.Sum(x => x.Quantity)
            })
            .ToListAsync(cancellationToken);

        var issuedByProduct = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => scopedIds.Contains(t.WarehouseId) &&
                        t.TransactionDate >= since &&
                        t.TransactionType != TransactionType.Move &&
                        (t.TransactionType == TransactionType.Export ||
                         t.TransactionType == TransactionType.Ship ||
                         t.TransactionType == TransactionType.TransferOut ||
                         t.TransactionType == TransactionType.AdjustmentDecrease))
            .GroupBy(t => t.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                IssuedQty = g.Sum(x => x.Quantity < 0 ? -x.Quantity : x.Quantity)
            })
            .ToListAsync(cancellationToken);

        var periodInByProduct = await _db.InventoryTransactions.AsNoTracking()
            .Where(t => scopedIds.Contains(t.WarehouseId) &&
                        t.TransactionDate >= since &&
                        t.TransactionType != TransactionType.Move &&
                        (t.TransactionType == TransactionType.Import ||
                         t.TransactionType == TransactionType.TransferIn ||
                         t.TransactionType == TransactionType.AdjustmentIncrease))
            .GroupBy(t => t.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                InboundQty = g.Sum(x => x.Quantity < 0 ? -x.Quantity : x.Quantity)
            })
            .ToListAsync(cancellationToken);

        var issuedMap = issuedByProduct.ToDictionary(x => x.ProductId, x => x.IssuedQty);
        var periodInMap = periodInByProduct.ToDictionary(x => x.ProductId, x => x.InboundQty);
        var turnoverItems = onHandByProduct
            .Select(p =>
            {
                var issued = issuedMap.GetValueOrDefault(p.ProductId, 0m);
                var periodIn = periodInMap.GetValueOrDefault(p.ProductId, 0m);
                var ratio = p.CurrentOnHand > 0 ? Math.Round(issued / p.CurrentOnHand, 2) : (issued > 0 ? 1m : 0m);
                var daysOnHand = issued > 0 ? Math.Round((p.CurrentOnHand / issued) * days, 1) : 999m;
                var movementClass = issued <= 0
                    ? "NON_MOVING"
                    : ratio >= 0.4m
                        ? "FAST_MOVING"
                        : ratio >= 0.1m
                            ? "NORMAL"
                            : "SLOW_MOVING";
                return new
                {
                    productId = p.ProductId,
                    productCode = (p.ProductCode ?? string.Empty).Trim(),
                    productName = (p.ProductName ?? string.Empty).Trim(),
                    unitName = ReportOperationsTextAndFallbackHelper.NormalizeUom(p.UnitName, "THÙNG"),
                    currentOnHand = p.CurrentOnHand,
                    periodInQuantity = periodIn,
                    issuedLastPeriod = issued,
                    turnoverRatio = ratio,
                    daysOnHand,
                    movementClass,
                    movementClassLabel = movementClass == "FAST_MOVING" ? "Luân chuyển nhanh" : movementClass == "NORMAL" ? "Luân chuyển bình thường" : movementClass == "SLOW_MOVING" ? "Luân chuyển chậm" : "Chưa phát sinh xuất"
                };
            })
            .OrderByDescending(x => x.turnoverRatio)
            .ThenByDescending(x => x.currentOnHand)
            .Take(30)
            .ToList();

        var totalPutawayTasks = putawayCompletedCount + putawayOpenCount;
        var openPickingBacklogCount = await _db.PickingTasks.AsNoTracking()
            .CountAsync(t => scopedIds.Contains(t.WarehouseId) && t.Status != PickingTaskStatus.Completed && t.Status != PickingTaskStatus.Cancelled, cancellationToken);

        var inboundReceiptSamples = (await _db.ImportReceipts.AsNoTracking()
            .Where(r => scopedIds.Contains(r.WarehouseId) &&
                        r.CreatedAt >= since &&
                        (r.Status == ReceiptStatus.Posted || r.Status == ReceiptStatus.Approved) &&
                        r.ApprovedAt.HasValue)
            .Select(r => new { r.CreatedAt, EndAt = r.ApprovedAt!.Value })
            .ToListAsync(cancellationToken))
            .Where(x => x.EndAt > x.CreatedAt)
            .ToList();

        var inboundPutawaySamples = (await _db.PutawayTasks.AsNoTracking()
            .Where(t => scopedIds.Contains(t.WarehouseId) &&
                        t.CreatedAt >= since &&
                        t.Status == PutawayTaskStatus.Completed &&
                        t.CompletedAt.HasValue)
            .Select(t => new { t.CreatedAt, EndAt = t.CompletedAt!.Value })
            .ToListAsync(cancellationToken))
            .Where(x => x.EndAt > x.CreatedAt)
            .ToList();

        var effectiveInboundSamples = inboundReceiptSamples.Count > 0 ? inboundReceiptSamples : inboundPutawaySamples;
        var avgInboundTurnaroundHours = effectiveInboundSamples.Count > 0
            ? Math.Round((decimal)effectiveInboundSamples.Average(x => (x.EndAt - x.CreatedAt).TotalHours), 1)
            : 0m;

        var outboundExportSamples = (await _db.ExportReceipts.AsNoTracking()
            .Where(r => scopedIds.Contains(r.WarehouseId) &&
                        r.CreatedAt >= since &&
                        (r.Status == ReceiptStatus.Dispatched || r.Status == ReceiptStatus.Posted || r.Status == ReceiptStatus.Approved) &&
                        (r.DispatchedAt.HasValue || r.ApprovedAt.HasValue))
            .Select(r => new { r.CreatedAt, EndAt = r.DispatchedAt ?? r.ApprovedAt!.Value })
            .ToListAsync(cancellationToken))
            .Where(x => x.EndAt > x.CreatedAt)
            .ToList();

        var outboundShipmentSamples = (await _db.Shipments.AsNoTracking()
            .Where(s => scopedIds.Contains(s.WarehouseId) &&
                        s.CreatedAt >= since &&
                        (s.Status == ShipmentStatus.Delivered || s.Status == ShipmentStatus.Completed || s.Status == ShipmentStatus.Dispatched || s.Status == ShipmentStatus.InTransit) &&
                        (s.CompletedAt.HasValue || s.DispatchedAt.HasValue))
            .Select(s => new { s.CreatedAt, EndAt = s.CompletedAt ?? s.DispatchedAt!.Value })
            .ToListAsync(cancellationToken))
            .Where(x => x.EndAt > x.CreatedAt)
            .ToList();

        var effectiveOutboundSamples = outboundExportSamples.Count > 0 ? outboundExportSamples : outboundShipmentSamples;
        var avgOutboundTurnaroundHours = effectiveOutboundSamples.Count > 0
            ? Math.Round((decimal)effectiveOutboundSamples.Average(x => (x.EndAt - x.CreatedAt).TotalHours), 1)
            : 0m;
        var todayUtc = DateTime.UtcNow.Date;
        var totalAvailableQty = (await _db.InventoryStocks.AsNoTracking()
            .Where(s => scopedIds.Contains(s.WarehouseId) && s.Status == InventoryStatus.Available)
            .Select(s => new { s.Quantity, s.ReservedQuantity, ExpiryDate = s.Lot != null ? s.Lot.ExpiryDate : (DateTime?)null })
            .ToListAsync(cancellationToken))
            .Where(s => !s.ExpiryDate.HasValue || s.ExpiryDate.Value.Date >= todayUtc)
            .Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity));

        return WmsEndpointOutcome.Ok(new
        {
            currentAvailableStock = totalAvailableQty,
            totalAvailableQty,
            totalAtpQuantity = totalAvailableQty,
            inboundPostedCount,
            putawayCompletedCount,
            putawayOpenCount,
            outboundShippedCount = outboundPostedExportsWithoutShipment + deliveredShipments,
            deliverySuccessRatePct,
            stocktakeTotalCount,
            stocktakeApprovedCount,
            stocktakeLineCount,
            stocktakeAccurateLineCount,
            stocktakeAccuracyRatePct,
            avgInboundTurnaroundHours,
            avgOutboundTurnaroundHours,
            avgInboundLeadTimeHours = avgInboundTurnaroundHours,
            avgOutboundLeadTimeHours = avgOutboundTurnaroundHours,
            openPickingBacklogCount,
            inboundOutboundSla = new
            {
                inboundTotalReceipts = inboundPostedCount,
                inboundPostedReceipts = inboundPostedCount,
                completedReceiptsCount = inboundPostedCount,
                avgInboundTurnaroundHours,
                avgInboundLeadTimeHours = avgInboundTurnaroundHours,
                openPutawayBacklogCount = putawayOpenCount,
                inboundCompletedPutawayTasks = putawayCompletedCount,
                inboundTotalPutawayTasks = totalPutawayTasks,
                inboundCompletionRatePercent = totalPutawayTasks > 0
                    ? Math.Round(putawayCompletedCount * 100m / totalPutawayTasks, 1)
                    : 100m,
                outboundTotalShipments = totalTrackedShipments,
                outboundDeliveredShipments = deliveredShipments,
                dispatchedExportsCount = outboundPostedExportsWithoutShipment + deliveredShipments,
                avgOutboundTurnaroundHours,
                avgOutboundLeadTimeHours = avgOutboundTurnaroundHours,
                openPickingBacklogCount,
                outboundDeliverySuccessRatePercent = deliverySuccessRatePct
            },
            stocktakeAccuracy = new
            {
                totalStocktakes = stocktakeTotalCount,
                approvedStocktakes = stocktakeApprovedCount,
                totalLinesCounted = stocktakeLineCount,
                accurateLinesCount = stocktakeAccurateLineCount,
                lineAccuracyPercent = stocktakeAccuracyRatePct
            },
            turnoverItems,
            turnoverRows = turnoverItems
        });
    }
}
