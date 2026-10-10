using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class PackingSessionIntegration(ErpKhoDbContext context) : IPackingSessionIntegration
{
    public async Task EnsureForPickingTaskAsync(
        int pickingTaskId,
        int actorId,
        CancellationToken cancellationToken = default)
    {
        if (await context.PackingSessions.AnyAsync(x => x.PickingTaskId == pickingTaskId, cancellationToken))
            return;

        var tracked = context.ChangeTracker.Entries<PickingTask>()
            .Select(x => x.Entity)
            .SingleOrDefault(x => x.Id == pickingTaskId);

        var task = tracked ?? await context.PickingTasks
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == pickingTaskId, cancellationToken)
            ?? throw new ConcurrencyException("Không tìm thấy Picking task để tạo Packing session.");

        if (task.Status != PickingTaskStatus.Completed)
            throw new ConcurrencyException("Picking task chưa hoàn tất nên chưa thể mở Packing session.");

        var picked = task.Lines.Sum(x => x.PickedQuantity);
        if (picked <= 0)
            throw new ConcurrencyException("Picking task không có số lượng đã lấy để đóng gói.");

        var now = DateTime.UtcNow;
        var session = new PackingSession
        {
            SessionCode = $"PACK-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..35].ToUpperInvariant(),
            PickingTaskId = task.Id,
            WarehouseId = task.WarehouseId,
            Status = PackingSessionStatus.Open,
            CreatedAt = now,
            CreatedBy = actorId
        };

        context.PackingSessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            Action = "PackingSession.Created",
            EntityName = "PackingSession",
            EntityId = session.Id,
            WarehouseId = task.WarehouseId,
            Timestamp = now,
            NewValues = $"SessionCode: {session.SessionCode}; PickingTaskId: {task.Id}; PickedQuantity: {picked}",
            Result = "Success",
            Severity = "Information"
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
