using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class SalesOrderBackorderService(
    ErpKhoDbContext context,
    IInventoryStockRepository stockRepository,
    IStockReservationService reservationService,
    IStockAllocationService allocationService,
    IWarehouseAuthorizationService warehouseAuthorization,
    ICurrentUser currentUser,
    IPickingTaskIntegration? pickingTaskIntegration = null) : ISalesOrderBackorderService
{
    private static readonly StockAllocationStatus[] CountedAllocationStatuses =
    [
        StockAllocationStatus.Active,
        StockAllocationStatus.Picking,
        StockAllocationStatus.Picked
    ];

    public async Task<IReadOnlyList<SalesOrderListDto>> ListSalesOrdersAsync(
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

        var parsedStatus = Enum.TryParse<SalesOrderStatus>(status, true, out var requestedStatus)
            ? requestedStatus
            : (SalesOrderStatus?)null;

        var orders = await OrderQuery().AsNoTracking()
            .Where(x => accessible.Contains(x.WarehouseId))
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(parsedStatus.HasValue ? 250 : 100)
            .ToListAsync(cancellationToken);

        var batchProgress = await BuildBatchLineProgressAsync(orders, cancellationToken);
        var result = new List<SalesOrderListDto>(orders.Count);
        foreach (var order in orders)
        {
            var progress = batchProgress.GetValueOrDefault(order.Id) ?? new Dictionary<int, LineProgress>();
            var dto = MapOrderDto(order, progress, false);
            if (!parsedStatus.HasValue ||
                string.Equals(dto.Status, parsedStatus.Value.ToString(), StringComparison.OrdinalIgnoreCase))
                result.Add(dto);
            if (result.Count == 100) break;
        }
        return result;
    }

    public async Task<SalesOrderDto> GetSalesOrderAsync(int id, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(id, false, cancellationToken);
        return (SalesOrderDto)await BuildOrderDtoAsync(order, true, cancellationToken);
    }

    public async Task<SalesOrderDto> CreateSalesOrderAsync(
        CreateSalesOrderDto request,
        CancellationToken cancellationToken = default)
    {
        await warehouseAuthorization.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);
        ValidateCreate(request);

        if (await context.SalesOrders.AnyAsync(x => x.ExternalOrderId == request.ExternalOrderId.Trim(), cancellationToken))
            throw Conflict("SALES_ORDER_DUPLICATE", "ExternalOrderId đã tồn tại.");

        var customer = await context.BusinessPartners.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.CustomerId && x.IsCustomer && x.IsActive,
            cancellationToken) ?? throw new NotFoundException("Không tìm thấy khách hàng đang hoạt động.");

        var productIds = request.Lines.Select(x => x.ProductId).Distinct().ToArray();
        var products = await context.Products.AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => productIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count != productIds.Length)
            throw new NotFoundException("Một hoặc nhiều sản phẩm không tồn tại hoặc đã ngừng hoạt động.");

        foreach (var line in request.Lines)
            ValidateQuantityPrecision(products[line.ProductId].Unit.DecimalPlaces, line.OrderedQuantity);

        var now = DateTime.UtcNow;
        var order = new SalesOrder
        {
            OrderCode = $"SO-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..33].ToUpperInvariant(),
            ExternalOrderId = request.ExternalOrderId.Trim(),
            CustomerId = customer.Id,
            WarehouseId = request.WarehouseId,
            RequestedShipDate = request.RequestedShipDate,
            Priority = request.Priority,
            ShippingMethod = string.IsNullOrWhiteSpace(request.ShippingMethod) ? null : request.ShippingMethod.Trim(),
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency.Trim().ToUpperInvariant(),
            Status = SalesOrderStatus.Draft,
            CreatedAt = now,
            CreatedBy = currentUser.UserId
        };

        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            order.Lines.Add(new SalesOrderLine
            {
                ExternalLineId = line.ExternalLineId.Trim(),
                ProductId = line.ProductId,
                OrderedQuantity = line.OrderedQuantity,
                UnitPrice = line.UnitPrice,
                TaxPercent = line.TaxPercent,
                UomCode = product.Unit.Code
            });
        }

        context.SalesOrders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        AddAudit("SalesOrder.Created", order, $"ExternalOrderId: {order.ExternalOrderId}; Lines: {order.Lines.Count}");
        await context.SaveChangesAsync(cancellationToken);
        return await GetSalesOrderAsync(order.Id, cancellationToken);
    }

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

    private async Task<SalesOrderDto> MutateOrderAsync(
        int id,
        string encodedRowVersion,
        Func<SalesOrder, CancellationToken, Task> mutation,
        CancellationToken cancellationToken)
    {
        int? lockWarehouseId = null;
        int[] lockProductIds = [];
        if (context.Database.IsSqlServer())
        {
            lockWarehouseId = await context.SalesOrders.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => (int?)x.WarehouseId)
                .FirstOrDefaultAsync(cancellationToken);
            if (lockWarehouseId.HasValue)
            {
                lockProductIds = await context.SalesOrderLines.AsNoTracking()
                    .Where(x => x.SalesOrderId == id)
                    .Select(x => x.ProductId)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArrayAsync(cancellationToken);
            }
        }

        await ExecuteSerializableAsync(async token =>
        {
            if (lockWarehouseId.HasValue && lockProductIds.Length > 0)
            {
                await AcquireAllocationLocksAsync(lockWarehouseId.Value, lockProductIds, token);
            }

            var order = await LoadOrderAsync(id, true, token);
            ApplyVersion(order, encodedRowVersion);
            await mutation(order, token);
            await context.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetSalesOrderAsync(id, cancellationToken);
    }

    private Task AcquireAllocationLocksAsync(
        int warehouseId,
        IEnumerable<int> productIds,
        CancellationToken token) =>
        context.AcquireInventoryAllocationLocksAsync(warehouseId, productIds, token);

    private async Task<SalesOrder> LoadOrderAsync(int id, bool tracking, CancellationToken token)
    {
        IQueryable<SalesOrder> query = OrderQuery();
        if (!tracking) query = query.AsNoTracking();
        var order = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy Sales Order.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(order.WarehouseId, token);
        return order;
    }

    private async Task<Backorder> LoadBackorderAsync(int id, bool tracking, CancellationToken token)
    {
        IQueryable<Backorder> query = BackorderQuery();
        if (!tracking) query = query.AsNoTracking();
        var row = await query.SingleOrDefaultAsync(x => x.Id == id, token)
            ?? throw new NotFoundException("Không tìm thấy Backorder.");
        await warehouseAuthorization.EnsureWarehouseAccessAsync(row.WarehouseId, token);
        return row;
    }

    private IQueryable<SalesOrder> OrderQuery() =>
        context.SalesOrders
            .AsSplitQuery()
            .Include(x => x.Customer)
            .Include(x => x.Warehouse)
            .Include(x => x.Lines).ThenInclude(x => x.Product).ThenInclude(x => x.Unit)
            .Include(x => x.Lines).ThenInclude(x => x.Backorder);

    private IQueryable<Backorder> BackorderQuery() =>
        context.Backorders
            .AsSplitQuery()
            .Include(x => x.Warehouse)
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.Product)
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.SalesOrder).ThenInclude(x => x.Customer)
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.SalesOrder).ThenInclude(x => x.Warehouse);

    private async Task<SalesOrderListDto> BuildOrderDtoAsync(
        SalesOrder order,
        bool includeLines,
        CancellationToken token)
    {
        var progress = await BuildLineProgressAsync(order, token);
        return MapOrderDto(order, progress, includeLines);
    }

    private static SalesOrderListDto MapOrderDto(
        SalesOrder order,
        IReadOnlyDictionary<int, LineProgress> progress,
        bool includeLines)
    {
        var lines = order.Lines.OrderBy(x => x.Id).Select(line =>
        {
            var p = progress.GetValueOrDefault(line.ProductId) ?? new LineProgress(0m, 0m, 0m, 0m, 0m);
            var effectiveOrdered = Math.Max(0m, line.OrderedQuantity - line.CancelledQuantity);
            var lineSubTotal = decimal.Round(effectiveOrdered * line.UnitPrice, 2, MidpointRounding.AwayFromZero);
            var lineTax = decimal.Round(lineSubTotal * (line.TaxPercent / 100m), 2, MidpointRounding.AwayFromZero);
            return new SalesOrderLineDto
            {
                Id = line.Id,
                ExternalLineId = line.ExternalLineId,
                ProductId = line.ProductId,
                ProductCode = line.Product.Code,
                ProductName = line.Product.Name,
                UomCode = !string.IsNullOrWhiteSpace(line.UomCode) ? line.UomCode : (line.Product?.Unit?.Code ?? string.Empty),
                OrderedQuantity = line.OrderedQuantity,
                UnitPrice = line.UnitPrice,
                TaxPercent = line.TaxPercent,
                LineSubTotal = lineSubTotal,
                LineTaxAmount = lineTax,
                LineTotal = lineSubTotal + lineTax,
                ReservedQuantity = p.Reserved,
                AllocatedQuantity = p.Allocated,
                PickedQuantity = p.Picked,
                ShippedQuantity = p.Shipped,
                BackorderQuantity = p.Backorder,
                CancelledQuantity = line.CancelledQuantity,
                OpenQuantity = Math.Max(0m, line.OrderedQuantity - p.Shipped - line.CancelledQuantity)
            };
        }).ToList();

        var ordered = lines.Sum(x => x.OrderedQuantity);
        var shipped = lines.Sum(x => x.ShippedQuantity);
        var cancelled = lines.Sum(x => x.CancelledQuantity);
        var open = lines.Sum(x => x.OpenQuantity);
        var subTotal = lines.Sum(x => x.LineSubTotal);
        var taxAmount = lines.Sum(x => x.LineTaxAmount);
        var status = EffectiveOrderStatus(order, shipped, open, cancelled);

        SalesOrderListDto dto = includeLines ? new SalesOrderDto() : new SalesOrderListDto();
        dto.Id = order.Id;
        dto.OrderCode = order.OrderCode;
        dto.ExternalOrderId = order.ExternalOrderId;
        dto.CustomerId = order.CustomerId;
        dto.CustomerCode = order.Customer.Code;
        dto.CustomerName = order.Customer.Name;
        dto.WarehouseId = order.WarehouseId;
        dto.WarehouseCode = order.Warehouse.Code;
        dto.WarehouseName = order.Warehouse.Name;
        dto.RequestedShipDate = order.RequestedShipDate;
        dto.Priority = order.Priority;
        dto.ShippingMethod = order.ShippingMethod;
        dto.Currency = string.IsNullOrWhiteSpace(order.Currency) ? "VND" : order.Currency;
        dto.Status = status.ToString();
        dto.OrderedQuantity = ordered;
        dto.ReservedQuantity = lines.Sum(x => x.ReservedQuantity);
        dto.AllocatedQuantity = lines.Sum(x => x.AllocatedQuantity);
        dto.PickedQuantity = lines.Sum(x => x.PickedQuantity);
        dto.ShippedQuantity = shipped;
        dto.BackorderQuantity = lines.Sum(x => x.BackorderQuantity);
        dto.CancelledQuantity = cancelled;
        dto.OpenQuantity = open;
        dto.SubTotalAmount = subTotal;
        dto.TaxAmount = taxAmount;
        dto.TotalAmount = subTotal + taxAmount;
        dto.CreatedAt = order.CreatedAt;
        dto.ReleasedAt = order.ReleasedAt;

        if (dto is SalesOrderDto detail)
        {
            detail.RowVersion = Convert.ToBase64String(order.RowVersion);
            detail.Lines = lines;
        }
        return dto;
    }

    private async Task<Dictionary<int, Dictionary<int, LineProgress>>> BuildBatchLineProgressAsync(
        IReadOnlyCollection<SalesOrder> orders,
        CancellationToken token)
    {
        if (orders.Count == 0)
            return new Dictionary<int, Dictionary<int, LineProgress>>();

        var orderIds = orders.Select(x => x.Id).Distinct().ToArray();

        var demandLines = await context.SalesOrderLines.AsNoTracking()
            .Include(x => x.Backorder)
            .Where(x => orderIds.Contains(x.SalesOrderId))
            .ToListAsync(token);

        var reservations = await context.StockReservations.AsNoTracking()
            .Where(x => x.SourceType == "SalesOrder" && x.SourceId.HasValue && orderIds.Contains(x.SourceId.Value))
            .ToListAsync(token);
        var reservationIds = reservations.Select(x => x.Id).ToArray();
        var reservationOrderMap = reservations.ToDictionary(x => x.Id, x => x.SourceId!.Value);

        var allocatedRows = reservationIds.Length == 0
            ? []
            : await context.StockAllocations.AsNoTracking()
                .Where(x => reservationIds.Contains(x.ReservationId) && CountedAllocationStatuses.Contains(x.Status))
                .GroupBy(x => new { x.ReservationId, x.ProductId })
                .Select(g => new { g.Key.ReservationId, g.Key.ProductId, Quantity = g.Sum(x => x.Quantity) })
                .ToListAsync(token);

        var allocatedByOrderProduct = allocatedRows
            .GroupBy(x => (OrderId: reservationOrderMap[x.ReservationId], x.ProductId))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var pickedRows = await context.PickingTaskLines.AsNoTracking()
            .Where(x => x.PickingTask.SourceType == "SalesOrder" &&
                        x.PickingTask.SourceId.HasValue &&
                        orderIds.Contains(x.PickingTask.SourceId.Value) &&
                        x.PickingTask.Status != PickingTaskStatus.Cancelled)
            .GroupBy(x => new { OrderId = x.PickingTask.SourceId!.Value, x.ProductId })
            .Select(g => new { g.Key.OrderId, g.Key.ProductId, Quantity = g.Sum(x => x.PickedQuantity) })
            .ToListAsync(token);
        var pickedByOrderProduct = pickedRows.ToDictionary(x => (x.OrderId, x.ProductId), x => x.Quantity);

        var shipmentSessions = await context.Shipments.AsNoTracking()
            .Where(x => x.SourceType == "SalesOrder" &&
                        x.SourceId.HasValue &&
                        orderIds.Contains(x.SourceId.Value) &&
                        (x.Status == ShipmentStatus.Dispatched ||
                         x.Status == ShipmentStatus.InTransit ||
                         x.Status == ShipmentStatus.Delivered ||
                         x.Status == ShipmentStatus.Completed ||
                         x.Status == ShipmentStatus.DeliveryFailed ||
                         x.Status == ShipmentStatus.ReturnToWarehouse))
            .Select(x => new { OrderId = x.SourceId!.Value, x.PackingSessionId })
            .ToListAsync(token);

        var packingSessionIds = shipmentSessions.Select(x => x.PackingSessionId).Distinct().ToArray();
        var shippedRows = packingSessionIds.Length == 0
            ? []
            : await context.HandlingUnitContents.AsNoTracking()
                .Where(x => packingSessionIds.Contains(x.HandlingUnit.PackingSessionId))
                .GroupBy(x => new { x.HandlingUnit.PackingSessionId, x.ProductId })
                .Select(g => new { g.Key.PackingSessionId, g.Key.ProductId, Quantity = g.Sum(x => x.Quantity) })
                .ToListAsync(token);

        var shippedBySession = shippedRows
            .GroupBy(x => x.PackingSessionId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ProductId, x => x.Quantity));

        var shippedByOrderProduct = new Dictionary<(int OrderId, int ProductId), decimal>();
        foreach (var s in shipmentSessions)
        {
            if (!shippedBySession.TryGetValue(s.PackingSessionId, out var prodMap)) continue;
            foreach (var (prodId, qty) in prodMap)
            {
                var key = (s.OrderId, prodId);
                shippedByOrderProduct[key] = shippedByOrderProduct.GetValueOrDefault(key) + qty;
            }
        }

        var reservationByOrderProduct = reservations
            .GroupBy(x => (x.SourceId!.Value, x.ProductId))
            .ToDictionary(g => g.Key, g => g.First());

        var result = orderIds.ToDictionary(id => id, _ => new Dictionary<int, LineProgress>());
        foreach (var line in demandLines)
        {
            var key = (line.SalesOrderId, line.ProductId);
            var reservation = reservationByOrderProduct.GetValueOrDefault(key);
            var backorder = line.Backorder;
            var remainingBackorder = backorder is null
                ? 0m
                : Math.Max(0m, backorder.Quantity - backorder.RecoveredQuantity - backorder.CancelledQuantity);
            result[line.SalesOrderId][line.ProductId] = new LineProgress(
                reservation is null
                    ? 0m
                    : Math.Max(0m, reservation.Quantity - reservation.ConsumedQuantity - reservation.ReleasedQuantity),
                allocatedByOrderProduct.GetValueOrDefault(key),
                pickedByOrderProduct.GetValueOrDefault(key),
                shippedByOrderProduct.GetValueOrDefault(key),
                remainingBackorder);
        }

        return result;
    }

    private async Task<Dictionary<int, LineProgress>> BuildLineProgressAsync(
        SalesOrder order,
        CancellationToken token)
    {
        var batch = await BuildBatchLineProgressAsync([order], token);
        return batch.GetValueOrDefault(order.Id) ?? new Dictionary<int, LineProgress>();
    }

    private BackorderDto MapBackorder(Backorder row, IReadOnlyDictionary<int, LineProgress> progress)
    {
        var line = row.SalesOrderLine;
        var order = line.SalesOrder;
        var p = progress.GetValueOrDefault(line.ProductId) ?? new LineProgress(0m, 0m, 0m, 0m, 0m);
        var remaining = Math.Max(0m, row.Quantity - row.RecoveredQuantity - row.CancelledQuantity);
        var effective = row.Status;
        if (row.Status != BackorderStatus.Cancelled)
        {
            if (remaining > 0)
                effective = row.RecoveredQuantity > 0 ? BackorderStatus.PartiallyAllocated : BackorderStatus.Open;
            else if (p.Shipped >= line.OrderedQuantity - line.CancelledQuantity && row.RecoveredQuantity > 0)
                effective = BackorderStatus.Fulfilled;
            else if (row.RecoveredQuantity > 0)
                effective = BackorderStatus.Allocated;
        }

        return new BackorderDto
        {
            Id = row.Id,
            BackorderCode = row.BackorderCode,
            SalesOrderId = order.Id,
            OrderCode = order.OrderCode,
            ExternalOrderId = order.ExternalOrderId,
            SalesOrderLineId = line.Id,
            WarehouseId = row.WarehouseId,
            WarehouseCode = row.Warehouse.Code,
            WarehouseName = row.Warehouse.Name,
            ProductId = line.ProductId,
            ProductCode = line.Product.Code,
            ProductName = line.Product.Name,
            OrderedQuantity = line.OrderedQuantity,
            Quantity = row.Quantity,
            RecoveredQuantity = row.RecoveredQuantity,
            CancelledQuantity = row.CancelledQuantity,
            RemainingQuantity = remaining,
            Status = effective.ToString(),
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            RowVersion = Convert.ToBase64String(row.RowVersion)
        };
    }

    private static SalesOrderStatus EffectiveOrderStatus(
        SalesOrder order,
        decimal shipped,
        decimal open,
        decimal cancelled)
    {
        if (order.Status is SalesOrderStatus.Draft or SalesOrderStatus.Hold or SalesOrderStatus.Cancelled)
            return order.Status;
        if (open <= 0)
            return shipped > 0 ? SalesOrderStatus.Fulfilled : SalesOrderStatus.Cancelled;
        if (shipped > 0)
            return SalesOrderStatus.PartiallyFulfilled;
        _ = cancelled;
        return SalesOrderStatus.Released;
    }

    private static void ValidateCreate(CreateSalesOrderDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalOrderId) || request.ExternalOrderId.Trim().Length > 100)
            throw new BusinessRuleException("ExternalOrderId là bắt buộc và tối đa 100 ký tự.");
        if (request.CustomerId <= 0 || request.WarehouseId <= 0)
            throw new BusinessRuleException("Khách hàng và kho là bắt buộc.");
        if (request.Priority < 0)
            throw new BusinessRuleException("Priority không được âm.");
        if (request.Lines.Count == 0)
            throw new BusinessRuleException("Sales Order phải có ít nhất một dòng.");
        if (request.Lines.Any(x => string.IsNullOrWhiteSpace(x.ExternalLineId) || x.ExternalLineId.Trim().Length > 100 || x.ProductId <= 0 || x.OrderedQuantity <= 0 || x.UnitPrice < 0m || x.TaxPercent < 0m || x.TaxPercent > 100m))
            throw new BusinessRuleException("Dòng Sales Order không hợp lệ.");
        if (request.Lines.GroupBy(x => x.ExternalLineId.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new BusinessRuleException("ExternalLineId không được trùng trong cùng Sales Order.");
        if (request.Lines.GroupBy(x => x.ProductId).Any(g => g.Count() > 1))
            throw new BusinessRuleException("Foundation hiện yêu cầu mỗi SKU chỉ xuất hiện một dòng trong Sales Order.");
    }

    private static void ValidateQuantityPrecision(int decimalPlaces, decimal quantity)
    {
        if (decimal.Round(quantity, decimalPlaces) != quantity)
            throw new BusinessRuleException($"Số lượng vượt quá độ chính xác Base UOM cho phép ({decimalPlaces} chữ số thập phân).");
    }

    private void ApplyVersion(SalesOrder order, string encoded)
    {
        var expected = DecodeVersion(encoded);
        if (!order.RowVersion.SequenceEqual(expected))
            throw Conflict("SALES_ORDER_VERSION_CONFLICT", "Sales Order đã thay đổi. Vui lòng tải lại.");
        context.Entry(order).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private void ApplyVersion(Backorder row, string encoded)
    {
        var expected = DecodeVersion(encoded);
        if (!row.RowVersion.SequenceEqual(expected))
            throw Conflict("BACKORDER_VERSION_CONFLICT", "Backorder đã thay đổi. Vui lòng tải lại.");
        context.Entry(row).Property(x => x.RowVersion).OriginalValue = expected;
    }

    private static byte[] DecodeVersion(string encoded)
    {
        try { return Convert.FromBase64String(encoded); }
        catch { throw new BusinessRuleException("RowVersion không hợp lệ."); }
    }

    private async Task<T> ExecuteSerializableAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken token)
    {
        var own = context.Database.CurrentTransaction is null;
        var maxAttempts = own && context.Database.IsSqlServer() ? 4 : 1;
        for (var attempt = 1; ; attempt++)
        {
            await using var transaction = own
                ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token)
                : null;
            try
            {
                var result = await action(token);
                if (transaction is not null) await transaction.CommitAsync(token);
                return result;
            }
            catch (Exception ex) when (own && attempt < maxAttempts && IsSqlDeadlock(ex))
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                context.ChangeTracker.Clear();
                await Task.Delay(15 * attempt, token);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw new ConcurrencyException("Demand đã thay đổi đồng thời. Vui lòng tải lại.", ex);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw Conflict("DEMAND_DUPLICATE", "Demand/Backorder bị trùng do yêu cầu đồng thời.");
            }
            catch
            {
                if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }

    private static bool IsSqlDeadlock(Exception ex) =>
        ex is DeadlockException ||
        ex is SqlException { Number: 1205 } ||
        ex is DbUpdateException { InnerException: SqlException { Number: 1205 } };

    private void AddAudit(string action, SalesOrder order, string values) =>
        context.AuditLogs.Add(new AuditLog
        {
            UserId = currentUser.UserId,
            Action = action,
            EntityName = "SalesOrder",
            EntityId = order.Id,
            WarehouseId = order.WarehouseId,
            Timestamp = DateTime.UtcNow,
            NewValues = values,
            Result = "Success",
            Severity = "Information"
        });

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }

    private sealed record LineProgress(
        decimal Reserved,
        decimal Allocated,
        decimal Picked,
        decimal Shipped,
        decimal Backorder);
}
