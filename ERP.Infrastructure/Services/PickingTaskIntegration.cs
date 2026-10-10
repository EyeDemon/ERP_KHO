using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class PickingTaskIntegration(ErpKhoDbContext context) : IPickingTaskIntegration
{
    private static readonly StockAllocationStatus[] CapacityStatuses =
    [
        StockAllocationStatus.Active,
        StockAllocationStatus.Picking,
        StockAllocationStatus.Picked
    ];

    public async Task PrepareReservationReleaseAsync(
        int reservationId,
        int actorId,
        string reason,
        bool sourceWide,
        CancellationToken cancellationToken = default)
    {
        var task = await context.PickingTasks
            .Include(x => x.Lines).ThenInclude(x => x.Allocation)
            .SingleOrDefaultAsync(
                x => x.Lines.Any(line => line.Allocation.ReservationId == reservationId) &&
                     x.Status != PickingTaskStatus.Cancelled,
                cancellationToken);
        if (task is null) return;

        if (!sourceWide && task.SourceId.HasValue)
            throw new ConcurrencyException("Reservation thuộc Picking task theo chứng từ nguồn. Hãy hủy từ chứng từ nguồn thay vì release riêng lẻ.");

        if (task.Status is not PickingTaskStatus.Open and not PickingTaskStatus.Assigned)
            throw new ConcurrencyException("Picking đã bắt đầu hoặc hoàn tất. Không thể release reservation/allocation trực tiếp.");

        task.Status = PickingTaskStatus.Cancelled;
        context.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            Action = "PickingTask.CancelledBySourceRelease",
            EntityName = "PickingTask",
            EntityId = task.Id,
            WarehouseId = task.WarehouseId,
            Timestamp = DateTime.UtcNow,
            NewValues = $"Reason: {reason}; ReservationId: {reservationId}; SourceWide: {sourceWide}",
            Result = "Success",
            Severity = "Information"
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task EnsureForReservationAsync(
        int reservationId,
        int actorId,
        CancellationToken cancellationToken = default)
    {
        var seed = await context.StockReservations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == reservationId, cancellationToken)
            ?? throw new ConcurrencyException("Không tìm thấy reservation để tạo nhiệm vụ Picking.");

        List<StockReservation> reservations;
        PickingTask? existingTask = null;
        if (seed.SourceId.HasValue)
        {
            existingTask = await context.PickingTasks
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.SourceType == seed.SourceType &&
                         x.SourceId == seed.SourceId &&
                         x.Status != PickingTaskStatus.Cancelled,
                    cancellationToken);

            reservations = await context.StockReservations
                .Where(x => x.SourceType == seed.SourceType &&
                            x.SourceId == seed.SourceId &&
                            x.WarehouseId == seed.WarehouseId)
                .OrderBy(x => x.ProductId)
                .ThenBy(x => x.Id)
                .ToListAsync(cancellationToken);
        }
        else
        {
            if (await context.PickingTaskLines.AnyAsync(
                    x => x.Allocation.ReservationId == reservationId,
                    cancellationToken))
                return;

            reservations = await context.StockReservations
                .Where(x => x.Id == reservationId)
                .ToListAsync(cancellationToken);
        }

        if (reservations.Count == 0) return;
        if (reservations.Any(x =>
        {
            var remaining = x.Quantity - x.ConsumedQuantity - x.ReleasedQuantity;
            return remaining <= 0 ||
                   x.AllocatedQuantity != remaining ||
                   x.Status != StockReservationStatus.Allocated;
        })) return;

        var reservationIds = reservations.Select(x => x.Id).ToArray();
        var allocations = await context.StockAllocations
            .Where(x => reservationIds.Contains(x.ReservationId) && CapacityStatuses.Contains(x.Status))
            .OrderBy(x => x.ProductId)
            .ThenBy(x => x.LocationId)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        foreach (var reservation in reservations)
        {
            var active = allocations.Where(x => x.ReservationId == reservation.Id).Sum(x => x.Quantity);
            if (active != reservation.AllocatedQuantity)
                throw new ConcurrencyException("Allocation chưa đối soát được để tạo nhiệm vụ Picking.");
        }

        if (allocations.Count == 0) return;

        if (existingTask is not null)
        {
            if (existingTask.Status is not PickingTaskStatus.Open and not PickingTaskStatus.Assigned)
                return;

            var existingAllocationIds = existingTask.Lines.Select(x => x.AllocationId).ToHashSet();
            var additions = allocations.Where(x => !existingAllocationIds.Contains(x.Id)).ToList();
            if (additions.Count == 0) return;

            var nextSequence = existingTask.Lines.Count == 0 ? 0 : existingTask.Lines.Max(x => x.Sequence);
            foreach (var allocation in additions)
            {
                existingTask.Lines.Add(new PickingTaskLine
                {
                    AllocationId = allocation.Id,
                    ProductId = allocation.ProductId,
                    SourceLocationId = allocation.LocationId,
                    RequestedQuantity = allocation.Quantity,
                    PickedQuantity = 0,
                    Sequence = ++nextSequence,
                    Status = PickingTaskLineStatus.Open
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            context.AuditLogs.Add(new AuditLog
            {
                UserId = actorId,
                Action = "PickingTask.ExpandedForRecoveredDemand",
                EntityName = "PickingTask",
                EntityId = existingTask.Id,
                WarehouseId = existingTask.WarehouseId,
                Timestamp = DateTime.UtcNow,
                NewValues = $"AddedLines: {additions.Count}; Source: {existingTask.SourceType}/{existingTask.SourceId}",
                Result = "Success",
                Severity = "Information"
            });
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;
        var task = new PickingTask
        {
            TaskCode = $"PICK-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..35].ToUpperInvariant(),
            SourceType = seed.SourceType,
            SourceId = seed.SourceId,
            SourceCode = seed.SourceCode,
            WarehouseId = seed.WarehouseId,
            PickingType = "STANDARD",
            Status = PickingTaskStatus.Open,
            Priority = 0,
            CreatedAt = now,
            CreatedBy = actorId
        };

        var sequence = 0;
        foreach (var allocation in allocations)
        {
            task.Lines.Add(new PickingTaskLine
            {
                AllocationId = allocation.Id,
                ProductId = allocation.ProductId,
                SourceLocationId = allocation.LocationId,
                RequestedQuantity = allocation.Quantity,
                PickedQuantity = 0,
                Sequence = ++sequence,
                Status = PickingTaskLineStatus.Open
            });
        }

        context.PickingTasks.Add(task);
        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            Action = "PickingTask.Created",
            EntityName = "PickingTask",
            EntityId = task.Id,
            WarehouseId = task.WarehouseId,
            Timestamp = now,
            NewValues = $"TaskCode: {task.TaskCode}; Source: {task.SourceType}/{task.SourceId}; Lines: {task.Lines.Count}",
            Result = "Success",
            Severity = "Information"
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
