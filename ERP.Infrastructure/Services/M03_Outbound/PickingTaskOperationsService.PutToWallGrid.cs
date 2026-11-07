using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PickingTaskOperationsService
{
    private async Task<(ERP.Domain.Entities.PickingTask? Task, List<PutToWallSlotData> Slots)> BuildPutToWallGridAsync(
        string? idOrCode,
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAuth,
        CancellationToken cancellationToken)
    {
        if (db == null)
        {
            var emptySlots = Enumerable.Range(1, 8)
                .Select(i => new PutToWallSlotData(
                    i,
                    $"SLOT-{i:D2}",
                    $"TOTE-{i:D2}",
                    null,
                    null,
                    $"SO-WAVE-{i:D2}",
                    null,
                    string.Empty,
                    string.Empty,
                    0m,
                    0m,
                    0m,
                    "EMPTY"))
                .ToList();
            return (null, emptySlots);
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        ERP.Domain.Entities.PickingTask? task = null;
        if (!string.IsNullOrWhiteSpace(idOrCode))
        {
            var trimmed = idOrCode.Trim();
            if (int.TryParse(trimmed, out var parsedId))
            {
                task = await db.PickingTasks
                    .AsNoTracking()
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Product)
                    .FirstOrDefaultAsync(t => t.Id == parsedId, cancellationToken);
            }

            if (task == null)
            {
                task = await db.PickingTasks
                    .AsNoTracking()
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Product)
                    .FirstOrDefaultAsync(t => t.TaskCode == trimmed || t.SourceCode == trimmed, cancellationToken);
            }
        }

        if (task == null)
        {
            var fallbackQuery = db.PickingTasks
                .AsNoTracking()
                .Include(t => t.Lines)
                    .ThenInclude(l => l.Product)
                .AsQueryable();
            if (warehouseId.HasValue)
            {
                fallbackQuery = fallbackQuery.Where(t => t.WarehouseId == warehouseId.Value);
            }
            else if (effectiveAuth != null)
            {
                var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds != null)
                    fallbackQuery = fallbackQuery.Where(t => accessibleIds.Contains(t.WarehouseId));
            }
            task = await fallbackQuery
                .OrderByDescending(t => t.Status == PickingTaskStatus.InProgress || t.Status == PickingTaskStatus.Assigned || t.Status == PickingTaskStatus.Open)
                .ThenByDescending(t => t.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (task != null && effectiveAuth != null)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(task.WarehouseId, cancellationToken);
        }

        var candidateLines = new List<(ERP.Domain.Entities.PickingTask OwnerTask, ERP.Domain.Entities.PickingTaskLine Line)>();
        if (task != null)
        {
            foreach (var l in task.Lines.OrderBy(x => x.Sequence).ThenBy(x => x.Id))
            {
                candidateLines.Add((task, l));
            }

            if (candidateLines.Count < 8)
            {
                var siblingTasks = await db.PickingTasks
                    .AsNoTracking()
                    .Include(t => t.Lines)
                        .ThenInclude(l => l.Product)
                    .Where(t => t.WarehouseId == task.WarehouseId && t.Id != task.Id)
                    .OrderByDescending(t => t.Id)
                    .Take(8)
                    .ToListAsync(cancellationToken);

                foreach (var st in siblingTasks)
                {
                    foreach (var sl in st.Lines.OrderBy(x => x.Sequence).ThenBy(x => x.Id))
                    {
                        if (candidateLines.Count >= 8) break;
                        candidateLines.Add((st, sl));
                    }
                    if (candidateLines.Count >= 8) break;
                }
            }
        }

        var persistedSlots = task != null && db != null
            ? await db.PutToWallSlots
                .AsNoTracking()
                .Where(s => s.PickingTaskId == task.Id)
                .ToListAsync(cancellationToken)
            : [];

        var persistedBySlot = new Dictionary<string, ERP.Domain.Entities.PutToWallSlot>(StringComparer.OrdinalIgnoreCase);
        foreach (var persisted in persistedSlots)
        {
            if (!string.IsNullOrWhiteSpace(persisted.SlotCode))
            {
                persistedBySlot[persisted.SlotCode.Trim()] = persisted;
            }
        }

        var slots = new List<PutToWallSlotData>(8);
        for (var i = 1; i <= 8; i++)
        {
            var slotCode = $"SLOT-{i:D2}";
            var toteBarcode = $"TOTE-{i:D2}";
            var persistedSlot = persistedBySlot.GetValueOrDefault(slotCode);
            if (i <= candidateLines.Count)
            {
                var (ownerTask, line) = candidateLines[i - 1];
                var baseSorted = persistedSlot?.SortedQuantity ?? 0m;
                var reqQty = line.RequestedQuantity > 0m ? line.RequestedQuantity : 1m;
                var sortedQty = Math.Min(reqQty, baseSorted);
                var remQty = Math.Max(0m, reqQty - sortedQty);
                var slotStatus = remQty <= 0m
                    ? "COMPLETED"
                    : (sortedQty > 0m ? "IN_PROGRESS" : "WAITING");

                slots.Add(new PutToWallSlotData(
                    i,
                    slotCode,
                    toteBarcode,
                    ownerTask.Id,
                    line.Id,
                    !string.IsNullOrWhiteSpace(ownerTask.SourceCode) ? ownerTask.SourceCode : ownerTask.TaskCode,
                    line.ProductId,
                    line.Product?.Code ?? $"SKU-{line.ProductId}",
                    line.Product?.Name ?? $"Sản phẩm #{line.ProductId}",
                    reqQty,
                    sortedQty,
                    remQty,
                    slotStatus,
                    persistedSlot?.PackingSessionId,
                    persistedSlot?.PackingSessionCode));
            }
            else
            {
                slots.Add(new PutToWallSlotData(
                    i,
                    slotCode,
                    toteBarcode,
                    task?.Id,
                    null,
                    $"WAVE-SLOT-{i:D2}",
                    null,
                    string.Empty,
                    string.Empty,
                    0m,
                    0m,
                    0m,
                    "EMPTY",
                    persistedSlot?.PackingSessionId,
                    persistedSlot?.PackingSessionCode));
            }
        }

        return (task, slots);
    }

    public async Task<WmsEndpointOutcome> GetPutToWallSlotsAsync(
        string? id = null,
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var (task, slots) = await BuildPutToWallGridAsync(id, warehouseId, warehouseAuth, cancellationToken);
        var activeSlotsCount = slots.Count(s => s.SlotStatus != "EMPTY");
        var completedSlotsCount = slots.Count(s => s.SlotStatus == "COMPLETED");

        return WmsEndpointOutcome.Ok(new
        {
            pickingTaskId = task?.Id ?? 0,
            taskCode = task?.TaskCode ?? "WAVE-PUT-WALL",
            warehouseId = task?.WarehouseId ?? warehouseId ?? 1,
            totalSlots = 8,
            activeSlotsCount,
            completedSlotsCount,
            slots,
            items = slots,
            statusLabel = $"Tường chia chọn Put-to-Wall 8 ô (SLOT-01 → SLOT-08): {completedSlotsCount}/{activeSlotsCount} ô hoàn tất"
        });
    }
}
