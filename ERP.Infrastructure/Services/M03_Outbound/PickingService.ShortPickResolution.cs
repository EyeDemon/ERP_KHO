using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PickingService
{
    private async Task ResolveAlternativeLocationAsync(
        PickingTask task,
        ShortPickException exception,
        string? note,
        CancellationToken token)
    {
        var line = exception.PickingTaskLine;
        var allocation = line.Allocation;
        if (allocation.Status != StockAllocationStatus.Picking)
            throw Conflict("PICK_ALLOCATION_CHANGED", "Allocation đã thay đổi trước khi xử lý Short Pick.");

        var today = DateTime.UtcNow.Date;
        var reservation = await context.StockReservations.SingleAsync(x => x.Id == allocation.ReservationId, token);
        if (context.Database.IsSqlServer())
        {
            var tenantId = context.CurrentTenantId;
            _ = await context.InventoryStocks
                .FromSqlInterpolated($"SELECT * FROM dbo.InventoryStocks WITH (INDEX(IX_InventoryStocks_CanonicalBucket), UPDLOCK, ROWLOCK, REPEATABLEREAD) WHERE TenantId = {tenantId} AND ProductId = {allocation.ProductId} AND WarehouseId = {allocation.WarehouseId} AND Status = {(int)allocation.InventoryStatus}")
                .OrderBy(x => x.Id)
                .ToListAsync(token);
        }
        var candidates = await context.InventoryStocks.AsNoTracking()
            .UnlockedAt(context, DateTime.UtcNow)
            .Where(x => x.ProductId == allocation.ProductId &&
                        x.WarehouseId == allocation.WarehouseId &&
                        x.Status == allocation.InventoryStatus &&
                        (x.StatusDefinition == null || x.StatusDefinition.IsPickable) &&
                        x.LocationId.HasValue &&
                        x.LocationId != allocation.LocationId &&
                        x.Location != null &&
                        x.Location.IsActive &&
                        !x.Location.IsBlocked &&
                        x.Location.IsPickable &&
                        !context.ShortPickExceptions.Any(sp =>
                            sp.PickingTaskLine.ProductId == allocation.ProductId &&
                            sp.PickingTaskLine.SourceLocationId == x.LocationId &&
                            sp.PickingTaskLine.PickingTask.WarehouseId == allocation.WarehouseId &&
                            sp.PickingTaskLine.PickingTask.Status != PickingTaskStatus.Completed &&
                            sp.PickingTaskLine.PickingTask.Status != PickingTaskStatus.Cancelled) &&
                        (x.Lot == null || !x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value >= today) &&
                        x.Quantity > x.ReservedQuantity)
            .OrderBy(x => x.Lot != null && x.Lot.ExpiryDate.HasValue ? 0 : 1)
            .ThenBy(x => x.Lot == null ? null : x.Lot.ExpiryDate)
            .ThenBy(x => x.Lot == null ? DateTime.MaxValue : x.Lot.ReceivedAt)
            .ThenBy(x => x.LocationId)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                LocationId = x.LocationId!.Value,
                x.LotId,
                x.SerialId,
                ExpiryDate = x.Lot == null ? null : x.Lot.ExpiryDate,
                x.Quantity,
                x.ReservedQuantity
            })
            .ToListAsync(token);

        var totalAvailable = candidates.Sum(x => Math.Max(0m, x.Quantity - x.ReservedQuantity));
        if (totalAvailable < exception.ShortageQuantity)
            throw Conflict("PICK_SHORT", "Không có đủ reserved capacity ở vị trí khác để reallocate phần Short Pick.");

        var originalQuantity = allocation.Quantity;
        if (line.PickedQuantity > 0)
        {
            allocation.Quantity = line.PickedQuantity;
            allocation.Status = StockAllocationStatus.Picked;
        }
        else
        {
            allocation.Status = StockAllocationStatus.Reallocated;
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleasedBy = currentUser.UserId;
            allocation.ReleaseReason = "Short Pick alternative location";
        }
        allocation.Version++;

        var remaining = exception.ShortageQuantity;
        var totalShiftedFromSource = 0m;
        var sequence = task.Lines.Max(x => x.Sequence);
        var now = DateTime.UtcNow;
        foreach (var candidate in candidates)
        {
            var unreservedAvailable = Math.Max(0m, candidate.Quantity - candidate.ReservedQuantity);
            var take = Math.Min(remaining, unreservedAvailable);
            if (take <= 0) continue;

            var targetQuery = context.InventoryStocks
                .UnlockedAt(context, now)
                .Where(x => x.Id == candidate.Id && x.Quantity - x.ReservedQuantity >= take);
            int targetUpdated;
            if (context.Database.IsRelational())
            {
                targetUpdated = await targetQuery.ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity + take)
                    .SetProperty(x => x.LastUpdated, now), token);
            }
            else
            {
                var targetRows = await targetQuery.ToListAsync(token);
                foreach (var row in targetRows)
                {
                    row.ReservedQuantity += take;
                    row.LastUpdated = now;
                }
                targetUpdated = targetRows.Count;
            }
            if (targetUpdated != 1)
                throw new ConcurrencyException("Tồn kho tại vị trí thay thế đã thay đổi trong lúc reallocate Short Pick.");
            totalShiftedFromSource += take;

            var created = new StockAllocation
            {
                AllocationCode = $"ALC-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..34].ToUpperInvariant(),
                ReservationId = allocation.ReservationId,
                WarehouseId = allocation.WarehouseId,
                ProductId = allocation.ProductId,
                LocationId = candidate.LocationId,
                InventoryStatus = allocation.InventoryStatus,
                LotId = candidate.LotId,
                SerialId = candidate.SerialId,
                Quantity = take,
                Status = StockAllocationStatus.Picking,
                Strategy = AllocationStrategy.LocationOrder,
                SelectionReason = $"Short Pick reallocation từ Allocation {allocation.AllocationCode}.",
                AllocatedAt = DateTime.UtcNow,
                AllocatedBy = currentUser.UserId
            };
            context.StockAllocations.Add(created);
            task.Lines.Add(new PickingTaskLine
            {
                Allocation = created,
                ProductId = created.ProductId,
                SourceLocationId = created.LocationId,
                RequestedQuantity = take,
                PickedQuantity = 0,
                Sequence = ++sequence,
                Status = PickingTaskLineStatus.InProgress
            });
            remaining -= take;
            if (remaining == 0) break;
        }
        if (remaining != 0)
            throw new ConcurrencyException("Không thể reallocate đầy đủ phần Short Pick.");

        if (totalShiftedFromSource > 0)
        {
            var sourceQuery = context.InventoryStocks
                .Where(x => x.ProductId == allocation.ProductId &&
                            x.WarehouseId == allocation.WarehouseId &&
                            x.LocationId == allocation.LocationId &&
                            x.Status == allocation.InventoryStatus &&
                            x.LotId == allocation.LotId &&
                            x.SerialId == allocation.SerialId &&
                            x.ReservedQuantity >= totalShiftedFromSource);
            int sourceUpdated;
            if (context.Database.IsRelational())
            {
                sourceUpdated = await sourceQuery.ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - totalShiftedFromSource)
                    .SetProperty(x => x.LastUpdated, now), token);
            }
            else
            {
                var sourceRows = await sourceQuery.ToListAsync(token);
                foreach (var row in sourceRows)
                {
                    row.ReservedQuantity -= totalShiftedFromSource;
                    row.LastUpdated = now;
                }
                sourceUpdated = sourceRows.Count;
            }
            if (sourceUpdated != 1)
                throw new ConcurrencyException("Reserved bucket tại vị trí nguồn đã thay đổi trong lúc chuyển vùng giữ hàng.");
        }

        reservation.AllocationVersion++;
        line.Status = PickingTaskLineStatus.Resolved;
        ResolveException(exception, ShortPickResolutionType.AlternativeLocation, note);
        task.Status = PickingTaskStatus.InProgress;
        AddAudit(
            "PickingTask.ShortPickReallocated",
            task,
            $"LineId: {line.Id}; OriginalAllocationId: {allocation.Id}; OriginalQuantity: {originalQuantity}; Shortage: {exception.ShortageQuantity}");
    }

    private async Task ResolveReleasedRemainderAsync(
        PickingTask task,
        ShortPickException exception,
        ShortPickResolutionType resolution,
        string? note,
        CancellationToken token)
    {
        var line = exception.PickingTaskLine;
        var allocation = line.Allocation;
        if (allocation.Status != StockAllocationStatus.Picking)
            throw Conflict("PICK_ALLOCATION_CHANGED", "Allocation đã thay đổi trước khi xử lý Short Pick.");

        var originalQuantity = allocation.Quantity;
        var shortage = exception.ShortageQuantity;
        var otherCommitted = await context.StockAllocations
            .Where(a => a.Id != allocation.Id &&
                        a.WarehouseId == allocation.WarehouseId &&
                        a.ProductId == allocation.ProductId &&
                        a.LocationId == allocation.LocationId &&
                        a.InventoryStatus == allocation.InventoryStatus &&
                        a.LotId == allocation.LotId &&
                        a.SerialId == allocation.SerialId &&
                        CapacityStatuses.Contains(a.Status))
            .SumAsync(a => (decimal?)a.Quantity, token) ?? 0m;

        var bucketQuery = context.InventoryStocks
            .Where(x => x.ProductId == allocation.ProductId &&
                        x.WarehouseId == allocation.WarehouseId &&
                        x.LocationId == allocation.LocationId &&
                        x.Status == allocation.InventoryStatus &&
                        x.LotId == allocation.LotId &&
                        x.SerialId == allocation.SerialId &&
                        x.ReservedQuantity >= otherCommitted + originalQuantity);
        int affected;
        if (context.Database.IsRelational())
        {
            affected = await bucketQuery.ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ReservedQuantity, x => x.ReservedQuantity - shortage)
                .SetProperty(x => x.LastUpdated, DateTime.UtcNow), token);
        }
        else
        {
            var bucketRows = await bucketQuery.ToListAsync(token);
            foreach (var row in bucketRows)
            {
                row.ReservedQuantity -= shortage;
                row.LastUpdated = DateTime.UtcNow;
            }
            affected = bucketRows.Count;
        }
        if (affected != 1)
            throw new ConcurrencyException("Reserved bucket đã thay đổi trước khi xử lý Short Pick.");

        var reservation = await context.StockReservations.SingleAsync(x => x.Id == allocation.ReservationId, token);
        if (line.PickedQuantity > 0)
        {
            allocation.Quantity = line.PickedQuantity;
            allocation.Status = StockAllocationStatus.Picked;
        }
        else
        {
            allocation.Status = StockAllocationStatus.Released;
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleasedBy = currentUser.UserId;
            allocation.ReleaseReason = $"Short Pick {resolution}";
        }
        allocation.Version++;

        reservation.AllocatedQuantity -= shortage;
        reservation.ReleasedQuantity += shortage;
        reservation.AllocationVersion++;
        reservation.ReleasedAt = DateTime.UtcNow;
        reservation.ReleasedBy = currentUser.UserId;
        reservation.ReleaseReason = $"Short Pick {resolution}";
        UpdateReservationStatus(reservation);

        if (task.SourceType == "SalesOrder" && task.SourceId.HasValue)
        {
            var soLine = await context.SalesOrderLines
                .Include(x => x.Backorder)
                .SingleOrDefaultAsync(
                    x => x.SalesOrderId == task.SourceId.Value && x.ProductId == allocation.ProductId,
                    token);
            if (soLine is not null)
            {
                if (resolution == ShortPickResolutionType.CancelRemainder)
                {
                    soLine.CancelledQuantity = Math.Min(soLine.OrderedQuantity, soLine.CancelledQuantity + shortage);
                }
                else if (resolution is ShortPickResolutionType.Backorder or ShortPickResolutionType.SupervisorOverride)
                {
                    if (soLine.Backorder is null)
                    {
                        soLine.Backorder = new Backorder
                        {
                            BackorderCode = $"BO-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..33].ToUpperInvariant(),
                            WarehouseId = task.WarehouseId,
                            Quantity = shortage,
                            Status = BackorderStatus.Open,
                            CreatedAt = DateTime.UtcNow
                        };
                    }
                    else
                    {
                        soLine.Backorder.Quantity += shortage;
                        soLine.Backorder.UpdatedAt = DateTime.UtcNow;
                        soLine.Backorder.ResolvedAt = null;
                        soLine.Backorder.Status = soLine.Backorder.RecoveredQuantity > 0
                            ? BackorderStatus.PartiallyAllocated
                            : BackorderStatus.Open;
                    }
                }
            }
        }
        else if (task.SourceType == "ExportReceipt" && task.SourceId.HasValue &&
                 resolution is ShortPickResolutionType.CancelRemainder or ShortPickResolutionType.SupervisorOverride)
        {
            var exportDetail = await context.ExportReceiptDetails
                .FirstOrDefaultAsync(
                    x => x.ExportReceiptId == task.SourceId.Value && x.ProductId == allocation.ProductId,
                    token);
            if (exportDetail is not null && exportDetail.Quantity > shortage)
            {
                exportDetail.Quantity -= shortage;
            }
        }

        line.Status = PickingTaskLineStatus.Resolved;
        ResolveException(exception, resolution, note);
        task.Status = task.Lines.All(x => x.Status is PickingTaskLineStatus.Picked or PickingTaskLineStatus.Resolved)
            ? PickingTaskStatus.Resolved
            : PickingTaskStatus.InProgress;
        AddAudit(
            "PickingTask.ShortPickResolved",
            task,
            $"LineId: {line.Id}; Resolution: {resolution}; Shortage: {shortage}");
    }

    private void ResolveException(
        ShortPickException exception,
        ShortPickResolutionType resolution,
        string? note)
    {
        exception.ResolutionType = resolution;
        exception.Status = ShortPickExceptionStatus.Resolved;
        exception.ResolvedAt = DateTime.UtcNow;
        exception.ResolvedBy = currentUser.UserId;
        exception.ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }

    private static void UpdateReservationStatus(StockReservation reservation)
    {
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (remaining <= 0)
            reservation.Status = StockReservationStatus.Released;
        else if (reservation.AllocatedQuantity <= 0)
            reservation.Status = reservation.ConsumedQuantity > 0 ? StockReservationStatus.PartiallyConsumed : StockReservationStatus.Active;
        else if (reservation.AllocatedQuantity < remaining)
            reservation.Status = StockReservationStatus.PartiallyAllocated;
        else
            reservation.Status = StockReservationStatus.Allocated;
    }

    private async Task EnsureSourceStillFullyAllocatedAsync(PickingTask task, CancellationToken token)
    {
        var allocationIds = task.Lines.Select(x => x.AllocationId).ToArray();
        var count = await context.StockAllocations.CountAsync(
            x => allocationIds.Contains(x.Id) && x.Status == StockAllocationStatus.Active,
            token);
        if (count != allocationIds.Length)
            throw Conflict("PICK_ALLOCATION_CHANGED", "Allocation đã thay đổi sau khi nhiệm vụ Picking được tạo.");
    }
}
