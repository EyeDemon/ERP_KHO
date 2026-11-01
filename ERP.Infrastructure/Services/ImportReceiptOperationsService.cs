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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int TenantId, int ReturnId), SemaphoreSlim> _dispositionLocks = new();

    private static string NormalizeDispositionCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "RESTOCK_AVAILABLE";
        var upper = raw.Trim().ToUpperInvariant();
        if (upper.Contains("RESTOCK") || upper.Contains("AVAILABLE"))
            return "RESTOCK_AVAILABLE";
        if (upper.Contains("REFURBISH") || upper.Contains("REPACK"))
            return "REFURBISH_REPACK";
        if (upper.Contains("RTV") || upper.Contains("VENDOR"))
            return "RTV_VENDOR";
        if (upper.Contains("SCRAP") || upper.Contains("DISPOSAL"))
            return "SCRAP_DISPOSAL";
        return upper;
    }

    private static string NormalizeConditionCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "GOOD";
        var upper = raw.Trim().ToUpperInvariant();
        if (upper is "GOOD" or "DAMAGED_BOX" or "DEFECTIVE")
            return upper;
        if (upper.Contains("DAMAGE") || upper.Contains("BOX"))
            return "DAMAGED_BOX";
        if (upper.Contains("DEFECT") || upper.Contains("BROKEN") || upper.Contains("BAD"))
            return "DEFECTIVE";
        return "GOOD";
    }

    private object MapCustomerReturnDto(
        ERP.Domain.Entities.CustomerReturn r,
        bool canReadCost,
        IReadOnlyDictionary<int, (string Code, string Name)> productMap,
        IReadOnlyDictionary<int, string> locationMap,
        IReadOnlyDictionary<int, string>? warehouseMap = null,
        string? inspectionVideoUrl = null)
    {
        var now = DateTime.UtcNow;
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
        var rowVersionBase64 = r.RowVersion is { Length: > 0 }
            ? Convert.ToBase64String(r.RowVersion)
            : string.Empty;

        var orderedLines = r.Lines.OrderBy(l => l.Id).ToList();
        var mappedLines = orderedLines.Select(l =>
        {
            productMap.TryGetValue(l.ProductId, out var prodInfo);
            var qLocId = l.QuarantineLocationId ?? r.QuarantineLocationId;
            var qLocCode = qLocId.HasValue && locationMap.TryGetValue(qLocId.Value, out var qCode) ? qCode : "QUARANTINE";
            var rLocId = l.RestockLocationId ?? l.TargetLocationId;
            var rLocCode = rLocId.HasValue && locationMap.TryGetValue(rLocId.Value, out var tCode) ? tCode : null;

            return new
            {
                id = l.Id,
                lineId = l.Id,
                customerReturnId = l.CustomerReturnId,
                productId = l.ProductId,
                productCode = prodInfo.Code ?? $"SKU-{l.ProductId}",
                productName = prodInfo.Name ?? $"Sản phẩm #{l.ProductId}",
                lotId = l.LotId,
                serialId = l.SerialId,
                expiryDate = l.ExpiryDate,
                baseUomId = l.BaseUomId,
                baseUomCode = l.BaseUomCode,
                transactionUomCode = l.TransactionUomCode,
                conversionRateSnapshot = l.ConversionRateSnapshot,
                quarantineLocationId = qLocId,
                quarantineLocationCode = qLocCode,
                targetLocationId = l.TargetLocationId,
                restockLocationId = rLocId,
                restockLocationCode = rLocCode,
                quantity = l.Quantity,
                unitCost = canReadCost ? (decimal?)l.UnitCost : null,
                condition = l.Condition,
                disposition = l.Disposition,
                packagingProductId = l.PackagingProductId,
                packagingQuantity = l.PackagingQuantity,
                packagingConsumedQty = l.PackagingConsumedQty,
                postedLedgerTransactionId = l.PostedLedgerTransactionId
            };
        }).ToList();

        var firstLine = orderedLines.FirstOrDefault();
        (string Code, string Name) firstProdInfo = default;
        if (firstLine != null)
        {
            productMap.TryGetValue(firstLine.ProductId, out firstProdInfo);
        }

        var firstQLocId = firstLine?.QuarantineLocationId ?? r.QuarantineLocationId;
        var primaryQuarantineLocationCode = firstQLocId.HasValue && locationMap.TryGetValue(firstQLocId.Value, out var primaryQCode)
            ? primaryQCode
            : "QUARANTINE";
        var firstRLocId = firstLine?.RestockLocationId ?? firstLine?.TargetLocationId;
        var primaryRestockLocationCode = firstRLocId.HasValue && locationMap.TryGetValue(firstRLocId.Value, out var primaryRCode)
            ? primaryRCode
            : null;
        var warehouseName = warehouseMap != null && warehouseMap.TryGetValue(r.WarehouseId, out var whName) && !string.IsNullOrWhiteSpace(whName)
            ? whName
            : $"Kho #{r.WarehouseId}";
        var salesOrderCode = !string.IsNullOrWhiteSpace(r.OrderNumber)
            ? r.OrderNumber
            : (r.SalesOrderId.HasValue ? $"SO-{r.SalesOrderId.Value}" : null);
        var exportReceiptCode = r.ExportReceiptId.HasValue ? $"EXP-{r.ExportReceiptId.Value}" : null;

        return new
        {
            id = r.Id,
            returnCode = r.ReturnCode,
            warehouseId = r.WarehouseId,
            warehouseName,
            salesOrderId = r.SalesOrderId,
            salesOrderCode,
            exportReceiptId = r.ExportReceiptId,
            exportReceiptCode,
            shipmentId = r.ShipmentId,
            quarantineLocationId = firstQLocId,
            quarantineLocationCode = primaryQuarantineLocationCode,
            restockLocationId = firstRLocId,
            restockLocationCode = primaryRestockLocationCode,
            trackingNumber = r.TrackingNumber,
            orderNumber = r.OrderNumber,
            returnReason = NormalizeVietnameseDiacritics(r.ReturnReason),
            status = r.Status,
            productId = firstLine?.ProductId ?? 0,
            productCode = firstProdInfo.Code ?? (firstLine != null ? $"SKU-{firstLine.ProductId}" : string.Empty),
            productName = firstProdInfo.Name ?? (firstLine != null ? $"Sản phẩm #{firstLine.ProductId}" : string.Empty),
            quantity = orderedLines.Count > 0 ? orderedLines.Sum(l => l.Quantity) : 0m,
            unitCost = canReadCost ? (decimal?)(firstLine?.UnitCost ?? 0m) : null,
            condition = firstLine?.Condition ?? "GOOD",
            disposition = firstLine?.Disposition ?? (string.Equals(r.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ? "RESTOCK_AVAILABLE" : "PENDING"),
            createdBy = r.CreatedBy,
            createdAtUtc = r.CreatedAtUtc,
            createdAt = r.CreatedAtUtc,
            approvedBy = r.ApprovedBy,
            approvedAtUtc = r.ApprovedAtUtc,
            postedAtUtc = r.PostedAtUtc,
            completedAtUtc = r.CompletedAtUtc,
            rowVersion = rowVersionBase64,
            costHiddenByRbac = !canReadCost,
            inspectionVideoUrl,
            returnAgingDays,
            isOverdueInTransit,
            overdueAlertLevel,
            disputeDeadlineUtc,
            disputeRemainingHours,
            disputeClaimStatus,
            lines = mappedLines
        };
    }

    public async Task<WmsEndpointOutcome> QueryCustomerReturnsAsync(
        CustomerReturnQueryFilter filter,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
        {
            return WmsEndpointOutcome.Ok(new
            {
                items = Array.Empty<object>(),
                totalCount = 0,
                pageNumber = filter.PageNumber,
                pageSize = filter.PageSize,
                summary = new { quarantinedCount = 0, inspectedCount = 0, restockedCount = 0, refurbishRtvScrapCount = 0 },
                costHiddenByRbac = !filter.CanReadCost
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (filter.WarehouseId.HasValue && effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(filter.WarehouseId.Value, cancellationToken);

        var query = db.CustomerReturns.AsNoTracking().Include(r => r.Lines).AsQueryable();
        if (filter.WarehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == filter.WarehouseId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var st = filter.Status.Trim().ToUpperInvariant();
            query = query.Where(r => r.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(filter.Condition))
        {
            var cond = filter.Condition.Trim().ToUpperInvariant();
            query = query.Where(r => r.Lines.Any(l => l.Condition == cond));
        }
        if (!string.IsNullOrWhiteSpace(filter.Disposition))
        {
            var disp = filter.Disposition.Trim().ToUpperInvariant();
            query = query.Where(r => r.Lines.Any(l => l.Disposition == disp));
        }
        if (!string.IsNullOrWhiteSpace(filter.Keyword))
        {
            var kw = filter.Keyword.Trim();
            query = query.Where(r =>
                r.ReturnCode.Contains(kw) ||
                (r.TrackingNumber != null && r.TrackingNumber.Contains(kw)) ||
                (r.OrderNumber != null && r.OrderNumber.Contains(kw)) ||
                r.ReturnReason.Contains(kw));
        }

        var pageNumber = Math.Max(1, filter.PageNumber);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var totalCount = await query.CountAsync(cancellationToken);

        var rawReturns = await query
            .OrderByDescending(r => r.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var allReturnsForSummary = await db.CustomerReturns.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => !filter.WarehouseId.HasValue || r.WarehouseId == filter.WarehouseId.Value)
            .ToListAsync(cancellationToken);

        var quarantinedCount = allReturnsForSummary.Count(r => string.Equals(r.Status, "QUARANTINED", StringComparison.OrdinalIgnoreCase));
        var inspectedCount = allReturnsForSummary.Count(r => string.Equals(r.Status, "INSPECTED", StringComparison.OrdinalIgnoreCase));
        var restockedCount = allReturnsForSummary.SelectMany(r => r.Lines).Count(l => string.Equals(l.Disposition, "RESTOCK_AVAILABLE", StringComparison.OrdinalIgnoreCase));
        var refurbishRtvScrapCount = allReturnsForSummary.SelectMany(r => r.Lines).Count(l =>
            string.Equals(l.Disposition, "REFURBISH_REPACK", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Disposition, "RTV_VENDOR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(l.Disposition, "SCRAP_DISPOSAL", StringComparison.OrdinalIgnoreCase));

        var productIds = rawReturns.SelectMany(r => r.Lines.Select(l => l.ProductId)).Distinct().ToList();
        var productMap = productIds.Count > 0
            ? await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (p.Code.Trim(), p.Name.Trim()), cancellationToken)
            : new Dictionary<int, (string Code, string Name)>();

        var locIds = rawReturns
            .SelectMany(r => r.Lines.SelectMany(l => new[] { l.QuarantineLocationId, l.TargetLocationId, l.RestockLocationId }).Append(r.QuarantineLocationId))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var locationMap = locIds.Count > 0
            ? await db.WarehouseLocations.AsNoTracking()
                .Where(l => locIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Code.Trim(), cancellationToken)
            : new Dictionary<int, string>();

        var whIds = rawReturns.Select(r => r.WarehouseId).Distinct().ToList();
        var warehouseMap = whIds.Count > 0
            ? await db.Warehouses.AsNoTracking()
                .Where(w => whIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id, w => w.Name.Trim(), cancellationToken)
            : new Dictionary<int, string>();

        var items = rawReturns
            .Select(r => MapCustomerReturnDto(r, filter.CanReadCost, productMap, locationMap, warehouseMap))
            .ToList();

        return WmsEndpointOutcome.Ok(new
        {
            items,
            totalCount,
            pageNumber,
            pageSize,
            summary = new
            {
                quarantinedCount,
                inspectedCount,
                restockedCount,
                refurbishRtvScrapCount
            },
            costHiddenByRbac = !filter.CanReadCost
        });
    }

    public async Task<WmsEndpointOutcome> GetCustomerReturnByIdAsync(
        int id,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var r = await db.CustomerReturns.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (r == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "RMA_NOT_FOUND",
                errorCode = "RMA_NOT_FOUND",
                message = $"Không tìm thấy phiếu hoàn trả #{id}."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(r.WarehouseId, cancellationToken);

        var productIds = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        var productMap = productIds.Count > 0
            ? await db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => (p.Code.Trim(), p.Name.Trim()), cancellationToken)
            : new Dictionary<int, (string Code, string Name)>();

        var locIds = r.Lines
            .SelectMany(l => new[] { l.QuarantineLocationId, l.TargetLocationId, l.RestockLocationId })
            .Append(r.QuarantineLocationId)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var locationMap = locIds.Count > 0
            ? await db.WarehouseLocations.AsNoTracking()
                .Where(l => locIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Code.Trim(), cancellationToken)
            : new Dictionary<int, string>();

        var warehouseMap = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == r.WarehouseId)
            .ToDictionaryAsync(w => w.Id, w => w.Name.Trim(), cancellationToken);

        return WmsEndpointOutcome.Ok(MapCustomerReturnDto(r, canReadCost, productMap, locationMap, warehouseMap));
    }

    public async Task<WmsEndpointOutcome> IntakeCustomerReturnAsync(
        CustomerReturnIntakeInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "RMA_INTAKE_REQUIRED",
                errorCode = "RMA_INTAKE_REQUIRED",
                message = "Dữ liệu tiếp nhận hàng hoàn trả không được để trống."
            });
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);
        var effectiveAuth = warehouseAuth ?? warehouseAccess;

        var trackingCandidate = (request.TrackingNumber ?? request.ReturnReference)?.Trim();
        var exportCodeCandidate = (request.ExportReceiptCode ?? request.ReturnReference)?.Trim();
        var soCodeCandidate = (request.SalesOrderCode ?? request.ReturnReference)?.Trim();

        ERP.Domain.Entities.Shipment? matchedShipment = null;
        if (request.ShipmentId.HasValue)
        {
            matchedShipment = await db.Shipments.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.ShipmentId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(trackingCandidate))
        {
            matchedShipment = await db.Shipments.AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.ShipmentCode == trackingCandidate || s.SealNumber == trackingCandidate,
                    cancellationToken);
        }

        ERP.Domain.Entities.ExportReceipt? matchedExport = null;
        var resolvedExportId = request.ExportReceiptId
            ?? (matchedShipment is { SourceType: "ExportReceipt", SourceId: not null } ? matchedShipment.SourceId : null);
        if (resolvedExportId.HasValue)
        {
            matchedExport = await db.ExportReceipts.AsNoTracking()
                .Include(e => e.Details)
                .FirstOrDefaultAsync(e => e.Id == resolvedExportId.Value, cancellationToken);
        }
        else if (matchedShipment != null)
        {
            matchedExport = await db.ExportReceipts.AsNoTracking()
                .Include(e => e.Details)
                .FirstOrDefaultAsync(e => e.ShipmentId == matchedShipment.Id, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(exportCodeCandidate))
        {
            matchedExport = await db.ExportReceipts.AsNoTracking()
                .Include(e => e.Details)
                .FirstOrDefaultAsync(e => e.Code == exportCodeCandidate, cancellationToken);
        }

        var resolvedSalesOrderId = request.SalesOrderId
            ?? matchedExport?.SalesOrderId
            ?? (matchedShipment is { SourceType: "SalesOrder", SourceId: not null } ? matchedShipment.SourceId : null);
        if (!resolvedSalesOrderId.HasValue && !string.IsNullOrWhiteSpace(soCodeCandidate))
        {
            resolvedSalesOrderId = await db.SalesOrders.AsNoTracking()
                .Where(s => s.OrderCode == soCodeCandidate || s.ExternalOrderId == soCodeCandidate)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var targetWhId = request.WarehouseId
            ?? matchedExport?.WarehouseId
            ?? matchedShipment?.WarehouseId
            ?? await db.Warehouses.AsNoTracking().OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (targetWhId <= 0) targetWhId = 1;

        if (effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(targetWhId, cancellationToken);

        int? resolvedQuarantineLocId = request.QuarantineLocationId;
        if (!resolvedQuarantineLocId.HasValue && !string.IsNullOrWhiteSpace(request.QuarantineLocationCode))
        {
            var qCode = request.QuarantineLocationCode.Trim();
            resolvedQuarantineLocId = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWhId && l.Code == qCode)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }
        if (!resolvedQuarantineLocId.HasValue)
        {
            resolvedQuarantineLocId = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWhId && l.IsActive)
                .OrderBy(l => l.Code.Contains("QUAR") || l.Code.Contains("RMA") ? 0 : (l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Receiving ? 1 : 2))
                .ThenBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var rawLines = new List<CustomerReturnIntakeLineInput>();
        if (request.Lines is { Count: > 0 })
        {
            rawLines.AddRange(request.Lines);
        }
        else if (request.ProductId.HasValue || !string.IsNullOrWhiteSpace(request.ProductCode))
        {
            rawLines.Add(new CustomerReturnIntakeLineInput(
                ProductId: request.ProductId,
                ProductCode: request.ProductCode,
                Quantity: request.Quantity ?? 1m,
                UnitCost: request.UnitCost,
                LotId: request.LotId,
                LotNumber: request.LotNumber,
                SerialId: request.SerialId,
                SerialNumber: request.SerialNumber,
                QuarantineLocationId: resolvedQuarantineLocId,
                Condition: request.Condition));
        }
        else if (matchedExport is { Details.Count: > 0 })
        {
            foreach (var d in matchedExport.Details.OrderBy(x => x.Id))
            {
                rawLines.Add(new CustomerReturnIntakeLineInput(
                    ProductId: d.ProductId,
                    Quantity: d.Quantity,
                    UnitCost: d.UnitPrice,
                    QuarantineLocationId: resolvedQuarantineLocId,
                    Condition: request.Condition ?? "GOOD",
                    BaseUomCode: d.BaseUomCodeSnapshot,
                    TransactionUomCode: d.BaseUomCodeSnapshot,
                    ConversionRateSnapshot: 1m));
            }
        }

        if (rawLines.Count == 0)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "RMA_LINES_REQUIRED",
                errorCode = "RMA_LINES_REQUIRED",
                message = "Cần chỉ định ít nhất 1 dòng sản phẩm hoàn trả hoặc mã vận đơn/phiếu xuất hợp lệ."
            });
        }

        var resolvedLines = new List<(
            int ProductId,
            int? LotId,
            int? SerialId,
            DateTime? ExpiryDate,
            int? BaseUomId,
            string BaseUomCode,
            string TransactionUomCode,
            decimal ConversionRate,
            int? QuarantineLocId,
            decimal Quantity,
            decimal UnitCost,
            string Condition)>();

        foreach (var rl in rawLines)
        {
            if (rl.Quantity <= 0m)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new
                {
                    code = "RMA_INVALID_QUANTITY",
                    errorCode = "RMA_INVALID_QUANTITY",
                    message = "Số lượng tiếp nhận hoàn trả phải lớn hơn 0."
                });
            }

            var prodId = rl.ProductId;
            if (!prodId.HasValue && !string.IsNullOrWhiteSpace(rl.ProductCode))
            {
                var pCode = rl.ProductCode.Trim();
                prodId = await db.Products.AsNoTracking()
                    .Where(p => p.Code == pCode)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (!prodId.HasValue)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "PRODUCT_NOT_FOUND",
                    errorCode = "PRODUCT_NOT_FOUND",
                    message = "Không tìm thấy sản phẩm hoàn trả."
                });
            }

            var product = await db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Id == prodId.Value, cancellationToken);
            if (product == null)
            {
                return WmsEndpointOutcome.NotFound(new
                {
                    code = "PRODUCT_NOT_FOUND",
                    errorCode = "PRODUCT_NOT_FOUND",
                    message = $"Không tìm thấy sản phẩm #{prodId.Value}."
                });
            }

            var matchedDetail = matchedExport?.Details.FirstOrDefault(d => d.ProductId == product.Id);

            var lotId = rl.LotId;
            if (!lotId.HasValue && !string.IsNullOrWhiteSpace(rl.LotNumber))
            {
                var lNum = rl.LotNumber.Trim();
                lotId = await db.InventoryLots.AsNoTracking()
                    .Where(l => l.ProductId == product.Id && l.LotNumber == lNum)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var serialId = rl.SerialId;
            if (!serialId.HasValue && !string.IsNullOrWhiteSpace(rl.SerialNumber))
            {
                var sNum = rl.SerialNumber.Trim();
                serialId = await db.InventorySerials.AsNoTracking()
                    .Where(s => s.ProductId == product.Id && s.SerialNumber == sNum)
                    .Select(s => (int?)s.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            var lineQId = rl.QuarantineLocationId ?? resolvedQuarantineLocId;
            if (!rl.QuarantineLocationId.HasValue && !string.IsNullOrWhiteSpace(rl.QuarantineLocationCode))
            {
                var qlCode = rl.QuarantineLocationCode.Trim();
                lineQId = await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWhId && l.Code == qlCode)
                    .Select(l => (int?)l.Id)
                    .FirstOrDefaultAsync(cancellationToken) ?? resolvedQuarantineLocId;
            }

            var baseUomCode = !string.IsNullOrWhiteSpace(rl.BaseUomCode)
                ? rl.BaseUomCode.Trim()
                : (!string.IsNullOrWhiteSpace(product.Unit?.Code) ? product.Unit.Code.Trim() : "PCS");
            var txUomCode = !string.IsNullOrWhiteSpace(rl.TransactionUomCode)
                ? rl.TransactionUomCode.Trim()
                : baseUomCode;
            var convRate = rl.ConversionRateSnapshot is > 0m ? rl.ConversionRateSnapshot.Value : 1m;
            var baseQty = decimal.Round(rl.Quantity * convRate, 4);
            var unitCost = rl.UnitCost ?? matchedDetail?.UnitPrice ?? product.MovingAverageCost;
            int? baseUomId = product.UnitId > 0 ? product.UnitId : (int?)null;

            resolvedLines.Add((
                product.Id,
                lotId,
                serialId,
                rl.ExpiryDate,
                baseUomId,
                baseUomCode,
                txUomCode,
                convRate,
                lineQId,
                baseQty,
                unitCost,
                NormalizeConditionCode(rl.Condition ?? request.Condition)));
        }

        var isRelational = db.Database.IsRelational();
        await using var tx = isRelational && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        try
        {
            await db.AcquireInventoryAllocationLocksAsync(
                targetWhId,
                resolvedLines.Select(x => x.ProductId).Distinct().OrderBy(x => x),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(request.ReturnCode) || !string.IsNullOrWhiteSpace(trackingCandidate))
            {
                var rCode = request.ReturnCode?.Trim();
                var existingReturn = await db.CustomerReturns.AsNoTracking()
                    .FirstOrDefaultAsync(r =>
                        r.WarehouseId == targetWhId &&
                        ((!string.IsNullOrWhiteSpace(rCode) && r.ReturnCode == rCode) ||
                         (!string.IsNullOrWhiteSpace(trackingCandidate) && r.TrackingNumber == trackingCandidate)),
                        cancellationToken);
                if (existingReturn != null)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return await GetCustomerReturnByIdAsync(existingReturn.Id, canReadCost, effectiveAuth, cancellationToken);
                }
            }

            var returnCode = !string.IsNullOrWhiteSpace(request.ReturnCode)
                ? request.ReturnCode.Trim()
                : $"RMA-{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

            var customerReturn = new ERP.Domain.Entities.CustomerReturn
            {
                ReturnCode = returnCode,
                WarehouseId = targetWhId,
                SalesOrderId = resolvedSalesOrderId,
                ExportReceiptId = matchedExport?.Id ?? request.ExportReceiptId,
                ShipmentId = matchedShipment?.Id ?? request.ShipmentId,
                QuarantineLocationId = resolvedQuarantineLocId,
                TrackingNumber = trackingCandidate ?? matchedShipment?.ShipmentCode,
                OrderNumber = request.SalesOrderCode ?? matchedExport?.Code ?? matchedShipment?.SourceCode,
                ReturnReason = NormalizeVietnameseDiacritics(request.ReturnReason ?? request.ReasonCode ?? "Khách hoàn hàng (RMA)"),
                Status = "QUARANTINED",
                CreatedBy = effectiveActorId,
                CreatedAtUtc = now
            };
            db.CustomerReturns.Add(customerReturn);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var line in resolvedLines)
            {
                var qStock = await db.InventoryStocks.FirstOrDefaultAsync(
                    s => s.WarehouseId == targetWhId &&
                         s.ProductId == line.ProductId &&
                         s.LocationId == line.QuarantineLocId &&
                         s.LotId == line.LotId &&
                         s.SerialId == line.SerialId &&
                         s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine,
                    cancellationToken);

                if (qStock == null)
                {
                    qStock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = targetWhId,
                        ProductId = line.ProductId,
                        LocationId = line.QuarantineLocId,
                        LotId = line.LotId,
                        SerialId = line.SerialId,
                        Status = ERP.Domain.Enums.InventoryStatus.Quarantine,
                        Quantity = line.Quantity,
                        ReservedQuantity = 0m,
                        MovingAverageCost = line.UnitCost,
                        LastUpdated = now
                    };
                    db.InventoryStocks.Add(qStock);
                }
                else
                {
                    qStock.Quantity += line.Quantity;
                    qStock.LastUpdated = now;
                }

                var intakeTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = line.ProductId,
                    WarehouseId = targetWhId,
                    LocationId = line.QuarantineLocId,
                    ToLocationId = line.QuarantineLocId,
                    LotId = line.LotId,
                    SerialId = line.SerialId,
                    InventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                    TransactionType = ERP.Domain.Enums.TransactionType.Import,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    ReferenceId = customerReturn.Id,
                    ReferenceType = "CustomerReturn",
                    TransactionDate = now,
                    CreatedBy = effectiveActorId,
                    Note = $"{customerReturn.ReturnCode}: Tiếp nhận hàng hoàn trả vào khu biệt trữ"
                };
                db.InventoryTransactions.Add(intakeTx);

                customerReturn.Lines.Add(new ERP.Domain.Entities.CustomerReturnLine
                {
                    CustomerReturnId = customerReturn.Id,
                    ProductId = line.ProductId,
                    LotId = line.LotId,
                    SerialId = line.SerialId,
                    ExpiryDate = line.ExpiryDate,
                    BaseUomId = line.BaseUomId,
                    BaseUomCode = line.BaseUomCode,
                    TransactionUomCode = line.TransactionUomCode,
                    ConversionRateSnapshot = line.ConversionRate,
                    QuarantineLocationId = line.QuarantineLocId,
                    TargetLocationId = line.QuarantineLocId,
                    Quantity = line.Quantity,
                    UnitCost = line.UnitCost,
                    Condition = line.Condition,
                    Disposition = "PENDING"
                });
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWhId,
                EntityName = "CustomerReturn",
                EntityId = customerReturn.Id,
                Action = "CustomerReturn.Intake",
                OldValues = "NONE",
                NewValues = $"Status: QUARANTINED; ReturnCode: {customerReturn.ReturnCode}",
                Reason = customerReturn.ReturnReason,
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);

            return await GetCustomerReturnByIdAsync(customerReturn.Id, canReadCost, effectiveAuth, cancellationToken);
        }
        catch
        {
            if (tx != null) await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<WmsEndpointOutcome> InspectCustomerReturnAsync(
        int id,
        CustomerReturnInspectInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var customerReturn = await db.CustomerReturns
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (customerReturn == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "RMA_NOT_FOUND",
                errorCode = "RMA_NOT_FOUND",
                message = $"Không tìm thấy phiếu hoàn trả #{id}."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(customerReturn.WarehouseId, cancellationToken);

        if (string.Equals(customerReturn.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            return WmsEndpointOutcome.Conflict(new
            {
                code = "RMA_ALREADY_COMPLETED",
                errorCode = "RMA_ALREADY_COMPLETED",
                message = $"Phiếu hoàn trả '{customerReturn.ReturnCode}' đã hoàn tất định đoạt, không thể kiểm định lại."
            });
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);

        if (request?.Lines is { Count: > 0 })
        {
            foreach (var reqLine in request.Lines)
            {
                var targetLine = reqLine.LineId.HasValue
                    ? customerReturn.Lines.FirstOrDefault(l => l.Id == reqLine.LineId.Value)
                    : (reqLine.ProductId.HasValue
                        ? customerReturn.Lines.FirstOrDefault(l => l.ProductId == reqLine.ProductId.Value)
                        : customerReturn.Lines.FirstOrDefault());
                if (targetLine == null) continue;

                if (!string.IsNullOrWhiteSpace(reqLine.Condition))
                    targetLine.Condition = NormalizeConditionCode(reqLine.Condition);
                if (!string.IsNullOrWhiteSpace(reqLine.Disposition))
                    targetLine.Disposition = NormalizeDispositionCode(reqLine.Disposition);
                if (reqLine.RestockLocationId.HasValue)
                    targetLine.RestockLocationId = reqLine.RestockLocationId.Value;
                if (reqLine.TargetLocationId.HasValue)
                    targetLine.TargetLocationId = reqLine.TargetLocationId.Value;
                if (reqLine.PackagingProductId.HasValue)
                    targetLine.PackagingProductId = reqLine.PackagingProductId.Value;
                if (reqLine.PackagingQuantity.HasValue)
                    targetLine.PackagingQuantity = reqLine.PackagingQuantity.Value;
            }
        }
        else
        {
            foreach (var line in customerReturn.Lines)
            {
                if (!string.IsNullOrWhiteSpace(request?.Condition))
                    line.Condition = NormalizeConditionCode(request.Condition);
                if (!string.IsNullOrWhiteSpace(request?.Disposition))
                    line.Disposition = NormalizeDispositionCode(request.Disposition);
            }
        }

        customerReturn.Status = "INSPECTED";
        if (!db.Database.IsSqlServer())
            customerReturn.RowVersion = Guid.NewGuid().ToByteArray()[..8];

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = customerReturn.WarehouseId,
            EntityName = "CustomerReturn",
            EntityId = customerReturn.Id,
            Action = "CustomerReturn.Inspected",
            OldValues = "QUARANTINED",
            NewValues = "Status: INSPECTED",
            Reason = request?.Note ?? "Hoàn tất kiểm định chất lượng hàng hoàn trả",
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return await GetCustomerReturnByIdAsync(customerReturn.Id, canReadCost, effectiveAuth, cancellationToken);
    }

    public async Task<WmsEndpointOutcome> DisposeCustomerReturnAsync(
        int id,
        CustomerReturnDisposeInput? request,
        int? actorUserId = null,
        bool canReadCost = true,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = db.CurrentTenantId > 0 ? db.CurrentTenantId : 1;
        var gate = _dispositionLocks.GetOrAdd((tenantId, id), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                : null;

            try
            {
                var customerReturn = await db.CustomerReturns
                    .Include(r => r.Lines)
                    .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
                if (customerReturn == null)
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return WmsEndpointOutcome.NotFound(new
                    {
                        code = "RMA_NOT_FOUND",
                        errorCode = "RMA_NOT_FOUND",
                        message = $"Không tìm thấy phiếu hoàn trả #{id}."
                    });
                }

                var effectiveAuth = warehouseAuth ?? warehouseAccess;
                if (effectiveAuth != null)
                    await effectiveAuth.EnsureWarehouseAccessAsync(customerReturn.WarehouseId, cancellationToken);

                if (string.Equals(customerReturn.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    if (tx != null) await tx.RollbackAsync(cancellationToken);
                    return WmsEndpointOutcome.Conflict(new
                    {
                        code = "RMA_ALREADY_COMPLETED",
                        errorCode = "RMA_ALREADY_COMPLETED",
                        message = $"Phiếu hoàn trả '{customerReturn.ReturnCode}' đã được hoàn tất định đoạt trước đó."
                    });
                }

                if (!string.IsNullOrWhiteSpace(request?.RowVersion) && customerReturn.RowVersion is { Length: > 0 })
                {
                    byte[] expectedVersion;
                    try
                    {
                        expectedVersion = Convert.FromBase64String(request.RowVersion.Trim());
                    }
                    catch
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.BadRequest(new
                        {
                            code = "INVALID_ROW_VERSION",
                            errorCode = "INVALID_ROW_VERSION",
                            message = "RowVersion không hợp lệ."
                        });
                    }

                    if (!customerReturn.RowVersion.SequenceEqual(expectedVersion))
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.Conflict(new
                        {
                            code = "CONCURRENCY_CONFLICT",
                            errorCode = "CONCURRENCY_CONFLICT",
                            message = "Dữ liệu phiếu hoàn trả đã bị thay đổi bởi người dùng khác. Vui lòng tải lại."
                        });
                    }
                }

                var splitsInput = request?.Splits ?? request?.Lines;
                var lockProductIds = customerReturn.Lines.Select(l => l.ProductId)
                    .Concat(customerReturn.Lines.Where(l => l.PackagingProductId.HasValue).Select(l => l.PackagingProductId!.Value))
                    .Concat(splitsInput?.Where(s => s.ProductId.HasValue).Select(s => s.ProductId!.Value) ?? [])
                    .Concat(splitsInput?.Where(s => s.PackagingProductId.HasValue).Select(s => s.PackagingProductId!.Value) ?? [])
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                if (lockProductIds.Count > 0)
                {
                    await db.AcquireInventoryAllocationLocksAsync(customerReturn.WarehouseId, lockProductIds, cancellationToken);
                }

                async Task<int?> ResolveLocationIdAsync(int? locId, string? locCode, int? fallbackLocId)
                {
                    if (locId.HasValue) return locId.Value;
                    if (!string.IsNullOrWhiteSpace(locCode))
                    {
                        var trimmedCode = locCode.Trim();
                        var found = await db.WarehouseLocations.AsNoTracking()
                            .Where(l => l.WarehouseId == customerReturn.WarehouseId && l.Code == trimmedCode)
                            .Select(l => (int?)l.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                        if (found.HasValue) return found.Value;
                    }
                    return fallbackLocId;
                }

                var topLevelRestockLocId = await ResolveLocationIdAsync(
                    request?.RestockLocationId ?? request?.TargetLocationId,
                    request?.RestockLocationCode,
                    null);

                if (splitsInput is { Count: > 0 })
                {
                    var existingLinesSnapshot = customerReturn.Lines.OrderBy(l => l.Id).ToList();
                    var groupedBySourceLine = new Dictionary<int, List<CustomerReturnDispositionSplitInput>>();

                    foreach (var split in splitsInput)
                    {
                        var matchedLine = split.LineId.HasValue
                            ? existingLinesSnapshot.FirstOrDefault(l => l.Id == split.LineId.Value)
                            : (split.ProductId.HasValue
                                ? existingLinesSnapshot.FirstOrDefault(l => l.ProductId == split.ProductId.Value)
                                : existingLinesSnapshot.FirstOrDefault());

                        if (matchedLine == null)
                        {
                            if (tx != null) await tx.RollbackAsync(cancellationToken);
                            return WmsEndpointOutcome.NotFound(new
                            {
                                code = "RMA_LINE_NOT_FOUND",
                                errorCode = "RMA_LINE_NOT_FOUND",
                                message = "Không tìm thấy dòng hàng hoàn trả tương ứng để tách định đoạt."
                            });
                        }

                        if (!groupedBySourceLine.TryGetValue(matchedLine.Id, out var bucket))
                        {
                            bucket = [];
                            groupedBySourceLine[matchedLine.Id] = bucket;
                        }
                        bucket.Add(split);
                    }

                    foreach (var (sourceLineId, splitGroup) in groupedBySourceLine)
                    {
                        var sourceLine = existingLinesSnapshot.Single(l => l.Id == sourceLineId);
                        var totalSplitQty = splitGroup.Sum(s => s.Quantity);
                        if (splitGroup.Any(s => s.Quantity <= 0m) || totalSplitQty != sourceLine.Quantity)
                        {
                            if (tx != null) await tx.RollbackAsync(cancellationToken);
                            return WmsEndpointOutcome.UnprocessableEntity(new
                            {
                                code = "RMA_SPLIT_QUANTITY_MISMATCH",
                                errorCode = "RMA_SPLIT_QUANTITY_MISMATCH",
                                message = $"Tổng số lượng tách định đoạt ({totalSplitQty}) phải khớp chính xác với số lượng dòng gốc ({sourceLine.Quantity})."
                            });
                        }

                        var originalQuarantineLocId = sourceLine.QuarantineLocationId ?? customerReturn.QuarantineLocationId;
                        var first = splitGroup[0];
                        var firstRestockLocId = await ResolveLocationIdAsync(
                            first.RestockLocationId ?? first.TargetLocationId,
                            first.RestockLocationCode,
                            topLevelRestockLocId ?? sourceLine.RestockLocationId ?? sourceLine.TargetLocationId);

                        sourceLine.Quantity = first.Quantity;
                        sourceLine.Disposition = NormalizeDispositionCode(first.Disposition ?? request?.Disposition);
                        if (!string.IsNullOrWhiteSpace(first.Condition))
                            sourceLine.Condition = NormalizeConditionCode(first.Condition);
                        sourceLine.RestockLocationId = firstRestockLocId;
                        sourceLine.TargetLocationId = firstRestockLocId ?? originalQuarantineLocId;
                        if (first.PackagingProductId.HasValue)
                            sourceLine.PackagingProductId = first.PackagingProductId.Value;
                        if (first.PackagingQuantity.HasValue)
                            sourceLine.PackagingQuantity = first.PackagingQuantity.Value;

                        for (var i = 1; i < splitGroup.Count; i++)
                        {
                            var extra = splitGroup[i];
                            var extraRestockLocId = await ResolveLocationIdAsync(
                                extra.RestockLocationId ?? extra.TargetLocationId,
                                extra.RestockLocationCode,
                                topLevelRestockLocId ?? sourceLine.RestockLocationId);

                            var clonedLine = new ERP.Domain.Entities.CustomerReturnLine
                            {
                                CustomerReturnId = customerReturn.Id,
                                ProductId = sourceLine.ProductId,
                                LotId = extra.LotId ?? sourceLine.LotId,
                                SerialId = extra.SerialId ?? sourceLine.SerialId,
                                ExpiryDate = sourceLine.ExpiryDate,
                                BaseUomId = sourceLine.BaseUomId,
                                BaseUomCode = sourceLine.BaseUomCode,
                                TransactionUomCode = sourceLine.TransactionUomCode,
                                ConversionRateSnapshot = sourceLine.ConversionRateSnapshot,
                                QuarantineLocationId = originalQuarantineLocId,
                                TargetLocationId = extraRestockLocId ?? originalQuarantineLocId,
                                RestockLocationId = extraRestockLocId,
                                Quantity = extra.Quantity,
                                UnitCost = sourceLine.UnitCost,
                                Condition = NormalizeConditionCode(extra.Condition ?? sourceLine.Condition),
                                Disposition = NormalizeDispositionCode(extra.Disposition ?? request?.Disposition),
                                PackagingProductId = extra.PackagingProductId ?? sourceLine.PackagingProductId,
                                PackagingQuantity = extra.PackagingQuantity ?? 0m
                            };
                            customerReturn.Lines.Add(clonedLine);
                            db.CustomerReturnLines.Add(clonedLine);
                        }
                    }
                }
                else
                {
                    foreach (var line in customerReturn.Lines)
                    {
                        var effectiveDisp = !string.IsNullOrWhiteSpace(request?.Disposition)
                            ? request.Disposition
                            : (line.Disposition == "PENDING" ? "RESTOCK_AVAILABLE" : line.Disposition);
                        line.Disposition = NormalizeDispositionCode(effectiveDisp);
                        if (topLevelRestockLocId.HasValue)
                        {
                            line.RestockLocationId = topLevelRestockLocId.Value;
                            line.TargetLocationId = topLevelRestockLocId.Value;
                        }
                        if (request?.PackagingProductId.HasValue == true)
                            line.PackagingProductId = request.PackagingProductId.Value;
                        if (request?.PackagingQuantity.HasValue == true)
                            line.PackagingQuantity = request.PackagingQuantity.Value;
                    }
                }

                var now = DateTime.UtcNow;
                var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);

                foreach (var line in customerReturn.Lines.ToList())
                {
                    var qLocId = line.QuarantineLocationId ?? customerReturn.QuarantineLocationId;
                    var quarantineStock = await db.InventoryStocks.FirstOrDefaultAsync(
                        s => s.WarehouseId == customerReturn.WarehouseId &&
                             s.ProductId == line.ProductId &&
                             (!qLocId.HasValue || s.LocationId == qLocId.Value) &&
                             s.LotId == line.LotId &&
                             s.SerialId == line.SerialId &&
                             s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine,
                        cancellationToken)
                        ?? await db.InventoryStocks.FirstOrDefaultAsync(
                            s => s.WarehouseId == customerReturn.WarehouseId &&
                                 s.ProductId == line.ProductId &&
                                 s.Status == ERP.Domain.Enums.InventoryStatus.Quarantine &&
                                 s.Quantity >= line.Quantity,
                            cancellationToken);

                    if (quarantineStock == null || quarantineStock.Quantity < line.Quantity)
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.UnprocessableEntity(new
                        {
                            code = "INSUFFICIENT_QUARANTINE_STOCK",
                            errorCode = "INSUFFICIENT_QUARANTINE_STOCK",
                            message = $"Không đủ tồn kho biệt trữ (Quarantine) cho sản phẩm #{line.ProductId} (Cần: {line.Quantity}, Hiện có: {quarantineStock?.Quantity ?? 0m})."
                        });
                    }

                    quarantineStock.Quantity -= line.Quantity;
                    quarantineStock.LastUpdated = now;

                    var disp = NormalizeDispositionCode(line.Disposition);
                    line.Disposition = disp;

                    if (disp is "RESTOCK_AVAILABLE" or "REFURBISH_REPACK")
                    {
                        var pkgId = line.PackagingProductId ?? request?.PackagingProductId;
                        var pkgQty = line.PackagingQuantity > 0m ? line.PackagingQuantity : (request?.PackagingQuantity ?? 0m);
                        if (disp == "REFURBISH_REPACK" && pkgId.HasValue && pkgQty > 0m)
                        {
                            var pkgStock = await db.InventoryStocks
                                .Where(s => s.WarehouseId == customerReturn.WarehouseId &&
                                            s.ProductId == pkgId.Value &&
                                            s.Status == ERP.Domain.Enums.InventoryStatus.Available &&
                                            s.Quantity - s.ReservedQuantity >= pkgQty)
                                .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                                .FirstOrDefaultAsync(cancellationToken);

                            if (pkgStock == null)
                            {
                                if (tx != null) await tx.RollbackAsync(cancellationToken);
                                return WmsEndpointOutcome.UnprocessableEntity(new
                                {
                                    code = "INSUFFICIENT_PACKAGING_STOCK",
                                    errorCode = "INSUFFICIENT_PACKAGING_STOCK",
                                    message = $"Không đủ tồn kho bao bì (#{pkgId.Value}) để đóng gói tân trang."
                                });
                            }

                            pkgStock.Quantity -= pkgQty;
                            pkgStock.LastUpdated = now;
                            line.PackagingConsumedQty = pkgQty;

                            db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                            {
                                ProductId = pkgId.Value,
                                WarehouseId = customerReturn.WarehouseId,
                                LocationId = pkgStock.LocationId,
                                FromLocationId = pkgStock.LocationId,
                                InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                                FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                                TransactionType = ERP.Domain.Enums.TransactionType.AdjustmentDecrease,
                                Quantity = pkgQty,
                                ReferenceId = customerReturn.Id,
                                ReferenceType = "CustomerReturnRefurbishPackaging",
                                TransactionDate = now,
                                CreatedBy = effectiveActorId,
                                Note = $"{customerReturn.ReturnCode}: Tiêu hao vật tư bao bì tân trang"
                            });
                        }

                        var restockLocId = line.RestockLocationId
                            ?? line.TargetLocationId
                            ?? topLevelRestockLocId;
                        if (!restockLocId.HasValue || restockLocId == quarantineStock.LocationId)
                        {
                            var preferredStorageLocId = await db.WarehouseLocations.AsNoTracking()
                                .Where(l => l.WarehouseId == customerReturn.WarehouseId &&
                                            l.IsActive &&
                                            !l.IsBlocked &&
                                            l.Id != quarantineStock.LocationId)
                                .OrderBy(l => l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Storage && l.IsPickable ? 0 : 1)
                                .ThenBy(l => l.Id)
                                .Select(l => (int?)l.Id)
                                .FirstOrDefaultAsync(cancellationToken);
                            restockLocId ??= preferredStorageLocId ?? quarantineStock.LocationId;
                        }

                        line.RestockLocationId = restockLocId;
                        line.TargetLocationId = restockLocId;

                        var availableStock = await db.InventoryStocks.FirstOrDefaultAsync(
                            s => s.WarehouseId == customerReturn.WarehouseId &&
                                 s.ProductId == line.ProductId &&
                                 s.LocationId == restockLocId &&
                                 s.LotId == line.LotId &&
                                 s.SerialId == line.SerialId &&
                                 s.Status == ERP.Domain.Enums.InventoryStatus.Available,
                            cancellationToken);

                        if (availableStock == null)
                        {
                            availableStock = new ERP.Domain.Entities.InventoryStock
                            {
                                WarehouseId = customerReturn.WarehouseId,
                                ProductId = line.ProductId,
                                LocationId = restockLocId,
                                LotId = line.LotId,
                                SerialId = line.SerialId,
                                Status = ERP.Domain.Enums.InventoryStatus.Available,
                                Quantity = line.Quantity,
                                ReservedQuantity = 0m,
                                MovingAverageCost = line.UnitCost,
                                LastUpdated = now
                            };
                            db.InventoryStocks.Add(availableStock);
                        }
                        else
                        {
                            availableStock.Quantity += line.Quantity;
                            availableStock.LastUpdated = now;
                        }

                        var txOut = new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = line.ProductId,
                            WarehouseId = customerReturn.WarehouseId,
                            LocationId = quarantineStock.LocationId,
                            FromLocationId = quarantineStock.LocationId,
                            ToLocationId = restockLocId,
                            LotId = line.LotId,
                            SerialId = line.SerialId,
                            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                            TransactionType = ERP.Domain.Enums.TransactionType.StatusChange,
                            Quantity = -line.Quantity,
                            UnitCost = line.UnitCost,
                            ReferenceId = customerReturn.Id,
                            ReferenceType = "CustomerReturn",
                            TransactionDate = now,
                            CreatedBy = effectiveActorId,
                            Note = $"{customerReturn.ReturnCode}: Xuất khỏi khu biệt trữ chuyển về tồn khả dụng ({disp})"
                        };
                        var txIn = new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = line.ProductId,
                            WarehouseId = customerReturn.WarehouseId,
                            LocationId = restockLocId,
                            FromLocationId = quarantineStock.LocationId,
                            ToLocationId = restockLocId,
                            LotId = line.LotId,
                            SerialId = line.SerialId,
                            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                            FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                            TransactionType = ERP.Domain.Enums.TransactionType.StatusChange,
                            Quantity = line.Quantity,
                            UnitCost = line.UnitCost,
                            ReferenceId = customerReturn.Id,
                            ReferenceType = "CustomerReturn",
                            TransactionDate = now,
                            CreatedBy = effectiveActorId,
                            Note = $"{customerReturn.ReturnCode}: Nhập lại kệ hàng khả dụng ({disp})"
                        };
                        db.InventoryTransactions.Add(txOut);
                        db.InventoryTransactions.Add(txIn);
                        await db.SaveChangesAsync(cancellationToken);
                        line.PostedLedgerTransactionId = txIn.Id;
                    }
                    else
                    {
                        var writeOffTx = new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = line.ProductId,
                            WarehouseId = customerReturn.WarehouseId,
                            LocationId = quarantineStock.LocationId,
                            FromLocationId = quarantineStock.LocationId,
                            LotId = line.LotId,
                            SerialId = line.SerialId,
                            InventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            FromInventoryStatus = ERP.Domain.Enums.InventoryStatus.Quarantine,
                            TransactionType = ERP.Domain.Enums.TransactionType.AdjustmentDecrease,
                            Quantity = line.Quantity,
                            UnitCost = line.UnitCost,
                            ReferenceId = customerReturn.Id,
                            ReferenceType = disp == "RTV_VENDOR" ? "QualityDispositionRtv" : "QualityDispositionScrap",
                            TransactionDate = now,
                            CreatedBy = effectiveActorId,
                            Note = $"{customerReturn.ReturnCode}: Xuất toán khỏi khu biệt trữ ({disp})"
                        };
                        db.InventoryTransactions.Add(writeOffTx);
                        await db.SaveChangesAsync(cancellationToken);
                        line.PostedLedgerTransactionId = writeOffTx.Id;
                    }
                }

                customerReturn.Status = "COMPLETED";
                customerReturn.ApprovedBy = effectiveActorId;
                customerReturn.ApprovedAtUtc = now;
                customerReturn.PostedAtUtc = now;
                customerReturn.CompletedAtUtc = now;
                if (!db.Database.IsSqlServer())
                    customerReturn.RowVersion = Guid.NewGuid().ToByteArray()[..8];

                db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = effectiveActorId,
                    WarehouseId = customerReturn.WarehouseId,
                    EntityName = "CustomerReturn",
                    EntityId = customerReturn.Id,
                    Action = "CustomerReturn.Disposed",
                    OldValues = "INSPECTED",
                    NewValues = $"Status: COMPLETED; Lines: {customerReturn.Lines.Count}",
                    Reason = request?.Note ?? request?.ReasonCode ?? "Hoàn tất định đoạt & ghi sổ hàng hoàn trả",
                    Result = "Success",
                    Timestamp = now
                });

                await db.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);

                return await GetCustomerReturnByIdAsync(customerReturn.Id, canReadCost, effectiveAuth, cancellationToken);
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<WmsEndpointOutcome> GetPipelineSummaryAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        if (db == null)
        {
            return WmsEndpointOutcome.Ok(new
            {
                totalReceipts = 0,
                draftOrReceivingCount = 0,
                qcOrApprovalPendingCount = 0,
                postedCount = 0,
                cancelledCount = 0,
                status = "Đã tải tổng quan luồng phiếu nhập kho",
                statusLabel = "Đã tải tổng quan luồng phiếu nhập kho"
            });
        }

        var query = db.ImportReceipts.AsNoTracking();
        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        else if (accessibleIds is { Count: > 0 })
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));

        var grouped = await query
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var totalReceipts = grouped.Sum(x => x.Count);
        var draftOrReceivingCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Draft
                or ERP.Domain.Enums.ReceiptStatus.Received
                or ERP.Domain.Enums.ReceiptStatus.Dispatched
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyPending
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancySubmitted
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyPendingApproval
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyResolved
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyRejected)
            .Sum(x => x.Count);
        var qcOrApprovalPendingCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.QcPending
                or ERP.Domain.Enums.ReceiptStatus.QcCompleted
                or ERP.Domain.Enums.ReceiptStatus.ReadyToPost)
            .Sum(x => x.Count);
        var postedCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Posted
                or ERP.Domain.Enums.ReceiptStatus.Approved)
            .Sum(x => x.Count);
        var cancelledCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Cancelled
                or ERP.Domain.Enums.ReceiptStatus.CancelPendingPutback)
            .Sum(x => x.Count);

        return WmsEndpointOutcome.Ok(new
        {
            totalReceipts,
            draftOrReceivingCount,
            qcOrApprovalPendingCount,
            postedCount,
            cancelledCount,
            status = "Đã tải tổng quan luồng phiếu nhập kho",
            statusLabel = "Đã tải tổng quan luồng phiếu nhập kho"
        });
    }

    public async Task<WmsEndpointOutcome> GetQcAndRmaSummaryAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        if (db == null)
        {
            return WmsEndpointOutcome.Ok(new
            {
                totalReceiptsInspected = 0,
                pendingQcOrPostingCount = 0,
                postedWithLotOrQcCount = 0,
                rmaEligibleReceiptsCount = 0,
                qcWorkCenterModes = new[] { "Kiểm định chất lượng đầu vào", "Kiểm định hàng trả về", "Rà soát hàng biệt trữ & hư hỏng" },
                status = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về",
                statusLabel = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về"
            });
        }

        var query = db.ImportReceipts.AsNoTracking();
        if (warehouseId.HasValue)
            query = query.Where(r => r.WarehouseId == warehouseId.Value);
        else if (accessibleIds is { Count: > 0 })
            query = query.Where(r => accessibleIds.Contains(r.WarehouseId));

        var grouped = await query
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var rmaEligibleReceiptsCount = await query.CountAsync(
            r => (r.Note != null && (r.Note.Contains("RMA") || r.Note.Contains("RETURN") || r.Note.Contains("trả") || r.Note.Contains("Trả"))) ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancyPending ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancySubmitted ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancyPendingApproval ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancyResolved ||
                 r.Status == ERP.Domain.Enums.ReceiptStatus.DiscrepancyRejected,
            cancellationToken);

        var totalReceiptsInspected = grouped.Sum(x => x.Count);
        var pendingQcOrPostingCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Draft
                or ERP.Domain.Enums.ReceiptStatus.Approved
                or ERP.Domain.Enums.ReceiptStatus.Received
                or ERP.Domain.Enums.ReceiptStatus.Dispatched
                or ERP.Domain.Enums.ReceiptStatus.QcPending
                or ERP.Domain.Enums.ReceiptStatus.QcCompleted
                or ERP.Domain.Enums.ReceiptStatus.ReadyToPost
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyPending
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancySubmitted
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyPendingApproval
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyResolved
                or ERP.Domain.Enums.ReceiptStatus.DiscrepancyRejected)
            .Sum(x => x.Count);
        var postedWithLotOrQcCount = grouped
            .Where(x => x.Status is ERP.Domain.Enums.ReceiptStatus.Posted
                or ERP.Domain.Enums.ReceiptStatus.Approved)
            .Sum(x => x.Count);

        return WmsEndpointOutcome.Ok(new
        {
            totalReceiptsInspected,
            pendingQcOrPostingCount,
            postedWithLotOrQcCount,
            rmaEligibleReceiptsCount,
            qcWorkCenterModes = new[] { "Kiểm định chất lượng đầu vào", "Kiểm định hàng trả về", "Rà soát hàng biệt trữ & hư hỏng" },
            status = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về",
            statusLabel = "Đã tải tổng quan trạm kiểm định chất lượng & tiếp nhận hàng trả về"
        });
    }

    public async Task<WmsEndpointOutcome> ExpressPostAsync(
        int id,
        ExpressPostImportReceiptInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "RECEIPT_NOT_FOUND",
                errorCode = "RECEIPT_NOT_FOUND",
                message = $"Không tìm thấy phiếu nhập kho #{id}."
            });
        }

        var receipt = await db.ImportReceipts
            .Include(r => r.Details)
                .ThenInclude(d => d.Product)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

        if (receipt == null)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "RECEIPT_NOT_FOUND",
                errorCode = "RECEIPT_NOT_FOUND",
                message = $"Không tìm thấy phiếu nhập kho #{id}."
            });
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        if (effectiveAuth != null)
            await effectiveAuth.EnsureWarehouseAccessAsync(receipt.WarehouseId, cancellationToken);

        if (receipt.Status == ERP.Domain.Enums.ReceiptStatus.Posted)
        {
            return WmsEndpointOutcome.Conflict(new
            {
                code = "RECEIPT_ALREADY_POSTED",
                errorCode = "RECEIPT_ALREADY_POSTED",
                message = "Phiếu nhập kho đã được ghi sổ tồn kho (Posted) trước đó."
            });
        }

        if (receipt.Status == ERP.Domain.Enums.ReceiptStatus.Cancelled)
        {
            return WmsEndpointOutcome.Conflict(new
            {
                code = "RECEIPT_CANCELLED",
                errorCode = "RECEIPT_CANCELLED",
                message = "Phiếu nhập kho đã bị hủy, không thể thực hiện nhập nhanh 1 bước."
            });
        }

        if (receipt.Details.Any(d => d.RequiresQc && d.QcState != ERP.Domain.Enums.ReceiptLineQcState.QcCompleted))
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "EXPRESS_POST_REQUIRES_QC_COMPLETION",
                errorCode = "EXPRESS_POST_REQUIRES_QC_COMPLETION",
                message = "Phiếu nhập có mặt hàng bắt buộc kiểm định QC; không thể nhập nhanh 1 bước khi chưa hoàn tất QC."
            });
        }

        var targetLocationId = request?.TargetLocationId;
        if (!targetLocationId.HasValue && !string.IsNullOrWhiteSpace(request?.TargetLocationCode))
        {
            var code = request.TargetLocationCode.Trim();
            targetLocationId = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == receipt.WarehouseId && l.Code == code)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        targetLocationId ??= await db.WarehouseLocations.AsNoTracking()
            .Where(l => l.WarehouseId == receipt.WarehouseId && l.IsActive && !l.IsBlocked)
            .OrderBy(l => l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Storage ? 0 : (l.LocationType == ERP.Domain.Enums.WarehouseLocationType.Receiving ? 1 : 2))
            .ThenBy(l => l.Id)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : receipt.CreatedBy);
        int? validAuditUserId = null;
        if (effectiveActorId > 0)
        {
            if (await db.Users.AsNoTracking().AnyAsync(u => u.Id == effectiveActorId, cancellationToken) ||
                (!db.Database.IsRelational() && !await db.Users.AsNoTracking().AnyAsync(cancellationToken)))
            {
                validAuditUserId = effectiveActorId;
            }
        }

        var tenantId = db.CurrentTenantId > 0 ? db.CurrentTenantId : 1;
        var gate = _dispositionLocks.GetOrAdd((tenantId, -id), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var tx = db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                await db.AcquireInventoryAllocationLocksAsync(
                    receipt.WarehouseId,
                    receipt.Details.Select(d => d.ProductId),
                    cancellationToken);

                var totalBasePostedQuantity = 0m;
                foreach (var detail in receipt.Details.OrderBy(d => d.ProductId).ThenBy(d => d.Id))
                {
                    var opQty = detail.AcceptedQuantity > 0m
                        ? detail.AcceptedQuantity
                        : (detail.ReceivedQuantity > 0m
                            ? detail.ReceivedQuantity
                            : (detail.ExpectedQuantity > 0m ? detail.ExpectedQuantity : detail.Quantity));
                    var conv = detail.ConversionFactor > 0m ? detail.ConversionFactor : 1m;
                    var baseQty = detail.BaseAcceptedQuantity > 0m
                        ? detail.BaseAcceptedQuantity
                        : (detail.BaseReceivedQuantity > 0m
                            ? detail.BaseReceivedQuantity
                            : (detail.BaseExpectedQuantity > 0m
                                ? detail.BaseExpectedQuantity
                                : Math.Round(opQty * conv, 4, MidpointRounding.AwayFromZero)));

                    if (detail.ExpectedQuantity <= 0m) detail.ExpectedQuantity = opQty;
                    if (detail.BaseExpectedQuantity <= 0m) detail.BaseExpectedQuantity = baseQty;
                    if (detail.ReceivedQuantity <= 0m) detail.ReceivedQuantity = opQty;
                    if (detail.AcceptedQuantity <= 0m) detail.AcceptedQuantity = opQty;
                    detail.PostedQuantity = opQty;
                    detail.FinalReceivedQuantity = opQty;
                    if (detail.BaseReceivedQuantity <= 0m) detail.BaseReceivedQuantity = baseQty;
                    if (detail.BaseAcceptedQuantity <= 0m) detail.BaseAcceptedQuantity = baseQty;
                    detail.BasePostedQuantity = baseQty;
                    detail.BaseFinalReceivedQuantity = baseQty;

                    totalBasePostedQuantity += baseQty;

                    var unitCost = detail.UnitPrice > 0m
                        ? (conv > 0m ? Math.Round(detail.UnitPrice / conv, 4, MidpointRounding.AwayFromZero) : detail.UnitPrice)
                        : (detail.Product?.MovingAverageCost ?? 0m);

                    var stock = await db.InventoryStocks.FirstOrDefaultAsync(
                        s => s.WarehouseId == receipt.WarehouseId &&
                             s.ProductId == detail.ProductId &&
                             s.LocationId == targetLocationId &&
                             s.LotId == null &&
                             s.SerialId == null &&
                             s.HandlingUnitId == null &&
                             s.OwnerId == null &&
                             s.Status == ERP.Domain.Enums.InventoryStatus.Available,
                        cancellationToken);

                    if (stock == null)
                    {
                        stock = new ERP.Domain.Entities.InventoryStock
                        {
                            TenantId = receipt.TenantId,
                            WarehouseId = receipt.WarehouseId,
                            ProductId = detail.ProductId,
                            LocationId = targetLocationId,
                            Status = ERP.Domain.Enums.InventoryStatus.Available,
                            Quantity = baseQty,
                            ReservedQuantity = 0m,
                            MovingAverageCost = unitCost,
                            LastUpdated = now
                        };
                        stock.RefreshGrainHash();
                        db.InventoryStocks.Add(stock);
                    }
                    else
                    {
                        var oldQty = Math.Max(0m, stock.Quantity);
                        var newTotalQty = oldQty + baseQty;
                        if (newTotalQty > 0m && unitCost > 0m)
                        {
                            stock.MovingAverageCost = Math.Round(
                                ((oldQty * stock.MovingAverageCost) + (baseQty * unitCost)) / newTotalQty,
                                4,
                                MidpointRounding.AwayFromZero);
                        }

                        stock.Quantity += baseQty;
                        stock.LastUpdated = now;
                        stock.RefreshGrainHash();
                    }

                    if (detail.Product != null && stock.MovingAverageCost > 0m)
                    {
                        detail.Product.MovingAverageCost = stock.MovingAverageCost;
                    }

                    db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                    {
                        TenantId = receipt.TenantId,
                        ProductId = detail.ProductId,
                        WarehouseId = receipt.WarehouseId,
                        LocationId = targetLocationId,
                        ToLocationId = targetLocationId,
                        InventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                        ToInventoryStatus = ERP.Domain.Enums.InventoryStatus.Available,
                        TransactionType = ERP.Domain.Enums.TransactionType.Import,
                        Quantity = baseQty,
                        UnitCost = unitCost,
                        ReferenceId = receipt.Id,
                        ReferenceType = "ImportReceipt",
                        SourceDocumentLineId = detail.Id,
                        TransactionDate = now,
                        CreatedBy = effectiveActorId > 0 ? effectiveActorId : 1,
                        Note = string.IsNullOrWhiteSpace(request?.Note)
                            ? $"{receipt.Code}: Nhập kho nhanh 1 bước (Express Inbound)"
                            : request.Note.Trim()
                    });
                }

                receipt.Status = ERP.Domain.Enums.ReceiptStatus.Posted;
                receipt.ApprovedBy ??= effectiveActorId > 0 ? effectiveActorId : 1;
                receipt.ApprovedAt ??= now;
                if (!db.Database.IsSqlServer())
                {
                    receipt.RowVersion = Guid.NewGuid().ToByteArray()[..8];
                }

                db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = validAuditUserId,
                    WarehouseId = receipt.WarehouseId,
                    EntityName = "ImportReceipt",
                    EntityId = receipt.Id,
                    Action = "ImportReceipt.ExpressPosted",
                    NewValues = $"Status: Posted; ExpressMode: true; TargetLocationId: {targetLocationId}; TotalBaseQty: {totalBasePostedQuantity}",
                    Reason = string.IsNullOrWhiteSpace(request?.Note)
                        ? $"Nhập kho nhanh 1 bước cho phiếu nhập {receipt.Code}"
                        : request.Note.Trim(),
                    Result = "Success",
                    Timestamp = now
                });

                await db.SaveChangesAsync(cancellationToken);
                if (tx != null) await tx.CommitAsync(cancellationToken);

                return WmsEndpointOutcome.Ok(new
                {
                    id = receipt.Id,
                    code = receipt.Code,
                    status = "Posted",
                    expressMode = true,
                    targetLocationId,
                    postedLineCount = receipt.Details.Count,
                    totalBasePostedQuantity,
                    message = "Đã nhập kho nhanh 1 bước và ghi sổ tồn kho thành công."
                });
            }
            catch
            {
                if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
