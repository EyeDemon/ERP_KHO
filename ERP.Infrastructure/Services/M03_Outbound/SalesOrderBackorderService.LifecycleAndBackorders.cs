using ERP.Application.DTOs;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderBackorderService
{
    public Task<SalesOrderDto> HoldSalesOrderAsync(
        int id,
        SalesOrderStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateOrderAsync(id, request.RowVersion, async (order, token) =>
        {
            if (order.Status != SalesOrderStatus.Draft)
                throw Conflict("SALES_ORDER_STATE_CONFLICT", "Chỉ Sales Order DRAFT mới có thể đưa vào HOLD.");
            order.Status = SalesOrderStatus.Hold;
            AddAudit("SalesOrder.Held", order, $"Reason: {request.Reason?.Trim()}");
            await Task.CompletedTask;
        }, cancellationToken);

    public Task<SalesOrderDto> ReleaseSalesOrderAsync(
        int id,
        SalesOrderStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateOrderAsync(id, request.RowVersion, async (order, token) =>
        {
            if (order.Status is not SalesOrderStatus.Draft and not SalesOrderStatus.Hold)
                throw Conflict("SALES_ORDER_ALREADY_RELEASED", "Sales Order không còn ở trạng thái có thể release.");

            if (order.Customer.CreditLimit > 0m)
            {
                var orderTotalAmount = order.Lines.Sum(l =>
                {
                    var effectiveQty = Math.Max(0m, l.OrderedQuantity - l.CancelledQuantity);
                    var subTotal = decimal.Round(effectiveQty * l.UnitPrice, 2, MidpointRounding.AwayFromZero);
                    var tax = decimal.Round(subTotal * (l.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero);
                    return subTotal + tax;
                });

                if (order.Customer.ReceivableBalance + orderTotalAmount > order.Customer.CreditLimit)
                {
                    throw Conflict(
                        "SALES_ORDER_CREDIT_LIMIT_EXCEEDED",
                        $"Vượt hạn mức công nợ khách hàng ({order.Customer.Code}): Công nợ hiện tại ({order.Customer.ReceivableBalance:N2}) + Giá trị đơn ({orderTotalAmount:N2}) vượt hạn mức ({order.Customer.CreditLimit:N2}).");
                }
            }

            var reservations = new List<(StockReservation Reservation, decimal Quantity)>();
            foreach (var line in order.Lines.OrderBy(x => x.ProductId))
            {
                var demand = line.OrderedQuantity - line.CancelledQuantity;
                if (demand <= 0) continue;

                var available = Math.Max(0m, await stockRepository.GetAvailableQuantityAsync(
                    line.ProductId,
                    order.WarehouseId,
                    token));
                var reservable = Math.Min(demand, available);
                if (reservable > 0)
                {
                    var reservation = await reservationService.ReserveForSourceAsync(
                        "SalesOrder",
                        order.Id,
                        order.ExternalOrderId,
                        order.WarehouseId,
                        line.ProductId,
                        reservable,
                        currentUser.UserId,
                        token);
                    reservations.Add((reservation, reservable));
                }

                var shortage = demand - reservable;
                if (shortage > 0)
                {
                    line.Backorder ??= new Backorder
                    {
                        BackorderCode = $"BO-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..33].ToUpperInvariant(),
                        WarehouseId = order.WarehouseId,
                        Quantity = shortage,
                        Status = BackorderStatus.Open,
                        CreatedAt = DateTime.UtcNow
                    };
                }
            }

            order.Status = SalesOrderStatus.Released;
            order.ReleasedAt = DateTime.UtcNow;
            AddAudit("SalesOrder.Released", order, $"Reservations: {reservations.Count}; Reason: {request.Reason?.Trim()}");
            await context.SaveChangesAsync(token);

            foreach (var item in reservations)
            {
                await allocationService.AutoAllocateAsync(new CreateStockAllocationDto
                {
                    ReservationId = item.Reservation.Id,
                    Quantity = item.Quantity
                }, token);

                if (pickingTaskIntegration is not null)
                {
                    await pickingTaskIntegration.EnsureForReservationAsync(item.Reservation.Id, currentUser.UserId, token);
                }
            }
        }, cancellationToken);

    public Task<SalesOrderDto> CancelSalesOrderAsync(
        int id,
        SalesOrderStateCommandDto request,
        CancellationToken cancellationToken = default) =>
        MutateOrderAsync(id, request.RowVersion, async (order, token) =>
        {
            if (order.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Fulfilled)
                throw Conflict("SALES_ORDER_STATE_CONFLICT", "Sales Order không còn ở trạng thái có thể cancel.");

            var progress = await BuildLineProgressAsync(order, token);
            if (order.Status is SalesOrderStatus.Released or SalesOrderStatus.PartiallyFulfilled)
            {
                await reservationService.ReleaseSourceAsync(
                    "SalesOrder",
                    order.Id,
                    currentUser.UserId,
                    string.IsNullOrWhiteSpace(request.Reason) ? "Sales order cancelled" : request.Reason.Trim(),
                    token);
            }

            foreach (var line in order.Lines)
            {
                var shipped = progress.TryGetValue(line.ProductId, out var p) ? p.Shipped : 0m;
                var cancelable = Math.Max(0m, line.OrderedQuantity - shipped - line.CancelledQuantity);
                line.CancelledQuantity += cancelable;
                if (line.Backorder is not null)
                {
                    var remainingBackorder = Math.Max(
                        0m,
                        line.Backorder.Quantity - line.Backorder.RecoveredQuantity - line.Backorder.CancelledQuantity);
                    line.Backorder.CancelledQuantity += remainingBackorder;
                    line.Backorder.Status = BackorderStatus.Cancelled;
                    line.Backorder.UpdatedAt = DateTime.UtcNow;
                    line.Backorder.ResolvedAt = DateTime.UtcNow;
                }
            }

            order.Status = SalesOrderStatus.Cancelled;
            order.CancelledAt = DateTime.UtcNow;
            order.CancellationReason = string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled" : request.Reason.Trim();
            AddAudit("SalesOrder.Cancelled", order, $"Reason: {order.CancellationReason}");
        }, cancellationToken);

    public async Task<IReadOnlyList<BackorderDto>> ListBackordersAsync(
        int? warehouseId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var accessible = await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken);
        if (warehouseId.HasValue)
        {
            await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessible = [warehouseId.Value];
        }

        var parsedStatus = Enum.TryParse<BackorderStatus>(status, true, out var requestedStatus)
            ? requestedStatus
            : (BackorderStatus?)null;

        var rows = await BackorderQuery().AsNoTracking()
            .Where(x => accessible.Contains(x.WarehouseId))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(parsedStatus.HasValue ? 500 : 250)
            .ToListAsync(cancellationToken);

        var distinctOrders = rows.Select(x => x.SalesOrderLine.SalesOrder).DistinctBy(x => x.Id).ToList();
        var cache = await BuildBatchLineProgressAsync(distinctOrders, cancellationToken);
        var result = new List<BackorderDto>(rows.Count);
        foreach (var row in rows)
        {
            var order = row.SalesOrderLine.SalesOrder;
            var progress = cache.GetValueOrDefault(order.Id) ?? new Dictionary<int, LineProgress>();
            var dto = MapBackorder(row, progress);
            if (!parsedStatus.HasValue ||
                string.Equals(dto.Status, parsedStatus.Value.ToString(), StringComparison.OrdinalIgnoreCase))
                result.Add(dto);
            if (result.Count == 250) break;
        }
        return result;
    }

    public async Task<BackorderDto> GetBackorderAsync(int id, CancellationToken cancellationToken = default)
    {
        var row = await LoadBackorderAsync(id, false, cancellationToken);
        var progress = await BuildLineProgressAsync(row.SalesOrderLine.SalesOrder, cancellationToken);
        return MapBackorder(row, progress);
    }

    public async Task<BackorderDto> ReallocateBackorderAsync(
        int id,
        BackorderReallocateDto request,
        CancellationToken cancellationToken = default)
    {
        var lockScope = context.Database.IsSqlServer()
            ? await context.Backorders.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.WarehouseId, x.SalesOrderLine.ProductId })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        await ExecuteSerializableAsync(async token =>
        {
            if (lockScope is not null)
            {
                await AcquireAllocationLocksAsync(lockScope.WarehouseId, [lockScope.ProductId], token);
            }

            var row = await LoadBackorderAsync(id, true, token);
            ApplyVersion(row, request.RowVersion);

            var order = row.SalesOrderLine.SalesOrder;
            if (order.Status is not SalesOrderStatus.Released and not SalesOrderStatus.PartiallyFulfilled)
                throw Conflict("BACKORDER_STATE_CONFLICT", "Sales Order không ở trạng thái cho phép recover backorder.");

            var activeTask = await context.PickingTasks.AsNoTracking()
                .Where(x => x.SourceType == "SalesOrder" &&
                            x.SourceId == order.Id &&
                            x.Status != PickingTaskStatus.Cancelled)
                .OrderByDescending(x => x.Id)
                .FirstOrDefaultAsync(token);
            if (activeTask is not null &&
                activeTask.Status is PickingTaskStatus.InProgress or PickingTaskStatus.ShortPick)
                throw Conflict("BACKORDER_EXECUTION_STARTED", "Picking đã bắt đầu; recovery sau execution cần multi-shipment workflow chưa được bật.");

            var remaining = row.Quantity - row.RecoveredQuantity - row.CancelledQuantity;
            if (remaining <= 0)
                throw Conflict("BACKORDER_RESOLVED", "Backorder không còn số lượng cần recover.");

            var requested = request.Quantity ?? remaining;
            if (requested <= 0 || requested > remaining)
                throw new BusinessRuleException("Số lượng recover backorder không hợp lệ.");

            var available = Math.Max(0m, await stockRepository.GetAvailableQuantityAsync(
                row.SalesOrderLine.ProductId,
                row.WarehouseId,
                token));
            var recover = Math.Min(requested, available);
            if (recover <= 0)
                throw Conflict("BACKORDER_INSUFFICIENT_AVAILABLE", "Chưa có tồn khả dụng để recover backorder.");

            var reservation = await reservationService.IncreaseSourceReservationAsync(
                "SalesOrder",
                order.Id,
                order.ExternalOrderId,
                order.WarehouseId,
                row.SalesOrderLine.ProductId,
                recover,
                currentUser.UserId,
                token);
            var allocatedDtos = await allocationService.AutoAllocateAsync(new CreateStockAllocationDto
            {
                ReservationId = reservation.Id,
                Quantity = recover
            }, token);

            var trackedTask = await context.PickingTasks
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.SourceType == "SalesOrder" &&
                                          x.SourceId == order.Id &&
                                          x.Status != PickingTaskStatus.Cancelled, token);
            if (trackedTask is not null && allocatedDtos.Count > 0)
            {
                var existingAllocIds = trackedTask.Lines.Select(l => l.AllocationId).ToHashSet();
                var missingAllocIds = allocatedDtos
                    .Select(a => a.Id)
                    .Where(aId => aId > 0 && !existingAllocIds.Contains(aId))
                    .ToArray();
                if (missingAllocIds.Length > 0)
                {
                    var newAllocs = await context.StockAllocations
                        .Where(a => missingAllocIds.Contains(a.Id))
                        .OrderBy(a => a.Id)
                        .ToListAsync(token);
                    var nextSeq = trackedTask.Lines.Count == 0 ? 0 : trackedTask.Lines.Max(l => l.Sequence);
                    var reopenToInProgress = trackedTask.Status is PickingTaskStatus.Resolved or PickingTaskStatus.Completed &&
                                             trackedTask.AssignedUserId.HasValue;
                    foreach (var alloc in newAllocs)
                    {
                        if (reopenToInProgress)
                        {
                            alloc.Status = StockAllocationStatus.Picking;
                            alloc.Version++;
                        }
                        trackedTask.Lines.Add(new PickingTaskLine
                        {
                            AllocationId = alloc.Id,
                            ProductId = alloc.ProductId,
                            SourceLocationId = alloc.LocationId,
                            RequestedQuantity = alloc.Quantity,
                            PickedQuantity = 0m,
                            Sequence = ++nextSeq,
                            Status = reopenToInProgress ? PickingTaskLineStatus.InProgress : PickingTaskLineStatus.Open
                        });
                    }
                    if (trackedTask.Status is PickingTaskStatus.Resolved or PickingTaskStatus.Completed)
                    {
                        trackedTask.Status = reopenToInProgress ? PickingTaskStatus.InProgress : PickingTaskStatus.Open;
                        trackedTask.CompletedAt = null;
                    }
                }
            }

            row.RecoveredQuantity += recover;
            row.UpdatedAt = DateTime.UtcNow;
            var after = row.Quantity - row.RecoveredQuantity - row.CancelledQuantity;
            row.Status = after <= 0 ? BackorderStatus.Allocated : BackorderStatus.PartiallyAllocated;
            if (after <= 0) row.ResolvedAt = DateTime.UtcNow;
            AddAudit(
                "Backorder.Reallocated",
                order,
                $"BackorderId: {row.Id}; Quantity: {recover}; Remaining: {Math.Max(0m, after)}");
            await context.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

        return await GetBackorderAsync(id, cancellationToken);
    }

    public async Task<BackorderDto> CancelBackorderAsync(
        int id,
        BackorderCancelDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new BusinessRuleException("Lý do hủy backorder là bắt buộc.");

        await ExecuteSerializableAsync(async token =>
        {
            var row = await LoadBackorderAsync(id, true, token);
            ApplyVersion(row, request.RowVersion);

            var remaining = row.Quantity - row.RecoveredQuantity - row.CancelledQuantity;
            if (remaining <= 0)
                throw Conflict("BACKORDER_RESOLVED", "Backorder không còn phần chưa recover để hủy.");

            var quantity = request.Quantity ?? remaining;
            if (quantity <= 0 || quantity > remaining)
                throw new BusinessRuleException("Số lượng hủy backorder không hợp lệ.");

            row.CancelledQuantity += quantity;
            row.SalesOrderLine.CancelledQuantity += quantity;
            row.UpdatedAt = DateTime.UtcNow;
            var after = row.Quantity - row.RecoveredQuantity - row.CancelledQuantity;
            if (after <= 0)
            {
                row.Status = BackorderStatus.Cancelled;
                row.ResolvedAt = DateTime.UtcNow;
            }
            AddAudit(
                "Backorder.Cancelled",
                row.SalesOrderLine.SalesOrder,
                $"BackorderId: {row.Id}; Quantity: {quantity}; Reason: {request.Reason.Trim()}");
            await context.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

        return await GetBackorderAsync(id, cancellationToken);
    }
}
