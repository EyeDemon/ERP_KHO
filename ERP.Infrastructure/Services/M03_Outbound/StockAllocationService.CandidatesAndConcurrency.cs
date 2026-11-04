using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Queries;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class StockAllocationService
{
    private async Task SynchronizeBucketAllocationAndReservationAsync(
        StockReservation reservation,
        IReadOnlyList<StockAllocation> createdAllocations,
        CancellationToken cancellationToken)
    {
        var warehouseStocks = await context.InventoryStocks
            .Where(x => x.WarehouseId == reservation.WarehouseId && x.ProductId == reservation.ProductId)
            .OrderBy(x => x.LocationId)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var alloc in createdAllocations)
        {
            var targetStock = warehouseStocks.FirstOrDefault(x =>
                x.LocationId == alloc.LocationId &&
                x.Status == alloc.InventoryStatus &&
                x.LotId == alloc.LotId &&
                x.SerialId == alloc.SerialId &&
                x.HandlingUnitId == alloc.HandlingUnitId &&
                x.OwnerId == alloc.OwnerId);
            if (targetStock is null) continue;

            targetStock.AllocatedQuantity += alloc.Quantity;
            if (targetStock.ReservedQuantity < targetStock.AllocatedQuantity)
            {
                var deficit = targetStock.AllocatedQuantity - targetStock.ReservedQuantity;
                foreach (var donor in warehouseStocks.Where(x => x.Id != targetStock.Id && x.ReservedQuantity > x.AllocatedQuantity))
                {
                    if (deficit <= 0) break;
                    var shift = Math.Min(deficit, donor.ReservedQuantity - donor.AllocatedQuantity);
                    if (shift <= 0) continue;
                    donor.ReservedQuantity -= shift;
                    targetStock.ReservedQuantity += shift;
                    deficit -= shift;
                }
            }
        }
    }

    private async Task<StockReservation> GetReservationAsync(
        int reservationId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<StockReservation> query = context.StockReservations;
        if (!tracking) query = query.AsNoTracking();
        var reservation = await query
            .Include(x => x.Product)
            .Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == reservationId, cancellationToken)
            ?? throw new NotFoundException("Không tìm thấy reservation hoặc bạn không có quyền truy cập.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(reservation.WarehouseId, cancellationToken);
        return reservation;
    }

    private static void ValidateAllocatableReservation(StockReservation reservation)
    {
        if (!AllocatableReservationStatuses.Contains(reservation.Status))
            throw new ConcurrencyException("Reservation không còn ở trạng thái có thể Allocation.");
        if (string.Equals(reservation.SourceType, "Manual", StringComparison.OrdinalIgnoreCase) && reservation.ExpiresAt <= DateTime.UtcNow)
            throw new ConcurrencyException("Reservation đã hết hạn.");
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (remaining <= 0)
            throw new ConcurrencyException("Reservation không còn số lượng để Allocation.");
    }

    private async Task<List<StockAllocationCandidateDto>> BuildCandidatesAsync(
        StockReservation reservation,
        int? fixedLocationId,
        int? excludedLocationId,
        CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var allowRebalanceFromPool = fixedLocationId.HasValue || excludedLocationId.HasValue;
        var query = context.InventoryStocks.AsNoTracking()
            .UnlockedAt(context, DateTime.UtcNow)
            .Where(x => x.ProductId == reservation.ProductId &&
                        x.WarehouseId == reservation.WarehouseId &&
                        x.StatusDefinition.IsAllocatable &&
                        x.LocationId.HasValue &&
                        x.Location != null &&
                        x.Location.IsActive &&
                        !x.Location.IsBlocked &&
                        x.Location.IsPickable &&
                        (allowRebalanceFromPool ? x.Quantity - x.LockedQuantity > 0 : x.ReservedQuantity > 0) &&
                        (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today));

        if (fixedLocationId.HasValue) query = query.Where(x => x.LocationId == fixedLocationId.Value);
        if (excludedLocationId.HasValue) query = query.Where(x => x.LocationId != excludedLocationId.Value);

        var raw = await query
            .OrderBy(x => x.Lot != null && x.Lot.ExpiryDate.HasValue ? 0 : 1)
            .ThenBy(x => x.Lot == null ? null : x.Lot.ExpiryDate)
            .ThenBy(x => x.Lot == null ? DateTime.MaxValue : x.Lot.ReceivedAt)
            .ThenBy(x => x.LocationId)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                InventoryStockId = x.Id,
                LocationId = x.LocationId!.Value,
                LocationCode = x.Location!.Code,
                LocationName = x.Location.Name,
                x.Status,
                x.LotId,
                LotNumber = x.Lot == null ? null : x.Lot.LotNumber,
                ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
                x.SerialId,
                SerialNumber = x.Serial == null ? null : x.Serial.SerialNumber,
                x.Quantity,
                x.LockedQuantity,
                x.ReservedQuantity,
                AllocatedQuantity = context.StockAllocations
                    .Where(a => a.WarehouseId == x.WarehouseId &&
                                a.ProductId == x.ProductId &&
                                a.LocationId == x.LocationId &&
                                a.InventoryStatus == x.Status &&
                                a.LotId == x.LotId &&
                                a.SerialId == x.SerialId &&
                                CapacityStatuses.Contains(a.Status))
                    .Sum(a => (decimal?)a.Quantity) ?? 0m
            })
            .Take(500)
            .ToListAsync(cancellationToken);

        return raw
            .Select((x, index) => new StockAllocationCandidateDto
            {
                InventoryStockId = x.InventoryStockId,
                LocationId = x.LocationId,
                LocationCode = x.LocationCode.Trim(),
                LocationName = x.LocationName.Trim(),
                InventoryStatus = x.Status.ToString(),
                LotId = x.LotId,
                LotNumber = x.LotNumber?.Trim(),
                ExpiryDate = x.ExpiryDate,
                SerialId = x.SerialId,
                SerialNumber = x.SerialNumber?.Trim(),
                ReservedQuantity = x.ReservedQuantity,
                AllocatedQuantity = x.AllocatedQuantity,
                AllocatableQuantity = allowRebalanceFromPool
                    ? Math.Max(0, x.Quantity - x.LockedQuantity - x.AllocatedQuantity)
                    : Math.Max(0, x.ReservedQuantity - x.AllocatedQuantity),
                Rank = index + 1,
                Reason = fixedLocationId.HasValue
                    ? "Vị trí cố định; bucket vẫn tuân status/Lot/Serial eligibility."
                    : x.ExpiryDate.HasValue
                        ? $"FEFO: expiry {x.ExpiryDate:yyyy-MM-dd}, sau đó receipt time/location/id."
                        : x.LotId.HasValue
                            ? "FIFO theo lot receipt time, sau đó location/id."
                            : "FIFO ổn định theo location/id cho inventory không theo lot."
            })
            .Where(x => x.AllocatableQuantity > 0)
            .ToList();
    }

    private List<StockAllocation> CreateRows(
        StockReservation reservation,
        decimal quantity,
        AllocationStrategy strategy,
        IReadOnlyList<StockAllocationCandidateDto> candidates)
    {
        var remaining = quantity;
        var rows = new List<StockAllocation>();
        foreach (var candidate in candidates)
        {
            var take = Math.Min(remaining, candidate.AllocatableQuantity);
            if (take <= 0) continue;
            rows.Add(new StockAllocation
            {
                AllocationCode = $"ALC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..34].ToUpperInvariant(),
                ReservationId = reservation.Id,
                WarehouseId = reservation.WarehouseId,
                ProductId = reservation.ProductId,
                LocationId = candidate.LocationId,
                LotId = candidate.LotId,
                SerialId = candidate.SerialId,
                InventoryStatus = Enum.Parse<InventoryStatus>(candidate.InventoryStatus),
                Quantity = take,
                Status = StockAllocationStatus.Active,
                Strategy = strategy,
                SelectionReason = candidate.Reason,
                AllocatedAt = DateTime.UtcNow,
                AllocatedBy = currentUser.UserId
            });
            remaining -= take;
            if (remaining == 0) break;
        }

        if (remaining != 0)
            throw new ConcurrencyException("Không đủ candidate để hoàn tất Allocation.");
        return rows;
    }

    private void AddAllocationAudits(IEnumerable<StockAllocation> rows, string action, string extra)
    {
        foreach (var row in rows)
        {
            context.AuditLogs.Add(Audit(
                action,
                row.Id,
                row.WarehouseId,
                $"Allocation: {row.AllocationCode}; ReservationId: {row.ReservationId}; LocationId: {row.LocationId}; Quantity: {row.Quantity}; Strategy: {row.Strategy}; {extra}"));
        }
    }

    private AuditLog Audit(string action, int entityId, int warehouseId, string values) => new()
    {
        UserId = currentUser.UserId,
        Action = action,
        EntityName = "StockAllocation",
        EntityId = entityId,
        WarehouseId = warehouseId,
        Timestamp = DateTime.UtcNow,
        NewValues = values,
        Result = "Success",
        Severity = "Information"
    };

    private static void UpdateReservationStatus(StockReservation reservation)
    {
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (reservation.AllocatedQuantity <= 0)
            reservation.Status = reservation.ConsumedQuantity > 0 ? StockReservationStatus.PartiallyConsumed : StockReservationStatus.Active;
        else if (reservation.AllocatedQuantity < remaining)
            reservation.Status = StockReservationStatus.PartiallyAllocated;
        else
            reservation.Status = StockReservationStatus.Allocated;
    }

    private async Task<T> ExecuteWithSerializableRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var ownTransaction = context.Database.CurrentTransaction is null;
        var attempts = ownTransaction ? 4 : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await using var transaction = ownTransaction
                ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                : null;
            try
            {
                var result = await action(cancellationToken);
                if (transaction is not null) await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < attempts)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
                _ = exception;
            }
            catch (Exception exception) when (attempt < attempts && IsDeadlock(exception))
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                throw new ConcurrencyException("Dữ liệu Allocation đã thay đổi. Vui lòng tải lại và thử lại.", exception);
            }
            catch (Exception exception) when (IsDeadlock(exception))
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                throw new DeadlockException("Giao dịch Allocation bị deadlock.", exception);
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        throw new ConcurrencyException("Không thể hoàn tất Allocation do dữ liệu thay đổi đồng thời.");
    }

    private static bool IsDeadlock(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is DeadlockException or SqlException { Number: 1205 })
                return true;
        }
        return false;
    }

    private IQueryable<StockAllocation> AllocationQuery() =>
        context.StockAllocations
            .Include(x => x.Reservation)
            .Include(x => x.Warehouse)
            .Include(x => x.Product)
            .Include(x => x.Location)
            .Include(x => x.Lot)
            .Include(x => x.Serial);

    private static StockAllocationDto Map(StockAllocation x) => new()
    {
        Id = x.Id,
        AllocationCode = x.AllocationCode.Trim(),
        ReservationId = x.ReservationId,
        ReservationCode = (x.Reservation?.ReservationCode ?? string.Empty).Trim(),
        SourceType = (x.Reservation?.SourceType ?? string.Empty).Trim(),
        SourceId = x.Reservation?.SourceId,
        SourceCode = x.Reservation?.SourceCode?.Trim(),
        WarehouseId = x.WarehouseId,
        WarehouseName = (x.Warehouse?.Name ?? string.Empty).Trim(),
        ProductId = x.ProductId,
        ProductCode = (x.Product?.Code ?? string.Empty).Trim(),
        ProductName = (x.Product?.Name ?? string.Empty).Trim(),
        LocationId = x.LocationId,
        LocationCode = (x.Location?.Code ?? string.Empty).Trim(),
        LocationName = (x.Location?.Name ?? string.Empty).Trim(),
        InventoryStatus = x.InventoryStatus.ToString(),
        LotId = x.LotId,
        LotNumber = x.Lot?.LotNumber?.Trim(),
        ExpiryDate = x.Lot?.ExpiryDate,
        SerialId = x.SerialId,
        SerialNumber = x.Serial?.SerialNumber?.Trim(),
        Quantity = x.Quantity,
        Status = x.Status.ToString(),
        Strategy = x.Strategy.ToString(),
        SelectionReason = (x.SelectionReason ?? string.Empty).Trim(),
        AllocatedAt = x.AllocatedAt,
        ReleasedAt = x.ReleasedAt,
        ReleaseReason = x.ReleaseReason?.Trim()
    };

    private async Task<IReadOnlyList<StockAllocationDto>> LoadDtosAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return [];
        var rows = await AllocationQuery().AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }
}
