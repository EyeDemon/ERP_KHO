using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    private async Task<decimal> PersistAbcSlottingReslottingAndOutboxAsync(
        EvaluateAbcSlottingInput? request,
        int targetWarehouseId,
        string evaluationId,
        ERP.Domain.Entities.WarehouseLocation? goldenPickBin,
        List<int> candidateProductIds,
        List<object> abcClassifications,
        List<object> reslottingRecommendations,
        List<object> interleavedTaskPairs,
        int classACount,
        int classBCount,
        int classCCount,
        decimal avgDeadheadSavedPct,
        int effectiveActorId,
        DateTime now,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx,
        CancellationToken cancellationToken)
    {
        decimal executedReslottingQty = 0m;
        if (request?.PersistAuditAndOutbox != false)
        {
            if (goldenPickBin != null)
            {
                var trackedCandidates = await db.InventoryStocks
                    .Include(s => s.Product)
                    .Include(s => s.Location)
                    .Where(s => s.WarehouseId == targetWarehouseId &&
                                s.Status == InventoryStatus.Available &&
                                s.LocationId.HasValue &&
                                s.LocationId.Value != goldenPickBin.Id &&
                                (s.Quantity - s.ReservedQuantity) > 0m)
                    .ToListAsync(cancellationToken);

                var sourceStock = trackedCandidates
                    .OrderBy(s =>
                    {
                        var idx = candidateProductIds.IndexOf(s.ProductId);
                        return idx >= 0 ? idx : int.MaxValue;
                    })
                    .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                    .FirstOrDefault();

                if (sourceStock != null && sourceStock.LocationId.HasValue)
                {
                    var srcLocId = sourceStock.LocationId.Value;
                    var srcLocCode = sourceStock.Location?.Code?.Trim() ?? $"LOC-{srcLocId}";
                    var dstLocCode = goldenPickBin.Code.Trim();
                    var availToMove = Math.Round(sourceStock.Quantity - sourceStock.ReservedQuantity, 4, MidpointRounding.AwayFromZero);
                    var moveQty = Math.Round(Math.Min(availToMove, 10m), 4, MidpointRounding.AwayFromZero);

                    if (goldenPickBin.MaxWeightKg is > 0m && sourceStock.Product?.UnitWeightKg is > 0m)
                    {
                        var maxByWeight = Math.Floor(goldenPickBin.MaxWeightKg.Value / sourceStock.Product.UnitWeightKg.Value);
                        if (maxByWeight > 0m)
                        {
                            moveQty = Math.Min(moveQty, maxByWeight);
                        }
                    }

                    if (goldenPickBin.MaxVolumeM3 is > 0m && sourceStock.Product?.UnitVolumeM3 is > 0m)
                    {
                        var maxByVol = Math.Floor(goldenPickBin.MaxVolumeM3.Value / sourceStock.Product.UnitVolumeM3.Value);
                        if (maxByVol > 0m)
                        {
                            moveQty = Math.Min(moveQty, maxByVol);
                        }
                    }

                    if (moveQty > 0m)
                    {
                        sourceStock.Quantity = Math.Round(sourceStock.Quantity - moveQty, 4, MidpointRounding.AwayFromZero);
                        sourceStock.LastUpdated = now;
                        sourceStock.RefreshGrainHash();

                        var destStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                            s.WarehouseId == targetWarehouseId &&
                            s.ProductId == sourceStock.ProductId &&
                            s.LocationId == goldenPickBin.Id &&
                            s.LotId == sourceStock.LotId &&
                            s.SerialId == sourceStock.SerialId &&
                            s.HandlingUnitId == sourceStock.HandlingUnitId &&
                            s.OwnerId == sourceStock.OwnerId &&
                            s.Status == sourceStock.Status,
                            cancellationToken);

                        if (destStock == null)
                        {
                            destStock = new ERP.Domain.Entities.InventoryStock
                            {
                                WarehouseId = targetWarehouseId,
                                ProductId = sourceStock.ProductId,
                                LocationId = goldenPickBin.Id,
                                LotId = sourceStock.LotId,
                                SerialId = sourceStock.SerialId,
                                HandlingUnitId = sourceStock.HandlingUnitId,
                                OwnerId = sourceStock.OwnerId,
                                Status = sourceStock.Status,
                                Quantity = moveQty,
                                ReservedQuantity = 0m,
                                LastUpdated = now
                            };
                            destStock.RefreshGrainHash();
                            db.InventoryStocks.Add(destStock);
                        }
                        else
                        {
                            destStock.Quantity = Math.Round(destStock.Quantity + moveQty, 4, MidpointRounding.AwayFromZero);
                            destStock.LastUpdated = now;
                            destStock.RefreshGrainHash();
                        }

                        var skuCode = sourceStock.Product?.Code?.Trim() ?? $"SKU-{sourceStock.ProductId}";
                        var slotNote = $"{evaluationId}: Re-slotting SKU {skuCode} ({srcLocCode} -> {dstLocCode})";

                        db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = sourceStock.ProductId,
                            WarehouseId = targetWarehouseId,
                            LocationId = srcLocId,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            HandlingUnitId = sourceStock.HandlingUnitId,
                            OwnerId = sourceStock.OwnerId,
                            InventoryStatus = sourceStock.Status,
                            FromInventoryStatus = sourceStock.Status,
                            ToInventoryStatus = sourceStock.Status,
                            TransactionType = TransactionType.Move,
                            Quantity = -moveQty,
                            ReferenceId = sourceStock.Id,
                            ReferenceType = "AbcSlottingReSlotting",
                            ReasonCode = "ABC_DYNAMIC_RESLOTTING",
                            TransactionDate = now,
                            ActualOccurredAtUtc = now,
                            PostedAtUtc = now,
                            CreatedBy = effectiveActorId,
                            Note = slotNote
                        });

                        db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = sourceStock.ProductId,
                            WarehouseId = targetWarehouseId,
                            LocationId = goldenPickBin.Id,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            HandlingUnitId = sourceStock.HandlingUnitId,
                            OwnerId = sourceStock.OwnerId,
                            InventoryStatus = sourceStock.Status,
                            FromInventoryStatus = sourceStock.Status,
                            ToInventoryStatus = sourceStock.Status,
                            TransactionType = TransactionType.Move,
                            Quantity = moveQty,
                            ReferenceId = sourceStock.Id,
                            ReferenceType = "AbcSlottingReSlotting",
                            ReasonCode = "ABC_DYNAMIC_RESLOTTING",
                            TransactionDate = now,
                            ActualOccurredAtUtc = now,
                            PostedAtUtc = now,
                            CreatedBy = effectiveActorId,
                            Note = slotNote
                        });

                        db.InventoryLocationMovements.Add(new ERP.Domain.Entities.InventoryLocationMovement
                        {
                            WarehouseId = targetWarehouseId,
                            ProductId = sourceStock.ProductId,
                            InventoryStatus = sourceStock.Status,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            BaseQuantity = moveQty,
                            EnteredQuantity = moveQty,
                            EnteredUnitCode = "EA",
                            ReferenceType = "AbcSlottingReSlotting",
                            ReferenceId = sourceStock.Id,
                            CreatedBy = effectiveActorId,
                            CreatedAt = now
                        });

                        executedReslottingQty = moveQty;
                    }
                }
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "InventoryControl",
                EntityId = targetWarehouseId,
                Action = "InventoryControl.AbcSlottingAndTaskInterleaving",
                NewValues = $"{evaluationId}:A={classACount},B={classBCount},C={classCCount},Pairs={interleavedTaskPairs.Count},MovedQty={executedReslottingQty:0.####}",
                Reason = $"Phân tích tối ưu vị trí kệ động ABC Velocity ({abcClassifications.Count} SKU) & ghép cặp Chuyến kép Task Interleaving ({interleavedTaskPairs.Count} cặp, tiết kiệm {avgDeadheadSavedPct:0.##}% quãng đường xe nâng chạy rỗng)",
                Result = "Success",
                Timestamp = now
            });

            db.IntegrationOutboxMessages.Add(new ERP.Domain.Entities.IntegrationOutboxMessage
            {
                EventId = evaluationId,
                EventType = "AbcSlottingAndInterleavingEvaluated",
                AggregateType = "Warehouse",
                AggregateId = targetWarehouseId,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    evaluationId,
                    warehouseId = targetWarehouseId,
                    evaluatedSkuCount = abcClassifications.Count,
                    classACount,
                    classBCount,
                    classCCount,
                    reslottingCount = reslottingRecommendations.Count,
                    executedReslottingBaseQty = executedReslottingQty,
                    interleavedPairCount = interleavedTaskPairs.Count,
                    averageDeadheadSavedPercent = avgDeadheadSavedPct,
                    evaluatedAtUtc = now
                }),
                Status = "PENDING",
                CreatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }
        return executedReslottingQty;
    }
}
