using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService
{
    public async Task<WmsEndpointOutcome> ExecuteRtvRefurbishAsync(
        ExecuteRtvRefurbishInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        var dispositionCode = $"DISP-RTV-{now:yyyyMMdd-HHmm}";
        var rawAction = !string.IsNullOrWhiteSpace(request?.ActionType)
            ? request.ActionType.Trim().ToUpperInvariant()
            : (!string.IsNullOrWhiteSpace(request?.DispositionType) ? request.DispositionType.Trim().ToUpperInvariant() : "REFURBISH_AND_RTV_SPLIT");
        var isRtvOrScrap = rawAction is "RTV" or "SCRAP" ||
                           (rawAction.StartsWith("RTV", StringComparison.Ordinal) && !rawAction.Contains("REFURBISH", StringComparison.Ordinal)) ||
                           rawAction.StartsWith("SCRAP", StringComparison.Ordinal);

        var ledgerUpdated = false;
        int? ledgerTransactionId = null;
        decimal? resultingStockQuantity = null;
        string? resultingStatus = null;

        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);

        var resolvedProductId = request?.ProductId;
        var resolvedLocationId = request?.LocationId;
        if (db != null)
        {
            if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(request?.ProductCode))
            {
                var code = request.ProductCode.Trim();
                resolvedProductId = await db.Products.AsNoTracking()
                    .Where(p => p.Code == code)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (!resolvedLocationId.HasValue && !string.IsNullOrWhiteSpace(request?.LocationCode))
            {
                var locCode = request.LocationCode.Trim();
                var locQuery = db.WarehouseLocations.AsNoTracking().Where(l => l.Code == locCode);
                if (warehouseId.HasValue)
                    locQuery = locQuery.Where(l => l.WarehouseId == warehouseId.Value);
                resolvedLocationId = await locQuery.Select(l => (int?)l.Id).FirstOrDefaultAsync(cancellationToken);
            }
        }

        if (db != null && (request?.ExecuteStockMutation == true || resolvedProductId.HasValue) && resolvedProductId.HasValue)
        {
            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
                : null;

            try
            {
                var stockQuery = db.InventoryStocks
                    .Where(s => s.ProductId == resolvedProductId.Value && (!resolvedLocationId.HasValue || s.LocationId == resolvedLocationId.Value));
                if (warehouseId.HasValue)
                    stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
                else if (accessibleIds != null)
                    stockQuery = stockQuery.Where(s => accessibleIds.Contains(s.WarehouseId));

                if (!string.IsNullOrWhiteSpace(request?.LotNumber))
                {
                    var normalizedLot = request.LotNumber.Trim();
                    stockQuery = stockQuery.Where(s => s.Lot != null && s.Lot.LotNumber == normalizedLot);
                }

                var stock = await stockQuery
                    .OrderBy(s => s.Status == ERP.Domain.Enums.InventoryStatus.Available ? 1 : 0)
                    .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                    .ThenBy(s => s.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (stock == null)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return WmsEndpointOutcome.NotFound(new { message = "Không tìm thấy tồn kho phù hợp để thực thi xử lý hàng hỏng / trả NCC / tân trang." });
                }

                warehouseId ??= stock.WarehouseId;

                if (isRtvOrScrap)
                {
                    var writeOffQty = request?.Quantity > 0m ? request.Quantity : 1m;
                    var availableInBucket = stock.Quantity - stock.ReservedQuantity;
                    if (availableInBucket < writeOffQty)
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.BadRequest(new
                        {
                            message = $"Không đủ tồn kho khả dụng để xử lý {rawAction} (Khả dụng: {availableInBucket}, Yêu cầu: {writeOffQty})."
                        });
                    }

                    stock.Quantity -= writeOffQty;
                    stock.LastUpdated = now;

                    var ledgerTx = new ERP.Domain.Entities.InventoryTransaction
                    {
                        ProductId = stock.ProductId,
                        WarehouseId = stock.WarehouseId,
                        LocationId = stock.LocationId,
                        LotId = stock.LotId,
                        SerialId = stock.SerialId,
                        InventoryStatus = stock.Status,
                        FromInventoryStatus = stock.Status,
                        ToInventoryStatus = null,
                        TransactionType = ERP.Domain.Enums.TransactionType.AdjustmentDecrease,
                        Quantity = writeOffQty,
                        ReferenceId = stock.Id,
                        ReferenceType = rawAction.StartsWith("RTV", StringComparison.Ordinal)
                            ? "QualityDispositionRtv"
                            : "QualityDispositionScrap",
                        TransactionDate = now,
                        CreatedBy = effectiveActorId ?? 1,
                        Note = $"{dispositionCode}: Xử lý {rawAction}"
                    };
                    db.InventoryTransactions.Add(ledgerTx);
                    await db.SaveChangesAsync(cancellationToken);

                    ledgerUpdated = true;
                    ledgerTransactionId = ledgerTx.Id;
                    resultingStockQuantity = stock.Quantity;
                    resultingStatus = stock.Status.ToString();
                }
                else
                {
                    var fromStatus = stock.Status;
                    var moveQty = request?.Quantity > 0m && request.Quantity <= stock.Quantity
                        ? request.Quantity
                        : stock.Quantity;

                    if (stock.Quantity - stock.ReservedQuantity < moveQty)
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.BadRequest(new
                        {
                            message = $"Không thể tân trang phần tồn kho đang giữ chỗ (Khả dụng: {stock.Quantity - stock.ReservedQuantity}, Yêu cầu: {moveQty})."
                        });
                    }

                    var existingAvailable = await db.InventoryStocks
                        .FirstOrDefaultAsync(s =>
                            s.Id != stock.Id &&
                            s.WarehouseId == stock.WarehouseId &&
                            s.ProductId == stock.ProductId &&
                            s.LocationId == stock.LocationId &&
                            s.LotId == stock.LotId &&
                            s.SerialId == stock.SerialId &&
                            s.Status == ERP.Domain.Enums.InventoryStatus.Available,
                            cancellationToken);

                    if (existingAvailable != null && stock.Status != ERP.Domain.Enums.InventoryStatus.Available)
                    {
                        stock.Quantity -= moveQty;
                        stock.LastUpdated = now;
                        existingAvailable.Quantity += moveQty;
                        existingAvailable.LastUpdated = now;
                        resultingStockQuantity = existingAvailable.Quantity;
                    }
                    else if (moveQty < stock.Quantity && stock.Status != ERP.Domain.Enums.InventoryStatus.Available)
                    {
                        stock.Quantity -= moveQty;
                        stock.LastUpdated = now;
                        var createdAvailable = new ERP.Domain.Entities.InventoryStock
                        {
                            WarehouseId = stock.WarehouseId,
                            ProductId = stock.ProductId,
                            LocationId = stock.LocationId,
                            LotId = stock.LotId,
                            SerialId = stock.SerialId,
                            Status = ERP.Domain.Enums.InventoryStatus.Available,
                            Quantity = moveQty,
                            ReservedQuantity = 0m,
                            LastUpdated = now
                        };
                        db.InventoryStocks.Add(createdAvailable);
                        resultingStockQuantity = createdAvailable.Quantity;
                    }
                    else
                    {
                        stock.Status = ERP.Domain.Enums.InventoryStatus.Available;
                        stock.LastUpdated = now;
                        resultingStockQuantity = stock.Quantity;
                    }

                    var effectiveFromStatus = fromStatus == ERP.Domain.Enums.InventoryStatus.Available
                        ? ERP.Domain.Enums.InventoryStatus.Quarantine
                        : fromStatus;
                    var effectiveQty = moveQty > 0m ? moveQty : 1m;
                    var statusTxOut = new ERP.Domain.Entities.InventoryTransaction
                    {
                        ProductId = stock.ProductId,
                        WarehouseId = stock.WarehouseId,
                        LocationId = stock.LocationId,
                        FromLocationId = stock.LocationId,
                        ToLocationId = stock.LocationId,
                        LotId = stock.LotId,
                        SerialId = stock.SerialId,
                        InventoryStatus = effectiveFromStatus,
                        FromInventoryStatus = effectiveFromStatus,
                        ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                        TransactionType = ERP.Domain.Enums.TransactionType.StatusChange,
                        Quantity = -effectiveQty,
                        ReferenceId = stock.Id,
                        ReferenceType = "InventoryStatusChange",
                        TransactionDate = now,
                        CreatedBy = effectiveActorId ?? 1,
                        Note = $"{dispositionCode}: Tân trang & chuyển về hàng khả dụng"
                    };
                    var statusTx = new ERP.Domain.Entities.InventoryTransaction
                    {
                        ProductId = stock.ProductId,
                        WarehouseId = stock.WarehouseId,
                        LocationId = stock.LocationId,
                        FromLocationId = stock.LocationId,
                        ToLocationId = stock.LocationId,
                        LotId = stock.LotId,
                        SerialId = stock.SerialId,
                        InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                        FromInventoryStatus = effectiveFromStatus,
                        ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                        TransactionType = ERP.Domain.Enums.TransactionType.StatusChange,
                        Quantity = effectiveQty,
                        ReferenceId = stock.Id,
                        ReferenceType = "InventoryStatusChange",
                        TransactionDate = now,
                        CreatedBy = effectiveActorId ?? 1,
                        Note = $"{dispositionCode}: Tân trang & chuyển về hàng khả dụng"
                    };
                    db.InventoryTransactions.Add(statusTxOut);
                    db.InventoryTransactions.Add(statusTx);
                    await db.SaveChangesAsync(cancellationToken);

                    ledgerUpdated = true;
                    ledgerTransactionId = statusTx.Id;
                    resultingStatus = ERP.Domain.Enums.InventoryStatus.Available.ToString();
                }

                if (tx != null) await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }


        return await FinalizeRtvRefurbishDispositionAsync(
            request,
            warehouseId,
            accessibleIds,
            rawAction,
            dispositionCode,
            resolvedProductId,
            resolvedLocationId,
            effectiveActorId,
            now,
            ledgerUpdated,
            ledgerTransactionId,
            resultingStockQuantity,
            resultingStatus,
            cancellationToken);
    }
}
