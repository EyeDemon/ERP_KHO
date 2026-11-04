using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ImportReceiptOperationsService(
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
}
