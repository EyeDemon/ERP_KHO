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
    private async Task<PutawayTaskDto> ReverseCrossDockExecutionAsync(
        int id,
        PutawayTask task,
        ExecuteCrossDockDto dto,
        int actorId,
        DateTime nowUtc,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx,
        CancellationToken token)
    {
                var cdRefTypes = new[] { "PutawayOut", "PutawayIn" };
                var allCdOrigTxs = await context.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.ReferenceId == task.Id &&
                                t.TransactionType != TransactionType.Reversal &&
                                t.ReferenceType != null &&
                                cdRefTypes.Contains(t.ReferenceType))
                    .OrderBy(t => t.Id)
                    .ToListAsync(token);

                if (!task.IsCrossDock || task.CrossDockAllocatedQty <= 0m)
                {
                    if (allCdOrigTxs.Count > 0)
                    {
                        var origIds = allCdOrigTxs.Select(x => x.Id).ToList();
                        var hasExistingReversal = await context.InventoryTransactions
                            .AsNoTracking()
                            .AnyAsync(t => t.ReversalOfTransactionId != null && origIds.Contains(t.ReversalOfTransactionId.Value), token);
                        if (hasExistingReversal)
                        {
                            throw new ConflictException(
                                "CROSS_DOCK_ALREADY_REVERSED",
                                $"Giao dịch Cross-Dock của nhiệm vụ #{task.Id} đã được đảo ngược trước đó.");
                        }
                    }
                    throw new BusinessRuleException("Nhiệm vụ chưa phát sinh Cross-Dock để đảo ngược.");
                }

                var reverseProductIds = task.Items.Select(x => x.ProductId).Distinct().OrderBy(x => x).ToList();
                await context.AcquireInventoryAllocationLocksAsync(task.WarehouseId, reverseProductIds, token);

                var alreadyReversedIds = await context.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.ReferenceId == task.Id && t.ReversalOfTransactionId != null)
                    .Select(t => t.ReversalOfTransactionId!.Value)
                    .ToListAsync(token);

                var cdTxs = allCdOrigTxs
                    .Where(t => !alreadyReversedIds.Contains(t.Id))
                    .OrderBy(t => t.Id)
                    .ToList();

                if (cdTxs.Count == 0)
                {
                    throw new ConflictException(
                        "CROSS_DOCK_ALREADY_REVERSED",
                        $"Toàn bộ bút toán Cross-Dock của nhiệm vụ #{task.Id} đã được đảo ngược trước đó.");
                }

                foreach (var orig in cdTxs)
                {
                    if (await context.InventoryTransactions.AsNoTracking().AnyAsync(t => t.ReversalOfTransactionId == orig.Id, token))
                    {
                        throw new ConflictException(
                            "CROSS_DOCK_ALREADY_REVERSED",
                            $"Bút toán Cross-Dock gốc #{orig.Id} của nhiệm vụ #{task.Id} đã tồn tại bút toán đảo trong sổ cái.");
                    }

                    var revDelta = -orig.Quantity;
                    var bucket = await context.InventoryStocks
                        .FirstOrDefaultAsync(s =>
                            s.WarehouseId == orig.WarehouseId &&
                            s.ProductId == orig.ProductId &&
                            s.LocationId == orig.LocationId &&
                            s.LotId == orig.LotId &&
                            s.SerialId == orig.SerialId &&
                            s.HandlingUnitId == orig.HandlingUnitId &&
                            s.OwnerId == orig.OwnerId &&
                            s.Status == orig.InventoryStatus,
                            token);

                    var locCode = orig.LocationId.HasValue
                        ? (await context.WarehouseLocations.AsNoTracking()
                            .Where(l => l.Id == orig.LocationId.Value)
                            .Select(l => l.Code)
                            .FirstOrDefaultAsync(token) ?? $"LOC-{orig.LocationId.Value}")
                        : "UNKNOWN";

                    if (revDelta < 0m)
                    {
                        if (bucket == null || bucket.Quantity + revDelta < 0m)
                        {
                            throw new ConflictException(
                                "CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK",
                                $"Không đủ tồn kho khả dụng tại vị trí '{locCode}' để đảo ngược Cross-Dock (Cần trừ: {Math.Abs(revDelta):0.0000}, Hiện có: {(bucket?.Quantity ?? 0m):0.0000}).");
                        }

                        var releaseAmt = Math.Min(-revDelta, bucket.ReservedQuantity);
                        bucket.ReservedQuantity = bucket.ReservedQuantity - releaseAmt;
                        var releaseAlloc = Math.Min(-revDelta, bucket.AllocatedQuantity);
                        bucket.AllocatedQuantity = bucket.AllocatedQuantity - releaseAlloc;
                    }
                    else if (bucket == null)
                    {
                        throw new ConflictException(
                            "CROSS_DOCK_REVERSAL_INSUFFICIENT_STOCK",
                            $"Không tìm thấy bucket tồn kho tại vị trí '{locCode}' để hoàn trả Cross-Dock.");
                    }

                    bucket.Quantity = bucket.Quantity + revDelta;
                    bucket.LastUpdated = nowUtc;
                    bucket.RefreshGrainHash();

                    context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        ProductId = orig.ProductId,
                        WarehouseId = orig.WarehouseId,
                        LocationId = orig.LocationId,
                        FromLocationId = orig.ToLocationId,
                        ToLocationId = orig.FromLocationId,
                        LotId = orig.LotId,
                        SerialId = orig.SerialId,
                        HandlingUnitId = orig.HandlingUnitId,
                        OwnerId = orig.OwnerId,
                        InventoryStatus = orig.InventoryStatus,
                        FromInventoryStatus = orig.ToInventoryStatus,
                        ToInventoryStatus = orig.FromInventoryStatus,
                        TransactionType = TransactionType.Reversal,
                        Quantity = revDelta,
                        UnitCost = orig.UnitCost,
                        ReferenceId = task.Id,
                        ReferenceType = orig.ReferenceType,
                        SourceDocumentLineId = orig.SourceDocumentLineId,
                        ReversalOfTransactionId = orig.Id,
                        ReasonCode = "CROSS_DOCK_REVERSAL",
                        TransactionDate = nowUtc,
                        ActualOccurredAtUtc = nowUtc,
                        PostedAtUtc = nowUtc,
                        CreatedBy = actorId,
                        Note = $"Đảo bút toán Cross-Dock PutawayTask #{task.Id} (OrigTx=#{orig.Id})"
                    });
                }

                var cdAllocations = await context.StockAllocations
                    .Include(a => a.Reservation)
                    .Where(a => a.WarehouseId == task.WarehouseId &&
                                a.SelectionReason == $"CROSS_DOCK_TASK_{task.Id}" &&
                                a.Status == StockAllocationStatus.Active)
                    .ToListAsync(token);

                foreach (var alloc in cdAllocations)
                {
                    alloc.Status = StockAllocationStatus.Released;
                    alloc.ReleasedAt = nowUtc;
                    alloc.ReleasedBy = actorId;
                    alloc.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                    if (alloc.Reservation != null)
                    {
                        if (alloc.Reservation.SourceCode == $"CD-TASK-{task.Id}")
                        {
                            alloc.Reservation.ReleasedQuantity = alloc.Reservation.Quantity;
                            alloc.Reservation.AllocatedQuantity = 0m;
                            alloc.Reservation.Status = StockReservationStatus.Released;
                            alloc.Reservation.ReleasedAt = nowUtc;
                            alloc.Reservation.ReleasedBy = actorId;
                            alloc.Reservation.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                        }
                        else
                        {
                            alloc.Reservation.Quantity = Math.Max(0m, alloc.Reservation.Quantity - alloc.Quantity);
                            alloc.Reservation.AllocatedQuantity = Math.Max(0m, alloc.Reservation.AllocatedQuantity - alloc.Quantity);
                            if (alloc.Reservation.AllocatedQuantity > 0m)
                            {
                                alloc.Reservation.Status = StockReservationStatus.Allocated;
                                alloc.Reservation.ReleasedQuantity = 0m;
                                alloc.Reservation.ReleasedAt = null;
                                alloc.Reservation.ReleasedBy = null;
                                alloc.Reservation.ReleaseReason = null;
                            }
                            else
                            {
                                alloc.Reservation.Status = StockReservationStatus.Released;
                                alloc.Reservation.ReleasedQuantity = alloc.Reservation.Quantity;
                                alloc.Reservation.ReleasedAt = nowUtc;
                                alloc.Reservation.ReleasedBy = actorId;
                                alloc.Reservation.ReleaseReason = dto.Reason ?? "Cross-Dock Reversal";
                            }
                        }
                    }
                }

                if (task.CrossDockTargetBackorderId.HasValue)
                {
                    var bo = await context.Backorders.FirstOrDefaultAsync(b => b.Id == task.CrossDockTargetBackorderId.Value, token);
                    if (bo != null)
                    {
                        bo.RecoveredQuantity = Math.Max(0m, bo.RecoveredQuantity - task.CrossDockAllocatedQty);
                        bo.Status = BackorderStatus.Open;
                        bo.ResolvedAt = null;
                        bo.UpdatedAt = nowUtc;
                    }
                }

                foreach (var it in task.Items)
                {
                    it.MovedBaseQuantity = Math.Max(0m, it.MovedBaseQuantity - task.CrossDockAllocatedQty);
                }

                task.IsCrossDock = false;
                task.CrossDockAllocatedQty = 0m;
                task.Status = PutawayTaskStatus.Open;
                task.CompletedAt = null;
                context.AuditLogs.Add(Audit(actorId, "PutawayTask.CrossDockReversed", task.Id, task.WarehouseId, $"Reason: {dto.Reason ?? "Cross-Dock Reversal"}"));
                await context.SaveChangesAsync(token);
                if (tx is not null) await tx.CommitAsync(token);
                return await GetAsync(id, token);
    }
}
