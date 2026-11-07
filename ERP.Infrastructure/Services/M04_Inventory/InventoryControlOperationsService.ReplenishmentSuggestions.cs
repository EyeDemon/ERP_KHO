using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    public async Task<WmsEndpointOutcome> ReplenishmentSuggestionsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null) return WmsEndpointOutcome.BadRequest("Database context is required.");
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(warehouseId, effectiveAuth, cancellationToken);
        if (warehouseId.HasValue && !await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == warehouseId.Value, cancellationToken))
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "WAREHOUSE_NOT_FOUND",
                errorCode = "WAREHOUSE_NOT_FOUND",
                message = $"Không tìm thấy kho #{warehouseId.Value}."
            });
        }

        var pickLocationsQuery = db.WarehouseLocations.AsNoTracking()
            .Where(l => l.IsActive && !l.IsBlocked && !l.IsSystemManaged && (l.IsPickable || l.MinBaseQty.HasValue || l.MaxBaseQty.HasValue));
        var activeStocksQuery = db.InventoryStocks.AsNoTracking()
            .Where(s => s.Status == InventoryStatus.Available && s.LocationId.HasValue);
        if (scopedWarehouseIds != null)
        {
            pickLocationsQuery = pickLocationsQuery.Where(l => scopedWarehouseIds.Contains(l.WarehouseId));
            activeStocksQuery = activeStocksQuery.Where(s => scopedWarehouseIds.Contains(s.WarehouseId));
        }

        var pickLocations = await pickLocationsQuery
            .OrderBy(l => l.Code)
            .Select(l => new
            {
                l.Id,
                l.WarehouseId,
                l.Code,
                l.Name,
                l.MinBaseQty,
                l.MaxBaseQty,
                l.ReplenishFromLocationId
            })
            .ToListAsync(cancellationToken);

        var activeStocks = await activeStocksQuery
            .Select(s => new
            {
                s.Id,
                s.WarehouseId,
                WarehouseName = s.Warehouse.Name,
                s.ProductId,
                ProductCode = s.Product.Code,
                ProductName = s.Product.Name,
                LocationId = s.LocationId!.Value,
                LocationCode = s.Location != null ? s.Location.Code : "DEFAULT",
                IsPickableLocation = s.Location != null && s.Location.IsPickable,
                s.LotId,
                LotNumber = s.Lot != null ? s.Lot.LotNumber : null,
                LotExpiryDate = s.Lot != null ? s.Lot.ExpiryDate : null,
                AvailableQty = Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity),
                s.Quantity
            })
            .ToListAsync(cancellationToken);

        var suggestions = new List<object>();
        var coveredTargetProductPairs = new HashSet<(int TargetLocationId, int ProductId)>();

        var configuredPickBins = pickLocations
            .Where(l => l.MinBaseQty.HasValue || l.MaxBaseQty.HasValue)
            .ToList();

        foreach (var targetBin in configuredPickBins)
        {
            var minQty = targetBin.MinBaseQty ?? 20m;
            var maxQty = targetBin.MaxBaseQty ?? Math.Max(minQty + 10m, 100m);
            if (maxQty <= minQty) maxQty = minQty + 10m;

            var candidateProductIds = activeStocks
                .Where(s => s.WarehouseId == targetBin.WarehouseId &&
                            (s.LocationId == targetBin.Id ||
                             (targetBin.ReplenishFromLocationId.HasValue && s.LocationId == targetBin.ReplenishFromLocationId.Value) ||
                             s.AvailableQty > 0m))
                .Select(s => s.ProductId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            foreach (var productId in candidateProductIds)
            {
                var currentAtTarget = activeStocks
                    .Where(s => s.WarehouseId == targetBin.WarehouseId && s.ProductId == productId && s.LocationId == targetBin.Id)
                    .Sum(s => s.Quantity);

                if (currentAtTarget > minQty)
                    continue;

                var neededQty = Math.Max(0m, maxQty - currentAtTarget);
                if (neededQty <= 0m)
                    continue;

                var fefoSources = activeStocks
                    .Where(s => s.WarehouseId == targetBin.WarehouseId &&
                                s.ProductId == productId &&
                                s.LocationId != targetBin.Id &&
                                s.AvailableQty > 0m &&
                                (!targetBin.ReplenishFromLocationId.HasValue || s.LocationId == targetBin.ReplenishFromLocationId.Value))
                    .OrderBy(s => targetBin.ReplenishFromLocationId.HasValue && s.LocationId == targetBin.ReplenishFromLocationId.Value ? 0 : 1)
                    .ThenBy(s => !s.IsPickableLocation ? 0 : 1)
                    .ThenBy(s => s.LotExpiryDate.HasValue ? 0 : 1)
                    .ThenBy(s => s.LotExpiryDate)
                    .ThenBy(s => s.LotId ?? int.MaxValue)
                    .ThenBy(s => s.Id)
                    .ToList();

                if (fefoSources.Count == 0 && targetBin.ReplenishFromLocationId.HasValue)
                {
                    fefoSources = activeStocks
                        .Where(s => s.WarehouseId == targetBin.WarehouseId &&
                                    s.ProductId == productId &&
                                    s.LocationId != targetBin.Id &&
                                    s.AvailableQty > 0m)
                        .OrderBy(s => !s.IsPickableLocation ? 0 : 1)
                        .ThenBy(s => s.LotExpiryDate.HasValue ? 0 : 1)
                        .ThenBy(s => s.LotExpiryDate)
                        .ThenBy(s => s.LotId ?? int.MaxValue)
                        .ThenBy(s => s.Id)
                        .ToList();
                }

                if (fefoSources.Count == 0)
                    continue;

                var totalSourceAvailable = fefoSources.Sum(s => s.AvailableQty);
                var suggestedQty = Math.Min(neededQty, totalSourceAvailable);
                var primarySrc = fefoSources[0];

                var remToPlan = suggestedQty;
                var fefoLots = new List<object>();
                for (var idx = 0; idx < fefoSources.Count; idx++)
                {
                    var fs = fefoSources[idx];
                    var plannedTake = remToPlan > 0m ? Math.Min(fs.AvailableQty, remToPlan) : 0m;
                    remToPlan -= plannedTake;
                    fefoLots.Add(new
                    {
                        fefoRank = idx + 1,
                        inventoryStockId = fs.Id,
                        sourceLocationId = fs.LocationId,
                        sourceLocationCode = fs.LocationCode.Trim(),
                        lotId = fs.LotId,
                        lotNumber = fs.LotNumber,
                        expiryDate = fs.LotExpiryDate,
                        availableQty = fs.AvailableQty,
                        suggestedTakeQty = plannedTake
                    });
                }

                suggestions.Add(new
                {
                    inventoryStockId = primarySrc.Id,
                    productId = primarySrc.ProductId,
                    productCode = primarySrc.ProductCode.Trim(),
                    productName = primarySrc.ProductName.Trim(),
                    warehouseId = primarySrc.WarehouseId,
                    warehouseName = primarySrc.WarehouseName.Trim(),
                    sourceLocationId = primarySrc.LocationId,
                    sourceLocationCode = primarySrc.LocationCode.Trim(),
                    fromLocationId = primarySrc.LocationId,
                    fromLocationCode = primarySrc.LocationCode.Trim(),
                    availableAtSource = totalSourceAvailable,
                    targetLocationId = targetBin.Id,
                    targetLocationCode = targetBin.Code.Trim(),
                    toLocationId = targetBin.Id,
                    toLocationCode = targetBin.Code.Trim(),
                    currentAtTarget,
                    currentTargetQty = currentAtTarget,
                    currentPickFaceQty = currentAtTarget,
                    minBaseQty = minQty,
                    maxBaseQty = maxQty,
                    neededQty,
                    suggestedQuantity = suggestedQty,
                    suggestedReplenishQty = suggestedQty,
                    sourceLotId = primarySrc.LotId,
                    sourceLotNumber = primarySrc.LotNumber,
                    sourceLotExpiryDate = primarySrc.LotExpiryDate,
                    fefoRank = 1,
                    fefoLots,
                    replenishFromLocationId = targetBin.ReplenishFromLocationId,
                    priority = currentAtTarget <= 0m ? "CRITICAL" : "HIGH",
                    triggerReason = "PICK_FACE_BELOW_MIN_FEFO"
                });
                coveredTargetProductPairs.Add((targetBin.Id, productId));
            }
        }

        var sourceCandidates = activeStocks
            .Where(s => s.AvailableQty > 0 && (!s.IsPickableLocation || s.AvailableQty >= 15m))
            .OrderBy(s => s.IsPickableLocation ? 1 : 0)
            .ThenBy(s => s.LotExpiryDate.HasValue ? 0 : 1)
            .ThenBy(s => s.LotExpiryDate)
            .ThenByDescending(s => s.AvailableQty)
            .ToList();

        foreach (var src in sourceCandidates)
        {
            var candidatePickBins = pickLocations
                .Where(l => l.WarehouseId == src.WarehouseId && l.Id != src.LocationId)
                .ToList();

            foreach (var targetBin in candidatePickBins)
            {
                if (coveredTargetProductPairs.Contains((targetBin.Id, src.ProductId)))
                    continue;

                var currentAtTarget = activeStocks
                    .Where(s => s.WarehouseId == src.WarehouseId && s.ProductId == src.ProductId && s.LocationId == targetBin.Id)
                    .Sum(s => s.Quantity);

                var minThreshold = targetBin.MinBaseQty ?? 20m;
                var maxThreshold = targetBin.MaxBaseQty ?? 25m;
                if (currentAtTarget < minThreshold && src.AvailableQty > currentAtTarget)
                {
                    var neededQty = Math.Max(5m, maxThreshold - currentAtTarget);
                    var suggestedQty = Math.Min(src.AvailableQty, neededQty);
                    suggestions.Add(new
                    {
                        inventoryStockId = src.Id,
                        productId = src.ProductId,
                        productCode = src.ProductCode.Trim(),
                        productName = src.ProductName.Trim(),
                        warehouseId = src.WarehouseId,
                        warehouseName = src.WarehouseName.Trim(),
                        sourceLocationId = src.LocationId,
                        sourceLocationCode = src.LocationCode.Trim(),
                        fromLocationId = src.LocationId,
                        fromLocationCode = src.LocationCode.Trim(),
                        availableAtSource = src.AvailableQty,
                        targetLocationId = targetBin.Id,
                        targetLocationCode = targetBin.Code.Trim(),
                        toLocationId = targetBin.Id,
                        toLocationCode = targetBin.Code.Trim(),
                        currentAtTarget,
                        currentTargetQty = currentAtTarget,
                        currentPickFaceQty = currentAtTarget,
                        minBaseQty = minThreshold,
                        maxBaseQty = maxThreshold,
                        neededQty,
                        suggestedQuantity = suggestedQty,
                        suggestedReplenishQty = suggestedQty,
                        sourceLotId = src.LotId,
                        sourceLotNumber = src.LotNumber,
                        sourceLotExpiryDate = src.LotExpiryDate,
                        fefoRank = 1,
                        priority = "NORMAL",
                        triggerReason = "HEURISTIC_LOW_PICK_FACE"
                    });
                    coveredTargetProductPairs.Add((targetBin.Id, src.ProductId));
                    break;
                }
            }
        }

        return WmsEndpointOutcome.Ok(suggestions.Take(25).ToList());
    }
}
