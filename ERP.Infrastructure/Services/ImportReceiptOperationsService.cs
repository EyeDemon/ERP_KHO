using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class ImportReceiptOperationsService(
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService? warehouseAccess = null,
    ICurrentUser? currentUser = null) : IImportReceiptOperationsService
{
    private static string NormalizeVietnameseDiacritics(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : text.Replace("chua kiểm định", "chưa kiểm định", StringComparison.OrdinalIgnoreCase);

    public async Task<WmsEndpointOutcome> RmaIntakeCheckAsync(
        RmaIntakeCheckInput? request = null,
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

        var quarantineBucketCount = 0;
        var pendingQcOrRmaReceiptsCount = 0;
        var openStocktakesCount = 0;
        var activeCountFreezeLocksCount = 0;
        if (db != null)
        {
            var query = db.InventoryStocks.AsNoTracking()
                .Where(s => s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine);
            var receiptQuery = db.ImportReceipts.AsNoTracking()
                .Where(r => r.Status != ERP.Domain.Enums.ReceiptStatus.Posted &&
                            r.Status != ERP.Domain.Enums.ReceiptStatus.Approved &&
                            r.Status != ERP.Domain.Enums.ReceiptStatus.Cancelled);
            var stocktakeQuery = db.Stocktakes.AsNoTracking()
                .Where(s => s.Status == ERP.Domain.Enums.StocktakeStatus.Draft);
            var lockQuery = db.InventoryLocks.AsNoTracking()
                .Where(l => l.Status == ERP.Domain.Enums.InventoryLockStatus.Active &&
                            l.LockType == ERP.Domain.Enums.InventoryLockType.CountFreeze);

            if (warehouseId.HasValue)
            {
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
                receiptQuery = receiptQuery.Where(r => r.WarehouseId == warehouseId.Value);
                stocktakeQuery = stocktakeQuery.Where(s => s.WarehouseId == warehouseId.Value);
                lockQuery = lockQuery.Where(l => l.WarehouseId == warehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
                receiptQuery = receiptQuery.Where(r => accessibleIds.Contains(r.WarehouseId));
                stocktakeQuery = stocktakeQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
                lockQuery = lockQuery.Where(l => accessibleIds.Contains(l.WarehouseId));
            }

            quarantineBucketCount = await query.CountAsync(cancellationToken);
            pendingQcOrRmaReceiptsCount = await receiptQuery.CountAsync(cancellationToken);
            openStocktakesCount = await stocktakeQuery.CountAsync(cancellationToken);
            activeCountFreezeLocksCount = await lockQuery.CountAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        var rmaInspectionCode = $"RMA-INSP-{now:yyyyMMdd-HHmm}";
        var rmaReferenceCode = string.IsNullOrWhiteSpace(request?.RmaReferenceCode)
            ? "RMA-AUTO"
            : request.RmaReferenceCode.Trim();

        int? rmaLedgerTransactionId = null;
        decimal? quarantineStockQuantity = null;
        int? customerReturnId = null;
        if (db != null)
        {
            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);

            var targetWhId = warehouseId ?? accessibleIds?.FirstOrDefault() ?? 1;
            var normalizedIntakeReason = NormalizeVietnameseDiacritics(request?.ReasonCode?.Trim());
            var customerReturn = new ERP.Domain.Entities.CustomerReturn
            {
                ReturnCode = rmaInspectionCode,
                WarehouseId = targetWhId,
                TrackingNumber = string.IsNullOrWhiteSpace(request?.ReturnReference) ? rmaReferenceCode : request.ReturnReference.Trim(),
                ReturnReason = string.IsNullOrWhiteSpace(normalizedIntakeReason) ? "RMA_INSPECTION" : normalizedIntakeReason,
                Status = "QUARANTINED",
                CreatedBy = effectiveActorId ?? 1,
                CreatedAtUtc = now
            };
            db.CustomerReturns.Add(customerReturn);
            await db.SaveChangesAsync(cancellationToken);
            customerReturnId = customerReturn.Id;

            if (request != null && request.Quantity.GetValueOrDefault() > 0m &&
                (request.ProductId.HasValue || !string.IsNullOrWhiteSpace(request.ProductCode)))
            {
                var resolvedProductId = request.ProductId;
                if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(request.ProductCode))
                {
                    var code = request.ProductCode.Trim();
                    resolvedProductId = await db.Products.AsNoTracking()
                        .Where(p => p.Code == code)
                        .Select(p => (int?)p.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                if (resolvedProductId.HasValue)
                {
                    var resolvedLocationId = request.LocationId;
                    if (!resolvedLocationId.HasValue && !string.IsNullOrWhiteSpace(request.LocationCode))
                    {
                        var locCode = request.LocationCode.Trim();
                        resolvedLocationId = await db.WarehouseLocations.AsNoTracking()
                            .Where(l => l.WarehouseId == targetWhId && l.Code == locCode)
                            .Select(l => (int?)l.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                    }
                    resolvedLocationId ??= await db.WarehouseLocations.AsNoTracking()
                        .Where(l => l.WarehouseId == targetWhId && l.IsActive)
                        .OrderByDescending(l => l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Receiving)
                        .ThenBy(l => l.Id)
                        .Select(l => (int?)l.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    int? resolvedLotId = null;
                    if (!string.IsNullOrWhiteSpace(request.LotNumber))
                    {
                        var lotNo = request.LotNumber.Trim();
                        resolvedLotId = await db.InventoryLots.AsNoTracking()
                            .Where(l => l.ProductId == resolvedProductId.Value && l.LotNumber == lotNo)
                            .Select(l => (int?)l.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                    }

                    var qty = request.Quantity!.Value;
                    var quarantineStock = await db.InventoryStocks.FirstOrDefaultAsync(
                        s => s.WarehouseId == targetWhId &&
                             s.ProductId == resolvedProductId.Value &&
                             s.LocationId == resolvedLocationId &&
                             s.LotId == resolvedLotId &&
                             s.SerialId == null &&
                             s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine,
                        cancellationToken);

                    if (quarantineStock == null)
                    {
                        quarantineStock = new ERP.Domain.Entities.InventoryStock
                        {
                            WarehouseId = targetWhId,
                            ProductId = resolvedProductId.Value,
                            LocationId = resolvedLocationId,
                            LotId = resolvedLotId,
                            Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            Quantity = qty,
                            ReservedQuantity = 0m,
                            LastUpdated = now
                        };
                        db.InventoryStocks.Add(quarantineStock);
                        await db.SaveChangesAsync(cancellationToken);
                        quarantineBucketCount++;
                    }
                    else
                    {
                        quarantineStock.Quantity += qty;
                        quarantineStock.LastUpdated = now;
                    }

                    var rmaTx = new ERP.Domain.Entities.InventoryTransaction
                    {
                        ProductId = resolvedProductId.Value,
                        WarehouseId = targetWhId,
                        LocationId = resolvedLocationId,
                        ToLocationId = resolvedLocationId,
                        LotId = resolvedLotId,
                        InventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                        ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                        TransactionType = ERP.Domain.Enums.TransactionType.Import,
                        Quantity = qty,
                        ReferenceId = quarantineStock.Id,
                        ReferenceType = "RmaInspection",
                        TransactionDate = now,
                        CreatedBy = effectiveActorId ?? 1,
                        Note = $"{rmaInspectionCode}: Tiếp nhận hàng trả về (RMA) vào khu vực biệt trữ"
                    };
                    db.InventoryTransactions.Add(rmaTx);
                    db.CustomerReturnLines.Add(new ERP.Domain.Entities.CustomerReturnLine
                    {
                        CustomerReturnId = customerReturn.Id,
                        ProductId = resolvedProductId.Value,
                        QuarantineLocationId = resolvedLocationId,
                        TargetLocationId = resolvedLocationId,
                        Quantity = qty,
                        Condition = "DEFECTIVE",
                        Disposition = "PENDING"
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    rmaLedgerTransactionId = rmaTx.Id;
                    quarantineStockQuantity = quarantineStock.Quantity;
                }
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = warehouseId,
                Action = "ImportReceipt.RmaIntakeCheck",
                EntityName = "RmaInspection",
                EntityId = customerReturnId,
                OldValues = "QUARANTINE",
                NewValues = rmaInspectionCode,
                Reason = string.IsNullOrWhiteSpace(request?.ReasonCode)
                    ? $"Tiếp nhận kiểm định RMA ({pendingQcOrRmaReceiptsCount} phiếu chờ QC/RMA, {quarantineBucketCount} vị trí biệt trữ)"
                    : NormalizeVietnameseDiacritics(request.ReasonCode.Trim())!,
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            customerReturnId,
            rmaCode = $"RMA-{now:yyyyMMdd}-01",
            rmaInspectionCode,
            rmaReferenceCode,
            returnReference = request?.ReturnReference ?? string.Empty,
            reasonCode = NormalizeVietnameseDiacritics(request?.ReasonCode) ?? string.Empty,
            quarantineBucketCount,
            pendingQcOrRmaReceiptsCount,
            openStocktakesCount,
            activeCountFreezeLocksCount,
            ledgerTransactionId = rmaLedgerTransactionId,
            quarantineStockQuantity,
            targetBucket = "Khu vực biệt trữ chờ kiểm định",
            nextStep = "Chuyển trạm kiểm định chất lượng đầu vào",
            dispositionSummary = $"Đã tiếp nhận kiểm định hàng trả về ({pendingQcOrRmaReceiptsCount} phiếu chờ kiểm định, {quarantineBucketCount} vị trí biệt trữ)",
            status = "Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ",
            statusLabel = "Đã khởi tạo phiếu kiểm định hàng trả về & định tuyến biệt trữ",
            queuedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> GetCustomerReturnsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
            return WmsEndpointOutcome.Ok(Array.Empty<object>());

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (warehouseId.HasValue && effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);

        var query = db.CustomerReturns.AsNoTracking().Include(r => r.Lines).AsQueryable();
        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId.Value);

        var rawReturns = await query
            .OrderByDescending(r => r.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var trackingNumbers = rawReturns
            .Where(r => !string.IsNullOrWhiteSpace(r.TrackingNumber))
            .Select(r => r.TrackingNumber!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var exportReceiptIds = rawReturns
            .Where(r => r.ExportReceiptId.HasValue)
            .Select(r => r.ExportReceiptId!.Value)
            .Distinct()
            .ToList();

        var videoEvidences = (trackingNumbers.Count > 0 || exportReceiptIds.Count > 0)
            ? await db.PackingVideoEvidences
                .AsNoTracking()
                .Where(v => (v.ExportReceiptId.HasValue && exportReceiptIds.Contains(v.ExportReceiptId.Value)) ||
                            trackingNumbers.Contains(v.TrackingNumber))
                .ToListAsync(cancellationToken)
            : [];

        var now = DateTime.UtcNow;
        var items = rawReturns.Select(r =>
        {
            var matchedVideo = videoEvidences.FirstOrDefault(v =>
                (r.ExportReceiptId.HasValue && v.ExportReceiptId == r.ExportReceiptId.Value) ||
                (!string.IsNullOrWhiteSpace(r.TrackingNumber) && string.Equals(v.TrackingNumber, r.TrackingNumber, StringComparison.OrdinalIgnoreCase)));
            var inspectionVideoUrl = matchedVideo?.StorageUrl;

            var returnAgingDays = Math.Max(0, (int)Math.Floor((now - r.CreatedAtUtc).TotalDays));
            var isOverdueInTransit = !string.Equals(r.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                                     !string.Equals(r.Status, "PUTAWAY_COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                                     returnAgingDays > 7;
            var overdueAlertLevel = isOverdueInTransit
                ? "CRITICAL_OVERDUE_7D"
                : (returnAgingDays >= 5 ? "WARNING_AGING" : "NORMAL");
            var disputeDeadlineUtc = r.CreatedAtUtc.AddHours(48);
            var disputeRemainingHours = Math.Round(Math.Max(0, (disputeDeadlineUtc - now).TotalHours), 1);
            var disputeClaimStatus = !string.IsNullOrWhiteSpace(inspectionVideoUrl)
                ? "EVIDENCE_ATTACHED_READY"
                : (disputeRemainingHours <= 0 ? "DISPUTE_WINDOW_EXPIRED" : "PENDING_VIDEO_EVIDENCE");

            return new
            {
                r.Id,
                r.ReturnCode,
                r.WarehouseId,
                r.SalesOrderId,
                r.ExportReceiptId,
                r.TrackingNumber,
                r.OrderNumber,
                ReturnReason = NormalizeVietnameseDiacritics(r.ReturnReason),
                r.Status,
                r.CreatedBy,
                r.CreatedAtUtc,
                createdAt = r.CreatedAtUtc,
                r.CompletedAtUtc,
                inspectionVideoUrl,
                returnAgingDays,
                isOverdueInTransit,
                overdueAlertLevel,
                disputeDeadlineUtc,
                disputeRemainingHours,
                disputeClaimStatus,
                lines = r.Lines.Select(l => new
                {
                    l.Id,
                    l.ProductId,
                    l.QuarantineLocationId,
                    l.TargetLocationId,
                    l.Quantity,
                    l.UnitCost,
                    l.Condition,
                    l.Disposition,
                    l.PackagingProductId,
                    l.PackagingQuantity
                }).ToList()
            };
        }).ToList();

        return WmsEndpointOutcome.Ok(items);
    }

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
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
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

        var nonAvailableBucketsCount = 0;
        int? dispositionReturnId = null;
        if (db != null)
        {
            var query = db.InventoryStocks.AsNoTracking()
                .Where(s => s.Status != ERP.Domain.Enums.InventoryStatus.Available);
            if (warehouseId.HasValue)
                query = query.Where(s => s.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(s => accessibleIds.Contains(s.WarehouseId));
            nonAvailableBucketsCount = await query.CountAsync(cancellationToken);

            var isRtv = rawAction.StartsWith("RTV", StringComparison.Ordinal) && !rawAction.Contains("REFURBISH", StringComparison.Ordinal);
            var isScrap = rawAction.StartsWith("SCRAP", StringComparison.Ordinal);
            var resolvedReturnStatus = !string.IsNullOrWhiteSpace(request?.Status)
                ? request.Status.Trim().ToUpperInvariant()
                : (isRtv || isScrap ? "QC_FAILED" : "COMPLETED");

            ERP.Domain.Entities.CustomerReturn? dispReturn = null;
            if (request?.CustomerReturnId.HasValue == true)
            {
                dispReturn = await db.CustomerReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == request.CustomerReturnId.Value, cancellationToken);
            }
            if (dispReturn == null && !string.IsNullOrWhiteSpace(request?.ReturnCode))
            {
                var rc = request.ReturnCode.Trim();
                dispReturn = await db.CustomerReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.ReturnCode == rc, cancellationToken);
            }
            if (dispReturn == null && !string.IsNullOrWhiteSpace(request?.TrackingNumber))
            {
                var tn = request.TrackingNumber.Trim();
                dispReturn = await db.CustomerReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.TrackingNumber == tn, cancellationToken);
            }
            if (dispReturn == null)
            {
                var openQuery = db.CustomerReturns
                    .Include(r => r.Lines)
                    .Where(r => r.Status != "COMPLETED" && r.Status != "REFUNDED");
                if (warehouseId.HasValue)
                    openQuery = openQuery.Where(r => r.WarehouseId == warehouseId.Value);
                if (resolvedProductId.HasValue)
                    openQuery = openQuery.Where(r => r.Lines.Any(l => l.ProductId == resolvedProductId.Value) || !r.Lines.Any());
                dispReturn = await openQuery.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
            }

            var mutatedQty = request?.Quantity > 0m ? request.Quantity : 1m;
            var lineCondition = isRtv || isScrap ? "DEFECTIVE" : "DAMAGED_BOX";
            var lineDisposition = isRtv ? "RTV_VENDOR" : (isScrap ? "SCRAP_DISPOSAL" : "REFURBISH_REPACK");

            if (dispReturn == null)
            {
                dispReturn = new ERP.Domain.Entities.CustomerReturn
                {
                    ReturnCode = dispositionCode,
                    WarehouseId = warehouseId ?? accessibleIds?.FirstOrDefault() ?? 1,
                    ReturnReason = string.IsNullOrWhiteSpace(request?.ReasonCode) ? rawAction : NormalizeVietnameseDiacritics(request.ReasonCode.Trim()),
                    Status = resolvedReturnStatus,
                    CreatedBy = effectiveActorId ?? 1,
                    CreatedAtUtc = now,
                    CompletedAtUtc = now
                };
                db.CustomerReturns.Add(dispReturn);
                await db.SaveChangesAsync(cancellationToken);

                if (resolvedProductId.HasValue)
                {
                    db.CustomerReturnLines.Add(new ERP.Domain.Entities.CustomerReturnLine
                    {
                        CustomerReturnId = dispReturn.Id,
                        ProductId = resolvedProductId.Value,
                        TargetLocationId = resolvedLocationId,
                        Quantity = mutatedQty,
                        Condition = lineCondition,
                        Disposition = lineDisposition
                    });
                }
            }
            else
            {
                dispReturn.Status = resolvedReturnStatus;
                dispReturn.CompletedAtUtc = now;
                if (!string.IsNullOrWhiteSpace(request?.ReasonCode))
                    dispReturn.ReturnReason = NormalizeVietnameseDiacritics(request.ReasonCode.Trim());
                else if (!string.IsNullOrWhiteSpace(dispReturn.ReturnReason))
                    dispReturn.ReturnReason = NormalizeVietnameseDiacritics(dispReturn.ReturnReason);

                var existingLine = resolvedProductId.HasValue
                    ? dispReturn.Lines.FirstOrDefault(l => l.ProductId == resolvedProductId.Value) ?? dispReturn.Lines.FirstOrDefault()
                    : dispReturn.Lines.FirstOrDefault();

                if (existingLine != null)
                {
                    existingLine.Condition = lineCondition;
                    existingLine.Disposition = lineDisposition;
                    if (resolvedLocationId.HasValue)
                        existingLine.TargetLocationId = resolvedLocationId.Value;
                }
                else if (resolvedProductId.HasValue)
                {
                    db.CustomerReturnLines.Add(new ERP.Domain.Entities.CustomerReturnLine
                    {
                        CustomerReturnId = dispReturn.Id,
                        ProductId = resolvedProductId.Value,
                        TargetLocationId = resolvedLocationId,
                        Quantity = mutatedQty,
                        Condition = lineCondition,
                        Disposition = lineDisposition
                    });
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            dispositionReturnId = dispReturn.Id;

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = warehouseId,
                Action = "ImportReceipt.ExecuteRtvRefurbish",
                EntityName = "RtvRefurbish",
                EntityId = dispositionReturnId,
                NewValues = dispositionCode,
                Reason = $"Thực thi xử lý hàng biệt trữ ({rawAction})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var vietnameseDispositionType = (rawAction.StartsWith("RTV", StringComparison.Ordinal) && !rawAction.Contains("REFURBISH", StringComparison.Ordinal))
            ? "Hoàn trả nhà cung cấp & giảm trừ tồn kho"
            : (rawAction.StartsWith("SCRAP", StringComparison.Ordinal)
                ? "Hủy bỏ hàng lỗi & xuất toán tồn kho"
                : "Tân trang hàng trả về & chuyển về tồn khả dụng");

        return WmsEndpointOutcome.Ok(new
        {
            customerReturnId = dispositionReturnId,
            dispositionCode,
            warehouseId,
            nonAvailableBucketsCount,
            dispositionType = vietnameseDispositionType,
            actionType = vietnameseDispositionType,
            dispositionSummary = $"{vietnameseDispositionType} ({nonAvailableBucketsCount} vị trí biệt trữ và hư hỏng đã rà soát)",
            status = "Đã thực thi & Ghi sổ kiểm toán",
            statusLabel = "Đã thực thi & Ghi sổ kiểm toán",
            ledgerUpdated,
            executedStockMutation = ledgerUpdated,
            mutatedQuantity = ledgerUpdated ? (request?.Quantity > 0m ? request.Quantity : 1m) : 0m,
            ledgerTransactionId,
            resultingStockQuantity,
            resultingStatus,
            refurbishRouting = "Chuyển tồn kho sau tân trang về khu vực hàng khả dụng",
            rtvClaimStatus = "Đã ghi nhận giảm trừ tồn kho và sẵn sàng hoàn trả nhà cung cấp",
            executedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> GetMacPreviewAsync(
        int id,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var receipt = await db.ImportReceipts
            .AsNoTracking()
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (receipt == null)
        {
            return WmsEndpointOutcome.NotFound(new { code = "NOT_FOUND", errorCode = "NOT_FOUND", message = $"Không tìm thấy phiếu nhập kho #{id}." });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);
        }

        var productIds = receipt.Details.Select(d => d.ProductId).Distinct().ToList();
        var existingStocks = await db.InventoryStocks
            .AsNoTracking()
            .Where(s => s.WarehouseId == receipt.WarehouseId && productIds.Contains(s.ProductId))
            .ToListAsync(cancellationToken);

        var lines = receipt.Details.OrderBy(d => d.Id).Select(d =>
        {
            var productStocks = existingStocks.Where(s => s.ProductId == d.ProductId).ToList();
            var oldQty = Math.Max(0m, productStocks.Sum(s => s.Quantity));
            var oldMac = productStocks.FirstOrDefault(s => s.MovingAverageCost > 0m)?.MovingAverageCost
                         ?? d.Product?.MovingAverageCost
                         ?? 0m;
            var incomingBaseQty = d.BaseAcceptedQuantity > 0m
                ? d.BaseAcceptedQuantity
                : (d.BaseReceivedQuantity > 0m
                    ? d.BaseReceivedQuantity
                    : (d.BaseExpectedQuantity > 0m ? d.BaseExpectedQuantity : d.Quantity * (d.ConversionFactor > 0m ? d.ConversionFactor : 1m)));
            if (incomingBaseQty <= 0m)
            {
                incomingBaseQty = d.Quantity;
            }
            var conversionFactor = d.ConversionFactor > 0m ? d.ConversionFactor : 1m;
            var receiptBaseUnitCost = d.UnitPrice > 0m
                ? decimal.Round(d.UnitPrice / conversionFactor, 4)
                : oldMac;
            var projectedTotalQty = oldQty + incomingBaseQty;
            var projectedMac = projectedTotalQty > 0m
                ? decimal.Round(((oldQty * oldMac) + (incomingBaseQty * receiptBaseUnitCost)) / projectedTotalQty, 4)
                : receiptBaseUnitCost;
            var macDelta = decimal.Round(projectedMac - oldMac, 4);
            var macDeltaPct = oldMac > 0m
                ? decimal.Round((macDelta * 100m) / oldMac, 2)
                : 0m;

            return new
            {
                detailId = d.Id,
                productId = d.ProductId,
                productCode = d.Product?.Code ?? $"SKU-{d.ProductId}",
                productName = d.Product?.Name ?? string.Empty,
                oldQuantity = oldQty,
                oldMovingAverageCost = oldMac,
                incomingQuantity = incomingBaseQty,
                receiptUnitCost = receiptBaseUnitCost,
                projectedTotalQuantity = projectedTotalQty,
                projectedMovingAverageCost = projectedMac,
                macDelta,
                macDeltaPct,
                projectedInventoryValuation = decimal.Round(projectedTotalQty * projectedMac, 2)
            };
        }).ToList();

        return WmsEndpointOutcome.Ok(new
        {
            importReceiptId = receipt.Id,
            importReceiptCode = receipt.Code,
            warehouseId = receipt.WarehouseId,
            status = receipt.Status.ToString(),
            formula = "(OldQty * OldMAC + IncomingQty * UnitCost) / (OldQty + IncomingQty)",
            costHiddenByRbac = false,
            totalIncomingLines = lines.Count,
            totalProjectedValuation = lines.Sum(x => x.projectedInventoryValuation),
            lines,
            items = lines,
            statusLabel = $"Đã tính toán bản xem trước giá vốn bình quân gia quyền (MAC) cho {lines.Count} dòng hàng"
        });
    }
}
