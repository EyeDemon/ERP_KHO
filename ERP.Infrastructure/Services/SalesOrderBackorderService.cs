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
    ICurrentUser currentUser) : ISalesOrderBackorderService
{
    private static readonly StockAllocationStatus[] CountedAllocationStatuses =
    [
        StockAllocationStatus.Active,
        StockAllocationStatus.Picking,
        StockAllocationStatus.Picked,
        StockAllocationStatus.Consumed
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

        var query = OrderQuery().AsNoTracking().Where(x => accessible.Contains(x.WarehouseId));
        if (Enum.TryParse<SalesOrderStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var orders = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var result = new List<SalesOrderListDto>(orders.Count);
        foreach (var order in orders)
            result.Add(await BuildOrderDtoAsync(order, false, cancellationToken));
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

        var query = BackorderQuery().AsNoTracking().Where(x => accessible.Contains(x.WarehouseId));
        if (Enum.TryParse<BackorderStatus>(status, true, out var parsed))
            query = query.Where(x => x.Status == parsed);

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Take(250)
            .ToListAsync(cancellationToken);

        var cache = new Dictionary<int, Dictionary<int, LineProgress>>();
        var result = new List<BackorderDto>(rows.Count);
        foreach (var row in rows)
        {
            var order = row.SalesOrderLine.SalesOrder;
            if (!cache.TryGetValue(order.Id, out var progress))
            {
                progress = await BuildLineProgressAsync(order, cancellationToken);
                cache[order.Id] = progress;
            }
            result.Add(MapBackorder(row, progress));
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
        await ExecuteSerializableAsync(async token =>
        {
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
                activeTask.Status is not PickingTaskStatus.Open and not PickingTaskStatus.Assigned)
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
            await allocationService.AutoAllocateAsync(new CreateStockAllocationDto
            {
                ReservationId = reservation.Id,
                Quantity = recover
            }, token);

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
        await ExecuteSerializableAsync(async token =>
        {
            var order = await LoadOrderAsync(id, true, token);
            ApplyVersion(order, encodedRowVersion);
            await mutation(order, token);
            await context.SaveChangesAsync(token);
            return true;
        }, cancellationToken);
        return await GetSalesOrderAsync(id, cancellationToken);
    }

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
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.SalesOrder).ThenInclude(x => x.Warehouse)
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.SalesOrder).ThenInclude(x => x.Lines).ThenInclude(x => x.Product)
            .Include(x => x.SalesOrderLine).ThenInclude(x => x.SalesOrder).ThenInclude(x => x.Lines).ThenInclude(x => x.Backorder);

    private async Task<SalesOrderListDto> BuildOrderDtoAsync(
        SalesOrder order,
        bool includeLines,
        CancellationToken token)
    {
        var progress = await BuildLineProgressAsync(order, token);
        var lines = order.Lines.OrderBy(x => x.Id).Select(line =>
        {
            var p = progress[line.ProductId];
            return new SalesOrderLineDto
            {
                Id = line.Id,
                ExternalLineId = line.ExternalLineId,
                ProductId = line.ProductId,
                ProductCode = line.Product.Code,
                ProductName = line.Product.Name,
                UomCode = line.UomCode,
                OrderedQuantity = line.OrderedQuantity,
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
        var status = EffectiveOrderStatus(order, shipped, open, cancelled);

        SalesOrderListDto dto = includeLines ? new SalesOrderDto() : new SalesOrderListDto();
        dto.Id = order.Id;
        dto.OrderCode = order.OrderCode;
        dto.ExternalOrderId = order.ExternalOrderId;
        dto.CustomerId = order.CustomerId;
        dto.CustomerCode = order.Customer.Code;
        dto.CustomerName = order.Customer.Name;
        dto.WarehouseId = order.WarehouseId;
        dto.WarehouseName = order.Warehouse.Name;
        dto.RequestedShipDate = order.RequestedShipDate;
        dto.Priority = order.Priority;
        dto.ShippingMethod = order.ShippingMethod;
        dto.Status = status.ToString();
        dto.OrderedQuantity = ordered;
        dto.ReservedQuantity = lines.Sum(x => x.ReservedQuantity);
        dto.AllocatedQuantity = lines.Sum(x => x.AllocatedQuantity);
        dto.PickedQuantity = lines.Sum(x => x.PickedQuantity);
        dto.ShippedQuantity = shipped;
        dto.BackorderQuantity = lines.Sum(x => x.BackorderQuantity);
        dto.CancelledQuantity = cancelled;
        dto.OpenQuantity = open;
        dto.CreatedAt = order.CreatedAt;
        dto.ReleasedAt = order.ReleasedAt;

        if (dto is SalesOrderDto detail)
        {
            detail.RowVersion = Convert.ToBase64String(order.RowVersion);
            detail.Lines = lines;
        }
        return dto;
    }

    private async Task<Dictionary<int, LineProgress>> BuildLineProgressAsync(
        SalesOrder order,
        CancellationToken token)
    {
        var reservations = await context.StockReservations.AsNoTracking()
            .Where(x => x.SourceType == "SalesOrder" && x.SourceId == order.Id)
            .ToListAsync(token);
        var reservationIds = reservations.Select(x => x.Id).ToArray();

        var allocated = reservationIds.Length == 0
            ? new Dictionary<int, decimal>()
            : await context.StockAllocations.AsNoTracking()
                .Where(x => reservationIds.Contains(x.ReservationId) && CountedAllocationStatuses.Contains(x.Status))
                .GroupBy(x => x.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, token);

        var picked = await context.PickingTaskLines.AsNoTracking()
            .Where(x => x.PickingTask.SourceType == "SalesOrder" &&
                        x.PickingTask.SourceId == order.Id &&
                        x.PickingTask.Status != PickingTaskStatus.Cancelled)
            .GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.PickedQuantity) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, token);

        var packingSessionIds = await context.Shipments.AsNoTracking()
            .Where(x => x.SourceType == "SalesOrder" &&
                        x.SourceId == order.Id &&
                        x.Status == ShipmentStatus.Dispatched)
            .Select(x => x.PackingSessionId)
            .ToArrayAsync(token);

        var shipped = packingSessionIds.Length == 0
            ? new Dictionary<int, decimal>()
            : await context.HandlingUnitContents.AsNoTracking()
                .Where(x => packingSessionIds.Contains(x.HandlingUnit.PackingSessionId))
                .GroupBy(x => x.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Quantity, token);

        var result = new Dictionary<int, LineProgress>();
        foreach (var line in order.Lines)
        {
            var reservation = reservations.SingleOrDefault(x => x.ProductId == line.ProductId);
            var backorder = line.Backorder;
            var remainingBackorder = backorder is null
                ? 0m
                : Math.Max(0m, backorder.Quantity - backorder.RecoveredQuantity - backorder.CancelledQuantity);
            result[line.ProductId] = new LineProgress(
                reservation?.Quantity ?? 0m,
                allocated.GetValueOrDefault(line.ProductId),
                picked.GetValueOrDefault(line.ProductId),
                shipped.GetValueOrDefault(line.ProductId),
                remainingBackorder);
        }
        return result;
    }

    private BackorderDto MapBackorder(Backorder row, IReadOnlyDictionary<int, LineProgress> progress)
    {
        var line = row.SalesOrderLine;
        var order = line.SalesOrder;
        var p = progress[line.ProductId];
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
        if (request.Lines.Any(x => string.IsNullOrWhiteSpace(x.ExternalLineId) || x.ExternalLineId.Trim().Length > 100 || x.ProductId <= 0 || x.OrderedQuantity <= 0))
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
        await using var transaction = own
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token)
            : null;
        try
        {
            var result = await action(token);
            if (transaction is not null) await transaction.CommitAsync(token);
            return result;
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
