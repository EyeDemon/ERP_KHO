using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class ShipmentIntegration(ErpKhoDbContext context) : IShipmentIntegration
{
    public async Task EnsureForPackingSessionAsync(
        int packingSessionId,
        int actorId,
        CancellationToken cancellationToken = default)
    {
        if (await context.Shipments.AnyAsync(x => x.PackingSessionId == packingSessionId, cancellationToken))
            return;

        var tracked = context.ChangeTracker.Entries<PackingSession>()
            .Select(x => x.Entity)
            .SingleOrDefault(x => x.Id == packingSessionId);

        var session = tracked ?? await context.PackingSessions
            .Include(x => x.PickingTask)
            .Include(x => x.HandlingUnits)
            .SingleOrDefaultAsync(x => x.Id == packingSessionId, cancellationToken)
            ?? throw new ConcurrencyException("Không tìm thấy Packing session để tạo Shipment.");

        if (session.Status is not PackingSessionStatus.Packed and not PackingSessionStatus.Closed)
            throw new ConcurrencyException("Packing chưa hoàn tất nên chưa thể tạo Shipment.");

        if (!context.Entry(session).Reference(x => x.PickingTask).IsLoaded)
            await context.Entry(session).Reference(x => x.PickingTask).LoadAsync(cancellationToken);
        if (!context.Entry(session).Collection(x => x.HandlingUnits).IsLoaded)
            await context.Entry(session).Collection(x => x.HandlingUnits).LoadAsync(cancellationToken);

        var active = session.HandlingUnits
            .Where(x => x.Status != HandlingUnitStatus.Cancelled)
            .OrderBy(x => x.Id)
            .ToList();
        if (active.Count == 0 || active.Any(x => x.Status != HandlingUnitStatus.Closed))
            throw new ConcurrencyException("Packing phải có Handling Unit đã đóng đầy đủ trước khi tạo Shipment.");

        var roots = active.Where(x => x.ParentHandlingUnitId is null).ToList();
        if (roots.Count == 0)
            throw new ConcurrencyException("Không tìm thấy Handling Unit gốc để gắn Shipment.");

        var now = DateTime.UtcNow;
        var shipment = new Shipment
        {
            ShipmentCode = $"SHIP-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..35].ToUpperInvariant(),
            PackingSessionId = session.Id,
            WarehouseId = session.WarehouseId,
            SourceType = session.PickingTask.SourceType,
            SourceId = session.PickingTask.SourceId,
            SourceCode = session.PickingTask.SourceCode,
            Status = ShipmentStatus.Ready,
            CreatedAt = now,
            CreatedBy = actorId
        };
        var sequence = 1;
        foreach (var root in roots)
        {
            shipment.HandlingUnits.Add(new ShipmentHandlingUnit
            {
                HandlingUnitId = root.Id,
                Sequence = sequence++,
                AssignedAt = now
            });
        }

        context.Shipments.Add(shipment);
        await context.SaveChangesAsync(cancellationToken);
        context.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            Action = "Shipment.CreatedFromPacking",
            EntityName = "Shipment",
            EntityId = shipment.Id,
            WarehouseId = shipment.WarehouseId,
            Timestamp = now,
            NewValues = $"ShipmentCode: {shipment.ShipmentCode}; PackingSessionId: {session.Id}; RootHUs: {roots.Count}",
            Result = "Success",
            Severity = "Information"
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
