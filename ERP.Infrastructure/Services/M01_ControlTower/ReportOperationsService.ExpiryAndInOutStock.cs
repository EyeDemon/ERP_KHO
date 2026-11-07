using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ReportOperationsService
{
    public async Task<WmsEndpointOutcome> GetExpiryAgingReportAsync(
        IWarehouseAuthorizationService? warehouseAuth = null,
        int? warehouseId = null,
        int? productId = null,
        int nearExpiryDays = 30,
        bool onlyLots = false,
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

        if (nearExpiryDays < 1) nearExpiryDays = 30;
        var today = DateTime.UtcNow.Date;
        var nearThreshold = today.AddDays(nearExpiryDays);

        var query = _db.InventoryStocks.AsNoTracking()
            .Where(s => accessibleIds.Contains(s.WarehouseId) && s.Quantity > 0);

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        if (productId.HasValue)
            query = query.Where(s => s.ProductId == productId.Value);
        if (onlyLots)
            query = query.Where(s => s.LotId.HasValue);

        var rows = await query
            .Select(s => new
            {
                InventoryStockId = s.Id,
                s.WarehouseId,
                WarehouseName = s.Warehouse.Name,
                s.LocationId,
                LocationCode = s.Location != null ? s.Location.Code : "DEFAULT",
                s.ProductId,
                ProductCode = s.Product.Code,
                ProductName = s.Product.Name,
                UnitName = s.Product.Unit.Name,
                Status = s.Status.ToString(),
                s.LotId,
                LotNumber = s.Lot != null ? s.Lot.LotNumber : null,
                ManufactureDate = s.Lot != null ? s.Lot.ManufactureDate : null,
                ExpiryDate = s.Lot != null ? s.Lot.ExpiryDate : null,
                LotCreatedAt = s.Lot != null ? (DateTime?)s.Lot.CreatedAt : null,
                s.Quantity,
                s.ReservedQuantity,
                AvailableQuantity = s.Quantity - s.ReservedQuantity,
                s.LastUpdated
            })
            .ToListAsync(cancellationToken);

        var result = rows
            .Select(r =>
            {
                var daysToExpiry = r.ExpiryDate.HasValue
                    ? (int?)(r.ExpiryDate.Value.Date - today).TotalDays
                    : null;
                var anchorDate = (r.ManufactureDate ?? r.LotCreatedAt ?? r.LastUpdated).Date;
                var ageDays = Math.Max(0, (int)(today - anchorDate).TotalDays);
                var expiryStatus = !r.ExpiryDate.HasValue
                    ? "NO_EXPIRY"
                    : r.ExpiryDate.Value.Date < today
                        ? "EXPIRED"
                        : r.ExpiryDate.Value.Date <= nearThreshold
                            ? "NEAR_EXPIRY"
                            : "GOOD";

                return new
                {
                    r.InventoryStockId,
                    r.WarehouseId,
                    WarehouseName = (r.WarehouseName ?? string.Empty).Trim(),
                    r.LocationId,
                    LocationCode = (r.LocationCode ?? "DEFAULT").Trim(),
                    r.ProductId,
                    ProductCode = (r.ProductCode ?? string.Empty).Trim(),
                    ProductName = (r.ProductName ?? string.Empty).Trim(),
                    UnitName = ReportOperationsTextAndFallbackHelper.NormalizeUom(r.UnitName),
                    Status = ReportOperationsTextAndFallbackHelper.TranslateInventoryStatusLabel(r.Status),
                    StatusLabel = ReportOperationsTextAndFallbackHelper.TranslateInventoryStatusLabel(r.Status),
                    r.LotId,
                    LotNumber = r.LotNumber != null ? r.LotNumber.Trim() : null,
                    r.ManufactureDate,
                    r.ExpiryDate,
                    r.Quantity,
                    r.ReservedQuantity,
                    AvailableQuantity = (string.Equals((r.Status ?? string.Empty).Trim(), "Available", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals((r.Status ?? string.Empty).Trim(), "Khả dụng", StringComparison.OrdinalIgnoreCase)) &&
                                        (!r.ExpiryDate.HasValue || r.ExpiryDate.Value.Date >= today)
                        ? Math.Max(0m, r.Quantity - r.ReservedQuantity)
                        : 0m,
                    DaysToExpiry = daysToExpiry,
                    AgeDays = ageDays,
                    ExpiryStatus = expiryStatus,
                    ExpiryStatusLabel = expiryStatus == "EXPIRED" ? "Đã hết hạn" : expiryStatus == "NEAR_EXPIRY" ? "Cận hạn sử dụng" : expiryStatus == "GOOD" ? "Còn hạn tốt" : "Không quản lý hạn",
                    AgingBucket = ageDays <= 30 ? "0-30 ngày" : ageDays <= 90 ? "31-90 ngày" : "Trên 90 ngày"
                };
            })
            .OrderBy(r => r.ExpiryStatus == "EXPIRED" ? 0 : r.ExpiryStatus == "NEAR_EXPIRY" ? 1 : r.ExpiryStatus == "GOOD" ? 2 : 3)
            .ThenBy(r => r.DaysToExpiry ?? int.MaxValue)
            .ThenByDescending(r => r.AgeDays)
            .ThenBy(r => r.ProductCode)
            .ToList();

        return WmsEndpointOutcome.Ok(result);
    }

    public async Task<IReadOnlyList<InventoryInOutReportDto>> ComputeInventoryInOutStockAsync(
        IWarehouseAuthorizationService? warehouseAuth,
        DateTime? fromDate,
        DateTime? toDate,
        int? warehouseId,
        int? productId,
        CancellationToken cancellationToken = default)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
        {
            throw new BusinessRuleException("Từ ngày không được lớn hơn đến ngày.");
        }

        var effectiveAuth = warehouseAuth ?? _warehouseAuth;
        IReadOnlyList<int>? allowedWarehouseIds = null;
        if (effectiveAuth is not null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
                allowedWarehouseIds = new[] { warehouseId.Value };
            }
            else
            {
                allowedWarehouseIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
            }
        }

        var query = _db.InventoryTransactions.AsNoTracking()
            .Where(t => t.InventoryStatus == InventoryStatus.Available &&
                        t.TransactionType != TransactionType.Move &&
                        (t.TransactionType == TransactionType.Import ||
                         t.TransactionType == TransactionType.Export ||
                         t.TransactionType == TransactionType.Ship ||
                         t.TransactionType == TransactionType.AdjustmentIncrease ||
                         t.TransactionType == TransactionType.AdjustmentDecrease ||
                         t.TransactionType == TransactionType.TransferIn ||
                         t.TransactionType == TransactionType.TransferOut));

        if (warehouseId.HasValue)
        {
            query = query.Where(t => t.WarehouseId == warehouseId.Value);
        }
        else if (allowedWarehouseIds is not null)
        {
            query = query.Where(t => allowedWarehouseIds.Contains(t.WarehouseId));
        }

        if (productId.HasValue)
        {
            query = query.Where(t => t.ProductId == productId.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(t => t.TransactionDate < toDate.Value);
        }

        var txRows = await query
            .Select(t => new
            {
                t.ProductId,
                t.WarehouseId,
                t.TransactionType,
                t.Quantity,
                t.TransactionDate
            })
            .ToListAsync(cancellationToken);

        if (txRows.Count == 0)
        {
            return Array.Empty<InventoryInOutReportDto>();
        }

        var productIds = txRows.Select(x => x.ProductId).Distinct().ToList();
        var warehouseIds = txRows.Select(x => x.WarehouseId).Distinct().ToList();

        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                UnitName = p.Unit != null ? p.Unit.Name : string.Empty
            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var warehouses = await _db.Warehouses.AsNoTracking()
            .Where(w => warehouseIds.Contains(w.Id))
            .Select(w => new { w.Id, w.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var report = txRows
            .GroupBy(x => new { x.ProductId, x.WarehouseId })
            .Select(g =>
            {
                products.TryGetValue(g.Key.ProductId, out var prod);
                warehouses.TryGetValue(g.Key.WarehouseId, out var whName);

                var opening = fromDate.HasValue
                    ? g.Where(x => x.TransactionDate < fromDate.Value)
                        .Sum(x => x.TransactionType is TransactionType.Import or TransactionType.TransferIn or TransactionType.AdjustmentIncrease
                            ? x.Quantity
                            : x.TransactionType is TransactionType.Export or TransactionType.Ship or TransactionType.TransferOut or TransactionType.AdjustmentDecrease
                                ? -x.Quantity
                                : 0m)
                    : 0m;

                var periodRows = fromDate.HasValue
                    ? g.Where(x => x.TransactionDate >= fromDate.Value).ToList()
                    : g.ToList();

                var importQty = periodRows.Where(x => x.TransactionType == TransactionType.Import).Sum(x => x.Quantity);
                var transferInQty = periodRows.Where(x => x.TransactionType == TransactionType.TransferIn).Sum(x => x.Quantity);
                var adjIncQty = periodRows.Where(x => x.TransactionType == TransactionType.AdjustmentIncrease).Sum(x => x.Quantity);
                var inQty = importQty + transferInQty + adjIncQty;

                var exportQty = periodRows.Where(x => x.TransactionType is TransactionType.Export or TransactionType.Ship).Sum(x => x.Quantity);
                var transferOutQty = periodRows.Where(x => x.TransactionType == TransactionType.TransferOut).Sum(x => x.Quantity);
                var adjDecQty = periodRows.Where(x => x.TransactionType == TransactionType.AdjustmentDecrease).Sum(x => x.Quantity);
                var outQty = exportQty + transferOutQty + adjDecQty;

                return new InventoryInOutReportDto
                {
                    ProductId = g.Key.ProductId,
                    ProductCode = (prod?.Code ?? string.Empty).Trim(),
                    ProductName = (prod?.Name ?? string.Empty).Trim(),
                    UnitName = ReportOperationsTextAndFallbackHelper.NormalizeUom(prod?.UnitName, "THÙNG"),
                    WarehouseId = g.Key.WarehouseId,
                    WarehouseName = (whName ?? string.Empty).Trim(),
                    OpeningQuantity = decimal.Round(opening, 4),
                    ImportQuantity = decimal.Round(importQty, 4),
                    TransferInQuantity = decimal.Round(transferInQty, 4),
                    AdjustmentIncreaseQuantity = decimal.Round(adjIncQty, 4),
                    InQuantity = decimal.Round(inQty, 4),
                    ExportQuantity = decimal.Round(exportQty, 4),
                    TransferOutQuantity = decimal.Round(transferOutQty, 4),
                    AdjustmentDecreaseQuantity = decimal.Round(adjDecQty, 4),
                    OutQuantity = decimal.Round(outQty, 4),
                    ClosingQuantity = decimal.Round(opening + inQty - outQty, 4)
                };
            })
            .OrderBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.WarehouseName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return report;
    }
}
