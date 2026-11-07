using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderBackorderService
{
    private IQueryable<SalesOrder> OrderQuery() =>
        context.SalesOrders
            .AsSplitQuery()
            .Include(x => x.Customer)
            .Include(x => x.Warehouse)
            .Include(x => x.Lines).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
            .Include(x => x.Lines).ThenInclude(x => x.Backorder);

    private IQueryable<Backorder> BackorderQuery() =>
        context.Backorders
            .AsSplitQuery()
            .Include(x => x.Warehouse)
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.Product)
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.SalesOrder).ThenInclude(x => x.Customer)
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.SalesOrder).ThenInclude(x => x.Warehouse);

    private async Task<SalesOrderListDto> BuildOrderDtoAsync(
        SalesOrder order,
        bool includeLines,
        CancellationToken token)
    {
        var progress = await BuildLineProgressAsync(order, token);
        return MapOrderDto(order, progress, includeLines);
    }

    private static string NormalizeUomCode(string? code, string? fallback)
    {
        var raw = !string.IsNullOrWhiteSpace(code) ? code.Trim() : (fallback?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(raw) ||
            string.Equals(raw, "QA_UNIT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(raw, "QA-PCS", StringComparison.OrdinalIgnoreCase))
            return "THÙNG";
        return raw;
    }

    private static SalesOrderListDto MapOrderDto(
        SalesOrder order,
        IReadOnlyDictionary<int, LineProgress> progress,
        bool includeLines)
    {
        var lines = order.Lines.OrderBy(x => x.Id).Select(line =>
        {
            var p = progress.GetValueOrDefault(line.ProductId) ?? new LineProgress(0m, 0m, 0m, 0m, 0m);
            var effectiveOrdered = Math.Max(0m, line.OrderedQuantity - line.CancelledQuantity);
            var lineSubTotal = decimal.Round(effectiveOrdered * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
            var lineTax = decimal.Round(lineSubTotal * (line.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero);
            return new SalesOrderLineDto
            {
                Id = line.Id,
                ExternalLineId = (line.ExternalLineId ?? string.Empty).Trim(),
                ProductId = line.ProductId,
                ProductCode = (line.Product?.Code ?? string.Empty).Trim(),
                ProductName = (line.Product?.Name ?? string.Empty).Trim(),
                UomCode = NormalizeUomCode(line.UomCode, line.Product?.Unit?.Code),
                OrderedQuantity = line.OrderedQuantity,
                UnitPrice = line.UnitPrice,
                TaxPercent = line.TaxPercent,
                LineSubTotal = lineSubTotal,
                LineTaxAmount = lineTax,
                LineTotal = lineSubTotal + lineTax,
                ReservedQuantity = p.Reserved,
                AllocatedQuantity = p.Allocated,
                PickedQuantity = p.Picked,
                ShippedQuantity = p.Shipped,
                BackorderQuantity = p.Backorder,
                CancelledQuantity = line.CancelledQuantity,
                OpenQuantity = Math.Max(0m, line.OrderedQuantity - p.Shipped - line.CancelledQuantity)
            };
        }).ToList();

        var ordered = lines.Sum(x => x.OrderedQuantity);
        var shipped = lines.Sum(x => x.ShippedQuantity);
        var cancelled = lines.Sum(x => x.CancelledQuantity);
        var open = lines.Sum(x => x.OpenQuantity);
        var subTotal = lines.Sum(x => x.LineSubTotal);
        var taxAmount = lines.Sum(x => x.LineTaxAmount);
        var status = EffectiveOrderStatus(order, shipped, open, cancelled);

        SalesOrderListDto dto = includeLines ? new SalesOrderDto() : new SalesOrderListDto();
        dto.Id = order.Id;
        dto.OrderCode = (order.OrderCode ?? string.Empty).Trim();
        dto.ExternalOrderId = (order.ExternalOrderId ?? string.Empty).Trim();
        dto.CustomerId = order.CustomerId;
        dto.CustomerCode = (order.Customer?.Code ?? string.Empty).Trim();
        dto.CustomerName = (order.Customer?.Name ?? string.Empty).Trim();
        dto.WarehouseId = order.WarehouseId;
        dto.WarehouseCode = (order.Warehouse?.Code ?? string.Empty).Trim();
        dto.WarehouseName = (order.Warehouse?.Name ?? string.Empty).Trim();
        dto.RequestedShipDate = order.RequestedShipDate;
        dto.Priority = order.Priority;
        dto.ShippingMethod = order.ShippingMethod?.Trim();
        dto.Currency = string.IsNullOrWhiteSpace(order.Currency) ? "VND" : order.Currency.Trim().ToUpperInvariant();
        dto.Status = status.ToString();
        dto.OrderedQuantity = ordered;
        dto.ReservedQuantity = lines.Sum(x => x.ReservedQuantity);
        dto.AllocatedQuantity = lines.Sum(x => x.AllocatedQuantity);
        dto.PickedQuantity = lines.Sum(x => x.PickedQuantity);
        dto.ShippedQuantity = shipped;
        dto.BackorderQuantity = lines.Sum(x => x.BackorderQuantity);
        dto.CancelledQuantity = cancelled;
        dto.OpenQuantity = open;
        dto.SubTotalAmount = subTotal;
        dto.TaxAmount = taxAmount;
        dto.TotalAmount = subTotal + taxAmount;
        dto.CreatedAt = order.CreatedAt;
        dto.ReleasedAt = order.ReleasedAt;

        if (dto is SalesOrderDto detail)
        {
            detail.RowVersion = Convert.ToBase64String(order.RowVersion);
            detail.Lines = lines;
        }
        return dto;
    }

    private async Task<Dictionary<int, Dictionary<int, LineProgress>>> BuildBatchLineProgressAsync(
        IReadOnlyCollection<SalesOrder> orders,
        CancellationToken token)
    {
        if (orders.Count == 0)
            return new Dictionary<int, Dictionary<int, LineProgress>>();

        var orderIds = orders.Select(x => x.Id).Distinct().ToArray();

        var demandLines = await context.SalesOrderLines.AsNoTracking()
            .Include(x => x.Backorder)
            .Where(x => orderIds.Contains(x.SalesOrderId))
            .ToListAsync(token);

        var reservations = await context.StockReservations.AsNoTracking()
            .Where(x => x.SourceType == "SalesOrder" && x.SourceId.HasValue && orderIds.Contains(x.SourceId.Value))
            .ToListAsync(token);
        var reservationIds = reservations.Select(x => x.Id).ToArray();
        var reservationOrderMap = reservations.ToDictionary(x => x.Id, x => x.SourceId!.Value);

        var allocatedRows = reservationIds.Length == 0
            ? []
            : await context.StockAllocations.AsNoTracking()
                .Where(x => reservationIds.Contains(x.ReservationId) && CountedAllocationStatuses.Contains(x.Status))
                .GroupBy(x => new { x.ReservationId, x.ProductId })
                .Select(g => new { g.Key.ReservationId, g.Key.ProductId, Quantity = g.Sum(x => x.Quantity) })
                .ToListAsync(token);

        var allocatedByOrderProduct = allocatedRows
            .GroupBy(x => (OrderId: reservationOrderMap[x.ReservationId], x.ProductId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var pickedRows = await context.PickingTaskLines.AsNoTracking()
            .Where(x => x.PickingTask.SourceType == "SalesOrder" &&
                        x.PickingTask.SourceId.HasValue &&
                        orderIds.Contains(x.PickingTask.SourceId.Value) &&
                        x.PickingTask.Status != PickingTaskStatus.Cancelled)
            .GroupBy(x => new { OrderId = x.PickingTask.SourceId!.Value, x.ProductId })
            .Select(g => new { g.Key.OrderId, g.Key.ProductId, Quantity = g.Sum(x => x.PickedQuantity) })
            .ToListAsync(token);
        var pickedByOrderProduct = pickedRows.ToDictionary(x => (x.OrderId, x.ProductId), x => x.Quantity);

        var shipmentSessions = await context.Shipments.AsNoTracking()
            .Where(x => x.SourceType == "SalesOrder" &&
                        x.SourceId.HasValue &&
                        orderIds.Contains(x.SourceId.Value) &&
                        (x.Status == ShipmentStatus.Dispatched ||
                         x.Status == ShipmentStatus.InTransit ||
                         x.Status == ShipmentStatus.Delivered ||
                         x.Status == ShipmentStatus.Completed ||
                         x.Status == ShipmentStatus.DeliveryFailed ||
                         x.Status == ShipmentStatus.ReturnToWarehouse))
            .Select(x => new { OrderId = x.SourceId!.Value, x.PackingSessionId })
            .ToListAsync(token);

        var packingSessionIds = shipmentSessions.Select(x => x.PackingSessionId).Distinct().ToArray();
        var shippedRows = packingSessionIds.Length == 0
            ? []
            : await context.HandlingUnitContents.AsNoTracking()
                .Where(x => packingSessionIds.Contains(x.HandlingUnit.PackingSessionId))
                .GroupBy(x => new { x.HandlingUnit.PackingSessionId, x.ProductId })
                .Select(g => new { g.Key.PackingSessionId, g.Key.ProductId, Quantity = g.Sum(x => x.Quantity) })
                .ToListAsync(token);

        var shippedBySession = shippedRows
            .GroupBy(x => x.PackingSessionId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ProductId, x => x.Quantity));

        var shippedByOrderProduct = new Dictionary<(int OrderId, int ProductId), decimal>();
        foreach (var s in shipmentSessions)
        {
            if (!shippedBySession.TryGetValue(s.PackingSessionId, out var prodMap)) continue;
            foreach (var (prodId, qty) in prodMap)
            {
                var key = (s.OrderId, prodId);
                shippedByOrderProduct[key] = shippedByOrderProduct.GetValueOrDefault(key) + qty;
            }
        }

        var reservationByOrderProduct = reservations
            .GroupBy(x => (x.SourceId!.Value, x.ProductId))
            .ToDictionary(g => g.Key, g => g.First());

        var result = orderIds.ToDictionary(id => id, _ => new Dictionary<int, LineProgress>());
        foreach (var line in demandLines)
        {
            var key = (line.SalesOrderId, line.ProductId);
            var reservation = reservationByOrderProduct.GetValueOrDefault(key);
            var backorder = line.Backorder;
            var remainingBackorder = backorder is null
                ? 0m
                : Math.Max(0m, backorder.Quantity - backorder.RecoveredQuantity - backorder.CancelledQuantity);
            result[line.SalesOrderId][line.ProductId] = new LineProgress(
                reservation is null
                    ? 0m
                    : Math.Max(0m, reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity),
                allocatedByOrderProduct.GetValueOrDefault(key),
                pickedByOrderProduct.GetValueOrDefault(key),
                shippedByOrderProduct.GetValueOrDefault(key),
                remainingBackorder);
        }

        return result;
    }

    private async Task<Dictionary<int, LineProgress>> BuildLineProgressAsync(
        SalesOrder order,
        CancellationToken token)
    {
        var batch = await BuildBatchLineProgressAsync([order], token);
        return batch.GetValueOrDefault(order.Id) ?? new Dictionary<int, LineProgress>();
    }

    private BackorderDto MapBackorder(Backorder row, IReadOnlyDictionary<int, LineProgress> progress)
    {
        var line = row.SalesOrderLine;
        var order = line.SalesOrder;
        var p = progress.GetValueOrDefault(line.ProductId) ?? new LineProgress(0m, 0m, 0m, 0m, 0m);
        var remaining = Math.Max(0m, row.Quantity - row.RecoveredQuantity - row.CancelledQuantity);
        var effective = row.Status;
        if (row.Status != BackorderStatus.Cancelled)
        {
            if (remaining > 0)
                effective = row.RecoveredQuantity > 0 ? BackorderStatus.PartiallyAllocated : BackorderStatus.Open;
            else if (p.Shipped >= line.OrderedQuantity - line.CancelledQuantity && row.RecoveredQuantity > 0)
                effective = BackorderStatus.Fulfilled;
            else if (row.RecoveredQuantity > 0)
                effective = BackorderStatus.Allocated;
        }

        return new BackorderDto
        {
            Id = row.Id,
            BackorderCode = row.BackorderCode.Trim(),
            SalesOrderId = order.Id,
            OrderCode = order.OrderCode.Trim(),
            ExternalOrderId = order.ExternalOrderId.Trim(),
            SalesOrderLineId = line.Id,
            WarehouseId = row.WarehouseId,
            WarehouseCode = row.Warehouse.Code.Trim(),
            WarehouseName = row.Warehouse.Name.Trim(),
            ProductId = line.ProductId,
            ProductCode = line.Product.Code.Trim(),
            ProductName = line.Product.Name.Trim(),
            OrderedQuantity = line.OrderedQuantity,
            Quantity = row.Quantity,
            RecoveredQuantity = row.RecoveredQuantity,
            CancelledQuantity = row.CancelledQuantity,
            RemainingQuantity = remaining,
            Status = effective.ToString(),
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            RowVersion = Convert.ToBase64String(row.RowVersion)
        };
    }

    private static SalesOrderStatus EffectiveOrderStatus(
        SalesOrder order,
        decimal shipped,
        decimal open,
        decimal cancelled)
    {
        if (order.Status is SalesOrderStatus.Draft or SalesOrderStatus.Hold or SalesOrderStatus.Cancelled)
            return order.Status;
        if (open <= 0)
            return shipped > 0 ? SalesOrderStatus.Fulfilled : SalesOrderStatus.Cancelled;
        if (shipped > 0)
            return SalesOrderStatus.PartiallyFulfilled;
        _ = cancelled;
        return SalesOrderStatus.Released;
    }

    private sealed record LineProgress(
        decimal Reserved,
        decimal Allocated,
        decimal Picked,
        decimal Shipped,
        decimal Backorder);
}
