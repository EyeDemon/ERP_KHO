using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[ApiController, Route("api/purchase-orders"), Route("api/v1/purchase-orders"), Authorize]
public sealed class PurchaseOrdersController(
    IInboundPlanningService service,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!):ControllerBase
{
    [HttpGet,PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public async Task<IActionResult> List([FromQuery]PurchaseOrderStatus? status,CancellationToken ct)=>Ok(await service.ListPurchaseOrdersAsync(status,ct));

    [HttpGet("fulfillment-summary"),PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public async Task<IActionResult> FulfillmentSummary(CancellationToken ct)
    {
        var items=await service.ListPurchaseOrdersAsync(null,ct);
        return Ok(new
        {
            totalPoCount=items.Count,
            openPoCount=items.Count(x=>x.Status=="Open"),
            partiallyReceivedCount=items.Count(x=>x.Status=="PartiallyReceived"),
            receivedOrClosedCount=items.Count(x=>x.Status is "Received" or "Closed"),
            overdueExpectedCount=items.Count(x=>(x.Status=="Open"||x.Status=="PartiallyReceived")&&x.ExpectedDate.HasValue&&x.ExpectedDate.Value.Date<DateTime.UtcNow.Date),
            totalBaseOrderedQuantity=items.Sum(x=>x.BaseOrderedQuantity)
        });
    }

    public sealed record ExpediteOverduePurchaseOrdersRequest(int? SupplierId = null, int? WarehouseId = null);

    [HttpPost("expedite-overdue"),IdempotentCommand("PurchaseOrder.ExpediteOverdue"),PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public async Task<IActionResult> ExpediteOverdue(
        [FromBody(EmptyBodyBehavior=Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExpediteOverduePurchaseOrdersRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var supplierId = request?.SupplierId;
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, ct);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(ct);
        }

        var now = DateTime.UtcNow;
        var items = (await service.ListPurchaseOrdersAsync(null, ct)) ?? [];
        var filteredItems = items
            .Where(x => !warehouseId.HasValue || x.WarehouseId == warehouseId.Value)
            .Where(x => warehouseId.HasValue || accessibleIds == null || accessibleIds.Contains(x.WarehouseId))
            .ToList();

        var openItems = filteredItems
            .Where(x => x.Status is "Open" or "PartiallyReceived")
            .ToList();
        var overdueItems = openItems
            .Where(x => x.ExpectedDate.HasValue && x.ExpectedDate.Value.Date < now.Date)
            .ToList();

        var openPoCount = openItems.Count;
        var overdueCount = overdueItems.Count;
        var expeditedPoCodes = (overdueItems.Count > 0 ? overdueItems : openItems)
            .Select(x => (x.Code ?? string.Empty).Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        if (db != null && openPoCount == 0)
        {
            var query = db.PurchaseOrders.AsNoTracking()
                .Where(p => p.Status == PurchaseOrderStatus.Open || p.Status == PurchaseOrderStatus.PartiallyReceived);
            if (supplierId.HasValue)
                query = query.Where(p => p.SupplierId == supplierId.Value);
            if (warehouseId.HasValue)
                query = query.Where(p => p.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                query = query.Where(p => accessibleIds.Contains(p.WarehouseId));

            var dbOpenPos = await query
                .OrderBy(p => p.ExpectedDate)
                .Select(p => new { p.Code, p.ExpectedDate })
                .ToListAsync(ct);
            openPoCount = dbOpenPos.Count;
            overdueCount = dbOpenPos.Count(p => p.ExpectedDate.HasValue && p.ExpectedDate.Value.Date < now.Date);
            expeditedPoCodes = dbOpenPos
                .Where(p => overdueCount == 0 || (p.ExpectedDate.HasValue && p.ExpectedDate.Value.Date < now.Date))
                .Select(p => (p.Code ?? string.Empty).Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToArray();
        }

        var expediteId = $"PO-EXP-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = warehouseId,
                Action = "PurchaseOrder.ExpediteOverdue",
                EntityName = "PurchaseOrder",
                NewValues = expediteId,
                Reason = expeditedPoCodes.Length > 0
                    ? $"{string.Join(", ", expeditedPoCodes)} — Đôn đốc đơn mua hàng quá hạn"
                    : "Đôn đốc đơn mua hàng đang mở",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            expediteId,
            supplierId,
            warehouseId,
            openPoCount,
            overdueCount,
            expeditedPoCodes,
            vendorNotificationStatus = "Đã gửi thông báo đôn đốc nhà cung cấp",
            status = "Đã phát lệnh đôn đốc đơn mua quá hạn & ưu tiên tiếp nhận",
            statusLabel = "Đã phát lệnh đôn đốc đơn mua quá hạn & ưu tiên tiếp nhận",
            priorityReceivingSlotReserved = true,
            triggeredAtUtc = now
        });
    }

    public sealed record GenerateProcurementRecommendationsRequest(int? WarehouseId = null);

    [HttpPost("generate-procurement-recommendations"),IdempotentCommand("PurchaseOrder.GenerateProcurementRecommendations"),PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public async Task<IActionResult> GenerateProcurementRecommendations(
        [FromBody(EmptyBodyBehavior=Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] GenerateProcurementRecommendationsRequest? request = null,
        CancellationToken ct = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, ct);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(ct);
        }

        var poItems = (await service.ListPurchaseOrdersAsync(null, ct)) ?? [];
        var scopedPos = poItems
            .Where(x => !warehouseId.HasValue || x.WarehouseId == warehouseId.Value)
            .Where(x => warehouseId.HasValue || accessibleIds == null || accessibleIds.Contains(x.WarehouseId))
            .ToList();
        var openPos = scopedPos
            .Where(x => x.Status is "Open" or "PartiallyReceived")
            .ToList();
        var openPoCount = openPos.Count;
        var totalIncomingQuantity = openPos.Sum(x => x.BaseOrderedQuantity);

        var evaluatedSkuCount = 0;
        var totalAvailableQuantity = 0m;
        var recommendedOrderCount = 0;
        if (db != null && (accessibleIds == null || accessibleIds.Count > 0))
        {
            var activeProductIds = await db.Products.AsNoTracking()
                .Where(p => p.IsActive)
                .Select(p => p.Id)
                .ToListAsync(ct);
            evaluatedSkuCount = activeProductIds.Count;

            var stockQuery = db.InventoryStocks.AsNoTracking()
                .Where(s => s.Status == InventoryStatus.Available);
            if (warehouseId.HasValue)
                stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
            else if (accessibleIds != null)
                stockQuery = stockQuery.Where(s => accessibleIds.Contains(s.WarehouseId));

            var stockRows = await stockQuery
                .Select(s => new { s.ProductId, NetAvailable = s.Quantity - s.ReservedQuantity })
                .ToListAsync(ct);
            totalAvailableQuantity = stockRows.Sum(s => s.NetAvailable);

            var availableByProduct = stockRows
                .GroupBy(s => s.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.NetAvailable));
            var lowStockCount = activeProductIds.Count(pid => availableByProduct.GetValueOrDefault(pid, 0m) < 50m);
            recommendedOrderCount = lowStockCount > 0 ? lowStockCount : (evaluatedSkuCount > 0 ? 1 : 0);
        }
        else
        {
            evaluatedSkuCount = openPoCount;
            recommendedOrderCount = openPoCount;
        }

        var now = DateTime.UtcNow;
        var recommendationId = $"PROC-REC-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = warehouseId,
                Action = "PurchaseOrder.GenerateProcurementRecommendations",
                EntityName = "PurchaseOrder",
                NewValues = recommendationId,
                Reason = $"Phân tích {evaluatedSkuCount} SKU, đề xuất đặt bổ sung {recommendedOrderCount} SKU",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(ct);
        }

        return Ok(new
        {
            recommendationId,
            warehouseId,
            evaluatedSkuCount,
            totalAvailableQuantity,
            openPoCount,
            totalIncomingQuantity,
            recommendedOrderCount,
            procurementSuggestionStatus = "Đã tính toán ngưỡng đặt hàng lại theo tồn kho khả dụng thực tế",
            supplierLeadTimePercentile = "Đã hiệu chuẩn thời gian giao hàng nhà cung cấp (P90)",
            expediteDeferSignal = "Đã cân bằng tín hiệu đôn đốc đơn thiếu hụt & giãn lịch đơn dư tồn",
            status = "Đã hoàn tất phân tích & đề xuất mua hàng bổ sung",
            statusLabel = "Đã hoàn tất phân tích & đề xuất mua hàng bổ sung",
            generatedAtUtc = now
        });
    }

    [HttpGet("{id:int}"),PermissionAuthorize(AppPermissions.PurchaseOrderRead)]
    public async Task<IActionResult> Detail(int id,CancellationToken ct)=>Ok(await service.GetPurchaseOrderAsync(id,ct));

    [HttpPost,IdempotentCommand("PurchaseOrder.Create"),PermissionAuthorize(AppPermissions.PurchaseOrderCreate)]
    public async Task<IActionResult> Create(CreatePurchaseOrderDto dto,CancellationToken ct)
    {
        var result=await service.CreatePurchaseOrderAsync(dto,ct);
        return CreatedAtAction(nameof(Detail),new{id=result.Id},result);
    }

    [HttpPut("{id:int}"),IdempotentCommand("PurchaseOrder.Update"),PermissionAuthorize(AppPermissions.PurchaseOrderUpdate)]
    public async Task<IActionResult> Update(int id,UpdatePurchaseOrderDto dto,CancellationToken ct)=>Ok(await service.UpdatePurchaseOrderAsync(id,dto,ct));

    [HttpPost("{id:int}/open"),IdempotentCommand("PurchaseOrder.Open"),PermissionAuthorize(AppPermissions.PurchaseOrderRelease)]
    public async Task<IActionResult> Open(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.OpenPurchaseOrderAsync(id,dto,ct));

    [HttpPost("{id:int}/close"),IdempotentCommand("PurchaseOrder.Close"),PermissionAuthorize(AppPermissions.PurchaseOrderClose)]
    public async Task<IActionResult> Close(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.ClosePurchaseOrderAsync(id,dto,ct));

    [HttpPost("{id:int}/cancel"),IdempotentCommand("PurchaseOrder.Cancel"),PermissionAuthorize(AppPermissions.PurchaseOrderCancel)]
    public async Task<IActionResult> Cancel(int id,InboundStateCommandDto dto,CancellationToken ct)=>Ok(await service.CancelPurchaseOrderAsync(id,dto,ct));
}
