using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    private async Task<WmsEndpointOutcome> FinalizeAndExecuteReplenishmentAsync(
        ExecuteReplenishmentInput? request,
        int targetWarehouseId,
        string replenishmentId,
        decimal requestedQty,
        bool shouldMutate,
        int? resolvedProductId,
        string resolvedProductCode,
        string resolvedProductName,
        int? resolvedFromLocationId,
        string resolvedFromCode,
        int? resolvedToLocationId,
        string resolvedToCode,
        int? effectiveActorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var movedQuantity = 0m;
        decimal? sourceRemainingQuantity = null;
        decimal? targetResultingQuantity = null;
        var stockMutated = false;
        int? ledgerTransactionId = null;
        int? primarySourceLotId = null;
        string? primarySourceLotNumber = null;
        DateTime? primarySourceLotExpiryDate = null;
        decimal? targetMinBaseQty = null;
        decimal? targetMaxBaseQty = null;
        var lotConsumptions = new List<object>();


        if (db != null)
        {
            if (shouldMutate)
            {
                var lockKey = $"{targetWarehouseId}:{resolvedToLocationId ?? 0}:{resolvedProductId ?? 0}";
                var sem = _replenishLocks.GetOrAdd(lockKey, static _ => new SemaphoreSlim(1, 1));
                await sem.WaitAsync(cancellationToken);

                try
                {
                    var isRelational = db.Database.IsRelational();
                    await using var tx = isRelational && db.Database.CurrentTransaction == null
                        ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
                        : null;

                    try
                    {
                        if (resolvedProductId.HasValue)
                        {
                            await db.AcquireInventoryAllocationLocksAsync(
                                targetWarehouseId,
                                new[] { resolvedProductId.Value },
                                cancellationToken);
                        }

                        ERP.Domain.Entities.WarehouseLocation? targetLocEntity = null;
                        if (resolvedToLocationId.HasValue)
                        {
                            targetLocEntity = await db.WarehouseLocations.AsNoTracking()
                                .FirstOrDefaultAsync(l => l.Id == resolvedToLocationId.Value, cancellationToken);
                            if (targetLocEntity != null)
                            {
                                resolvedToCode = targetLocEntity.Code.Trim();
                                targetMinBaseQty = targetLocEntity.MinBaseQty;
                                targetMaxBaseQty = targetLocEntity.MaxBaseQty;
                                if (!resolvedFromLocationId.HasValue && targetLocEntity.ReplenishFromLocationId.HasValue)
                                {
                                    resolvedFromLocationId = targetLocEntity.ReplenishFromLocationId.Value;
                                }
                            }
                        }

                        var stockQuery = db.InventoryStocks
                            .Include(s => s.Product)
                            .Include(s => s.Location)
                            .Include(s => s.Lot)
                            .Where(s => s.WarehouseId == targetWarehouseId &&
                                        s.Status == InventoryStatus.Available &&
                                        (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) > 0m);

                        if (resolvedProductId.HasValue)
                            stockQuery = stockQuery.Where(s => s.ProductId == resolvedProductId.Value);
                        if (resolvedFromLocationId.HasValue)
                            stockQuery = stockQuery.Where(s => s.LocationId == resolvedFromLocationId.Value);
                        if (request?.LotId is > 0)
                            stockQuery = stockQuery.Where(s => s.LotId == request.LotId.Value);
                        else if (!string.IsNullOrWhiteSpace(request?.LotNumber))
                        {
                            var lotNum = request.LotNumber.Trim();
                            stockQuery = stockQuery.Where(s => s.Lot != null && s.Lot.LotNumber == lotNum);
                        }

                        var sourceStocks = await stockQuery
                            .OrderBy(s => s.Lot != null && s.Lot.ExpiryDate.HasValue ? 0 : 1)
                            .ThenBy(s => s.Lot != null ? s.Lot.ExpiryDate : null)
                            .ThenBy(s => s.LotId ?? int.MaxValue)
                            .ThenBy(s => s.Id)
                            .ToListAsync(cancellationToken);

                        if (sourceStocks.Count == 0 && resolvedFromLocationId.HasValue)
                        {
                            var fallbackQuery = db.InventoryStocks
                                .Include(s => s.Product)
                                .Include(s => s.Location)
                                .Include(s => s.Lot)
                                .Where(s => s.WarehouseId == targetWarehouseId &&
                                            s.Status == InventoryStatus.Available &&
                                            (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) > 0m &&
                                            s.LocationId != resolvedToLocationId);
                            if (resolvedProductId.HasValue)
                                fallbackQuery = fallbackQuery.Where(s => s.ProductId == resolvedProductId.Value);
                            sourceStocks = await fallbackQuery
                                .OrderBy(s => s.Lot != null && s.Lot.ExpiryDate.HasValue ? 0 : 1)
                                .ThenBy(s => s.Lot != null ? s.Lot.ExpiryDate : null)
                                .ThenBy(s => s.LotId ?? int.MaxValue)
                                .ThenBy(s => s.Id)
                                .ToListAsync(cancellationToken);
                        }

                        if (sourceStocks.Count > 0 && resolvedToLocationId.HasValue)
                        {
                            var firstSrc = sourceStocks[0];
                            if (!resolvedProductId.HasValue)
                            {
                                resolvedProductId = firstSrc.ProductId;
                                await db.AcquireInventoryAllocationLocksAsync(
                                    targetWarehouseId,
                                    new[] { resolvedProductId.Value },
                                    cancellationToken);
                            }

                            var currentPickBinQty = await db.InventoryStocks
                                .Where(s => s.WarehouseId == targetWarehouseId &&
                                            s.ProductId == resolvedProductId.Value &&
                                            s.LocationId == resolvedToLocationId.Value &&
                                            s.Status == InventoryStatus.Available)
                                .SumAsync(s => s.Quantity, cancellationToken);

                            var effectiveRequestedQty = requestedQty;
                            if (targetLocEntity?.MaxBaseQty is > 0m)
                            {
                                var maxCap = targetLocEntity.MaxBaseQty.Value;
                                var roomToMax = Math.Max(0m, maxCap - currentPickBinQty);
                                if (roomToMax <= 0m)
                                {
                                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                                    return WmsEndpointOutcome.Json(409, new
                                    {
                                        code = "REPLENISH_TARGET_AT_MAX_CAPACITY",
                                        errorCode = "REPLENISH_TARGET_AT_MAX_CAPACITY",
                                        warehouseId = targetWarehouseId,
                                        productId = resolvedProductId,
                                        toLocationId = resolvedToLocationId,
                                        toLocationCode = resolvedToCode,
                                        currentPickBinQty,
                                        maxBaseQty = maxCap,
                                        movedQuantity = 0m,
                                        message = $"Vị trí kệ nhặt lẻ {resolvedToCode} đã đầy theo ngưỡng tối đa MaxBaseQty ({currentPickBinQty:0.####}/{maxCap:0.####})."
                                    });
                                }

                                if (request?.Quantity <= 0m || effectiveRequestedQty > roomToMax)
                                {
                                    effectiveRequestedQty = roomToMax;
                                }
                            }

                            var remToMove = effectiveRequestedQty;
                            foreach (var sourceStock in sourceStocks)
                            {
                                if (remToMove <= 0m) break;
                                if (sourceStock.LocationId == resolvedToLocationId.Value) continue;

                                var available = Math.Max(0m, sourceStock.Quantity - sourceStock.ReservedQuantity - sourceStock.AllocatedQuantity - sourceStock.LockedQuantity);
                                var moveQty = Math.Min(remToMove, available);
                                if (moveQty <= 0m) continue;

                                resolvedProductId = sourceStock.ProductId;
                                if (sourceStock.Product != null)
                                {
                                    resolvedProductCode = sourceStock.Product.Code.Trim();
                                    resolvedProductName = sourceStock.Product.Name.Trim();
                                }
                                resolvedFromLocationId = sourceStock.LocationId;
                                if (sourceStock.Location != null)
                                    resolvedFromCode = sourceStock.Location.Code.Trim();

                                primarySourceLotId ??= sourceStock.LotId;
                                primarySourceLotNumber ??= sourceStock.Lot?.LotNumber;
                                primarySourceLotExpiryDate ??= sourceStock.Lot?.ExpiryDate;

                                sourceStock.Quantity -= moveQty;
                                sourceStock.LastUpdated = now;
                                sourceStock.RefreshGrainHash();
                                sourceRemainingQuantity = sourceStock.Quantity;
                                remToMove -= moveQty;

                                var targetStock = await db.InventoryStocks
                                    .FirstOrDefaultAsync(s =>
                                        s.WarehouseId == targetWarehouseId &&
                                        s.ProductId == sourceStock.ProductId &&
                                        s.LocationId == resolvedToLocationId.Value &&
                                        s.LotId == sourceStock.LotId &&
                                        s.SerialId == sourceStock.SerialId &&
                                        s.HandlingUnitId == sourceStock.HandlingUnitId &&
                                        s.OwnerId == sourceStock.OwnerId &&
                                        s.Status == InventoryStatus.Available,
                                        cancellationToken);

                                if (targetStock != null)
                                {
                                    targetStock.Quantity += moveQty;
                                    if (sourceStock.MovingAverageCost > 0m && targetStock.MovingAverageCost <= 0m)
                                        targetStock.MovingAverageCost = sourceStock.MovingAverageCost;
                                    targetStock.LastUpdated = now;
                                    targetStock.RefreshGrainHash();
                                }
                                else
                                {
                                    targetStock = new ERP.Domain.Entities.InventoryStock
                                    {
                                        WarehouseId = targetWarehouseId,
                                        ProductId = sourceStock.ProductId,
                                        LocationId = resolvedToLocationId.Value,
                                        LotId = sourceStock.LotId,
                                        SerialId = sourceStock.SerialId,
                                        HandlingUnitId = sourceStock.HandlingUnitId,
                                        OwnerId = sourceStock.OwnerId,
                                        Status = InventoryStatus.Available,
                                        Quantity = moveQty,
                                        ReservedQuantity = 0m,
                                        MovingAverageCost = sourceStock.MovingAverageCost,
                                        LastUpdated = now
                                    };
                                    targetStock.RefreshGrainHash();
                                    db.InventoryStocks.Add(targetStock);
                                }

                                var moveOutTx = new ERP.Domain.Entities.InventoryTransaction
                                {
                                    ProductId = sourceStock.ProductId,
                                    WarehouseId = targetWarehouseId,
                                    LocationId = sourceStock.LocationId,
                                    FromLocationId = sourceStock.LocationId,
                                    ToLocationId = resolvedToLocationId.Value,
                                    LotId = sourceStock.LotId,
                                    SerialId = sourceStock.SerialId,
                                    HandlingUnitId = sourceStock.HandlingUnitId,
                                    OwnerId = sourceStock.OwnerId,
                                    InventoryStatus = InventoryStatus.Available,
                                    FromInventoryStatus = InventoryStatus.Available,
                                    ToInventoryStatus = InventoryStatus.Available,
                                    TransactionType = TransactionType.Move,
                                    Quantity = -moveQty,
                                    UnitCost = sourceStock.MovingAverageCost,
                                    ReferenceId = sourceStock.Id,
                                    ReferenceType = "Replenishment",
                                    TransactionDate = now,
                                    CreatedBy = effectiveActorId ?? 1,
                                    Note = $"{replenishmentId}: Bổ sung kệ nhặt FEFO từ {resolvedFromCode} sang {resolvedToCode}"
                                };
                                var moveTx = new ERP.Domain.Entities.InventoryTransaction
                                {
                                    ProductId = sourceStock.ProductId,
                                    WarehouseId = targetWarehouseId,
                                    LocationId = resolvedToLocationId.Value,
                                    FromLocationId = sourceStock.LocationId,
                                    ToLocationId = resolvedToLocationId.Value,
                                    LotId = sourceStock.LotId,
                                    SerialId = sourceStock.SerialId,
                                    HandlingUnitId = sourceStock.HandlingUnitId,
                                    OwnerId = sourceStock.OwnerId,
                                    InventoryStatus = InventoryStatus.Available,
                                    FromInventoryStatus = InventoryStatus.Available,
                                    ToInventoryStatus = InventoryStatus.Available,
                                    TransactionType = TransactionType.Move,
                                    Quantity = moveQty,
                                    UnitCost = sourceStock.MovingAverageCost,
                                    ReferenceId = sourceStock.Id,
                                    ReferenceType = "Replenishment",
                                    TransactionDate = now,
                                    CreatedBy = effectiveActorId ?? 1,
                                    Note = $"{replenishmentId}: Bổ sung kệ nhặt FEFO từ {resolvedFromCode} sang {resolvedToCode}"
                                };
                                db.InventoryTransactions.Add(moveOutTx);
                                db.InventoryTransactions.Add(moveTx);
                                await db.SaveChangesAsync(cancellationToken);

                                movedQuantity += moveQty;
                                stockMutated = true;
                                ledgerTransactionId ??= moveTx.Id;

                                lotConsumptions.Add(new
                                {
                                    lotId = sourceStock.LotId,
                                    lotNumber = sourceStock.Lot?.LotNumber,
                                    expiryDate = sourceStock.Lot?.ExpiryDate,
                                    movedQuantity = moveQty,
                                    sourceRemainingQuantity = sourceStock.Quantity
                                });
                            }

                            if (stockMutated && resolvedProductId.HasValue)
                            {
                                targetResultingQuantity = await db.InventoryStocks
                                    .Where(s => s.WarehouseId == targetWarehouseId &&
                                                s.ProductId == resolvedProductId.Value &&
                                                s.LocationId == resolvedToLocationId.Value &&
                                                s.Status == InventoryStatus.Available)
                                    .SumAsync(s => s.Quantity, cancellationToken);
                            }
                        }

                        if (tx != null) await tx.CommitAsync(cancellationToken);
                    }
                    catch
                    {
                        if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                        throw;
                    }
                }
                finally
                {
                    sem.Release();
                }
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "InventoryControl",
                EntityId = resolvedProductId,
                Action = "ExecuteReplenishment",
                NewValues = $"{replenishmentId}|{resolvedFromCode}->{resolvedToCode}|{movedQuantity:0.##}",
                Reason = !string.IsNullOrWhiteSpace(request?.Note)
                    ? request.Note.Trim()
                    : $"Bổ sung kệ nhặt từ {resolvedFromCode} sang {resolvedToCode} (SL: {movedQuantity:0.##})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            replenishmentId,
            warehouseId = targetWarehouseId,
            productId = resolvedProductId,
            productCode = string.IsNullOrWhiteSpace(resolvedProductCode) ? "SKU-REPL-AUTO" : resolvedProductCode,
            productName = resolvedProductName,
            fromLocationId = resolvedFromLocationId,
            fromLocationCode = resolvedFromCode,
            toLocationId = resolvedToLocationId,
            toLocationCode = resolvedToCode,
            movedQuantity,
            sourceRemainingQuantity,
            targetResultingQuantity,
            sourceLotId = primarySourceLotId,
            sourceLotNumber = primarySourceLotNumber,
            sourceLotExpiryDate = primarySourceLotExpiryDate,
            minBaseQty = targetMinBaseQty,
            maxBaseQty = targetMaxBaseQty,
            lotConsumptions,
            stockMutated,
            executed = stockMutated,
            executedStockMutation = stockMutated,
            ledgerTransactionId,
            status = "Đã thực thi bổ sung tồn kho từ kệ dự trữ sang kệ nhặt",
            statusLabel = "Đã thực thi bổ sung tồn kho từ kệ dự trữ sang kệ nhặt",
            executedAtUtc = now
        });
    }
}
