using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    private async Task<WmsEndpointOutcome> ReverseAbcSlottingEvaluationCoreAsync(
        EvaluateAbcSlottingInput request,
        int targetWarehouseId,
        int effectiveActorId,
        DateTime now,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx,
        CancellationToken cancellationToken)
    {
            var evalIdToReverse = request.OriginalEvaluationId?.Trim();
            var origOutbox = !string.IsNullOrWhiteSpace(evalIdToReverse)
                ? await db.IntegrationOutboxMessages.FirstOrDefaultAsync(
                    m => m.EventId == evalIdToReverse && m.EventType == "AbcSlottingAndInterleavingEvaluated",
                    cancellationToken)
                : await db.IntegrationOutboxMessages
                    .Where(m => m.AggregateId == targetWarehouseId && m.EventType == "AbcSlottingAndInterleavingEvaluated")
                    .OrderByDescending(m => m.Id)
                    .FirstOrDefaultAsync(cancellationToken);

            if (origOutbox == null || string.Equals(origOutbox.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ABC_SLOTTING_EVALUATION_ALREADY_REVERSED",
                    "Phiên đánh giá tối ưu ABC Slotting & Task Interleaving không tồn tại hoặc đã được hoàn tác trước đó.");
            }

            var alreadyRevIds = await db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId && t.ReversalOfTransactionId != null)
                .Select(t => t.ReversalOfTransactionId!.Value)
                .ToListAsync(cancellationToken);

            var origSlottingTxs = await db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ReferenceType == "AbcSlottingReSlotting" &&
                            t.TransactionType != TransactionType.Reversal &&
                            t.Note != null &&
                            t.Note.StartsWith(origOutbox.EventId + ":") &&
                            !alreadyRevIds.Contains(t.Id))
                .OrderBy(t => t.Id)
                .ToListAsync(cancellationToken);

            var slottingOuts = origSlottingTxs.Where(t => t.Quantity < 0m).ToList();
            var slottingIns = origSlottingTxs.Where(t => t.Quantity > 0m).ToList();
            var slottingPairCount = Math.Min(slottingOuts.Count, slottingIns.Count);
            decimal totalReversedQty = 0m;

            for (var pIdx = 0; pIdx < slottingPairCount; pIdx++)
            {
                var origOut = slottingOuts[pIdx];
                var origIn = slottingIns[pIdx];
                var revQty = Math.Round(Math.Min(Math.Abs(origOut.Quantity), origIn.Quantity), 4, MidpointRounding.AwayFromZero);
                if (revQty <= 0m) continue;

                var destStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                    s.WarehouseId == origIn.WarehouseId &&
                    s.ProductId == origIn.ProductId &&
                    s.LocationId == origIn.LocationId &&
                    s.LotId == origIn.LotId &&
                    s.SerialId == origIn.SerialId &&
                    s.HandlingUnitId == origIn.HandlingUnitId &&
                    s.OwnerId == origIn.OwnerId &&
                    s.Status == origIn.InventoryStatus,
                    cancellationToken);

                var destAvailable = destStock != null ? destStock.Quantity - destStock.ReservedQuantity : 0m;
                if (destStock == null || destAvailable < revQty)
                {
                    throw new ERP.Domain.Exceptions.ConflictException(
                        "SLOTTING_REVERSAL_INSUFFICIENT_DEST_AVAILABLE_STOCK",
                        $"Tồn khả dụng tại kệ đích không đủ để hoàn tác đảo kệ động ABC Slotting ({destAvailable:0.####} < {revQty:0.####}).");
                }

                destStock.Quantity = Math.Round(destStock.Quantity - revQty, 4, MidpointRounding.AwayFromZero);
                destStock.LastUpdated = now;
                destStock.RefreshGrainHash();

                var srcStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                    s.WarehouseId == origOut.WarehouseId &&
                    s.ProductId == origOut.ProductId &&
                    s.LocationId == origOut.LocationId &&
                    s.LotId == origOut.LotId &&
                    s.SerialId == origOut.SerialId &&
                    s.HandlingUnitId == origOut.HandlingUnitId &&
                    s.OwnerId == origOut.OwnerId &&
                    s.Status == origOut.InventoryStatus,
                    cancellationToken);

                if (srcStock == null)
                {
                    srcStock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = origOut.WarehouseId,
                        ProductId = origOut.ProductId,
                        LocationId = origOut.LocationId,
                        LotId = origOut.LotId,
                        SerialId = origOut.SerialId,
                        HandlingUnitId = origOut.HandlingUnitId,
                        OwnerId = origOut.OwnerId,
                        Status = origOut.InventoryStatus,
                        Quantity = revQty,
                        ReservedQuantity = 0m,
                        LastUpdated = now
                    };
                    srcStock.RefreshGrainHash();
                    db.InventoryStocks.Add(srcStock);
                }
                else
                {
                    srcStock.Quantity = Math.Round(srcStock.Quantity + revQty, 4, MidpointRounding.AwayFromZero);
                    srcStock.LastUpdated = now;
                    srcStock.RefreshGrainHash();
                }

                db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = origIn.ProductId,
                    WarehouseId = origIn.WarehouseId,
                    LocationId = origIn.LocationId,
                    FromLocationId = origIn.LocationId,
                    ToLocationId = origOut.LocationId,
                    LotId = origIn.LotId,
                    SerialId = origIn.SerialId,
                    HandlingUnitId = origIn.HandlingUnitId,
                    OwnerId = origIn.OwnerId,
                    InventoryStatus = origIn.InventoryStatus,
                    FromInventoryStatus = origIn.InventoryStatus,
                    ToInventoryStatus = origOut.InventoryStatus,
                    TransactionType = TransactionType.Reversal,
                    Quantity = -revQty,
                    ReferenceId = origIn.ReferenceId,
                    ReferenceType = "AbcSlottingReSlottingReversal",
                    ReversalOfTransactionId = origIn.Id,
                    ReasonCode = "ABC_SLOTTING_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = $"{origOutbox.EventId}: Đảo bút toán Re-slotting (-{revQty:0.####})"
                });

                db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = origOut.ProductId,
                    WarehouseId = origOut.WarehouseId,
                    LocationId = origOut.LocationId,
                    FromLocationId = origIn.LocationId,
                    ToLocationId = origOut.LocationId,
                    LotId = origOut.LotId,
                    SerialId = origOut.SerialId,
                    HandlingUnitId = origOut.HandlingUnitId,
                    OwnerId = origOut.OwnerId,
                    InventoryStatus = origOut.InventoryStatus,
                    FromInventoryStatus = origIn.InventoryStatus,
                    ToInventoryStatus = origOut.InventoryStatus,
                    TransactionType = TransactionType.Reversal,
                    Quantity = revQty,
                    ReferenceId = origOut.ReferenceId,
                    ReferenceType = "AbcSlottingReSlottingReversal",
                    ReversalOfTransactionId = origOut.Id,
                    ReasonCode = "ABC_SLOTTING_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = $"{origOutbox.EventId}: Đảo bút toán Re-slotting (+{revQty:0.####})"
                });

                totalReversedQty = Math.Round(totalReversedQty + revQty, 4, MidpointRounding.AwayFromZero);
            }

            origOutbox.Status = "REVERSED";
            origOutbox.LastError = "Reversed by operator/test cleanup";

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "InventoryControl",
                EntityId = targetWarehouseId,
                Action = "InventoryControl.AbcSlottingAndTaskInterleavingReversed",
                NewValues = origOutbox.EventId,
                Reason = $"Hoàn tác phiên đánh giá tối ưu vị trí ABC & Task Interleaving ({origOutbox.EventId})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return WmsEndpointOutcome.Ok(new
            {
                evaluationId = origOutbox.EventId,
                warehouseId = targetWarehouseId,
                reversedBaseQuantity = totalReversedQty,
                reversed = true,
                status = "REVERSED",
                statusLabel = $"Đã hoàn tác phiên đánh giá tối ưu vị trí ABC & Task Interleaving ({origOutbox.EventId})",
                reversedAtUtc = now
            });
    }

    private async Task<WmsEndpointOutcome> ReverseThreePlBillingStatementCoreAsync(
        ThreePlDepositorPortalInput request,
        int? warehouseId,
        IWarehouseAuthorizationService? effectiveAuth,
        int effectiveActorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
#pragma warning disable CS0618
            var boundOwnerId = request.AuthenticatedOwnerId;
#pragma warning restore CS0618
            var revOwnerId = request.RequestedOwnerId ?? request.OwnerId ?? boundOwnerId;
            if (!request.OriginalStatementId.HasValue && !revOwnerId.HasValue)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new
                {
                    code = "THREEPL_REVERSAL_TARGET_REQUIRED",
                    errorCode = "THREEPL_REVERSAL_TARGET_REQUIRED",
                    message = "Vui lòng chỉ định mã bảng kê gốc (OriginalStatementId) hoặc chủ hàng (OwnerId) để hoàn tác."
                });
            }

            var stmtQuery = db.ThreePlBillingStatements.AsQueryable();
            if (request.OriginalStatementId.HasValue)
            {
                stmtQuery = stmtQuery.Where(s => s.Id == request.OriginalStatementId.Value);
            }
            if (revOwnerId.HasValue)
            {
                stmtQuery = stmtQuery.Where(s => s.PartnerId == revOwnerId.Value);
            }

            var targetStmt = await stmtQuery.OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (targetStmt == null || string.Equals(targetStmt.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "THREEPL_STATEMENT_ALREADY_REVERSED",
                    "Bảng kê phí lưu kho 3PL của chủ hàng không tồn tại hoặc đã được hoàn tác trước đó.");
            }

            if (boundOwnerId.HasValue && targetStmt.PartnerId != boundOwnerId.Value)
            {
                throw new ERP.Application.Exceptions.ForbiddenException(
                    $"THREEPL_DEPOSITOR_CROSS_OWNER_FORBIDDEN: Chủ hàng ký gửi 3PL (OwnerId={boundOwnerId.Value}) không có quyền hoàn tác bảng kê phí của chủ hàng khác (OwnerId={targetStmt.PartnerId}).");
            }

            if (targetStmt.WarehouseId is > 0 && effectiveAuth != null)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(targetStmt.WarehouseId.Value, cancellationToken);
            }

            var relatedSnapshots = await db.ThreePlStorageSnapshots
                .Where(s => s.OwnerId == targetStmt.PartnerId && s.SnapshotCode.StartsWith($"3PL-SNAP-{targetStmt.PartnerId}-"))
                .ToListAsync(cancellationToken);
            if (relatedSnapshots.Count > 0)
            {
                db.ThreePlStorageSnapshots.RemoveRange(relatedSnapshots);
            }

            var removedStatementId = targetStmt.Id;
            var removedStatementCode = targetStmt.StatementCode;
            db.ThreePlBillingStatements.Remove(targetStmt);

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetStmt.WarehouseId ?? warehouseId ?? 1,
                EntityName = "ThreePlBillingStatement",
                EntityId = removedStatementId,
                Action = "ThreePlDepositorPortal.ReverseBillingStatement",
                NewValues = $"Reversed:{removedStatementCode}",
                Reason = $"Hoàn tác bảng kê phí dịch vụ 3PL chủ hàng ({removedStatementCode})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);

            return WmsEndpointOutcome.Ok(new
            {
                statementId = removedStatementId,
                statementCode = removedStatementCode,
                reversed = true,
                status = "REVERSED",
                statusLabel = $"Đã hoàn tác bảng kê phí dịch vụ 3PL ({removedStatementCode})",
                reversedAtUtc = now
            });
    }
}
