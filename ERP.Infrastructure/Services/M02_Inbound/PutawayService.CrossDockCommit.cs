using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PutawayService
{
    private async Task<PutawayTaskDto> CommitForwardCrossDockExecutionAsync(
        int id,
        PutawayTask task,
        PutawayTaskItem item,
        WarehouseLocation stagingLoc,
        List<InventoryStock> sourceBuckets,
        decimal crossDockQty,
        Backorder? matchedBackorder,
        int? matchedSalesOrderId,
        int? matchedExportReceiptId,
        int actorId,
        DateTime nowUtc,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx,
        CancellationToken token)
    {
            var rsvSourceType = "CrossDockPutawayTask";
            var rsvSourceId = task.Id;
            var reservation = await context.StockReservations
                .FirstOrDefaultAsync(r =>
                    r.ProductId == item.ProductId &&
                    r.SourceType == rsvSourceType &&
                    r.SourceId == rsvSourceId,
                    token);
            if (reservation is null)
            {
                reservation = new StockReservation
                {
                    ReservationCode = $"RSV-CD-{task.Id}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ProductId = item.ProductId,
                    WarehouseId = task.WarehouseId,
                    Quantity = crossDockQty,
                    AllocatedQuantity = crossDockQty,
                    ConsumedQuantity = 0m,
                    ReleasedQuantity = 0m,
                    BaseUomIdSnapshot = item.BaseUnitId > 0 ? item.BaseUnitId : 1,
                    BaseUomCodeSnapshot = !string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot) ? item.BaseUnitCodeSnapshot : "THÙNG",
                    BaseUomDecimalPlacesSnapshot = item.BaseUnitDecimalPlaces,
                    AllocationVersion = 1,
                    Status = StockReservationStatus.Allocated,
                    SourceType = rsvSourceType,
                    SourceId = rsvSourceId,
                    SourceCode = $"CD-TASK-{task.Id}",
                    CreatedAt = nowUtc,
                    CreatedBy = actorId,
                    ExpiresAt = nowUtc.AddDays(7)
                };
                context.StockReservations.Add(reservation);
            }
            else
            {
                if (reservation.Status is StockReservationStatus.Released or StockReservationStatus.Cancelled)
                {
                    reservation.Quantity = crossDockQty;
                    reservation.AllocatedQuantity = crossDockQty;
                    reservation.ConsumedQuantity = 0m;
                    reservation.ReleasedQuantity = 0m;
                    reservation.ReleasedAt = null;
                    reservation.ReleasedBy = null;
                    reservation.ReleaseReason = null;
                }
                else
                {
                    reservation.Quantity = Math.Round(reservation.Quantity + crossDockQty, 4);
                    reservation.AllocatedQuantity = Math.Round(reservation.AllocatedQuantity + crossDockQty, 4);
                }
                reservation.Status = StockReservationStatus.Allocated;
                reservation.AllocationVersion += 1;
            }
            await context.SaveChangesAsync(token);

            var remToMove = crossDockQty;
            var sourceCode = item.SourceLocation?.Code?.Trim() ?? "RECEIVING";
            foreach (var srcBucket in sourceBuckets)
            {
                if (remToMove <= 0m) break;
                var avail = Math.Max(0m, srcBucket.Quantity - srcBucket.ReservedQuantity);
                if (avail <= 0m) continue;
                var chunk = Math.Min(remToMove, avail);

                srcBucket.Quantity -= chunk;
                srcBucket.LastUpdated = nowUtc;
                srcBucket.RefreshGrainHash();

                var destBucket = context.InventoryStocks.Local.FirstOrDefault(x =>
                                     x.ProductId == item.ProductId &&
                                     x.WarehouseId == task.WarehouseId &&
                                     x.Status == InventoryStatus.Available &&
                                     x.LocationId == stagingLoc.Id &&
                                     x.LotId == srcBucket.LotId &&
                                     x.SerialId == srcBucket.SerialId &&
                                     x.HandlingUnitId == srcBucket.HandlingUnitId &&
                                     x.OwnerId == srcBucket.OwnerId)
                                 ?? await context.InventoryStocks.FirstOrDefaultAsync(x =>
                                     x.ProductId == item.ProductId &&
                                     x.WarehouseId == task.WarehouseId &&
                                     x.Status == InventoryStatus.Available &&
                                     x.LocationId == stagingLoc.Id &&
                                     x.LotId == srcBucket.LotId &&
                                     x.SerialId == srcBucket.SerialId &&
                                     x.HandlingUnitId == srcBucket.HandlingUnitId &&
                                     x.OwnerId == srcBucket.OwnerId, token);

                if (destBucket is null)
                {
                    destBucket = new InventoryStock
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        Status = InventoryStatus.Available,
                        LocationId = stagingLoc.Id,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        Quantity = chunk,
                        ReservedQuantity = chunk,
                        AllocatedQuantity = chunk,
                        MovingAverageCost = srcBucket.MovingAverageCost,
                        LastUpdated = nowUtc
                    };
                    destBucket.RefreshGrainHash();
                    context.InventoryStocks.Add(destBucket);
                }
                else
                {
                    destBucket.Quantity += chunk;
                    destBucket.ReservedQuantity += chunk;
                    destBucket.AllocatedQuantity += chunk;
                    destBucket.LastUpdated = nowUtc;
                    destBucket.RefreshGrainHash();
                }

                context.StockAllocations.Add(new StockAllocation
                {
                    AllocationCode = $"ALC-CD-{task.Id}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                    ReservationId = reservation.Id,
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    LocationId = stagingLoc.Id,
                    LotId = srcBucket.LotId,
                    SerialId = srcBucket.SerialId,
                    HandlingUnitId = srcBucket.HandlingUnitId,
                    OwnerId = srcBucket.OwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    Quantity = chunk,
                    Status = StockAllocationStatus.Active,
                    Strategy = AllocationStrategy.LocationOrder,
                    SelectionReason = $"CROSS_DOCK_TASK_{task.Id}",
                    AllocatedAt = nowUtc,
                    AllocatedBy = actorId,
                    Version = 1
                });

                context.InventoryLocationMovements.Add(new InventoryLocationMovement
                {
                    WarehouseId = task.WarehouseId,
                    ProductId = item.ProductId,
                    InventoryStatus = InventoryStatus.Available,
                    LotId = srcBucket.LotId,
                    SerialId = srcBucket.SerialId,
                    FromLocationId = item.SourceLocationId,
                    ToLocationId = stagingLoc.Id,
                    BaseQuantity = chunk,
                    EnteredQuantity = chunk,
                    EnteredUnitCode = !string.IsNullOrWhiteSpace(item.BaseUnitCodeSnapshot) ? item.BaseUnitCodeSnapshot : "THÙNG",
                    ReferenceType = "CrossDock",
                    ReferenceId = task.Id,
                    PutawayTaskId = task.Id,
                    PutawayTaskItemId = item.Id,
                    ReceiptId = task.ReceiptId,
                    ReceiptLineId = item.ReceiptLineId,
                    CreatedBy = actorId
                });

                var noteText = $"CROSS-DOCK từ {sourceCode} thẳng sang bãi xuất {stagingLoc.Code}";
                context.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = item.SourceLocationId,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = stagingLoc.Id,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = -chunk,
                        UnitCost = srcBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayOut",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = noteText
                    },
                    new InventoryTransaction
                    {
                        ProductId = item.ProductId,
                        WarehouseId = task.WarehouseId,
                        LocationId = stagingLoc.Id,
                        FromLocationId = item.SourceLocationId,
                        ToLocationId = stagingLoc.Id,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = InventoryStatus.Available,
                        ToInventoryStatus = InventoryStatus.Available,
                        LotId = srcBucket.LotId,
                        SerialId = srcBucket.SerialId,
                        HandlingUnitId = srcBucket.HandlingUnitId,
                        OwnerId = srcBucket.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = chunk,
                        UnitCost = srcBucket.MovingAverageCost,
                        ReferenceId = task.Id,
                        ReferenceType = "PutawayIn",
                        SourceDocumentLineId = item.ReceiptLineId,
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = noteText
                    });

                remToMove -= chunk;
            }

            if (matchedBackorder != null)
            {
                var maxRecoverable = Math.Max(0m, matchedBackorder.Quantity - matchedBackorder.CancelledQuantity);
                matchedBackorder.RecoveredQuantity = Math.Min(maxRecoverable, Math.Round(matchedBackorder.RecoveredQuantity + crossDockQty, 4));
                matchedBackorder.UpdatedAt = nowUtc;
                if (matchedBackorder.RecoveredQuantity + matchedBackorder.CancelledQuantity >= matchedBackorder.Quantity)
                {
                    matchedBackorder.Status = BackorderStatus.Fulfilled;
                    matchedBackorder.ResolvedAt = nowUtc;
                }
            }

            item.MovedBaseQuantity += crossDockQty;
            task.IsCrossDock = true;
            task.CrossDockAllocatedQty = Math.Round(task.CrossDockAllocatedQty + crossDockQty, 4);
            task.CrossDockTargetBackorderId = matchedBackorder?.Id ?? task.CrossDockTargetBackorderId;
            task.CrossDockTargetSalesOrderId = matchedSalesOrderId ?? task.CrossDockTargetSalesOrderId;
            task.CrossDockTargetExportReceiptId = matchedExportReceiptId ?? task.CrossDockTargetExportReceiptId;
            task.AssignedUserId ??= actorId;
            task.StartedAt ??= nowUtc;
            task.Status = task.Items.All(x => x.RemainingBaseQuantity == 0)
                ? PutawayTaskStatus.Completed
                : PutawayTaskStatus.InProgress;
            if (task.Status == PutawayTaskStatus.Completed)
                task.CompletedAt = nowUtc;

            context.AuditLogs.Add(Audit(
                actorId,
                "PutawayTask.CrossDockExecuted",
                task.Id,
                task.WarehouseId,
                $"CrossDockQty: {crossDockQty:0.####}; StagingBin: {stagingLoc.Code}; SalesOrderId: {task.CrossDockTargetSalesOrderId}; BackorderId: {task.CrossDockTargetBackorderId}"));

            await context.SaveChangesAsync(token);
            if (tx is not null) await tx.CommitAsync(token);
            return await GetAsync(id, token);
    }
}
