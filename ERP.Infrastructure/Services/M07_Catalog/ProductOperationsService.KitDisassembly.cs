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
    private async Task<WmsEndpointOutcome?> ExecuteDisassembleKitStockCoreAsync(
        KitWorkOrder workOrder,
        Product kitProduct,
        decimal completedKitQty,
        int? defaultLocationId,
        int actorId,
        DateTime now,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx,
        CancellationToken cancellationToken)
    {
            // Disassemble: deduct parent Kit and return components to their target LotId/LocationId
            var kitStocksQuery = _db.InventoryStocks
                .Include(s => s.Lot)
                .Where(s => s.WarehouseId == workOrder.WarehouseId &&
                            s.ProductId == workOrder.KitProductId &&
                            s.Status == InventoryStatus.Available &&
                            s.Quantity > 0m);
            if (workOrder.TargetLocationId is > 0)
                kitStocksQuery = kitStocksQuery.Where(s => s.LocationId == workOrder.TargetLocationId.Value);
            if (workOrder.TargetLotId is > 0)
                kitStocksQuery = kitStocksQuery.Where(s => s.LotId == workOrder.TargetLotId.Value);

            var kitStocks = await kitStocksQuery
                .OrderByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
                .ThenBy(s => s.Id)
                .ToListAsync(cancellationToken);

            var totalKitFree = kitStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
            if (totalKitFree < completedKitQty)
            {
                if (tx != null) await tx.RollbackAsync(cancellationToken);
                return WmsEndpointOutcome.Json(422, new
                {
                    code = "VAS_DEKIT_INSUFFICIENT_KIT_STOCK",
                    errorCode = "VAS_DEKIT_INSUFFICIENT_KIT_STOCK",
                    kitProductId = workOrder.KitProductId,
                    requiredKitQuantity = completedKitQty,
                    availableKitQuantity = totalKitFree,
                    message = $"Không đủ tồn khả dụng của bộ KIT {kitProduct.Code} để rã bộ (cần {completedKitQty:0.####}, khả dụng {totalKitFree:0.####})."
                });
            }

            var remToDeduct = completedKitQty;
            var totalKitDeductedCost = 0m;
            int? deductedLocationId = workOrder.TargetLocationId;
            int? deductedLotId = workOrder.TargetLotId;
            DateTime? parentExpiry = null;

            foreach (var ks in kitStocks)
            {
                if (remToDeduct <= 0m) break;
                var free = Math.Max(0m, ks.Quantity - ks.ReservedQuantity - ks.AllocatedQuantity - ks.LockedQuantity);
                if (free <= 0m) continue;
                var deduct = Math.Min(free, remToDeduct);
                var unitCost = ks.MovingAverageCost > 0m
                    ? ks.MovingAverageCost
                    : (kitProduct.MovingAverageCost > 0m ? kitProduct.MovingAverageCost : workOrder.RolledUpUnitCost);

                ks.Quantity -= deduct;
                ks.LastUpdated = now;
                ks.RefreshGrainHash();
                remToDeduct -= deduct;
                totalKitDeductedCost += deduct * unitCost;

                deductedLocationId ??= ks.LocationId;
                deductedLotId ??= ks.LotId;
                parentExpiry ??= ks.Lot?.ExpiryDate;

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = workOrder.KitProductId,
                    WarehouseId = workOrder.WarehouseId,
                    LocationId = ks.LocationId,
                    FromLocationId = ks.LocationId,
                    LotId = ks.LotId,
                    SerialId = ks.SerialId,
                    HandlingUnitId = ks.HandlingUnitId,
                    OwnerId = ks.OwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    FromInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.TransferOut,
                    Quantity = -deduct,
                    UnitCost = Math.Round(unitCost, 4),
                    ReferenceId = workOrder.Id,
                    ReferenceType = "KitDisassembleIssue",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = actorId,
                    Note = $"{workOrder.WorkOrderCode}: Xuất rã bộ KIT -{deduct:0.####}"
                });
            }

            workOrder.TargetLocationId ??= deductedLocationId ?? defaultLocationId;
            workOrder.TargetLotId ??= deductedLotId;
            workOrder.RolledUpUnitCost = completedKitQty > 0m ? Math.Round(totalKitDeductedCost / completedKitQty, 4) : 0m;
            workOrder.EarliestComponentExpiryDate ??= parentExpiry;

            var totalRatioSum = workOrder.ComponentLines.Sum(l => l.ComponentRatioQty > 0m ? l.ComponentRatioQty : 1m);
            foreach (var line in workOrder.ComponentLines.OrderBy(l => l.ComponentProductId).ThenBy(l => l.Id))
            {
                var returnQty = Math.Round(completedKitQty * line.ComponentRatioQty, 4);
                line.RequiredBaseQty = returnQty;
                line.ConsumedBaseQty = returnQty;

                var compLocId = line.SourceLocationId ?? deductedLocationId ?? defaultLocationId;
                line.SourceLocationId = compLocId;

                var compProduct = await _db.Products.FirstOrDefaultAsync(p => p.Id == line.ComponentProductId, cancellationToken);
                if (!line.SourceLotId.HasValue && compProduct != null &&
                    (compProduct.TrackingType == ProductTrackingType.Lot || compProduct.ExpiryControl))
                {
                    var lotNum = $"LOT-DEKIT-{workOrder.WorkOrderCode}-{line.ComponentProductId}";
                    var compLot = await _db.InventoryLots
                        .FirstOrDefaultAsync(l => l.ProductId == line.ComponentProductId && l.LotNumber == lotNum, cancellationToken);
                    if (compLot == null)
                    {
                        compLot = new InventoryLot
                        {
                            ProductId = line.ComponentProductId,
                            LotNumber = lotNum,
                            ManufactureDate = now.Date,
                            ExpiryDate = parentExpiry,
                            ReceivedAt = now,
                            CreatedAt = now
                        };
                        _db.InventoryLots.Add(compLot);
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                    line.SourceLotId = compLot.Id;
                }

                var compUnitCost = line.ComponentUnitCost > 0m
                    ? line.ComponentUnitCost
                    : (compProduct?.MovingAverageCost > 0m
                        ? compProduct.MovingAverageCost
                        : (totalRatioSum > 0m ? Math.Round(workOrder.RolledUpUnitCost / totalRatioSum, 4) : 0m));
                line.ComponentUnitCost = compUnitCost;

                var compStock = await _db.InventoryStocks
                    .FirstOrDefaultAsync(s =>
                        s.WarehouseId == workOrder.WarehouseId &&
                        s.ProductId == line.ComponentProductId &&
                        s.LocationId == compLocId &&
                        s.LotId == line.SourceLotId &&
                        s.SerialId == null &&
                        s.HandlingUnitId == null &&
                        s.OwnerId == null &&
                        s.Status == InventoryStatus.Available,
                        cancellationToken);

                if (compStock != null)
                {
                    compStock.Quantity += returnQty;
                    if (compUnitCost > 0m) compStock.MovingAverageCost = compUnitCost;
                    compStock.LastUpdated = now;
                    compStock.RefreshGrainHash();
                }
                else
                {
                    compStock = new InventoryStock
                    {
                        WarehouseId = workOrder.WarehouseId,
                        ProductId = line.ComponentProductId,
                        LocationId = compLocId,
                        LotId = line.SourceLotId,
                        Status = InventoryStatus.Available,
                        Quantity = returnQty,
                        MovingAverageCost = compUnitCost,
                        LastUpdated = now
                    };
                    compStock.RefreshGrainHash();
                    _db.InventoryStocks.Add(compStock);
                }

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = line.ComponentProductId,
                    WarehouseId = workOrder.WarehouseId,
                    LocationId = compLocId,
                    ToLocationId = compLocId,
                    LotId = line.SourceLotId,
                    InventoryStatus = InventoryStatus.Available,
                    ToInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.TransferIn,
                    Quantity = returnQty,
                    UnitCost = compUnitCost,
                    ReferenceId = workOrder.Id,
                    ReferenceType = "KitComponentReturn",
                    SourceDocumentLineId = line.Id,
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = actorId,
                    Note = $"{workOrder.WorkOrderCode}: Hoàn nguyên linh kiện rã bộ KIT +{returnQty:0.####}"
                });
            }
        return null;
    }

    private WmsEndpointOutcome BuildCompletedKitWorkOrderResponse(
        KitWorkOrder workOrder,
        Product kitProduct,
        int? actorUserId)
    {
        var canReadCostComplete = _currentUser is null ? actorUserId is > 0 : CanReadInventoryCost();
        var completeQtyForTotal = workOrder.CompletedKitQty > 0m ? workOrder.CompletedKitQty : workOrder.PlannedKitQty;
        var completeTotalCost = Math.Round(completeQtyForTotal * workOrder.RolledUpUnitCost, 4);
        return WmsEndpointOutcome.Ok(new
        {
            id = workOrder.Id,
            workOrderId = workOrder.Id,
            workOrderCode = workOrder.WorkOrderCode,
            warehouseId = workOrder.WarehouseId,
            kitProductId = workOrder.KitProductId,
            kitProductCode = kitProduct.Code,
            kitProductName = kitProduct.Name,
            workOrderType = workOrder.WorkOrderType.ToString(),
            status = workOrder.Status,
            plannedKitQty = workOrder.PlannedKitQty,
            completedKitQty = workOrder.CompletedKitQty,
            baseUomCode = NormalizeKitUomCode(workOrder.BaseUomCode),
            conversionRateSnapshot = workOrder.ConversionRateSnapshot > 0m ? workOrder.ConversionRateSnapshot : 1m,
            targetLocationId = workOrder.TargetLocationId,
            targetLotId = workOrder.TargetLotId,
            targetHandlingUnitId = workOrder.TargetHandlingUnitId,
            rolledUpUnitCost = canReadCostComplete ? (decimal?)workOrder.RolledUpUnitCost : null,
            unitCost = canReadCostComplete ? (decimal?)workOrder.RolledUpUnitCost : null,
            totalCost = canReadCostComplete ? (decimal?)completeTotalCost : null,
            costHiddenByRbac = !canReadCostComplete,
            earliestComponentExpiryDate = workOrder.EarliestComponentExpiryDate,
            completedAtUtc = workOrder.CompletedAtUtc,
            componentLines = workOrder.ComponentLines.Select(l => new
            {
                id = l.Id,
                componentProductId = l.ComponentProductId,
                componentRatioQty = l.ComponentRatioQty,
                requiredBaseQty = l.RequiredBaseQty,
                consumedBaseQty = l.ConsumedBaseQty,
                baseUomCode = NormalizeKitUomCode(l.BaseUomCode),
                conversionRateSnapshot = l.ConversionRateSnapshot > 0m ? l.ConversionRateSnapshot : 1m,
                sourceLocationId = l.SourceLocationId,
                sourceLotId = l.SourceLotId,
                componentUnitCost = canReadCostComplete ? (decimal?)l.ComponentUnitCost : null
            }).ToList(),
            statusLabel = canReadCostComplete
                ? $"Đã hoàn tất lệnh {workOrder.WorkOrderType} bộ KIT {workOrder.WorkOrderCode} (MAC: {workOrder.RolledUpUnitCost:0.##})"
                : $"Đã hoàn tất lệnh {workOrder.WorkOrderType} bộ KIT {workOrder.WorkOrderCode}"
        });
    }
}
