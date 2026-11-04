using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    private async Task<WmsEndpointOutcome> ReverseBreakBulkReplenishmentCoreAsync(
        ExecuteBreakBulkReplenishmentInput request,
        int targetWarehouseId,
        int? resolvedProductId,
        string resolvedProductCode,
        string resolvedProductName,
        int? resolvedSourceLocationId,
        int? resolvedTargetLocationId,
        string resolvedSourceUomCode,
        string baseUomCode,
        decimal sourcePackQty,
        decimal conversionFactor,
        decimal movedBaseQty,
        int effectiveActorId,
        DateTime now,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx,
        CancellationToken cancellationToken)
    {
            if (!resolvedProductId.HasValue) return WmsEndpointOutcome.BadRequest(new { message = "ProductId is required." });
            var alreadyReversedTxIds = await db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ProductId == resolvedProductId.Value &&
                            t.ReversalOfTransactionId != null)
                .Select(t => t.ReversalOfTransactionId!.Value)
                .ToListAsync(cancellationToken);

            var origQuery = db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ProductId == resolvedProductId.Value &&
                            t.TransactionType != TransactionType.Reversal &&
                            t.ReferenceType == "BreakBulkReplenishment" &&
                            !alreadyReversedTxIds.Contains(t.Id));

            if (request.OriginalReferenceId.HasValue)
            {
                origQuery = origQuery.Where(t => t.ReferenceId == request.OriginalReferenceId.Value);
            }
            else if (resolvedSourceLocationId.HasValue && resolvedTargetLocationId.HasValue)
            {
                origQuery = origQuery.Where(t =>
                    t.FromLocationId == resolvedSourceLocationId.Value &&
                    t.ToLocationId == resolvedTargetLocationId.Value);
            }

            var origTxs = await origQuery
                .OrderByDescending(t => t.Id)
                .ToListAsync(cancellationToken);

            var origOuts = origTxs.Where(t => t.Quantity < 0m).ToList();
            var origIns = origTxs.Where(t => t.Quantity > 0m).ToList();
            var matchedOrigPairs = new List<(ERP.Domain.Entities.InventoryTransaction OrigOut, ERP.Domain.Entities.InventoryTransaction OrigIn, decimal LegQty)>();
            var remainingToMatch = movedBaseQty;
            var pairCount = Math.Min(origOuts.Count, origIns.Count);
            for (var i = 0; i < pairCount && remainingToMatch > 0m; i++)
            {
                var oOut = origOuts[i];
                var oIn = origIns[i];
                var legQty = Math.Min(remainingToMatch, Math.Min(Math.Abs(oOut.Quantity), oIn.Quantity));
                if (legQty <= 0m) continue;
                matchedOrigPairs.Add((oOut, oIn, legQty));
                remainingToMatch = Math.Round(remainingToMatch - legQty, 4, MidpointRounding.AwayFromZero);
            }

            if (matchedOrigPairs.Count == 0 || remainingToMatch > 0m)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "BREAK_BULK_REVERSAL_ORIGINAL_NOT_FOUND_OR_ALREADY_REVERSED",
                    "Không tìm thấy cặp bút toán tách thùng (Break-Bulk) gốc chưa đảo hoặc giao dịch này đã được đảo ngược trước đó.");
            }

            resolvedSourceLocationId ??= matchedOrigPairs[0].OrigOut.FromLocationId ?? matchedOrigPairs[0].OrigOut.LocationId;
            resolvedTargetLocationId ??= matchedOrigPairs[0].OrigIn.ToLocationId ?? matchedOrigPairs[0].OrigIn.LocationId;

            if (!resolvedSourceLocationId.HasValue || !resolvedTargetLocationId.HasValue)
            {
                return WmsEndpointOutcome.BadRequest(new { message = "Cần chỉ định vị trí nguồn (Reserve) và vị trí đích (Forward-Pick) để hoàn tác xả thùng." });
            }

            var requiredOwnerId = matchedOrigPairs[0].OrigIn.OwnerId;
            var pickStock = await db.InventoryStocks
                .Include(s => s.Location)
                .Where(s => s.WarehouseId == targetWarehouseId &&
                            s.ProductId == resolvedProductId.Value &&
                            s.LocationId == resolvedTargetLocationId.Value &&
                            s.HandlingUnitId == null &&
                            s.OwnerId == requiredOwnerId &&
                            s.Status == InventoryStatus.Available &&
                            (request.SourceLotId == null || s.LotId == request.SourceLotId))
                .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            var pickAvailable = pickStock != null ? pickStock.Quantity - pickStock.ReservedQuantity : 0m;
            if (pickStock == null || pickAvailable < movedBaseQty)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = $"INV_INSUFFICIENT_AVAILABLE_STOCK: Tồn khả dụng tại kệ nhặt lẻ ({pickAvailable:0.####}) không đủ để đảo bút toán xả thùng ({movedBaseQty:0.####} {baseUomCode}).", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
            }

            pickStock.Quantity = Math.Round(pickStock.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
            pickStock.LastUpdated = now;
            pickStock.RefreshGrainHash();

            var revId = $"BB-REV-{now:yyyyMMdd-HHmmss}";
            var revNote = $"{revId}: Đảo bút toán xả thùng {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode})";
            ERP.Domain.Entities.InventoryStock? lastReserveStock = null;

            foreach (var (origOut, origIn, legQty) in matchedOrigPairs)
            {
                int origOutTxId = origOut.Id;
                int origInTxId = origIn.Id;
                var targetHuId = request.SourceHandlingUnitId ?? origOut.HandlingUnitId;
                var targetLotId = origOut.LotId ?? pickStock.LotId;
                var targetSerialId = origOut.SerialId ?? pickStock.SerialId;
                var targetOwnerId = origOut.OwnerId ?? pickStock.OwnerId;

                var reserveStock = db.InventoryStocks.Local.FirstOrDefault(s =>
                        s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.LocationId == resolvedSourceLocationId.Value &&
                        s.LotId == targetLotId &&
                        s.SerialId == targetSerialId &&
                        s.HandlingUnitId == targetHuId &&
                        s.OwnerId == targetOwnerId &&
                        s.Status == InventoryStatus.Available)
                    ?? await db.InventoryStocks.FirstOrDefaultAsync(s =>
                        s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.LocationId == resolvedSourceLocationId.Value &&
                        s.LotId == targetLotId &&
                        s.SerialId == targetSerialId &&
                        s.HandlingUnitId == targetHuId &&
                        s.OwnerId == targetOwnerId &&
                        s.Status == InventoryStatus.Available,
                        cancellationToken);

                if (reserveStock == null)
                {
                    reserveStock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = targetWarehouseId,
                        ProductId = resolvedProductId.Value,
                        LocationId = resolvedSourceLocationId.Value,
                        LotId = targetLotId,
                        SerialId = targetSerialId,
                        HandlingUnitId = targetHuId,
                        OwnerId = targetOwnerId,
                        Status = InventoryStatus.Available,
                        Quantity = legQty,
                        ReservedQuantity = 0m,
                        LastUpdated = now
                    };
                    reserveStock.RefreshGrainHash();
                    db.InventoryStocks.Add(reserveStock);
                }
                else
                {
                    reserveStock.Quantity = Math.Round(reserveStock.Quantity + legQty, 4, MidpointRounding.AwayFromZero);
                    reserveStock.LastUpdated = now;
                    reserveStock.RefreshGrainHash();
                }

                lastReserveStock = reserveStock;

                if (targetHuId.HasValue)
                {
                    var huContent = db.HandlingUnitContents.Local.FirstOrDefault(c =>
                            c.HandlingUnitId == targetHuId.Value &&
                            c.ProductId == resolvedProductId.Value)
                        ?? await db.HandlingUnitContents.FirstOrDefaultAsync(c =>
                            c.HandlingUnitId == targetHuId.Value &&
                            c.ProductId == resolvedProductId.Value,
                            cancellationToken);
                    if (huContent != null)
                    {
                        huContent.Quantity = Math.Round(huContent.Quantity + legQty, 4, MidpointRounding.AwayFromZero);
                    }
                }

                var revOutTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = resolvedProductId.Value,
                    WarehouseId = targetWarehouseId,
                    LocationId = resolvedTargetLocationId.Value,
                    FromLocationId = resolvedTargetLocationId.Value,
                    ToLocationId = resolvedSourceLocationId.Value,
                    LotId = targetLotId,
                    SerialId = targetSerialId,
                    HandlingUnitId = null,
                    OwnerId = targetOwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    FromInventoryStatus = InventoryStatus.Available,
                    ToInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Reversal,
                    Quantity = -legQty,
                    ReferenceId = request.OriginalReferenceId ?? pickStock.Id,
                    ReferenceType = "BreakBulkReplenishmentReversal",
                    ReversalOfTransactionId = origInTxId,
                    ReasonCode = "BREAK_BULK_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = revNote
                };
                var revInTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = resolvedProductId.Value,
                    WarehouseId = targetWarehouseId,
                    LocationId = resolvedSourceLocationId.Value,
                    FromLocationId = resolvedTargetLocationId.Value,
                    ToLocationId = resolvedSourceLocationId.Value,
                    LotId = targetLotId,
                    SerialId = targetSerialId,
                    HandlingUnitId = targetHuId,
                    OwnerId = targetOwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    FromInventoryStatus = InventoryStatus.Available,
                    ToInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Reversal,
                    Quantity = legQty,
                    ReferenceId = request.OriginalReferenceId ?? reserveStock.Id,
                    ReferenceType = "BreakBulkReplenishmentReversal",
                    ReversalOfTransactionId = origOutTxId,
                    ReasonCode = "BREAK_BULK_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = revNote
                };

                db.InventoryTransactions.Add(revOutTx);
                db.InventoryTransactions.Add(revInTx);
            }

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return WmsEndpointOutcome.Ok(new
            {
                replenishmentId = revId,
                warehouseId = targetWarehouseId,
                productId = resolvedProductId.Value,
                productCode = resolvedProductCode,
                productName = resolvedProductName,
                sourceLocationId = resolvedSourceLocationId.Value,
                targetLocationId = resolvedTargetLocationId.Value,
                sourceUomCode = resolvedSourceUomCode,
                baseUomCode,
                sourcePackQty,
                conversionFactor,
                movedBaseQty,
                baseUomDelta = 0.0000m,
                sourceRemainingQuantity = lastReserveStock?.Quantity ?? 0m,
                targetResultingQuantity = pickStock.Quantity,
                reversed = true,
                status = "REVERSED",
                statusLabel = "Đã hoàn tác xả thùng bổ sung kệ nhặt lẻ",
                executedAtUtc = now
            });
    }
}
