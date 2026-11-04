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
    public async Task<WmsEndpointOutcome> DekitAsync(
        int? id,
        ProductDekitInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var kitQty = request?.Quantity ?? 1m;
        if (kitQty <= 0m)
        {
            return WmsEndpointOutcome.Json(422, new
            {
                code = "VAS_KIT_INVALID_QTY",
                errorCode = "VAS_KIT_INVALID_QTY",
                message = "Số lượng rã bộ KIT phải lớn hơn 0."
            });
        }

        var targetKitProductId = id;
        if (!targetKitProductId.HasValue || targetKitProductId.Value <= 0)
        {
            targetKitProductId = await _db.Products.AsNoTracking().OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(cancellationToken);
        }

        if (!targetKitProductId.HasValue)
        {
            return WmsEndpointOutcome.Json(404, new { code = "PRODUCT_NOT_FOUND", errorCode = "PRODUCT_NOT_FOUND", message = "Không tìm thấy sản phẩm KIT." });
        }

        var kitProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == targetKitProductId.Value, cancellationToken);
        if (kitProduct == null)
        {
            return WmsEndpointOutcome.Json(404, new { code = "PRODUCT_NOT_FOUND", errorCode = "PRODUCT_NOT_FOUND", message = "Không tìm thấy sản phẩm KIT." });
        }

        var targetWarehouseId = request?.WarehouseId ?? 1;
        var bundleRows = await _db.ProductBundleComponents
            .AsNoTracking()
            .Include(b => b.ComponentProduct)
            .Where(b => b.ParentProductId == targetKitProductId.Value && b.IsActive)
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var componentSpecs = bundleRows.Count > 0
            ? bundleRows.Select(b => (
                ComponentProductId: b.ComponentProductId,
                ComponentCode: b.ComponentProduct?.Code ?? $"COMP-{b.ComponentProductId}",
                ComponentName: b.ComponentProduct?.Name ?? $"Thành phần #{b.ComponentProductId}",
                Ratio: b.ComponentQuantity > 0m ? b.ComponentQuantity : 1m)).ToList()
            : new List<(int ComponentProductId, string ComponentCode, string ComponentName, decimal Ratio)>
            {
                (targetKitProductId.Value, kitProduct.Code, kitProduct.Name, 1m)
            };

        var now = DateTime.UtcNow;
        var actorId = actorUserId is > 0 ? actorUserId.Value : 1;

        await using var tx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var lockProductIds = componentSpecs.Select(x => x.ComponentProductId).Append(targetKitProductId.Value).Distinct().OrderBy(x => x).ToList();
        await _db.AcquireInventoryAllocationLocksAsync(targetWarehouseId, lockProductIds, cancellationToken);

        var kitStocksQuery = _db.InventoryStocks
            .Where(s => s.WarehouseId == targetWarehouseId &&
                        s.ProductId == targetKitProductId.Value &&
                        s.Status == InventoryStatus.Available &&
                        s.Quantity > 0m);
        if (request?.LocationId.HasValue == true)
        {
            kitStocksQuery = kitStocksQuery.Where(s => s.LocationId == request.LocationId.Value);
        }
        if (request?.LotId.HasValue == true)
        {
            kitStocksQuery = kitStocksQuery.Where(s => s.LotId == request.LotId.Value);
        }

        var kitStocks = await kitStocksQuery
            .OrderByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var kitAvail = kitStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
        if (kitAvail < kitQty)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(422, new
            {
                code = "VAS_DEKIT_INSUFFICIENT_KIT_STOCK",
                errorCode = "VAS_DEKIT_INSUFFICIENT_KIT_STOCK",
                kitProductId = targetKitProductId.Value,
                requiredKitQuantity = kitQty,
                availableKitQuantity = kitAvail,
                message = $"Không đủ tồn kho khả dụng của bộ KIT {kitProduct.Code} để rã bộ (cần {kitQty:0.##}, khả dụng {kitAvail:0.##})."
            });
        }

        var defaultLocId = kitStocks.FirstOrDefault(s => s.LocationId.HasValue)?.LocationId
            ?? await _db.WarehouseLocations
                .AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId && !l.IsBlocked)
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var remainingToDeduct = kitQty;
        int? primaryDeductedLocationId = defaultLocId;
        int? primaryDeductedLotId = null;
        int? primaryDeductedHuId = null;
        int? primaryDeductedOwnerId = null;
        InventoryTransaction? firstKitTx = null;

        foreach (var ks in kitStocks)
        {
            if (remainingToDeduct <= 0m) break;
            var free = Math.Max(0m, ks.Quantity - ks.ReservedQuantity - ks.AllocatedQuantity - ks.LockedQuantity);
            if (free <= 0m) continue;
            var deduct = Math.Min(free, remainingToDeduct);
            ks.Quantity -= deduct;
            ks.LastUpdated = now;
            ks.RefreshGrainHash();
            remainingToDeduct -= deduct;
            primaryDeductedLocationId = ks.LocationId ?? defaultLocId;
            primaryDeductedLotId ??= ks.LotId;
            primaryDeductedHuId ??= ks.HandlingUnitId;
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
                CreatedBy = actorId,
                Note = $"VAS-DEKIT: Xuất rã bộ KIT -{deduct:0.##}"
            };
            _db.InventoryTransactions.Add(kTx);
            firstKitTx ??= kTx;
        }

        InventoryTransaction? firstCompTx = null;
        foreach (var spec in componentSpecs)
        {
            var addCompQty = Math.Round(kitQty * spec.Ratio, 4);
            var preserveKitGrain = spec.ComponentProductId == targetKitProductId.Value;
            var compLocId = preserveKitGrain ? (primaryDeductedLocationId ?? defaultLocId) : defaultLocId;
            var compLotId = preserveKitGrain ? primaryDeductedLotId : request?.LotId;
            var compHuId = preserveKitGrain ? primaryDeductedHuId : null;
            var compOwnerId = preserveKitGrain ? primaryDeductedOwnerId : null;

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
                CreatedBy = actorId,
                Note = $"VAS-DEKIT: Nhập hoàn nguyên thành phần +{addCompQty:0.##} ({spec.ComponentCode})"
            };
            _db.InventoryTransactions.Add(cTx);
            firstCompTx ??= cTx;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            WarehouseId = targetWarehouseId,
            Action = "VAS_DEKIT_EXECUTED",
            EntityName = "Product",
            EntityId = targetKitProductId.Value,
            NewValues = $"Operation=DEKIT;Kit={kitProduct.Code};Qty={kitQty:0.##};LotId={primaryDeductedLotId}",
            Reason = request?.Note ?? "Thực thi rã bộ KIT (De-kitting) bảo toàn 9D grain",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            operationMode = "DEKIT",
            warehouseId = targetWarehouseId,
            kitProductId = targetKitProductId.Value,
            kitProductCode = kitProduct.Code,
            kitQuantity = kitQty,
            locationId = primaryDeductedLocationId,
            lotId = primaryDeductedLotId,
            kitTransferOutTxId = firstKitTx?.Id,
            componentTransferInTxId = firstCompTx?.Id
        });
    }
}
