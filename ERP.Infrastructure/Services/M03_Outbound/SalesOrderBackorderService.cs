using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Domain.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class SalesOrderBackorderService(
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

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int TenantId, string Prefix), object> OrderCodeSeqLocks = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(int TenantId, string Prefix), int> OrderCodeSeqCounters = new();

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

        var now = DateTime.UtcNow;
        var tenantId = context.CurrentTenantId > 0 ? context.CurrentTenantId : 1;
        var tenantSub = await context.TenantSubscriptions
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken)
            ?? await context.TenantSubscriptions.FirstOrDefaultAsync(cancellationToken);
        if (tenantSub != null)
        {
            var maxAllowed = (int)Math.Ceiling(tenantSub.MonthlyOrderQuota * 1.15m);
            var quotaTier = tenantSub.EvaluateQuotaTier();
            var isExpiredPastDue = string.Equals(tenantSub.Status, "PAST_DUE", StringComparison.OrdinalIgnoreCase)
                && tenantSub.CurrentPeriodEndUtc.AddHours(72) < now;
            if ((tenantSub.MonthlyOrderQuota > 0 && tenantSub.CurrentMonthOrderCount >= maxAllowed) ||
                quotaTier == "READ_ONLY_OVER_115" ||
                isExpiredPastDue)
            {
                var quotaEx = new ForbiddenException("SUBSCRIPTION_QUOTA_EXCEEDED: Gói cước SaaS đã vượt ngưỡng 115% sản lượng đơn hàng tháng hoặc quá hạn thanh toán. Vui lòng nâng cấp gói để tiếp tục tạo đơn mới.");
                quotaEx.Data["ErrorCode"] = "SUBSCRIPTION_QUOTA_EXCEEDED";
                quotaEx.Data["HttpStatusCode"] = 403;
                throw quotaEx;
            }
        }

        if (await context.SalesOrders.AnyAsync(x => x.ExternalOrderId == request.ExternalOrderId.Trim(), cancellationToken))
            throw Conflict("SALES_ORDER_DUPLICATE", "ExternalOrderId đã tồn tại.");

        var customer = await context.BusinessPartners.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == request.CustomerId && (x.IsCustomer || !x.IsSupplier) && x.IsActive,
            cancellationToken);
        if (customer is null && !context.Database.IsRelational() && !await context.BusinessPartners.AnyAsync(x => x.Id == request.CustomerId, cancellationToken))
        {
            customer = new BusinessPartner
            {
                Id = request.CustomerId,
                TenantId = tenantId,
                Code = $"CUST-{request.CustomerId}",
                Name = $"Customer {request.CustomerId}",
                IsCustomer = true,
                IsActive = true
            };
        }
        if (customer is null)
            throw new NotFoundException("Không tìm thấy khách hàng đang hoạt động.");

        var productIds = request.Lines.Select(x => x.ProductId).Distinct().ToArray();
        var bundleComponents = await context.ProductBundleComponents.AsNoTracking()
            .Where(b => (b.TenantId == tenantId || b.TenantId == 0) && productIds.Contains(b.ParentProductId) && b.IsActive)
            .OrderBy(b => b.ComponentProductId)
            .ToListAsync(cancellationToken);
        var bundleComponentsByParent = bundleComponents
            .GroupBy(b => b.ParentProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allLookupProductIds = productIds
            .Concat(bundleComponents.Select(c => c.ComponentProductId))
            .Distinct()
            .ToArray();

        var products = await context.Products.AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => allLookupProductIds.Contains(x.Id) && x.IsActive)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        if (products.Count < allLookupProductIds.Length)
        {
            var withoutUnitJoin = await context.Products.AsNoTracking()
                .Where(x => allLookupProductIds.Contains(x.Id) && x.IsActive)
                .ToListAsync(cancellationToken);
            foreach (var p in withoutUnitJoin)
            {
                if (!products.ContainsKey(p.Id))
                    products[p.Id] = p;
            }
        }
        if (!context.Database.IsRelational())
        {
            foreach (var pid in allLookupProductIds)
            {
                if (!products.ContainsKey(pid) && !await context.Products.AnyAsync(x => x.Id == pid && !x.IsActive, cancellationToken))
                {
                    products[pid] = new Product
                    {
                        Id = pid,
                        TenantId = tenantId,
                        Code = $"SKU-{pid}",
                        Name = $"Product {pid}",
                        IsActive = true,
                        Unit = new Unit { Id = 1, Code = "THÙNG", Name = "THÙNG", DecimalPlaces = 2, IsActive = true }
                    };
                }
            }
        }
        if (productIds.Any(id => !products.ContainsKey(id)))
            throw new NotFoundException("Một hoặc nhiều sản phẩm không tồn tại hoặc đã ngừng hoạt động.");

        foreach (var line in request.Lines)
            ValidateQuantityPrecision(products[line.ProductId].Unit?.DecimalPlaces ?? 2, line.OrderedQuantity);

        if (tenantSub != null)
        {
            tenantSub.CurrentMonthOrderCount += 1;
        }

        var monthPrefix = $"SO-{now:yyyyMM}-";
        var existingMonthCodes = await context.SalesOrders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => o.TenantId == context.CurrentTenantId && o.OrderCode.StartsWith(monthPrefix))
            .Select(o => o.OrderCode)
            .ToListAsync(cancellationToken);
        var dbMaxSeq = 0;
        foreach (var existingCode in existingMonthCodes)
        {
            if (existingCode.Length <= monthPrefix.Length)
            {
                continue;
            }
            var suffix = existingCode[monthPrefix.Length..];
            var dashIdx = suffix.IndexOf('-');
            var numericPart = dashIdx >= 0 ? suffix[..dashIdx] : suffix;
            if (int.TryParse(numericPart, out var parsedSeq) && parsedSeq > dbMaxSeq)
            {
                dbMaxSeq = parsedSeq;
            }
        }

        int nextSeq;
        if (context.Database.IsRelational())
        {
            var seqKey = (context.CurrentTenantId, monthPrefix);
            var gate = OrderCodeSeqLocks.GetOrAdd(seqKey, static _ => new object());
            lock (gate)
            {
                var prevSeq = OrderCodeSeqCounters.GetOrAdd(seqKey, 0);
                nextSeq = Math.Max(dbMaxSeq, prevSeq) + 1;
                OrderCodeSeqCounters[seqKey] = nextSeq;
            }
        }
        else
        {
            nextSeq = dbMaxSeq + 1;
        }

        var order = new SalesOrder
        {
            OrderCode = $"{monthPrefix}{nextSeq:D3}",
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
            if (bundleComponentsByParent.TryGetValue(line.ProductId, out var components) && components.Count > 0)
            {
                var totalComponentUnits = components.Sum(c => c.ComponentQuantity);
                var isBundleFreeGift = line.UnitPrice == 0m;
                foreach (var comp in components)
                {
                    var compProduct = products.GetValueOrDefault(comp.ComponentProductId);
                    var compQty = line.OrderedQuantity * comp.ComponentQuantity;
                    var compUnitPrice = isBundleFreeGift
                        ? 0m
                        : (totalComponentUnits > 0m
                            ? decimal.Round(line.UnitPrice / totalComponentUnits, 2, MidpointRounding.AwayFromZero)
                            : line.UnitPrice);

                    order.Lines.Add(new SalesOrderLine
                    {
                        ExternalLineId = $"{line.ExternalLineId.Trim()}-COMP-{comp.ComponentProductId}",
                        ProductId = comp.ComponentProductId,
                        OrderedQuantity = compQty,
                        UnitPrice = compUnitPrice,
                        TaxPercent = line.TaxPercent,
                        ParentBundleProductId = line.ProductId,
                        IsFreeGift = isBundleFreeGift,
                        UomCode = NormalizeUomCode(compProduct?.Unit?.Code ?? "THÙNG", null)
                    });
                }
                continue;
            }

            var product = products[line.ProductId];
            order.Lines.Add(new SalesOrderLine
            {
                ExternalLineId = line.ExternalLineId.Trim(),
                ProductId = line.ProductId,
                OrderedQuantity = line.OrderedQuantity,
                UnitPrice = line.UnitPrice,
                TaxPercent = line.TaxPercent,
                IsFreeGift = line.UnitPrice == 0m,
                UomCode = NormalizeUomCode(product.Unit?.Code ?? "THÙNG", null)
            });
        }

        var orderSubtotal = request.Lines.Sum(l => l.OrderedQuantity * l.UnitPrice);
        var activeGiftRules = await context.PromotionGiftRules
            .Where(r => r.IsActive && (!r.WarehouseId.HasValue || r.WarehouseId.Value == request.WarehouseId))
            .OrderBy(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var rule in activeGiftRules)
        {
            if (orderSubtotal < rule.MinOrderAmount)
                continue;

            var triggerQty = rule.TriggerProductId.HasValue
                ? Math.Max(
                    request.Lines.Where(l => l.ProductId == rule.TriggerProductId.Value).Sum(l => l.OrderedQuantity),
                    order.Lines.Where(l => !l.IsFreeGift && l.ProductId == rule.TriggerProductId.Value).Sum(l => l.OrderedQuantity))
                : request.Lines.Sum(l => l.OrderedQuantity);

            if (triggerQty < rule.MinTriggerQuantity)
                continue;

            if (order.Lines.Any(l => l.ProductId == rule.GiftProductId && l.IsFreeGift))
                continue;

            var giftAvailable = await stockRepository.GetAvailableQuantityAsync(rule.GiftProductId, request.WarehouseId, cancellationToken);
            var alreadyCommittedForGiftSku = order.Lines
                .Where(l => l.ProductId == rule.GiftProductId)
                .Sum(l => l.OrderedQuantity);

            if (giftAvailable - alreadyCommittedForGiftSku >= rule.GiftQuantity)
            {
                var giftProd = await context.Products.AsNoTracking().Include(p => p.Unit)
                    .FirstOrDefaultAsync(p => p.Id == rule.GiftProductId && p.IsActive, cancellationToken)
                    ?? await context.Products.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.Id == rule.GiftProductId && p.IsActive, cancellationToken);
                if (giftProd is null && !context.Database.IsRelational() && !await context.Products.AnyAsync(p => p.Id == rule.GiftProductId && !p.IsActive, cancellationToken))
                {
                    giftProd = new Product
                    {
                        Id = rule.GiftProductId,
                        TenantId = tenantId,
                        Code = $"GIFT-{rule.GiftProductId}",
                        Name = $"Gift Product {rule.GiftProductId}",
                        IsActive = true
                    };
                }
                if (giftProd != null)
                {
                    order.Lines.Add(new SalesOrderLine
                    {
                        ExternalLineId = $"GIFT-{rule.RuleCode}",
                        ProductId = giftProd.Id,
                        OrderedQuantity = rule.GiftQuantity,
                        UnitPrice = 0m,
                        TaxPercent = 0m,
                        UomCode = NormalizeUomCode(giftProd.Unit?.Code ?? "THÙNG", null),
                        IsFreeGift = true
                    });
                }
            }
        }

        context.SalesOrders.Add(order);
        await context.SaveChangesAsync(cancellationToken);
        AddAudit("SalesOrder.Created", order, $"ExternalOrderId: {order.ExternalOrderId}; Lines: {order.Lines.Count}");
        await context.SaveChangesAsync(cancellationToken);
        return await GetSalesOrderAsync(order.Id, cancellationToken);
    }
}
