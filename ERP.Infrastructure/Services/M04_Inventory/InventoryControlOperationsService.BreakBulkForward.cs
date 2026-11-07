using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    private async Task<WmsEndpointOutcome> ExecuteForwardBreakBulkReplenishmentCoreAsync(
        ExecuteBreakBulkReplenishmentInput? request,
        int targetWarehouseId,
        int? resolvedProductId,
        string resolvedProductCode,
        string resolvedProductName,
        int? resolvedUomId,
        string resolvedSourceUomCode,
        string baseUomCode,
        decimal sourcePackQty,
        decimal conversionFactor,
        decimal movedBaseQty,
        int? resolvedSourceLocationId,
        string resolvedSourceLocationCode,
        int? resolvedTargetLocationId,
        string resolvedTargetLocationCode,
        int effectiveActorId,
        DateTime now,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx,
        CancellationToken cancellationToken)
    {
        // Forward Break-Bulk Replenishment
        if (!resolvedProductId.HasValue) return WmsEndpointOutcome.BadRequest(new { message = "ProductId is required." });
        var sourceQuery = db.InventoryStocks
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId.HasValue);

        if (resolvedSourceLocationId.HasValue)
            sourceQuery = sourceQuery.Where(s => s.LocationId == resolvedSourceLocationId.Value);
        if (request?.SourceHandlingUnitId.HasValue == true)
            sourceQuery = sourceQuery.Where(s => s.HandlingUnitId == request.SourceHandlingUnitId.Value);
        if (request?.SourceLotId.HasValue == true)
            sourceQuery = sourceQuery.Where(s => s.LotId == request.SourceLotId.Value);

        var sourceStockBucket = await sourceQuery
            .OrderByDescending(s => s.HandlingUnitId.HasValue ? 1 : 0)
            .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var sourceAvail = sourceStockBucket != null ? sourceStockBucket.Quantity - sourceStockBucket.ReservedQuantity : 0m;
        if (sourceStockBucket == null || sourceAvail < movedBaseQty)
        {
            return WmsEndpointOutcome.UnprocessableEntity(
                new { message = $"INV_INSUFFICIENT_AVAILABLE_STOCK: Tồn khả dụng tại vị trí lưu trữ ({sourceAvail:0.####} {baseUomCode}) không đủ để xả {sourcePackQty:0.####} {resolvedSourceUomCode} ({movedBaseQty:0.####} {baseUomCode}).", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
        }

        resolvedSourceLocationId = sourceStockBucket.LocationId!.Value;
        resolvedSourceLocationCode = sourceStockBucket.Location?.Code?.Trim() ?? $"LOC-{resolvedSourceLocationId}";

        if (!resolvedTargetLocationId.HasValue || resolvedTargetLocationId.Value == resolvedSourceLocationId.Value)
        {
            var pickLoc = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId &&
                            l.IsActive &&
                            !l.IsBlocked &&
                            !l.IsSystemManaged &&
                            l.IsPickable &&
                            l.Id != resolvedSourceLocationId.Value)
                .OrderBy(l => l.Code)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId &&
                                l.IsActive &&
                                l.Id != resolvedSourceLocationId.Value)
                    .OrderBy(l => l.Code)
                    .FirstOrDefaultAsync(cancellationToken);

            if (pickLoc == null)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_TARGET_LOCATION_NOT_FOUND: Không tìm thấy vị trí kệ nhặt lẻ đích (Forward-Pick Bin) hợp lệ.", code = "INV_TARGET_LOCATION_NOT_FOUND" });
            }

            resolvedTargetLocationId = pickLoc.Id;
            resolvedTargetLocationCode = pickLoc.Code.Trim();
        }
        else if (string.IsNullOrWhiteSpace(resolvedTargetLocationCode))
        {
            var pickLoc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == resolvedTargetLocationId.Value, cancellationToken);
            resolvedTargetLocationCode = pickLoc?.Code?.Trim() ?? $"LOC-{resolvedTargetLocationId.Value}";
        }

        var detachedHandlingUnitId = sourceStockBucket.HandlingUnitId;

        sourceStockBucket.Quantity = Math.Round(sourceStockBucket.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
        sourceStockBucket.LastUpdated = now;
        sourceStockBucket.RefreshGrainHash();

        if (detachedHandlingUnitId.HasValue)
        {
            var huContent = await db.HandlingUnitContents
                .FirstOrDefaultAsync(c => c.HandlingUnitId == detachedHandlingUnitId.Value &&
                                          c.ProductId == resolvedProductId.Value, cancellationToken);
            if (huContent != null && huContent.Quantity >= movedBaseQty)
            {
                huContent.Quantity = Math.Round(huContent.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
            }
        }

        // Deposit into Forward-Pick Bin in BaseUom (detached from source HandlingUnit: HandlingUnitId = null)
        var targetStockBucket = await db.InventoryStocks
            .FirstOrDefaultAsync(s =>
                s.WarehouseId == targetWarehouseId &&
                s.ProductId == resolvedProductId.Value &&
                s.LocationId == resolvedTargetLocationId.Value &&
                s.LotId == sourceStockBucket.LotId &&
                s.SerialId == sourceStockBucket.SerialId &&
                s.HandlingUnitId == null &&
                s.OwnerId == sourceStockBucket.OwnerId &&
                s.Status == InventoryStatus.Available,
                cancellationToken);

        if (targetStockBucket == null)
        {
            targetStockBucket = new ERP.Domain.Entities.InventoryStock
            {
                WarehouseId = targetWarehouseId,
                ProductId = resolvedProductId.Value,
                LocationId = resolvedTargetLocationId.Value,
                LotId = sourceStockBucket.LotId,
                SerialId = sourceStockBucket.SerialId,
                HandlingUnitId = null,
                OwnerId = sourceStockBucket.OwnerId,
                Status = InventoryStatus.Available,
                Quantity = movedBaseQty,
                ReservedQuantity = 0m,
                LastUpdated = now
            };
            targetStockBucket.RefreshGrainHash();
            db.InventoryStocks.Add(targetStockBucket);
        }
        else
        {
            targetStockBucket.Quantity = Math.Round(targetStockBucket.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
            targetStockBucket.LastUpdated = now;
            targetStockBucket.RefreshGrainHash();
        }

        var breakBulkId = $"BB-REPL-{now:yyyyMMdd-HHmmss}";
        var txNote = string.IsNullOrWhiteSpace(request?.Note)
            ? $"{breakBulkId}: Xả thùng bổ sung kệ nhặt lẻ {sourcePackQty:0.####} {resolvedSourceUomCode} x {conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode} ({resolvedSourceLocationCode} -> {resolvedTargetLocationCode})"
            : $"{breakBulkId}: {request!.Note!.Trim()} [{sourcePackQty:0.####} {resolvedSourceUomCode} x {conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode}]";

        var outTx = new ERP.Domain.Entities.InventoryTransaction
        {
            ProductId = resolvedProductId.Value,
            WarehouseId = targetWarehouseId,
            LocationId = resolvedSourceLocationId.Value,
            FromLocationId = resolvedSourceLocationId.Value,
            ToLocationId = resolvedTargetLocationId.Value,
            LotId = sourceStockBucket.LotId,
            SerialId = sourceStockBucket.SerialId,
            HandlingUnitId = detachedHandlingUnitId,
            OwnerId = sourceStockBucket.OwnerId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = -movedBaseQty,
            ReferenceId = sourceStockBucket.Id,
            ReferenceType = "BreakBulkReplenishment",
            TransactionDate = now,
            CreatedBy = effectiveActorId,
            Note = txNote
        };

        var inTx = new ERP.Domain.Entities.InventoryTransaction
        {
            ProductId = resolvedProductId.Value,
            WarehouseId = targetWarehouseId,
            LocationId = resolvedTargetLocationId.Value,
            FromLocationId = resolvedSourceLocationId.Value,
            ToLocationId = resolvedTargetLocationId.Value,
            LotId = sourceStockBucket.LotId,
            SerialId = sourceStockBucket.SerialId,
            HandlingUnitId = null,
            OwnerId = sourceStockBucket.OwnerId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = movedBaseQty,
            ReferenceId = sourceStockBucket.Id,
            ReferenceType = "BreakBulkReplenishment",
            TransactionDate = now,
            CreatedBy = effectiveActorId,
            Note = txNote
        };

        db.InventoryTransactions.Add(outTx);
        db.InventoryTransactions.Add(inTx);

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = targetWarehouseId,
            EntityName = "InventoryStock",
            EntityId = sourceStockBucket.Id,
            Action = "ExecuteBreakBulkReplenishment",
            NewValues = $"{breakBulkId}:{resolvedProductCode}:{sourcePackQty:0.####} {resolvedSourceUomCode}->{movedBaseQty:0.####} {baseUomCode}",
            Reason = txNote,
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        var baseUomDelta = Math.Round(outTx.Quantity + inTx.Quantity, 4, MidpointRounding.AwayFromZero);

        return WmsEndpointOutcome.Ok(new
        {
            replenishmentId = breakBulkId,
            referenceId = sourceStockBucket.Id,
            warehouseId = targetWarehouseId,
            productId = resolvedProductId.Value,
            productCode = resolvedProductCode,
            productName = resolvedProductName,
            sourceLocationId = resolvedSourceLocationId.Value,
            sourceLocationCode = resolvedSourceLocationCode,
            targetLocationId = resolvedTargetLocationId.Value,
            targetLocationCode = resolvedTargetLocationCode,
            sourceHandlingUnitId = detachedHandlingUnitId,
            targetHandlingUnitId = (int?)null,
            detachedFromHandlingUnit = detachedHandlingUnitId.HasValue,
            sourceUomId = resolvedUomId,
            sourceUomCode = resolvedSourceUomCode,
            baseUomCode,
            sourcePackQty,
            conversionFactor,
            movedBaseQty,
            movedQuantity = movedBaseQty,
            baseUomDelta,
            sourceRemainingQuantity = sourceStockBucket.Quantity,
            targetResultingQuantity = targetStockBucket.Quantity,
            outTransactionId = outTx.Id,
            inTransactionId = inTx.Id,
            stockMutated = true,
            status = "COMPLETED",
            statusLabel = $"Đã xả {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####}) = {movedBaseQty:0.####} {baseUomCode} từ {resolvedSourceLocationCode} sang {resolvedTargetLocationCode}",
            executedAtUtc = now
        });
    }
}
