using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ProductOperationsService
{
    private static string NormalizeKitUomCode(string? uomCode)
    {
        if (string.IsNullOrWhiteSpace(uomCode))
        {
            return "PCS";
        }

        var trimmed = uomCode.Trim();
        return string.Equals(trimmed, "Vỏ hộp (Cái)", StringComparison.OrdinalIgnoreCase)
            ? "Cái"
            : trimmed;
    }

    public async Task<WmsEndpointOutcome> CancelKitWorkOrderAsync(
        int workOrderId,
        CancelKitWorkOrderInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var ownTx = _db.Database.CurrentTransaction == null;
        await using var tx = ownTx ? await _db.Database.BeginTransactionAsync(cancellationToken) : null;

        var workOrder = await _db.KitWorkOrders
            .Include(w => w.ComponentLines)
            .FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder == null)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(404, new
            {
                code = "KIT_WORK_ORDER_NOT_FOUND",
                errorCode = "KIT_WORK_ORDER_NOT_FOUND",
                message = $"Không tìm thấy lệnh gia công KIT #{workOrderId}."
            });
        }

        if (string.Equals(workOrder.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Ok(new
            {
                id = workOrder.Id,
                workOrderId = workOrder.Id,
                workOrderCode = workOrder.WorkOrderCode,
                status = workOrder.Status,
                reversedTransactionsCount = 0
            });
        }

        var now = DateTime.UtcNow;
        var actorId = actorUserId is > 0 ? actorUserId.Value : (workOrder.CreatedByUserId ?? 1);
        var reversedCount = 0;

        if (string.Equals(workOrder.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            var lockProductIds = workOrder.ComponentLines
                .Select(l => l.ComponentProductId)
                .Append(workOrder.KitProductId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();
            await _db.AcquireInventoryAllocationLocksAsync(workOrder.WarehouseId, lockProductIds, cancellationToken);

            var kitRefTypes = new[] { "KitComponentIssue", "KitFinishedReceipt", "KitDisassembleIssue", "KitComponentReturn" };
            var origTxs = await _db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.ReferenceId == workOrder.Id && t.ReferenceType != null && kitRefTypes.Contains(t.ReferenceType))
                .OrderBy(t => t.Id)
                .ToListAsync(cancellationToken);

            foreach (var orig in origTxs)
            {
                var reverseDelta = -orig.Quantity;
                var bucket = await _db.InventoryStocks
                    .FirstOrDefaultAsync(s =>
                        s.WarehouseId == orig.WarehouseId &&
                        s.ProductId == orig.ProductId &&
                        s.LocationId == orig.LocationId &&
                        s.LotId == orig.LotId &&
                        s.SerialId == orig.SerialId &&
                        s.HandlingUnitId == orig.HandlingUnitId &&
                        s.OwnerId == orig.OwnerId &&
                        s.Status == orig.InventoryStatus,
                        cancellationToken);

                if (bucket != null)
                {
                    if (bucket.Quantity + reverseDelta < 0m)
                    {
                        if (tx != null) await tx.RollbackAsync(cancellationToken);
                        return WmsEndpointOutcome.Json(422, new
                        {
                            code = "KIT_REVERSAL_INSUFFICIENT_STOCK",
                            errorCode = "KIT_REVERSAL_INSUFFICIENT_STOCK",
                            message = $"Không đủ tồn kho tại vị trí #{orig.LocationId} để đảo bút toán lệnh KIT {workOrder.WorkOrderCode}."
                        });
                    }
                    bucket.Quantity += reverseDelta;
                    bucket.LastUpdated = now;
                    bucket.RefreshGrainHash();
                }
                else if (reverseDelta > 0m)
                {
                    var restored = new InventoryStock
                    {
                        WarehouseId = orig.WarehouseId,
                        ProductId = orig.ProductId,
                        LocationId = orig.LocationId,
                        LotId = orig.LotId,
                        SerialId = orig.SerialId,
                        HandlingUnitId = orig.HandlingUnitId,
                        OwnerId = orig.OwnerId,
                        Status = orig.InventoryStatus,
                        Quantity = reverseDelta,
                        MovingAverageCost = orig.UnitCost,
                        LastUpdated = now
                    };
                    restored.RefreshGrainHash();
                    _db.InventoryStocks.Add(restored);
                }

                _db.InventoryTransactions.Add(new InventoryTransaction
                {
                    ProductId = orig.ProductId,
                    WarehouseId = orig.WarehouseId,
                    LocationId = orig.LocationId,
                    FromLocationId = orig.ToLocationId,
                    ToLocationId = orig.FromLocationId,
                    LotId = orig.LotId,
                    SerialId = orig.SerialId,
                    HandlingUnitId = orig.HandlingUnitId,
                    OwnerId = orig.OwnerId,
                    InventoryStatus = orig.InventoryStatus,
                    FromInventoryStatus = orig.ToInventoryStatus,
                    ToInventoryStatus = orig.FromInventoryStatus,
                    TransactionType = TransactionType.Reversal,
                    Quantity = reverseDelta,
                    UnitCost = orig.UnitCost,
                    ReferenceId = workOrder.Id,
                    ReferenceType = orig.ReferenceType,
                    SourceDocumentLineId = orig.SourceDocumentLineId,
                    ReversalOfTransactionId = orig.Id,
                    ReasonCode = "KIT_WO_CANCEL_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = actorId,
                    Note = $"Đảo bút toán hủy lệnh KIT {workOrder.WorkOrderCode} (OrigTx=#{orig.Id})"
                });
                reversedCount++;
            }
        }

        workOrder.Status = "Cancelled";
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            WarehouseId = workOrder.WarehouseId,
            Action = "KIT_WORK_ORDER_CANCELLED",
            EntityName = "KitWorkOrder",
            EntityId = workOrder.Id,
            NewValues = $"Code={workOrder.WorkOrderCode};Status=Cancelled;ReversedTxs={reversedCount}",
            Reason = request?.Reason ?? $"Hủy/Đảo lệnh gia công KIT {workOrder.WorkOrderCode}",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            id = workOrder.Id,
            workOrderId = workOrder.Id,
            workOrderCode = workOrder.WorkOrderCode,
            status = workOrder.Status,
            reversedTransactionsCount = reversedCount,
            statusLabel = $"Đã hủy lệnh gia công KIT {workOrder.WorkOrderCode} (Đảo {reversedCount} bút toán sổ cái)"
        });
    }

    private static int? ParseOptionalInt(JsonElement? element)
    {
        if (!element.HasValue)
        {
            return null;
        }

        var el = element.Value;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var numVal))
        {
            return numVal;
        }

        if (el.ValueKind == JsonValueKind.String)
        {
            var str = el.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(str) && int.TryParse(str, out var parsedVal))
            {
                return parsedVal;
            }
        }

        return null;
    }
}
