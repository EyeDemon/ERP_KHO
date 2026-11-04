using System.Data;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PackingSessionOperationsService
{
    private async Task<(int? CompId, int? KitId, int? LocId, decimal KitQty, decimal Ratio, List<ProductBundleComponent> BomComponents)> ResolveKittingInputsAsync(
        ExecuteKittingVasInput? request,
        int targetWarehouseId,
        CancellationToken cancellationToken)
    {
        var resolvedCompId = request?.ComponentProductId;
        if (!resolvedCompId.HasValue && !string.IsNullOrWhiteSpace(request?.ComponentProductCode))
        {
            var compCode = request.ComponentProductCode.Trim();
            resolvedCompId = await _db!.Products.AsNoTracking()
                .Where(p => p.Code == compCode)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var resolvedKitId = request?.KitProductId ?? request?.ParentProductId;
        var kitCodeLookup = !string.IsNullOrWhiteSpace(request?.KitProductCode) ? request.KitProductCode.Trim() : request?.KitSkuCode?.Trim();
        if (!resolvedKitId.HasValue && !string.IsNullOrWhiteSpace(kitCodeLookup))
        {
            resolvedKitId = await _db!.Products.AsNoTracking()
                .Where(p => p.Code == kitCodeLookup)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var resolvedLocId = request?.LocationId;
        if (!resolvedLocId.HasValue && !string.IsNullOrWhiteSpace(request?.LocationCode))
        {
            var locCode = request.LocationCode.Trim();
            resolvedLocId = await _db!.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId && l.Code == locCode)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var kitQty = (request?.KitQuantity ?? request?.Quantity) is > 0m
            ? (request?.KitQuantity ?? request?.Quantity)!.Value
            : 1m;
        var ratio = request?.ComponentRatio > 0m ? request.ComponentRatio.Value : 1m;

        var bomComponents = resolvedKitId.HasValue
            ? await _db!.ProductBundleComponents
                .AsNoTracking()
                .Include(b => b.ComponentProduct)
                .Where(b => b.ParentProductId == resolvedKitId.Value && b.IsActive)
                .OrderBy(b => b.Id)
                .ToListAsync(cancellationToken)
            : [];

        return (resolvedCompId, resolvedKitId, resolvedLocId, kitQty, ratio, bomComponents);
    }

    private async Task<VasMutationResult> ExecuteDekittingMutationAsync(
        ExecuteKittingVasInput? request,
        int targetWarehouseId,
        string workOrderId,
        int resolvedActorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var (resolvedCompId, resolvedKitId, resolvedLocId, kitQty, ratio, bomComponents) =
            await ResolveKittingInputsAsync(request, targetWarehouseId, cancellationToken);

        var targetKitProductId = resolvedKitId
            ?? bomComponents.FirstOrDefault()?.ParentProductId
            ?? resolvedCompId
            ?? await _db!.InventoryStocks.AsNoTracking()
                .Where(s => s.WarehouseId == targetWarehouseId &&
                            s.Status == InventoryStatus.Available &&
                            (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) >= kitQty)
                .OrderByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
                .Select(s => (int?)s.ProductId)
                .FirstOrDefaultAsync(cancellationToken);

        if (!targetKitProductId.HasValue)
        {
            var fail = WmsEndpointOutcome.BadRequest(new
            {
                code = "KIT_PRODUCT_REQUIRED",
                errorCode = "KIT_PRODUCT_REQUIRED",
                message = "Phải chỉ định mã sản phẩm Combo/Kit (KitProductId) cần rã bộ."
            });
            return new VasMutationResult(fail, false, 0m, 0m, null, null, null, null, []);
        }

        if (bomComponents.Count == 0 && !resolvedCompId.HasValue)
        {
            bomComponents = await _db!.ProductBundleComponents
                .AsNoTracking()
                .Include(b => b.ComponentProduct)
                .Where(b => b.ParentProductId == targetKitProductId.Value && b.IsActive)
                .OrderBy(b => b.Id)
                .ToListAsync(cancellationToken);
        }

        var componentSpecs = new List<(int ComponentProductId, string ComponentCode, string ComponentName, decimal Ratio)>();
        if (bomComponents.Count > 0)
        {
            foreach (var bc in bomComponents)
            {
                componentSpecs.Add((
                    bc.ComponentProductId,
                    bc.ComponentProduct?.Code ?? $"SKU-{bc.ComponentProductId}",
                    bc.ComponentProduct?.Name ?? $"Thành phần #{bc.ComponentProductId}",
                    bc.ComponentQuantity > 0m ? bc.ComponentQuantity : 1m));
            }
        }
        else
        {
            var fallbackCompId = resolvedCompId ?? targetKitProductId.Value;
            var fallbackProd = await _db!.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == fallbackCompId, cancellationToken);
            componentSpecs.Add((
                fallbackCompId,
                fallbackProd?.Code ?? $"SKU-{fallbackCompId}",
                fallbackProd?.Name ?? $"Thành phần #{fallbackCompId}",
                ratio));
        }

        await using var sqlTx = _db!.Database.CurrentTransaction == null && _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        var lockProductIds = componentSpecs
            .Select(c => c.ComponentProductId)
            .Append(targetKitProductId.Value)
            .Distinct()
            .ToList();
        await _db.AcquireInventoryAllocationLocksAsync(targetWarehouseId, lockProductIds, cancellationToken);

        var kitStockQuery = _db.InventoryStocks
            .Where(s => s.WarehouseId == targetWarehouseId &&
                        s.ProductId == targetKitProductId.Value &&
                        s.Status == InventoryStatus.Available);
        if (resolvedLocId.HasValue)
        {
            kitStockQuery = kitStockQuery.Where(s => s.LocationId == resolvedLocId.Value);
        }

        var kitStocks = await kitStockQuery
            .OrderByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var availableKitQty = kitStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
        if (availableKitQty < kitQty)
        {
            if (sqlTx != null) await sqlTx.RollbackAsync(cancellationToken);
            var insufficient = WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "KIT_STOCK_INSUFFICIENT_FOR_DEKIT",
                errorCode = "KIT_STOCK_INSUFFICIENT_FOR_DEKIT",
                kitProductId = targetKitProductId.Value,
                requiredKitQty = kitQty,
                availableKitQty,
                message = $"Tồn kho bộ Kit #{targetKitProductId.Value} không đủ để rã bộ (Cần: {kitQty:0.##}, Khả dụng: {availableKitQty:0.##})."
            });
            return new VasMutationResult(insufficient, false, 0m, 0m, null, null, null, null, []);
        }

        var defaultLocId = resolvedLocId
            ?? kitStocks.FirstOrDefault()?.LocationId
            ?? await _db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId && l.IsActive)
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var remainingKitToDeduct = kitQty;
        decimal? kitResultingQuantity = null;
        InventoryTransaction? firstKitTx = null;
        int? primaryDeductedLocationId = defaultLocId;
        int? primaryDeductedLotId = null;
        int? primaryDeductedHandlingUnitId = null;
        int? primaryDeductedOwnerId = null;
        foreach (var ks in kitStocks)
        {
            if (remainingKitToDeduct <= 0m) break;
            var free = Math.Max(0m, ks.Quantity - ks.ReservedQuantity - ks.AllocatedQuantity - ks.LockedQuantity);
            if (free <= 0m) continue;
            var deduct = Math.Min(free, remainingKitToDeduct);
            ks.Quantity -= deduct;
            ks.LastUpdated = now;
            ks.RefreshGrainHash();
            remainingKitToDeduct -= deduct;
            kitResultingQuantity = ks.Quantity;
            primaryDeductedLocationId = ks.LocationId ?? defaultLocId;
            primaryDeductedLotId ??= ks.LotId;
            primaryDeductedHandlingUnitId ??= ks.HandlingUnitId;
            primaryDeductedOwnerId ??= ks.OwnerId;

            var kTx = new InventoryTransaction
            {
                ProductId = targetKitProductId.Value,
                WarehouseId = targetWarehouseId,
                LocationId = ks.LocationId,
                LotId = ks.LotId,
                SerialId = ks.SerialId,
                HandlingUnitId = ks.HandlingUnitId,
                OwnerId = ks.OwnerId,
                InventoryStatus = InventoryStatus.Available,
                FromInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.TransferOut,
                Quantity = deduct,
                ReferenceId = ks.Id,
                ReferenceType = "VAS_DEKIT",
                TransactionDate = now,
                ActualOccurredAtUtc = now,
                PostedAtUtc = now,
                CreatedBy = resolvedActorId,
                Note = $"{workOrderId}: Xuất rã bộ KIT (De-kitting) -{deduct:0.##}"
            };
            _db.InventoryTransactions.Add(kTx);
            firstKitTx ??= kTx;
        }

        decimal componentConsumedQuantity = 0m;
        decimal? componentRemainingQuantity = null;
        var componentBreakdown = new List<object>();
        InventoryTransaction? firstCompTx = null;

        foreach (var spec in componentSpecs)
        {
            var addCompQty = Math.Round(kitQty * spec.Ratio, 4);
            var preserveKitGrain = spec.ComponentProductId == targetKitProductId.Value;
            var compLocId = preserveKitGrain ? (primaryDeductedLocationId ?? defaultLocId) : defaultLocId;
            var compLotId = preserveKitGrain ? primaryDeductedLotId : null;
            var compHuId = preserveKitGrain ? primaryDeductedHandlingUnitId : null;
            var compOwnerId = preserveKitGrain ? primaryDeductedOwnerId : null;

            if (!preserveKitGrain && compLotId == null)
            {
                var existingCompLotStock = await _db.InventoryStocks
                    .Where(s =>
                        s.WarehouseId == targetWarehouseId &&
                        s.ProductId == spec.ComponentProductId &&
                        s.LocationId == compLocId &&
                        s.SerialId == null &&
                        s.Status == InventoryStatus.Available &&
                        s.LotId != null)
                    .OrderByDescending(s => s.Quantity)
                    .ThenBy(s => s.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (existingCompLotStock != null)
                {
                    compLotId = existingCompLotStock.LotId;
                    compHuId = existingCompLotStock.HandlingUnitId;
                    compOwnerId = existingCompLotStock.OwnerId;
                }
            }

            var compStock = await _db.InventoryStocks
                .FirstOrDefaultAsync(s =>
                    s.WarehouseId == targetWarehouseId &&
                    s.ProductId == spec.ComponentProductId &&
                    s.LocationId == compLocId &&
                    s.LotId == compLotId &&
                    s.SerialId == null &&
                    s.HandlingUnitId == compHuId &&
                    s.OwnerId == compOwnerId &&
                    s.Status == InventoryStatus.Available,
                    cancellationToken);

            if (compStock != null)
            {
                compStock.Quantity += addCompQty;
                compStock.LastUpdated = now;
                compStock.RefreshGrainHash();
            }
            else
            {
                compStock = new InventoryStock
                {
                    WarehouseId = targetWarehouseId,
                    ProductId = spec.ComponentProductId,
                    LocationId = compLocId,
                    LotId = compLotId,
                    HandlingUnitId = compHuId,
                    OwnerId = compOwnerId,
                    Status = InventoryStatus.Available,
                    Quantity = addCompQty,
                    ReservedQuantity = 0m,
                    AllocatedQuantity = 0m,
                    LockedQuantity = 0m,
                    LastUpdated = now
                };
                compStock.RefreshGrainHash();
                _db.InventoryStocks.Add(compStock);
            }

            componentConsumedQuantity += addCompQty;
            componentRemainingQuantity = compStock.Quantity;

            var cTx = new InventoryTransaction
            {
                ProductId = spec.ComponentProductId,
                WarehouseId = targetWarehouseId,
                LocationId = compLocId,
                LotId = compLotId,
                HandlingUnitId = compHuId,
                OwnerId = compOwnerId,
                InventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.TransferIn,
                Quantity = addCompQty,
                ReferenceId = targetKitProductId.Value,
                ReferenceType = "VAS_DEKIT",
                TransactionDate = now,
                ActualOccurredAtUtc = now,
                PostedAtUtc = now,
                CreatedBy = resolvedActorId,
                Note = $"{workOrderId}: Nhập hoàn nguyên thành phần +{addCompQty:0.##} ({spec.ComponentCode}) từ rã bộ KIT"
            };
            _db.InventoryTransactions.Add(cTx);
            firstCompTx ??= cTx;

            componentBreakdown.Add(new
            {
                componentProductId = spec.ComponentProductId,
                componentProductCode = spec.ComponentCode,
                componentProductName = spec.ComponentName,
                requiredPerKit = spec.Ratio,
                returnedQuantity = addCompQty,
                resultingComponentQuantity = compStock.Quantity
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        if (sqlTx != null)
        {
            await sqlTx.CommitAsync(cancellationToken);
        }

        return new VasMutationResult(
            null,
            true,
            componentConsumedQuantity,
            kitQty,
            componentRemainingQuantity,
            kitResultingQuantity,
            firstCompTx?.Id,
            firstKitTx?.Id,
            componentBreakdown);
    }
}
