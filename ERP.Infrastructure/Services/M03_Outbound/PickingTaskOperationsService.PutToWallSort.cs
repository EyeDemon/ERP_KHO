using ERP.Application.Common;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class PickingTaskOperationsService
{
    public async Task<WmsEndpointOutcome> PutToWallSortAsync(
        string? id = null,
        PutToWallSortInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var (task, slots) = await BuildPutToWallGridAsync(id, null, warehouseAuth, cancellationToken);
        var activeSlots = slots.Where(s => s.ProductId.HasValue).ToList();
        if (activeSlots.Count == 0)
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "PUT_TO_WALL_NO_ACTIVE_SLOTS",
                errorCode = "PUT_TO_WALL_NO_ACTIVE_SLOTS",
                message = "Không tìm thấy dòng sản phẩm cần chia chọn trên tường Put-to-Wall."
            });
        }

        var rawSku = (request?.ScannedSkuOrBarcode ?? request?.ProductBarcode)?.Trim();
        PutToWallSlotData? matchedSlot = null;

        if (!string.IsNullOrWhiteSpace(rawSku))
        {
            matchedSlot = activeSlots.FirstOrDefault(s =>
                string.Equals(s.ProductCode, rawSku, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.ProductId?.ToString(), rawSku, StringComparison.OrdinalIgnoreCase));

            if (matchedSlot == null && db != null)
            {
                var prodIdByBarcode = await db.ProductBarcodes
                    .AsNoTracking()
                    .Where(b => b.Value == rawSku)
                    .Select(b => (int?)b.ProductId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (prodIdByBarcode.HasValue)
                {
                    matchedSlot = activeSlots.FirstOrDefault(s => s.ProductId == prodIdByBarcode.Value);
                }
            }
        }
        else
        {
            matchedSlot = activeSlots.FirstOrDefault(s => s.RemainingQty > 0m) ?? activeSlots.First();
        }

        if (matchedSlot == null)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new
            {
                code = "PUT_TO_WALL_SKU_NOT_IN_WAVE",
                errorCode = "PUT_TO_WALL_SKU_NOT_IN_WAVE",
                scannedSkuOrBarcode = rawSku,
                message = $"Sản phẩm '{rawSku}' không thuộc danh sách chia chọn của sóng lấy hàng hiện tại."
            });
        }

        var scannedSlot = (request?.ScannedSlotCode ?? request?.SlotCode ?? request?.ScannedToteBarcode)?.Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(scannedSlot))
        {
            var matchesExpected =
                string.Equals(scannedSlot, matchedSlot.SlotCode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scannedSlot, matchedSlot.ToteBarcode, StringComparison.OrdinalIgnoreCase);

            if (!matchesExpected)
            {
                var alternateSameSkuSlot = activeSlots.FirstOrDefault(s =>
                    s.ProductId == matchedSlot.ProductId &&
                    (string.Equals(scannedSlot, s.SlotCode, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scannedSlot, s.ToteBarcode, StringComparison.OrdinalIgnoreCase)));

                if (alternateSameSkuSlot != null)
                {
                    matchedSlot = alternateSameSkuSlot;
                }
                else
                {
                    return WmsEndpointOutcome.UnprocessableEntity(new
                    {
                        code = "PUT_TO_WALL_WRONG_SLOT",
                        errorCode = "PUT_TO_WALL_WRONG_SLOT",
                        scannedSkuOrBarcode = matchedSlot.ProductCode,
                        scannedSlotCode = scannedSlot,
                        expectedSlotCode = matchedSlot.SlotCode,
                        expectedToteBarcode = matchedSlot.ToteBarcode,
                        message = $"Sai ô chia chọn! Sản phẩm {matchedSlot.ProductCode} phải bỏ vào ô {matchedSlot.SlotCode}"
                    });
                }
            }
        }

        var sortQty = request?.Quantity is > 0m ? request.Quantity : 1m;
        var now = DateTime.UtcNow;
        int? resolvedPackingSessionId = null;
        string? resolvedPackingSessionCode = null;

        if (db != null && task != null)
        {
            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);

            var slotEntity = await db.PutToWallSlots
                .FirstOrDefaultAsync(s => s.PickingTaskId == task.Id && s.SlotCode == matchedSlot.SlotCode, cancellationToken);
            var newSortedQty = Math.Min(matchedSlot.RequiredQty, (slotEntity?.SortedQuantity ?? matchedSlot.SortedQty) + sortQty);
            var newSlotStatus = newSortedQty >= matchedSlot.RequiredQty
                ? "COMPLETED"
                : (newSortedQty > 0m ? "IN_PROGRESS" : "WAITING");

            if (slotEntity == null)
            {
                slotEntity = new ERP.Domain.Entities.PutToWallSlot
                {
                    WarehouseId = task.WarehouseId,
                    PickingTaskId = task.Id,
                    PickingTaskLineId = matchedSlot.PickingTaskLineId,
                    SlotIndex = matchedSlot.SlotIndex,
                    SlotCode = matchedSlot.SlotCode,
                    ToteBarcode = matchedSlot.ToteBarcode,
                    OrderReference = matchedSlot.OrderReference,
                    ProductId = matchedSlot.ProductId,
                    ProductCode = matchedSlot.ProductCode,
                    RequiredQuantity = matchedSlot.RequiredQty,
                    SortedQuantity = newSortedQty,
                    SlotStatus = newSlotStatus,
                    LastSortedByUserId = effectiveActorId,
                    UpdatedAtUtc = now
                };
                db.PutToWallSlots.Add(slotEntity);
            }
            else
            {
                slotEntity.SortedQuantity = newSortedQty;
                slotEntity.SlotStatus = newSlotStatus;
                slotEntity.LastSortedByUserId = effectiveActorId;
                slotEntity.UpdatedAtUtc = now;
            }

            if (newSlotStatus == "COMPLETED")
            {
                var packingSession = await db.PackingSessions
                    .FirstOrDefaultAsync(ps => ps.PickingTaskId == task.Id, cancellationToken);
                if (packingSession == null)
                {
                    packingSession = new ERP.Domain.Entities.PackingSession
                    {
                        SessionCode = $"PACK-PTW-{task.Id}-{now:HHmmss}",
                        PickingTaskId = task.Id,
                        WarehouseId = task.WarehouseId,
                        Status = PackingSessionStatus.Open,
                        CreatedAt = now,
                        CreatedBy = effectiveActorId ?? 1
                    };
                    db.PackingSessions.Add(packingSession);
                    await db.SaveChangesAsync(cancellationToken);
                }

                slotEntity.PackingSessionId = packingSession.Id;
                slotEntity.PackingSessionCode = packingSession.SessionCode;

                var taskSlots = activeSlots.Where(s => s.PickingTaskId == task.Id).ToList();
                var allTaskSlotsCompleted = taskSlots.Count > 0 && taskSlots.All(s =>
                    string.Equals(s.SlotCode, matchedSlot.SlotCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(s.SlotStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase));
                if (allTaskSlotsCompleted)
                {
                    var trackedTask = await db.PickingTasks
                        .FirstOrDefaultAsync(t => t.Id == task.Id, cancellationToken);
                    if (trackedTask != null && trackedTask.Status != PickingTaskStatus.Completed)
                    {
                        trackedTask.Status = PickingTaskStatus.Completed;
                        trackedTask.CompletedAt ??= now;
                    }
                }
            }

            resolvedPackingSessionId = slotEntity.PackingSessionId;
            resolvedPackingSessionCode = slotEntity.PackingSessionCode;

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = task.WarehouseId,
                EntityName = "PutToWallSortation",
                EntityId = task.Id,
                Action = "PUT_TO_WALL_SLOT_SORTED",
                OldValues = matchedSlot.SlotCode,
                NewValues = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{matchedSlot.ProductCode}|{sortQty}"),
                Reason = $"Chia chọn Put-to-Wall: {sortQty} x {matchedSlot.ProductCode} vào {matchedSlot.SlotCode} ({matchedSlot.ToteBarcode})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        var (_, updatedSlots) = await BuildPutToWallGridAsync(task?.Id.ToString() ?? id, task?.WarehouseId, warehouseAuth, cancellationToken);
        var updatedTargetSlot = updatedSlots.First(s => s.SlotCode == matchedSlot.SlotCode);
        var finalPackingSessionId = resolvedPackingSessionId ?? updatedTargetSlot.PackingSessionId;
        var finalPackingSessionCode = resolvedPackingSessionCode ?? updatedTargetSlot.PackingSessionCode;
        var autoCreatedPackingSession = finalPackingSessionId.HasValue;
        var handoffStatus = autoCreatedPackingSession ? "READY_FOR_PACKING" : "SORTING_IN_PROGRESS";

        return WmsEndpointOutcome.Ok(new
        {
            pickingTaskId = task?.Id ?? 0,
            taskCode = task?.TaskCode ?? "WAVE-PUT-WALL",
            matched = true,
            sortedSlotCode = updatedTargetSlot.SlotCode,
            toteBarcode = updatedTargetSlot.ToteBarcode,
            orderReference = updatedTargetSlot.OrderReference,
            productId = updatedTargetSlot.ProductId,
            productCode = updatedTargetSlot.ProductCode,
            sortedQuantity = sortQty,
            slotSortedTotal = updatedTargetSlot.SortedQty,
            slotRemainingQty = updatedTargetSlot.RemainingQty,
            slotStatus = updatedTargetSlot.SlotStatus,
            autoCreatedPackingSession,
            packingSessionId = finalPackingSessionId,
            packingSessionCode = finalPackingSessionCode,
            handoffStatus,
            slots = updatedSlots,
            statusLabel = autoCreatedPackingSession && !string.IsNullOrWhiteSpace(finalPackingSessionCode)
                ? $"Đã chia chọn {sortQty} x {updatedTargetSlot.ProductCode} vào ô {updatedTargetSlot.SlotCode} ({updatedTargetSlot.ToteBarcode}) • Khởi tạo phiên đóng gói {finalPackingSessionCode}"
                : $"Đã chia chọn {sortQty} x {updatedTargetSlot.ProductCode} vào ô {updatedTargetSlot.SlotCode} ({updatedTargetSlot.ToteBarcode})"
        });
    }
}
