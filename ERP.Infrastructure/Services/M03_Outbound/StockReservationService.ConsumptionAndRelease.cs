using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public partial class StockReservationService
{
    public async Task<IReadOnlyList<InventoryStockConsumption>> ConsumeAsync(StockReservation reservation, int userId, CancellationToken cancellationToken = default)
    {
        await context.AcquireInventoryAllocationLocksAsync([(reservation.WarehouseId, reservation.ProductId)], cancellationToken);
        var now = DateTime.UtcNow;
        var expired = string.Equals(reservation.SourceType, "Manual", StringComparison.OrdinalIgnoreCase) && reservation.ExpiresAt <= now;
        var completedPickingProtectsExpiry = expired && await context.PickingTaskLines.AsNoTracking().AnyAsync(
            line => line.Allocation.ReservationId == reservation.Id &&
                    line.PickingTask.Status == PickingTaskStatus.Completed,
            cancellationToken);
        if ((expired && !completedPickingProtectsExpiry) ||
            reservation.Status is not StockReservationStatus.Active and
                not StockReservationStatus.PartiallyConsumed and
                not StockReservationStatus.PartiallyAllocated and
                not StockReservationStatus.Allocated)
            throw new ConcurrencyException("Reservation đã hết hạn hoặc không còn hiệu lực.");
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (remaining <= 0)
            throw new ConcurrencyException("Reservation không còn số lượng để tiêu thụ.");

        var allocations = await context.StockAllocations
            .Where(x => x.ReservationId == reservation.Id &&
                        (x.Status == StockAllocationStatus.Active || x.Status == StockAllocationStatus.Picking || x.Status == StockAllocationStatus.Picked))
            .OrderBy(x => x.LocationId).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyList<InventoryStockConsumption> consumptions;
        if (allocations.Count > 0)
        {
            if (allocations.Sum(x => x.Quantity) != remaining || reservation.AllocatedQuantity != remaining)
                throw new ConcurrencyException("Reservation đang được Allocation một phần. Hãy hoàn tất hoặc giải phóng Allocation trước khi tiêu thụ.");

            var consumed = new List<InventoryStockConsumption>();
            foreach (var allocation in allocations)
            {
                if (!await stockRepository.TryConsumeReservationAtBucketAsync(
                        reservation.ProductId,
                        reservation.WarehouseId,
                        allocation.LocationId,
                        allocation.InventoryStatus,
                        allocation.LotId,
                        allocation.SerialId,
                        allocation.Quantity,
                        cancellationToken))
                    throw new ConcurrencyException("Bucket Allocation Lot/Serial/Status đã thay đổi trước khi tiêu thụ.");

                if (!context.Database.IsRelational())
                {
                    var bucketStock = await context.InventoryStocks.FirstOrDefaultAsync(
                        x => x.ProductId == reservation.ProductId &&
                             x.WarehouseId == reservation.WarehouseId &&
                             x.LocationId == allocation.LocationId &&
                             x.Status == allocation.InventoryStatus &&
                             x.LotId == allocation.LotId &&
                             x.SerialId == allocation.SerialId,
                        cancellationToken);
                    if (bucketStock is not null)
                    {
                        bucketStock.AllocatedQuantity = Math.Max(0m, bucketStock.AllocatedQuantity - allocation.Quantity);
                    }
                }

                consumed.Add(new InventoryStockConsumption(
                    allocation.LocationId,
                    allocation.Quantity,
                    allocation.InventoryStatus,
                    allocation.LotId,
                    allocation.SerialId));
                allocation.Status = StockAllocationStatus.Consumed;
                allocation.Version++;
                context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "StockAllocation.Consumed",
                    EntityName = "StockAllocation",
                    EntityId = allocation.Id,
                    WarehouseId = reservation.WarehouseId,
                    Timestamp = now,
                    NewValues = $"Allocation: {allocation.AllocationCode}; Quantity: {allocation.Quantity}",
                    Result = "Success",
                    Severity = "Information"
                });
            }
            reservation.AllocatedQuantity = 0;
            reservation.AllocationVersion++;
            consumptions = consumed;
        }
        else
        {
            consumptions = await stockRepository.ConsumeReservationWithBreakdownAsync(
                reservation.ProductId, reservation.WarehouseId, remaining, cancellationToken);
            if (consumptions.Count == 0 || consumptions.Sum(x => x.Quantity) != remaining)
                throw new ConcurrencyException("Reservation không thể được tiêu thụ do dữ liệu tồn kho đã thay đổi.");
        }

        reservation.ConsumedQuantity += remaining;
        reservation.ConsumedAt = now;
        reservation.Status = StockReservationStatus.Consumed;
        context.AuditLogs.Add(Audit(userId, "StockReservation.Consumed", reservation, $"Quantity: {remaining}"));
        return consumptions;
    }

    public async Task ReleaseSourceAsync(string sourceType, int sourceId, int userId, string reason, CancellationToken cancellationToken = default)
    {
        var reservations = await context.StockReservations
            .Where(x => x.SourceType == sourceType &&
                        x.SourceId == sourceId &&
                        (x.Status == StockReservationStatus.Active ||
                         x.Status == StockReservationStatus.PartiallyConsumed ||
                         x.Status == StockReservationStatus.PartiallyAllocated ||
                         x.Status == StockReservationStatus.Allocated))
            .OrderBy(x => x.WarehouseId)
            .ThenBy(x => x.ProductId)
            .ToListAsync(cancellationToken);
        await context.AcquireInventoryAllocationLocksAsync(
            reservations.Select(x => (x.WarehouseId, x.ProductId)),
            cancellationToken);
        foreach (var reservation in reservations)
            await ReleaseCoreAsync(
                reservation,
                null,
                userId,
                reason,
                StockReservationStatus.Cancelled,
                cancellationToken,
                sourceWide: true);
    }

    public async Task ReleaseAsync(int id, ReleaseStockReservationDto request, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            await unitOfWork.BeginTransactionAsync();
            try
            {
                var reservation = await ScopedQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy reservation.");
                if (!string.Equals(reservation.SourceType, "Manual", StringComparison.OrdinalIgnoreCase))
                    throw new ConcurrencyException(
                        "Chỉ được giải phóng thủ công reservation Manual; reservation thuộc chứng từ xuất phải được xử lý theo vòng đời chứng từ.",
                        "DOCUMENT_OWNED_RESERVATION");
                await context.AcquireInventoryAllocationLocksAsync([(reservation.WarehouseId, reservation.ProductId)], cancellationToken);
                await ReleaseCoreAsync(reservation, request.Quantity, currentUser.UserId, request.Reason, StockReservationStatus.Released, cancellationToken);
                await unitOfWork.CommitTransactionAsync();
                return;
            }
            catch (DeadlockException) when (attempt < maxAttempts)
            {
                await unitOfWork.RollbackTransactionAsync();
                await Task.Delay(15 * attempt, cancellationToken);
            }
            catch
            {
                await unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }
    }

    private async Task ReleaseCoreAsync(
        StockReservation reservation,
        decimal? requested,
        int userId,
        string reason,
        StockReservationStatus finalStatus,
        CancellationToken cancellationToken,
        bool sourceWide = false)
    {
        await context.AcquireInventoryAllocationLocksAsync([(reservation.WarehouseId, reservation.ProductId)], cancellationToken);
        if (reservation.Status is not StockReservationStatus.Active and
            not StockReservationStatus.PartiallyConsumed and
            not StockReservationStatus.PartiallyAllocated and
            not StockReservationStatus.Allocated)
            throw new ConcurrencyException("Reservation không còn có thể giải phóng.");

        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        var quantity = requested ?? remaining;
        if (quantity <= 0 || quantity > remaining)
            throw new BusinessRuleException("Số lượng giải phóng không hợp lệ.");

        var releaseAll = quantity == remaining;
        if (releaseAll)
        {
            if (pickingTaskIntegration is not null)
                await pickingTaskIntegration.PrepareReservationReleaseAsync(
                    reservation.Id,
                    userId,
                    reason,
                    sourceWide,
                    cancellationToken);
            await ReleaseActiveAllocationsAsync(reservation, userId, reason, cancellationToken);
        }
        else
        {
            var unallocated = remaining - reservation.AllocatedQuantity;
            if (quantity > unallocated)
                throw new BusinessRuleException("Số lượng giải phóng vượt phần reservation chưa Allocation. Hãy giải phóng Allocation trước.");
        }

        if (!await stockRepository.TryReleaseReservationAsync(
                reservation.ProductId,
                reservation.WarehouseId,
                quantity,
                releaseAll ? reservation.Id : null,
                cancellationToken))
            throw new ConcurrencyException("Dữ liệu giữ hàng đã thay đổi.");

        reservation.ReleasedQuantity += quantity;
        reservation.ReleasedAt = DateTime.UtcNow;
        reservation.ReleasedBy = userId;
        reservation.ReleaseReason = reason;

        if (reservation.ConsumedQuantity + reservation.ReleasedQuantity == reservation.Quantity)
            reservation.Status = finalStatus;
        else
            UpdateActiveReservationStatus(reservation);

        context.AuditLogs.Add(Audit(userId, "StockReservation.Released", reservation, $"Quantity: {quantity}, Reason: {reason}"));
    }

    private async Task ReleaseActiveAllocationsAsync(StockReservation reservation, int userId, string reason, CancellationToken cancellationToken)
    {
        var allocations = await context.StockAllocations
            .Where(x => x.ReservationId == reservation.Id &&
                        (x.Status == StockAllocationStatus.Active ||
                         x.Status == StockAllocationStatus.Picking ||
                         x.Status == StockAllocationStatus.Picked))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (allocations.Count == 0)
        {
            if (reservation.AllocatedQuantity != 0)
                throw new ConcurrencyException("Dữ liệu Allocation của reservation cần được đối soát.");
            return;
        }

        var activeQuantity = allocations.Sum(x => x.Quantity);
        if (activeQuantity != reservation.AllocatedQuantity)
            throw new ConcurrencyException("Dữ liệu Allocation của reservation cần được đối soát.");

        foreach (var allocation in allocations)
        {
            allocation.Status = StockAllocationStatus.Released;
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleasedBy = userId;
            allocation.ReleaseReason = reason;
            allocation.Version++;

            if (context.Database.IsRelational())
            {
                await context.InventoryStocks
                    .Where(x => x.ProductId == reservation.ProductId &&
                                x.WarehouseId == reservation.WarehouseId &&
                                x.LocationId == allocation.LocationId &&
                                x.Status == allocation.InventoryStatus &&
                                x.LotId == allocation.LotId &&
                                x.SerialId == allocation.SerialId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.AllocatedQuantity, x => x.AllocatedQuantity >= allocation.Quantity ? x.AllocatedQuantity - allocation.Quantity : 0m),
                        cancellationToken);
            }
            else
            {
                var bucketStock = await context.InventoryStocks.FirstOrDefaultAsync(
                    x => x.ProductId == reservation.ProductId &&
                         x.WarehouseId == reservation.WarehouseId &&
                         x.LocationId == allocation.LocationId &&
                         x.Status == allocation.InventoryStatus &&
                         x.LotId == allocation.LotId &&
                         x.SerialId == allocation.SerialId,
                    cancellationToken);
                if (bucketStock is not null)
                {
                    bucketStock.AllocatedQuantity = Math.Max(0m, bucketStock.AllocatedQuantity - allocation.Quantity);
                }
            }

            context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "StockAllocation.ReleasedWithReservation",
                EntityName = "StockAllocation",
                EntityId = allocation.Id,
                WarehouseId = reservation.WarehouseId,
                Timestamp = DateTime.UtcNow,
                NewValues = $"Allocation: {allocation.AllocationCode}; Quantity: {allocation.Quantity}; Reason: {reason}",
                Result = "Success",
                Severity = "Information"
            });
        }

        reservation.AllocatedQuantity = 0;
        reservation.AllocationVersion++;
    }

    private static void UpdateActiveReservationStatus(StockReservation reservation)
    {
        var remaining = reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity;
        if (reservation.AllocatedQuantity <= 0)
            reservation.Status = reservation.ConsumedQuantity > 0
                ? StockReservationStatus.PartiallyConsumed
                : StockReservationStatus.Active;
        else if (reservation.AllocatedQuantity < remaining)
            reservation.Status = StockReservationStatus.PartiallyAllocated;
        else
            reservation.Status = StockReservationStatus.Allocated;
    }

    public async Task<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        await unitOfWork.BeginTransactionAsync();
        try
        {
            var expired = await ScopedQuery()
                .Where(x => x.SourceType == "Manual" &&
                            x.ExpiresAt <= DateTime.UtcNow &&
                            (x.Status == StockReservationStatus.Active ||
                             x.Status == StockReservationStatus.PartiallyConsumed ||
                             x.Status == StockReservationStatus.PartiallyAllocated ||
                             x.Status == StockReservationStatus.Allocated) &&
                            !context.PickingTaskLines.Any(line =>
                                line.Allocation.ReservationId == x.Id &&
                                line.PickingTask.Status != PickingTaskStatus.Cancelled))
                .OrderBy(x => x.WarehouseId)
                .ThenBy(x => x.ProductId)
                .ToListAsync(cancellationToken);
            await context.AcquireInventoryAllocationLocksAsync(
                expired.Select(x => (x.WarehouseId, x.ProductId)),
                cancellationToken);
            foreach (var item in expired) await ReleaseCoreAsync(item, null, currentUser.UserId, "Expired", StockReservationStatus.Expired, cancellationToken);
            await unitOfWork.CommitTransactionAsync();
            return expired.Count;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
