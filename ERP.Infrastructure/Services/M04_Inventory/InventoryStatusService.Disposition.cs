using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryStatusService
{
    public async Task<InventoryDispositionResultDto> ExecuteDispositionAsync(
        CreateInventoryDispositionDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.InventoryStockId <= 0 || request.Quantity <= 0)
            throw new BusinessRuleException("InventoryStockId và Quantity phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do xử lý (disposition) là bắt buộc.");

        var normalizedAction = request.Action?.Trim().Replace("_", "", StringComparison.Ordinal).ToUpperInvariant();
        var isRelease = normalizedAction is "RELEASETOAVAILABLE" or "RELEASE";
        var isScrap = normalizedAction is "SCRAP";
        if (!isRelease && !isScrap)
            throw new BusinessRuleException("Hành động disposition không hợp lệ (chỉ hỗ trợ ReleaseToAvailable hoặc Scrap).");

        var scope = await context.InventoryStocks.AsNoTracking()
            .Where(x => x.Id == request.InventoryStockId)
            .Select(x => new { x.ProductId, x.WarehouseId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy inventory bucket.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(scope.WarehouseId, cancellationToken);

        var isSqlServer = context.Database.IsRelational() && context.Database.IsSqlServer();
        var ownTransaction = context.Database.CurrentTransaction is null;
        var maxAttempts = ownTransaction && isSqlServer ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = ownTransaction
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;
            try
            {
                await AcquireProductWarehouseLockAndStockLocksAsync(scope.ProductId, scope.WarehouseId, cancellationToken);

                var source = await context.InventoryStocks
                    .Include(x => x.StatusDefinition)
                    .Include(x => x.Lot)
                    .Include(x => x.Location)
                    .SingleOrDefaultAsync(x => x.Id == request.InventoryStockId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy inventory bucket.");

                if (source.Status == InventoryStatus.Available)
                    throw Conflict("INV_DISPOSITION_NOT_ALLOWED", "Disposition chỉ áp dụng cho nhóm tồn không ở trạng thái AVAILABLE.");
                if (source.Quantity - Math.Max(source.ReservedQuantity, source.AllocatedQuantity) - source.LockedQuantity < request.Quantity)
                    throw Conflict("INV_INSUFFICIENT_ON_HAND", "Không đủ tồn khả dụng chưa giữ để xử lý disposition.");

                if (isScrap)
                {
                    var scrapValuation = request.Quantity * source.MovingAverageCost;
                    if (scrapValuation > 500_000m || request.Reason.Contains("[SOD]", StringComparison.OrdinalIgnoreCase))
                    {
                        var lastQualityHoldCreatorId = await context.InventoryTransactions.AsNoTracking()
                            .Where(x => x.WarehouseId == source.WarehouseId &&
                                        x.ProductId == source.ProductId &&
                                        x.LocationId == source.LocationId &&
                                        x.ToInventoryStatus == source.Status &&
                                        x.TransactionType == TransactionType.StatusChange)
                            .OrderByDescending(x => x.Id)
                            .Select(x => (int?)x.CreatedBy)
                            .FirstOrDefaultAsync(cancellationToken);

                        if (lastQualityHoldCreatorId.HasValue && lastQualityHoldCreatorId.Value == currentUser.UserId)
                        {
                            throw Conflict(
                                "SOD_MAKER_CHECKER_VIOLATION",
                                "Bắt buộc phê duyệt độc lập theo nguyên tắc bất kiêm nhiệm khi hủy/thanh lý.");
                        }
                    }
                }

                var targetLocationId = source.LocationId;
                if (isRelease)
                {
                    var needsPickableRelocation = source.Location is null ||
                        !source.Location.IsPickable ||
                        !source.Location.IsActive ||
                        source.Location.IsBlocked ||
                        source.Location.LocationType is WarehouseLocationType.Damaged or WarehouseLocationType.Rejected;
                    if (needsPickableRelocation)
                    {
                        var pickableLocationId = await context.WarehouseLocations
                            .Where(x => x.WarehouseId == source.WarehouseId &&
                                        x.IsActive &&
                                        !x.IsBlocked &&
                                        x.IsPickable &&
                                        x.LocationType != WarehouseLocationType.Damaged &&
                                        x.LocationType != WarehouseLocationType.Rejected)
                            .OrderBy(x => x.LocationType == WarehouseLocationType.Storage ? 0 : x.Code == "LEGACY" ? 1 : 2)
                            .ThenBy(x => x.Id)
                            .Select(x => (int?)x.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                        if (!pickableLocationId.HasValue)
                            throw Conflict("INV_DISPOSITION_NOT_ALLOWED", "Kho chưa có vị trí lưu trữ khả dụng (IsPickable) để giải tỏa tồn kho về AVAILABLE.");
                        targetLocationId = pickableLocationId.Value;
                    }
                }

                if (inventoryLocks is not null)
                {
                    await inventoryLocks.EnsureBucketUnlockedAsync(
                        source.WarehouseId, source.LocationId, source.ProductId, source.Status, source.LotId, source.SerialId, cancellationToken);
                    if (isRelease)
                    {
                        await inventoryLocks.EnsureBucketUnlockedAsync(
                            source.WarehouseId, targetLocationId, source.ProductId, InventoryStatus.Available, source.LotId, source.SerialId, cancellationToken);
                    }
                }

                var fromStatus = source.Status;
                int? destinationStockId = null;
                string? toStatusCode = null;
                InventoryTransaction ledger;

                if (isRelease)
                {
                    if (fromStatus is InventoryStatus.Expired or InventoryStatus.RecallBlocked)
                        throw Conflict("INV_DISPOSITION_NOT_ALLOWED", $"Không thể phát hành (ReleaseToAvailable) nhóm tồn đang ở trạng thái {fromStatus}.");
                    if (source.Lot?.ExpiryDate != null && source.Lot.ExpiryDate.Value.Date < DateTime.UtcNow.Date)
                        throw Conflict("INV_DISPOSITION_EXPIRED_LOT", "Không thể phát hành lô đã hết hạn về trạng thái AVAILABLE.");

                    var destination = await context.InventoryStocks.SingleOrDefaultAsync(
                        x => x.ProductId == source.ProductId &&
                             x.WarehouseId == source.WarehouseId &&
                             x.LocationId == targetLocationId &&
                             x.Status == InventoryStatus.Available &&
                             x.LotId == source.LotId &&
                             x.SerialId == source.SerialId,
                        cancellationToken);

                    var nowUtc = DateTime.UtcNow;
                    source.Quantity -= request.Quantity;
                    source.LastUpdated = nowUtc;
                    if (destination is null)
                    {
                        destination = new InventoryStock
                        {
                            ProductId = source.ProductId,
                            WarehouseId = source.WarehouseId,
                            LocationId = targetLocationId,
                            LotId = source.LotId,
                            SerialId = source.SerialId,
                            HandlingUnitId = source.HandlingUnitId,
                            OwnerId = source.OwnerId,
                            Status = InventoryStatus.Available,
                            Quantity = request.Quantity,
                            ReservedQuantity = 0,
                            MovingAverageCost = source.MovingAverageCost,
                            LastUpdated = nowUtc
                        };
                        context.InventoryStocks.Add(destination);
                    }
                    else
                    {
                        destination.Quantity += request.Quantity;
                        destination.LastUpdated = nowUtc;
                    }

                    var ledgerOut = new InventoryTransaction
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = source.LocationId,
                        FromLocationId = source.LocationId,
                        ToLocationId = targetLocationId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        HandlingUnitId = source.HandlingUnitId,
                        OwnerId = source.OwnerId,
                        InventoryStatus = fromStatus,
                        FromInventoryStatus = fromStatus,
                        ToInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.StatusChange,
                        Quantity = -request.Quantity,
                        UnitCost = source.MovingAverageCost,
                        ReferenceId = source.Id,
                        ReferenceType = "QualityDispositionRelease",
                        TransactionDate = nowUtc,
                        CreatedBy = currentUser.UserId,
                        Note = request.Reason.Trim()
                    };
                    ledger = new InventoryTransaction
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = targetLocationId,
                        FromLocationId = source.LocationId,
                        ToLocationId = targetLocationId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        HandlingUnitId = source.HandlingUnitId,
                        OwnerId = source.OwnerId,
                        InventoryStatus = InventoryStatus.Available,
                        FromInventoryStatus = fromStatus,
                        ToInventoryStatus = InventoryStatus.Available,
                        TransactionType = TransactionType.StatusChange,
                        Quantity = request.Quantity,
                        UnitCost = source.MovingAverageCost,
                        ReferenceId = source.Id,
                        ReferenceType = "QualityDispositionRelease",
                        TransactionDate = nowUtc,
                        CreatedBy = currentUser.UserId,
                        Note = request.Reason.Trim()
                    };
                    context.InventoryTransactions.AddRange(ledgerOut, ledger);
                    context.AuditLogs.Add(new AuditLog
                    {
                        UserId = currentUser.UserId,
                        Action = "Inventory.DispositionReleased",
                        EntityName = "InventoryStock",
                        EntityId = source.Id,
                        WarehouseId = source.WarehouseId,
                        Timestamp = nowUtc,
                        OldValues = $"Status: {fromStatus}; LocationId: {source.LocationId}; Quantity: {source.Quantity + request.Quantity}",
                        NewValues = $"Status: {InventoryStatus.Available}; LocationId: {targetLocationId}; ReleasedQuantity: {request.Quantity}; LotId: {source.LotId}; SerialId: {source.SerialId}",
                        Reason = request.Reason.Trim(),
                        Result = "Success",
                        Severity = "Information"
                    });

                    await context.SaveChangesAsync(cancellationToken);
                    destinationStockId = destination.Id;
                    toStatusCode = ToCode(InventoryStatus.Available);
                }
                else
                {
                    source.Quantity -= request.Quantity;
                    source.LastUpdated = DateTime.UtcNow;

                    ledger = new InventoryTransaction
                    {
                        ProductId = source.ProductId,
                        WarehouseId = source.WarehouseId,
                        LocationId = source.LocationId,
                        LotId = source.LotId,
                        SerialId = source.SerialId,
                        HandlingUnitId = source.HandlingUnitId,
                        OwnerId = source.OwnerId,
                        InventoryStatus = fromStatus,
                        FromInventoryStatus = fromStatus,
                        ToInventoryStatus = null,
                        TransactionType = TransactionType.AdjustmentDecrease,
                        Quantity = request.Quantity,
                        UnitCost = source.MovingAverageCost,
                        ReferenceId = source.Id,
                        ReferenceType = "QualityDispositionScrap",
                        TransactionDate = DateTime.UtcNow,
                        CreatedBy = currentUser.UserId,
                        Note = request.Reason.Trim()
                    };
                    context.InventoryTransactions.Add(ledger);
                    context.AuditLogs.Add(new AuditLog
                    {
                        UserId = currentUser.UserId,
                        Action = "Inventory.DispositionScrapped",
                        EntityName = "InventoryStock",
                        EntityId = source.Id,
                        WarehouseId = source.WarehouseId,
                        Timestamp = DateTime.UtcNow,
                        OldValues = $"Status: {fromStatus}; Quantity: {source.Quantity + request.Quantity}",
                        NewValues = $"ScrappedQuantity: {request.Quantity}; RemainingQuantity: {source.Quantity}; LotId: {source.LotId}; SerialId: {source.SerialId}",
                        Reason = request.Reason.Trim(),
                        Result = "Success",
                        Severity = "Information"
                    });

                    await context.SaveChangesAsync(cancellationToken);
                }

                var result = new InventoryDispositionResultDto
                {
                    SourceStockId = source.Id,
                    DestinationStockId = destinationStockId,
                    TransactionId = ledger.Id,
                    Action = isRelease ? "ReleaseToAvailable" : "Scrap",
                    FromStatus = ToCode(fromStatus),
                    ToStatus = toStatusCode,
                    Quantity = request.Quantity,
                    RemainingSourceQuantity = source.Quantity,
                    ProductId = source.ProductId,
                    WarehouseId = source.WarehouseId,
                    LocationId = (isRelease ? targetLocationId : source.LocationId) ?? 0,
                    LotId = source.LotId,
                    SerialId = source.SerialId
                };
                if (tx is not null) await tx.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex) when (ownTransaction && attempt < maxAttempts &&
                                       (ex is DeadlockException or SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } }))
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw Conflict("CONCURRENCY_CONFLICT", "Inventory bucket đích đã thay đổi đồng thời.");
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
