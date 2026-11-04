using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderOperationsService
{
    private async Task<(SalesOrder? Order, ExportReceipt? Receipt)> ResolveWebhookOrderAndReceiptAsync(
        BuyerCancellationWebhookInput request,
        CancellationToken cancellationToken)
    {
        SalesOrder? order = null;
        if (request.SalesOrderId is > 0)
        {
            order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == request.SalesOrderId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.OrderNumber))
        {
            var code = request.OrderNumber.Trim();
            order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.OrderCode == code || o.ExternalOrderId == code, cancellationToken);
        }

        ExportReceipt? receipt = null;
        if (request.ExportReceiptId is > 0)
        {
            receipt = await _db.ExportReceipts.Include(r => r.Details).FirstOrDefaultAsync(r => r.Id == request.ExportReceiptId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.MarketplaceOrderId))
        {
            var mpOrder = request.MarketplaceOrderId.Trim();
            receipt = await _db.ExportReceipts.Include(r => r.Details).FirstOrDefaultAsync(r => r.MarketplaceOrderId == mpOrder, cancellationToken);
        }
        else if (order != null)
        {
            receipt = await _db.ExportReceipts.Include(r => r.Details).FirstOrDefaultAsync(r => r.SalesOrderId == order.Id, cancellationToken);
        }

        if (order == null && receipt?.SalesOrderId is > 0)
        {
            order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == receipt.SalesOrderId.Value, cancellationToken);
        }

        return (order, receipt);
    }

    private async Task<List<StockAllocation>> LoadActiveOrderAndReceiptAllocationsAsync(
        SalesOrder? order,
        ExportReceipt? receipt,
        CancellationToken cancellationToken)
    {
        var allocations = new List<StockAllocation>();
        if (order != null)
        {
            var soAllocs = await _db.StockAllocations
                .Include(a => a.Reservation)
                .Where(a => a.Reservation != null &&
                            a.Reservation.SourceType == "SalesOrder" &&
                            a.Reservation.SourceId == order.Id &&
                            (a.Status == StockAllocationStatus.Active ||
                             a.Status == StockAllocationStatus.Picked))
                .ToListAsync(cancellationToken);
            allocations.AddRange(soAllocs);
        }
        if (receipt != null)
        {
            var erAllocs = await _db.StockAllocations
                .Include(a => a.Reservation)
                .Where(a => a.Reservation != null &&
                            a.Reservation.SourceType == "ExportReceipt" &&
                            a.Reservation.SourceId == receipt.Id &&
                            (a.Status == StockAllocationStatus.Active ||
                             a.Status == StockAllocationStatus.Picked))
                .ToListAsync(cancellationToken);
            allocations.AddRange(erAllocs);
        }
        return allocations;
    }

    private async Task<int?> ResolveValidAuditUserIdAsync(int resolvedActorId, CancellationToken cancellationToken)
    {
        return await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == resolvedActorId)
            .Select(u => (int?)u.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? await _db.Users
                .AsNoTracking()
                .OrderBy(u => u.Id)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<int?> CreateWebhookPutbackTaskAsync(
        SalesOrder? order,
        ExportReceipt? receipt,
        IReadOnlyList<StockAllocation> allocations,
        int targetWarehouseId,
        int creatorUserId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var defaultLocId = await _db.WarehouseLocations
            .AsNoTracking()
            .Where(l => l.WarehouseId == targetWarehouseId && l.IsActive)
            .OrderBy(l => l.Id)
            .Select(l => l.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (defaultLocId <= 0)
            return null;

        var putbackLines = new List<(int ProductId, decimal Quantity, int SourceLocationId)>();
        if (order?.Lines is { Count: > 0 })
        {
            foreach (var line in order.Lines.Where(l => l.ProductId > 0 && l.OrderedQuantity > 0m))
            {
                var allocLoc = allocations.FirstOrDefault(a => a.ProductId == line.ProductId && a.LocationId > 0)?.LocationId ?? defaultLocId;
                putbackLines.Add((line.ProductId, line.OrderedQuantity, allocLoc));
            }
        }
        else if (receipt?.Details is { Count: > 0 })
        {
            foreach (var detail in receipt.Details.Where(d => d.ProductId > 0 && d.Quantity > 0m))
            {
                var allocLoc = allocations.FirstOrDefault(a => a.ProductId == detail.ProductId && a.LocationId > 0)?.LocationId ?? defaultLocId;
                putbackLines.Add((detail.ProductId, detail.Quantity, allocLoc));
            }
        }
        else
        {
            foreach (var alloc in allocations.Where(a => a.ProductId > 0 && a.Quantity > 0m))
            {
                putbackLines.Add((alloc.ProductId, alloc.Quantity, alloc.LocationId > 0 ? alloc.LocationId : defaultLocId));
            }
        }

        if (putbackLines.Count == 0)
            return null;

        var defaultUnitId = await _db.Units
            .AsNoTracking()
            .OrderBy(u => u.Id)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (defaultUnitId <= 0) defaultUnitId = 1;

        var putbackReceipt = new ImportReceipt
        {
            Code = $"PB-{order?.Id ?? receipt?.Id ?? 1}-{now:HHmmssfff}",
            WarehouseId = targetWarehouseId,
            Status = ReceiptStatus.Received,
            CreatedBy = creatorUserId,
            CreatedAt = now,
            Note = $"Phiếu hoàn kệ nội bộ cho đơn hủy ({order?.OrderCode ?? receipt?.Code})"
        };

        foreach (var pb in putbackLines)
        {
            var prodUnitId = await _db.Products
                .AsNoTracking()
                .Where(p => p.Id == pb.ProductId && p.UnitId > 0)
                .Select(p => (int?)p.UnitId)
                .FirstOrDefaultAsync(cancellationToken) ?? defaultUnitId;

            putbackReceipt.Details.Add(new ImportReceiptDetail
            {
                ProductId = pb.ProductId,
                Quantity = pb.Quantity,
                ExpectedQuantity = pb.Quantity,
                ReceivedQuantity = pb.Quantity,
                AcceptedQuantity = pb.Quantity,
                BaseExpectedQuantity = pb.Quantity,
                BaseReceivedQuantity = pb.Quantity,
                BaseAcceptedQuantity = pb.Quantity,
                OperationUnitId = prodUnitId,
                OperationUnitCodeSnapshot = "CÁI",
                BaseUnitId = prodUnitId,
                BaseUnitCodeSnapshot = "CÁI",
                ConversionFactor = 1m,
                ConversionVersion = 1
            });
        }

        _db.ImportReceipts.Add(putbackReceipt);
        await _db.SaveChangesAsync(cancellationToken);

        var putbackTask = new PutawayTask
        {
            TaskType = "Putback",
            ReceiptId = putbackReceipt.Id,
            WarehouseId = targetWarehouseId,
            Status = PutawayTaskStatus.Open,
            CreatedBy = creatorUserId,
            CreatedAt = now,
            ExceptionReason = $"Cất trả hàng cho đơn hủy giữa chừng ({order?.OrderCode ?? receipt?.Code})"
        };

        var savedDetails = putbackReceipt.Details.ToList();
        for (var idx = 0; idx < putbackLines.Count; idx++)
        {
            var pb = putbackLines[idx];
            var detail = savedDetails[idx];
            putbackTask.Items.Add(new PutawayTaskItem
            {
                ReceiptLineId = detail.Id,
                ProductId = pb.ProductId,
                SourceLocationId = pb.SourceLocationId,
                OperationUnitId = detail.OperationUnitId,
                OperationUnitCodeSnapshot = detail.OperationUnitCodeSnapshot,
                BaseUnitId = detail.BaseUnitId,
                BaseUnitCodeSnapshot = detail.BaseUnitCodeSnapshot,
                ConversionFactorSnapshot = 1m,
                ConversionVersionSnapshot = 1,
                RequiredOperationQuantity = pb.Quantity,
                RequiredBaseQuantity = pb.Quantity,
                MovedBaseQuantity = 0m
            });
        }

        _db.PutawayTasks.Add(putbackTask);
        await _db.SaveChangesAsync(cancellationToken);
        return putbackTask.Id;
    }

    private async Task ReleaseWebhookReservationsAndAllocationsAsync(
        SalesOrder? order,
        ExportReceipt? receipt,
        IReadOnlyList<StockAllocation> allocations,
        int resolvedActorId,
        string? buyerReason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var reason = buyerReason ?? "Khách hủy đơn trên sàn trước khi nhặt hàng";
        foreach (var alloc in allocations)
        {
            var bucket = await _db.InventoryStocks.FirstOrDefaultAsync(
                s => s.WarehouseId == alloc.WarehouseId &&
                     s.ProductId == alloc.ProductId &&
                     s.LocationId == alloc.LocationId &&
                     s.LotId == alloc.LotId &&
                     s.SerialId == alloc.SerialId &&
                     s.Status == alloc.InventoryStatus,
                cancellationToken);
            if (bucket != null)
            {
                bucket.AllocatedQuantity = Math.Max(0m, bucket.AllocatedQuantity - alloc.Quantity);
                bucket.ReservedQuantity = Math.Max(bucket.AllocatedQuantity, bucket.ReservedQuantity - alloc.Quantity);
                bucket.LastUpdated = now;
            }
            alloc.Status = StockAllocationStatus.Released;
            alloc.ReleasedAt = now;
            alloc.ReleasedBy = resolvedActorId;
            alloc.ReleaseReason = reason;
            if (alloc.Reservation != null)
            {
                alloc.Reservation.Status = StockReservationStatus.Released;
                alloc.Reservation.ReleasedAt = now;
                alloc.Reservation.ReleasedBy = resolvedActorId;
                alloc.Reservation.ReleaseReason = reason;
            }
        }

        var unallocatedReservations = new List<StockReservation>();
        if (order != null)
        {
            var soRes = await _db.StockReservations
                .Where(r => r.SourceType == "SalesOrder" && r.SourceId == order.Id && r.Status == StockReservationStatus.Active)
                .ToListAsync(cancellationToken);
            unallocatedReservations.AddRange(soRes);
        }
        if (receipt != null)
        {
            var erRes = await _db.StockReservations
                .Where(r => r.SourceType == "ExportReceipt" && r.SourceId == receipt.Id && r.Status == StockReservationStatus.Active)
                .ToListAsync(cancellationToken);
            unallocatedReservations.AddRange(erRes);
        }

        foreach (var res in unallocatedReservations)
        {
            var bucket = await _db.InventoryStocks
                .Where(s => s.WarehouseId == res.WarehouseId && s.ProductId == res.ProductId && s.Status == InventoryStatus.Available)
                .OrderByDescending(s => s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);
            if (bucket != null)
            {
                bucket.ReservedQuantity = Math.Max(bucket.AllocatedQuantity, bucket.ReservedQuantity - res.Quantity);
                bucket.LastUpdated = now;
            }
            res.Status = StockReservationStatus.Released;
            res.ReleasedAt = now;
            res.ReleasedBy = resolvedActorId;
            res.ReleaseReason = reason;
        }

        if (order != null)
        {
            order.Status = SalesOrderStatus.Cancelled;
            order.CancellationReason = reason;
            order.CancelledAt = now;
            foreach (var line in order.Lines)
            {
                line.CancelledQuantity = line.OrderedQuantity;
            }
        }
        if (receipt != null)
        {
            receipt.Status = ReceiptStatus.Cancelled;
        }
    }
}
