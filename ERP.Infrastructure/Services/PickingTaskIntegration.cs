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

    public async Task EnsureForReservationAsync(
        int reservationId,
        int actorId,
        CancellationToken cancellationToken = default)
    {
        var seed = await context.StockReservations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == reservationId, cancellationToken)
            ?? throw new ConcurrencyException("Không tìm thấy reservation để tạo nhiệm vụ Picking.");

        List<StockReservation> reservations;
        if (seed.SourceId.HasValue)
        {
            if (await context.PickingTasks.AnyAsync(
                    x => x.SourceType == seed.SourceType && x.SourceId == seed.SourceId,
                    cancellationToken))
                return;

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
