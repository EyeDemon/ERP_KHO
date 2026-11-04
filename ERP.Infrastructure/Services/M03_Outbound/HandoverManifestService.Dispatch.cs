using System.Data;
using System.Text.Json;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class HandoverManifestService
{
    private async Task<HandoverManifestOperationResult?> DispatchManifestReceiptsAsync(
        List<ExportReceipt> receipts,
        int actorUserId,
        int? validAuditUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (receipts.Count == 0)
        {
            return null;
        }

        foreach (var receipt in receipts)
        {
            var linkedOrderCancelled = receipt.SalesOrderId.HasValue && await _db.SalesOrders
                .AsNoTracking()
                .AnyAsync(o => o.Id == receipt.SalesOrderId.Value &&
                               (o.Status == SalesOrderStatus.Cancelled ||
                                o.Status == SalesOrderStatus.CancelPendingPutback),
                          cancellationToken);

            if (receipt.Status == ReceiptStatus.Cancelled ||
                receipt.Status == ReceiptStatus.CancelPendingPutback ||
                linkedOrderCancelled)
            {
                return HandoverManifestOperationResult.Fail(409, new
                {
                    code = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    errorCode = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                    exportReceiptId = receipt.Id,
                    exportReceiptCode = receipt.Code,
                    trackingNumber = receipt.TrackingNumber,
                    message = "Đơn hàng đã hủy hoặc đang chờ hoàn kệ (Putback), không thể bàn giao vận chuyển."
                });
            }
        }

        foreach (var receipt in receipts.Where(r => r.Status == ReceiptStatus.Approved))
        {
            var alreadyDeductedBeforeCall = await _db.InventoryTransactions.AsNoTracking().AnyAsync(
                t => t.WarehouseId == receipt.WarehouseId &&
                     ((t.ReferenceType == "ExportReceipt" && t.ReferenceId == receipt.Id &&
                       (t.TransactionType == TransactionType.Export || t.TransactionType == TransactionType.Ship)) ||
                      (receipt.ShipmentId.HasValue &&
                       t.ReferenceType == "Shipment" &&
                       t.ReferenceId == receipt.ShipmentId.Value &&
                       t.TransactionType == TransactionType.Ship)),
                cancellationToken);

            if (!alreadyDeductedBeforeCall && !receipt.ShipmentId.HasValue)
            {
                try
                {
                    await _exportReceiptService.DispatchAsync(receipt.Id, actorUserId);
                }
                catch (Exception ex) when (ex is BusinessRuleException or ConcurrencyException)
                {
                    return HandoverManifestOperationResult.Fail(409, new
                    {
                        code = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                        errorCode = "ORDER_CANCELLED_PUTBACK_REQUIRED",
                        exportReceiptId = receipt.Id,
                        message = ex.Message
                    });
                }
            }

            var refreshed = await _db.ExportReceipts
                .Include(r => r.Details)
                .FirstOrDefaultAsync(r => r.Id == receipt.Id, cancellationToken);

            if (refreshed != null && refreshed.Status != ReceiptStatus.Dispatched)
            {
                var isRelational = _db.Database.IsRelational();
                await using var tx = isRelational && _db.Database.CurrentTransaction == null
                    ? await _db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
                    : null;

                var productIds = refreshed.Details.Select(d => d.ProductId).Distinct().OrderBy(id => id).ToList();
                if (productIds.Count > 0)
                {
                    await _db.AcquireInventoryAllocationLocksAsync(refreshed.WarehouseId, productIds, cancellationToken);
                }

                var alreadyDeductedAfterLock = await _db.InventoryTransactions.AnyAsync(
                    t => t.WarehouseId == refreshed.WarehouseId &&
                         ((t.ReferenceType == "ExportReceipt" && t.ReferenceId == refreshed.Id &&
                           (t.TransactionType == TransactionType.Export || t.TransactionType == TransactionType.Ship)) ||
                          (refreshed.ShipmentId.HasValue &&
                           t.ReferenceType == "Shipment" &&
                           t.ReferenceId == refreshed.ShipmentId.Value &&
                           t.TransactionType == TransactionType.Ship)),
                    cancellationToken);

                if (!alreadyDeductedAfterLock)
                {
                    foreach (var detail in refreshed.Details)
                    {
                        var stocks = await _db.InventoryStocks
                            .Where(s => s.WarehouseId == refreshed.WarehouseId &&
                                        s.ProductId == detail.ProductId &&
                                        s.Status == InventoryStatus.Available)
                            .OrderByDescending(s => s.Quantity)
                            .ToListAsync(cancellationToken);

                        var remainingToDeduct = detail.Quantity;
                        decimal unitCostSnapshot = 0m;
                        int? deductedLocationId = null;
                        int? deductedLotId = null;
                        int? deductedSerialId = null;
                        foreach (var stock in stocks)
                        {
                            if (remainingToDeduct <= 0m) break;
                            unitCostSnapshot = stock.MovingAverageCost;
                            deductedLocationId ??= stock.LocationId;
                            deductedLotId ??= stock.LotId;
                            deductedSerialId ??= stock.SerialId;
                            var deduct = Math.Min(stock.Quantity, remainingToDeduct);
                            if (deduct <= 0m) deduct = remainingToDeduct;
                            stock.Quantity = Math.Max(0m, stock.Quantity - deduct);
                            if (stock.ReservedQuantity > 0m)
                            {
                                stock.ReservedQuantity = Math.Max(0m, stock.ReservedQuantity - deduct);
                            }
                            if (stock.AllocatedQuantity > 0m)
                            {
                                stock.AllocatedQuantity = Math.Max(0m, stock.AllocatedQuantity - deduct);
                            }
                            stock.LastUpdated = now;
                            remainingToDeduct -= deduct;
                        }

                        var reservations = await _db.StockReservations
                            .Include(r => r.Allocations)
                            .Where(r => r.WarehouseId == refreshed.WarehouseId &&
                                        r.ProductId == detail.ProductId &&
                                        r.Status == StockReservationStatus.Active &&
                                        ((r.SourceType == "ExportReceipt" && r.SourceId == refreshed.Id) ||
                                         (refreshed.SalesOrderId.HasValue && r.SourceType == "SalesOrder" && r.SourceId == refreshed.SalesOrderId.Value)))
                            .ToListAsync(cancellationToken);

                        foreach (var res in reservations)
                        {
                            res.Status = StockReservationStatus.Consumed;
                            res.ConsumedQuantity = res.Quantity;
                            foreach (var alloc in res.Allocations)
                            {
                                alloc.Status = StockAllocationStatus.Picked;
                            }
                        }

                        _db.InventoryTransactions.Add(new InventoryTransaction
                        {
                            ProductId = detail.ProductId,
                            WarehouseId = refreshed.WarehouseId,
                            LocationId = deductedLocationId,
                            LotId = deductedLotId,
                            SerialId = deductedSerialId,
                            InventoryStatus = InventoryStatus.Available,
                            TransactionType = TransactionType.Export,
                            Quantity = -detail.Quantity,
                            UnitCost = unitCostSnapshot,
                            SourceDocumentLineId = detail.Id,
                            ReferenceId = refreshed.Id,
                            ReferenceType = "ExportReceipt",
                            CreatedBy = actorUserId,
                            TransactionDate = now,
                            Note = $"Xuất kho bàn giao vận chuyển phiếu {refreshed.Code}"
                        });
                    }
                }

                refreshed.Status = ReceiptStatus.Dispatched;
                refreshed.DispatchedAt = now;
                receipt.Status = ReceiptStatus.Dispatched;
                receipt.DispatchedAt = now;

                _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
                {
                    EventId = $"EXP-DSP-{refreshed.Id}-{now.Ticks}",
                    EventType = "ExportReceiptDispatched",
                    AggregateType = "ExportReceipt",
                    AggregateId = refreshed.Id,
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        exportReceiptId = refreshed.Id,
                        exportReceiptCode = refreshed.Code,
                        warehouseId = refreshed.WarehouseId,
                        dispatchedAtUtc = now
                    }),
                    Status = "PENDING",
                    CreatedAt = now
                });

                _db.AuditLogs.Add(new AuditLog
                {
                    UserId = validAuditUserId,
                    WarehouseId = refreshed.WarehouseId,
                    EntityName = "ExportReceipt",
                    EntityId = refreshed.Id,
                    Action = "HandoverManifest.DispatchReceipt",
                    NewValues = $"Status=Dispatched; Code={refreshed.Code}",
                    Reason = $"Xuất kho theo biên bản bàn giao vận chuyển ({refreshed.Code})",
                    Result = "Success",
                    Timestamp = now
                });

                await _db.SaveChangesAsync(cancellationToken);
                if (tx != null)
                {
                    await tx.CommitAsync(cancellationToken);
                }
            }
            else if (refreshed != null)
            {
                receipt.Status = refreshed.Status;
                receipt.DispatchedAt = refreshed.DispatchedAt;
            }
        }

        return null;
    }
}
