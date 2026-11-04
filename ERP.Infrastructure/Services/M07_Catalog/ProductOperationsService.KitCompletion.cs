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
    public async Task<WmsEndpointOutcome> CompleteKitWorkOrderAsync(
        int workOrderId,
        CompleteKitWorkOrderInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var ownTx = _db.Database.CurrentTransaction == null;
        var isSqlServer = _db.Database.IsRelational() && _db.Database.IsSqlServer();
        await using var tx = ownTx ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;

        if (isSqlServer)
        {
            var tenantId = _db.CurrentTenantId;
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT Id FROM dbo.KitWorkOrders WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TenantId = {tenantId} AND Id = {workOrderId};",
                cancellationToken);
        }

        var workOrder = await _db.KitWorkOrders
            .Include(w => w.ComponentLines)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder == null)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(404, new
            {
                code = "KIT_WORK_ORDER_NOT_FOUND",
                errorCode = "KIT_WORK_ORDER_NOT_FOUND",
                message = $"Không tìm thấy lệnh gia công KIT #{workOrderId}."
            });
        }

        if (string.Equals(workOrder.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(409, new
            {
                code = "KIT_WORK_ORDER_ALREADY_COMPLETED",
                errorCode = "KIT_WORK_ORDER_ALREADY_COMPLETED",
                workOrderId = workOrder.Id,
                workOrderCode = workOrder.WorkOrderCode,
                message = $"Lệnh gia công KIT {workOrder.WorkOrderCode} đã hoàn tất trước đó."
            });
        }

        if (string.Equals(workOrder.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(422, new
            {
                code = "KIT_WORK_ORDER_CANCELLED",
                errorCode = "KIT_WORK_ORDER_CANCELLED",
                workOrderId = workOrder.Id,
                message = $"Lệnh gia công KIT {workOrder.WorkOrderCode} đã bị hủy."
            });
        }

        var completedKitQty = request?.CompletedKitQty is > 0m
            ? Math.Round(request.CompletedKitQty.Value, 4)
            : workOrder.PlannedKitQty;
        if (completedKitQty <= 0m)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(422, new
            {
                code = "VAS_KIT_INVALID_QTY",
                errorCode = "VAS_KIT_INVALID_QTY",
                message = "Số lượng hoàn thành bộ KIT phải lớn hơn 0."
            });
        }

        if (request?.TargetLocationId is > 0)
            workOrder.TargetLocationId = request.TargetLocationId.Value;
        if (request?.TargetLotId is > 0)
            workOrder.TargetLotId = request.TargetLotId.Value;
        if (request?.TargetHandlingUnitId is > 0)
            workOrder.TargetHandlingUnitId = request.TargetHandlingUnitId.Value;

        var lockProductIds = workOrder.ComponentLines
            .Select(l => l.ComponentProductId)
            .Append(workOrder.KitProductId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
        await _db.AcquireInventoryAllocationLocksAsync(workOrder.WarehouseId, lockProductIds, cancellationToken);

        var kitProduct = await _db.Products.FirstOrDefaultAsync(p => p.Id == workOrder.KitProductId, cancellationToken);
        if (kitProduct == null)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(404, new
            {
                code = "PRODUCT_NOT_FOUND",
                errorCode = "PRODUCT_NOT_FOUND",
                message = "Không tìm thấy sản phẩm KIT cha."
            });
        }

        var now = DateTime.UtcNow;
        var actorId = actorUserId is > 0 ? actorUserId.Value : (workOrder.CreatedByUserId ?? 1);
        var defaultLocationId = workOrder.TargetLocationId
            ?? await _db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == workOrder.WarehouseId && l.IsActive && !l.IsBlocked)
                .OrderByDescending(l => l.IsPickable)
                .ThenBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);

        if (workOrder.WorkOrderType == KitWorkOrderType.Assemble)
        {
            var totalComponentCost = 0m;
            DateTime? earliestExpiryDate = null;
            int? firstComponentLocId = null;

            foreach (var line in workOrder.ComponentLines.OrderBy(l => l.ComponentProductId).ThenBy(l => l.Id))
            {
                var reqCompQty = Math.Round(completedKitQty * line.ComponentRatioQty, 4);
                line.RequiredBaseQty = reqCompQty;

                var compProduct = await _db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == line.ComponentProductId, cancellationToken);

                var compStocksQuery = _db.InventoryStocks
                    .Include(s => s.Lot)
                    .Where(s => s.WarehouseId == workOrder.WarehouseId &&
                                s.ProductId == line.ComponentProductId &&
                                s.Status == InventoryStatus.Available &&
                                s.Quantity > 0m);

                if (line.SourceLocationId is > 0)
                    compStocksQuery = compStocksQuery.Where(s => s.LocationId == line.SourceLocationId.Value);
                if (line.SourceLotId is > 0)
                    compStocksQuery = compStocksQuery.Where(s => s.LotId == line.SourceLotId.Value);

                var compStocks = await compStocksQuery
                    .OrderBy(s => s.Lot != null && s.Lot.ExpiryDate.HasValue ? 0 : 1)
                    .ThenBy(s => s.Lot != null ? s.Lot.ExpiryDate : null)
                    .ThenBy(s => s.LocationId)
                    .ThenBy(s => s.Id)
                    .ToListAsync(cancellationToken);

                var totalFree = compStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
                if (totalFree < reqCompQty)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return WmsEndpointOutcome.Json(422, new
                    {
                        code = "KIT_INSUFFICIENT_COMPONENT_STOCK",
                        errorCode = "KIT_INSUFFICIENT_COMPONENT_STOCK",
                        componentProductId = line.ComponentProductId,
                        requiredBaseQty = reqCompQty,
                        availableBaseQty = totalFree,
                        message = $"Không đủ tồn khả dụng cho thành phần #{line.ComponentProductId} (cần {reqCompQty:0.####}, khả dụng {totalFree:0.####})."
                    });
                }

                var remToConsume = reqCompQty;
                var lineTotalCost = 0m;
                foreach (var cs in compStocks)
                {
                    if (remToConsume <= 0m) break;
                    var free = Math.Max(0m, cs.Quantity - cs.ReservedQuantity - cs.AllocatedQuantity - cs.LockedQuantity);
                    if (free <= 0m) continue;
                    var take = Math.Min(free, remToConsume);
                    var unitCost = cs.MovingAverageCost > 0m
                        ? cs.MovingAverageCost
                        : (compProduct?.MovingAverageCost > 0m
                            ? compProduct.MovingAverageCost
                            : (line.ComponentUnitCost > 0m ? line.ComponentUnitCost : 0m));

                    cs.Quantity -= take;
                    cs.LastUpdated = now;
                    cs.RefreshGrainHash();
                    remToConsume -= take;
                    lineTotalCost += take * unitCost;

                    firstComponentLocId ??= cs.LocationId;
                    line.SourceLocationId ??= cs.LocationId;
                    line.SourceLotId ??= cs.LotId;

                    var lotExpiry = cs.Lot?.ExpiryDate;
                    if (!lotExpiry.HasValue && cs.LotId.HasValue)
                    {
                        lotExpiry = await _db.InventoryLots.AsNoTracking()
                            .Where(lt => lt.Id == cs.LotId.Value)
                            .Select(lt => lt.ExpiryDate)
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                    if (lotExpiry.HasValue && (!earliestExpiryDate.HasValue || lotExpiry.Value < earliestExpiryDate.Value))
                    {
                        earliestExpiryDate = lotExpiry.Value;
                    }

                    _db.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = line.ComponentProductId,
                        WarehouseId = workOrder.WarehouseId,
                        LocationId = cs.LocationId,
                        FromLocationId = cs.LocationId,
                        LotId = cs.LotId,
                        SerialId = cs.SerialId,
                        HandlingUnitId = cs.HandlingUnitId,
                        OwnerId = cs.OwnerId,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.TransferOut,
                        Quantity = -take,
                        UnitCost = Math.Round(unitCost, 4),
                        ReferenceId = workOrder.Id,
                        ReferenceType = "KitComponentIssue",
                        SourceDocumentLineId = line.Id,
                        TransactionDate = now,
                        ActualOccurredAtUtc = now,
                        PostedAtUtc = now,
                        CreatedBy = actorId,
                        Note = $"{workOrder.WorkOrderCode}: Xuất linh kiện lắp bộ KIT -{take:0.####}"
                    });
                }

                line.ConsumedBaseQty = reqCompQty;
                line.ComponentUnitCost = reqCompQty > 0m ? Math.Round(lineTotalCost / reqCompQty, 4) : 0m;
                totalComponentCost += lineTotalCost;
            }

            var rolledUpUnitCost = Math.Round(totalComponentCost / completedKitQty, 4);
            workOrder.RolledUpUnitCost = rolledUpUnitCost;
            workOrder.EarliestComponentExpiryDate = earliestExpiryDate;

            var targetLocId = workOrder.TargetLocationId ?? firstComponentLocId ?? defaultLocationId;
            workOrder.TargetLocationId = targetLocId;

            if (workOrder.TargetLotId.HasValue)
            {
                var targetLot = await _db.InventoryLots.FirstOrDefaultAsync(l => l.Id == workOrder.TargetLotId.Value, cancellationToken);
                if (targetLot != null && earliestExpiryDate.HasValue &&
                    (!targetLot.ExpiryDate.HasValue || earliestExpiryDate.Value < targetLot.ExpiryDate.Value))
                {
                    targetLot.ExpiryDate = earliestExpiryDate.Value;
                }
            }
            else if (earliestExpiryDate.HasValue || kitProduct.TrackingType == ProductTrackingType.Lot || kitProduct.ExpiryControl)
            {
                var kitLotNumber = $"LOT-{workOrder.WorkOrderCode}";
                var targetLot = await _db.InventoryLots
                    .FirstOrDefaultAsync(l => l.ProductId == workOrder.KitProductId && l.LotNumber == kitLotNumber, cancellationToken);
                if (targetLot == null)
                {
                    targetLot = new InventoryLot
                    {
                        ProductId = workOrder.KitProductId,
                        LotNumber = kitLotNumber,
                        ManufactureDate = now.Date,
                        ExpiryDate = earliestExpiryDate,
                        ReceivedAt = now,
                        CreatedAt = now
                    };
                    _db.InventoryLots.Add(targetLot);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                else if (earliestExpiryDate.HasValue)
                {
                    targetLot.ExpiryDate = earliestExpiryDate.Value;
                }
                workOrder.TargetLotId = targetLot.Id;
            }

            var kitStock = await _db.InventoryStocks
                .FirstOrDefaultAsync(s =>
                    s.WarehouseId == workOrder.WarehouseId &&
                    s.ProductId == workOrder.KitProductId &&
                    s.LocationId == targetLocId &&
                    s.LotId == workOrder.TargetLotId &&
                    s.SerialId == null &&
                    s.HandlingUnitId == workOrder.TargetHandlingUnitId &&
                    s.OwnerId == null &&
                    s.Status == InventoryStatus.Available,
                    cancellationToken);

            if (kitStock != null)
            {
                var oldQty = Math.Max(0m, kitStock.Quantity);
                var oldMac = kitStock.MovingAverageCost > 0m ? kitStock.MovingAverageCost : rolledUpUnitCost;
                var newQty = oldQty + completedKitQty;
                kitStock.MovingAverageCost = newQty > 0m
                    ? Math.Round(((oldQty * oldMac) + (completedKitQty * rolledUpUnitCost)) / newQty, 4)
                    : rolledUpUnitCost;
                kitStock.Quantity += completedKitQty;
                kitStock.LastUpdated = now;
                kitStock.RefreshGrainHash();
            }
            else
            {
                kitStock = new InventoryStock
                {
                    WarehouseId = workOrder.WarehouseId,
                    ProductId = workOrder.KitProductId,
                    LocationId = targetLocId,
                    LotId = workOrder.TargetLotId,
                    HandlingUnitId = workOrder.TargetHandlingUnitId,
                    Status = InventoryStatus.Available,
                    Quantity = completedKitQty,
                    MovingAverageCost = rolledUpUnitCost,
                    LastUpdated = now
                };
                kitStock.RefreshGrainHash();
                _db.InventoryStocks.Add(kitStock);
            }

            kitProduct.MovingAverageCost = rolledUpUnitCost;

            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                ProductId = workOrder.KitProductId,
                WarehouseId = workOrder.WarehouseId,
                LocationId = targetLocId,
                ToLocationId = targetLocId,
                LotId = workOrder.TargetLotId,
                HandlingUnitId = workOrder.TargetHandlingUnitId,
                InventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.TransferIn,
                Quantity = completedKitQty,
                UnitCost = rolledUpUnitCost,
                ReferenceId = workOrder.Id,
                ReferenceType = "KitFinishedReceipt",
                TransactionDate = now,
                ActualOccurredAtUtc = now,
                PostedAtUtc = now,
                CreatedBy = actorId,
                Note = $"{workOrder.WorkOrderCode}: Nhập thành phẩm lắp bộ KIT +{completedKitQty:0.####} (MAC={rolledUpUnitCost:0.####})"
            });
        }
        else
        {
            var disassembleError = await ExecuteDisassembleKitStockCoreAsync(workOrder, kitProduct, completedKitQty, defaultLocationId, actorId, now, tx, cancellationToken);
            if (disassembleError != null) return disassembleError;
        }

        workOrder.Status = "Completed";
        workOrder.CompletedKitQty = completedKitQty;
        workOrder.CompletedByUserId = actorId;
        workOrder.CompletedAtUtc = now;

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            WarehouseId = workOrder.WarehouseId,
            Action = "KIT_WORK_ORDER_COMPLETED",
            EntityName = "KitWorkOrder",
            EntityId = workOrder.Id,
            NewValues = $"Code={workOrder.WorkOrderCode};Type={workOrder.WorkOrderType};CompletedQty={completedKitQty:0.####};MAC={workOrder.RolledUpUnitCost:0.####};EarliestExpiry={workOrder.EarliestComponentExpiryDate:yyyy-MM-dd}",
            Reason = request?.Note ?? $"Hoàn tất lệnh gia công {workOrder.WorkOrderType} bộ KIT {kitProduct.Code}",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return BuildCompletedKitWorkOrderResponse(workOrder, kitProduct, actorUserId);
    }
}
