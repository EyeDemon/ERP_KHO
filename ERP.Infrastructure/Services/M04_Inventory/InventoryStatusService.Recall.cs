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
    public async Task<InventoryRecallBlockResultDto> ExecuteRecallBlockAsync(
        CreateInventoryRecallBlockDto request,
        CancellationToken cancellationToken = default)
    {
        if (request.WarehouseId <= 0 || request.ProductId <= 0)
            throw new BusinessRuleException("WarehouseId và ProductId phải hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.LotNumber))
            throw new BusinessRuleException("LotNumber là bắt buộc khi kích hoạt thu hồi lô.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do thu hồi lô là bắt buộc.");

        await warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);
        var normalizedLot = request.LotNumber.Trim();
        var cleanReason = request.Reason.Trim();

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
                await AcquireProductWarehouseLockAndStockLocksAsync(request.ProductId, request.WarehouseId, cancellationToken);

                var lot = await context.InventoryLots
                    .FirstOrDefaultAsync(x => x.ProductId == request.ProductId && x.LotNumber == normalizedLot, cancellationToken)
                    ?? throw new NotFoundException($"Không tìm thấy lô {normalizedLot} của sản phẩm #{request.ProductId}.");

                var sources = await context.InventoryStocks
                    .Where(x => x.WarehouseId == request.WarehouseId &&
                                x.ProductId == request.ProductId &&
                                x.LotId == lot.Id &&
                                x.Status != InventoryStatus.RecallBlocked &&
                                x.Quantity - x.ReservedQuantity > 0)
                    .OrderBy(x => x.Id)
                    .ToListAsync(cancellationToken);

                decimal totalBlocked = 0m;
                var affectedCount = 0;

                foreach (var source in sources)
                {
                    var moveQty = source.Quantity - source.ReservedQuantity;
                    if (moveQty <= 0) continue;

                    var fromStatus = source.Status;
                    var destination = context.InventoryStocks.Local.FirstOrDefault(
                                          x => x.ProductId == source.ProductId &&
                                               x.WarehouseId == source.WarehouseId &&
                                               x.LocationId == source.LocationId &&
                                               x.Status == InventoryStatus.RecallBlocked &&
                                               x.LotId == source.LotId &&
                                               x.SerialId == source.SerialId)
                                      ?? await context.InventoryStocks.SingleOrDefaultAsync(
                                          x => x.ProductId == source.ProductId &&
                                               x.WarehouseId == source.WarehouseId &&
                                               x.LocationId == source.LocationId &&
                                               x.Status == InventoryStatus.RecallBlocked &&
                                               x.LotId == source.LotId &&
                                               x.SerialId == source.SerialId,
                                          cancellationToken);

                    var nowUtc = DateTime.UtcNow;
                    source.Quantity -= moveQty;
                    source.LastUpdated = nowUtc;

                    if (destination is null)
                    {
                        destination = new InventoryStock
                        {
                            ProductId = source.ProductId,
                            WarehouseId = source.WarehouseId,
                            LocationId = source.LocationId,
                            LotId = source.LotId,
                            SerialId = source.SerialId,
                            HandlingUnitId = source.HandlingUnitId,
                            OwnerId = source.OwnerId,
                            Status = InventoryStatus.RecallBlocked,
                            Quantity = moveQty,
                            ReservedQuantity = 0,
                            MovingAverageCost = source.MovingAverageCost,
                            LastUpdated = nowUtc
                        };
                        context.InventoryStocks.Add(destination);
                    }
                    else
                    {
                        destination.Quantity += moveQty;
                        destination.LastUpdated = nowUtc;
                    }

                    context.InventoryTransactions.AddRange(
                        new InventoryTransaction
                        {
                            ProductId = source.ProductId,
                            WarehouseId = source.WarehouseId,
                            LocationId = source.LocationId,
                            FromLocationId = source.LocationId,
                            ToLocationId = source.LocationId,
                            LotId = source.LotId,
                            SerialId = source.SerialId,
                            HandlingUnitId = source.HandlingUnitId,
                            OwnerId = source.OwnerId,
                            InventoryStatus = fromStatus,
                            FromInventoryStatus = fromStatus,
                            ToInventoryStatus = InventoryStatus.RecallBlocked,
                            TransactionType = TransactionType.StatusChange,
                            Quantity = -moveQty,
                            UnitCost = source.MovingAverageCost,
                            ReferenceId = source.Id,
                            ReferenceType = "RecallCampaignBlock",
                            TransactionDate = nowUtc,
                            CreatedBy = currentUser.UserId,
                            Note = cleanReason
                        },
                        new InventoryTransaction
                        {
                            ProductId = source.ProductId,
                            WarehouseId = source.WarehouseId,
                            LocationId = source.LocationId,
                            FromLocationId = source.LocationId,
                            ToLocationId = source.LocationId,
                            LotId = source.LotId,
                            SerialId = source.SerialId,
                            HandlingUnitId = source.HandlingUnitId,
                            OwnerId = source.OwnerId,
                            InventoryStatus = InventoryStatus.RecallBlocked,
                            FromInventoryStatus = fromStatus,
                            ToInventoryStatus = InventoryStatus.RecallBlocked,
                            TransactionType = TransactionType.StatusChange,
                            Quantity = moveQty,
                            UnitCost = source.MovingAverageCost,
                            ReferenceId = source.Id,
                            ReferenceType = "RecallCampaignBlock",
                            TransactionDate = nowUtc,
                            CreatedBy = currentUser.UserId,
                            Note = cleanReason
                        });

                    totalBlocked += moveQty;
                    affectedCount++;
                }

                var recallLock = new InventoryLock
                {
                    LockType = InventoryLockType.InvestigationHold,
                    Status = InventoryLockStatus.Active,
                    WarehouseId = request.WarehouseId,
                    ProductId = request.ProductId,
                    LotId = lot.Id,
                    InventoryStatus = InventoryStatus.RecallBlocked,
                    Reason = cleanReason,
                    CreatedBy = currentUser.UserId,
                    CreatedAt = DateTime.UtcNow
                };
                context.InventoryLocks.Add(recallLock);
                await context.SaveChangesAsync(cancellationToken);

                context.AuditLogs.Add(new AuditLog
                {
                    UserId = currentUser.UserId,
                    Action = "Inventory.RecallCampaignBlocked",
                    EntityName = "InventoryLot",
                    EntityId = lot.Id,
                    WarehouseId = request.WarehouseId,
                    Timestamp = DateTime.UtcNow,
                    NewValues = $"LotNumber: {lot.LotNumber}; AffectedBuckets: {affectedCount}; BlockedQuantity: {totalBlocked}; LockId: {recallLock.Id}",
                    Reason = cleanReason,
                    Result = "Success",
                    Severity = "Warning"
                });
                await context.SaveChangesAsync(cancellationToken);

                if (tx is not null) await tx.CommitAsync(cancellationToken);

                return new InventoryRecallBlockResultDto
                {
                    WarehouseId = request.WarehouseId,
                    ProductId = request.ProductId,
                    LotId = lot.Id,
                    LotNumber = lot.LotNumber.Trim(),
                    AffectedBucketCount = affectedCount,
                    BlockedQuantity = totalBlocked,
                    InventoryLockId = recallLock.Id,
                    Reason = cleanReason
                };
            }
            catch (Exception ex) when (ownTransaction && attempt < maxAttempts &&
                                       (ex is DeadlockException or SqlException { Number: 1205 } ||
                                        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } }))
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch
            {
                if (tx is not null) await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private Task AcquireProductWarehouseLockAndStockLocksAsync(
        int productId,
        int warehouseId,
        CancellationToken cancellationToken) =>
        context.AcquireInventoryAllocationLocksAsync([(warehouseId, productId)], cancellationToken);

    private static void ValidateTransition(InventoryStatus fromStatus, InventoryStatus toStatus)
    {
        if (fromStatus is InventoryStatus.Expired or InventoryStatus.RecallBlocked)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", $"Status {fromStatus} chỉ được release qua disposition/recall workflow chuyên biệt.");

        if (toStatus == InventoryStatus.Available &&
            fromStatus is InventoryStatus.Quarantine or InventoryStatus.Damaged or InventoryStatus.Rejected)
            throw Conflict("INV_STATUS_CHANGE_NOT_ALLOWED", $"Không thể generic-release {fromStatus} về AVAILABLE; cần disposition/release workflow chuyên biệt.");
    }

    private static bool TryParseStatus(string value, out InventoryStatus status)
    {
        var normalized = value?.Trim().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<InventoryStatus>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ToCode(candidate).Replace("_", "", StringComparison.Ordinal), normalized, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }
        status = default;
        return false;
    }

    private static string ToCode(InventoryStatus status) => status switch
    {
        InventoryStatus.QcHold => "QC_HOLD",
        InventoryStatus.RecallBlocked => "RECALL_BLOCKED",
        _ => status.ToString().ToUpperInvariant()
    };

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
