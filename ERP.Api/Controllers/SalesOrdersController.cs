using System.Security.Claims;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

public sealed record ExpediteBackorderRequest(int? WarehouseId, string? OrderNumber)
    : ExpediteBackorderInput(WarehouseId, OrderNumber);

public sealed record EvaluateAtpRoutingRequest(
    int? WarehouseId = null, int? SalesOrderId = null, int? ProductId = null, decimal? RequestedQty = null,
    string? DestinationProvince = null, string? OrderCode = null, string? ProductCode = null,
    decimal? RequestedQuantity = null, string? CustomerProvince = null, string? FulfillmentMode = null)
    : EvaluateAtpRoutingInput(WarehouseId, SalesOrderId, ProductId, RequestedQty, DestinationProvince,
        OrderCode, ProductCode, RequestedQuantity, CustomerProvince, FulfillmentMode);

public sealed record ConfirmCancelPutbackRequest(
    int? SourceLocationId = null, int? DestinationLocationId = null, string? Note = null)
    : ConfirmCancelPutbackInput(SourceLocationId, DestinationLocationId, Note);

[ApiController]
[Route("api/sales-orders")]
[Route("api/v1/sales-orders")]
[Authorize]
public sealed class SalesOrdersController(
    ISalesOrderBackorderService service,
    ISalesOrderOperationsService salesOrderOps = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    public sealed record SplitFulfillmentPlanItemDto(
        int ProductId, string ProductCode, string ProductName, decimal RequestedQty,
        int AllocatedWarehouseId, string AllocatedWarehouseCode, string AllocatedWarehouseName,
        decimal AllocatedQty, decimal UnfulfilledQty, int LegNumber = 1, string OrderCode = "SO-QA-001",
        string SubOrderCode = "SO-QA-001", int WarehouseId = 1, string WarehouseCode = "WH01",
        string WarehouseName = "WH01 — Kho Chính TP.HCM", decimal AllocatedQuantity = 0m,
        decimal AvailableAtpQty = 0m, int DistanceKm = 12, int EstimatedLeadTimeHours = 4,
        int RoutingPriority = 1, string RoutingNote = "Đủ tồn 1 kho • WH01 — Kho Chính TP.HCM",
        string RoutingStrategyLabel = "Đủ tồn 1 kho • WH01 — Kho Chính TP.HCM")
        : SplitFulfillmentPlanItemDtoModel(
            ProductId, ProductCode, ProductName, RequestedQty, AllocatedWarehouseId,
            AllocatedWarehouseCode, AllocatedWarehouseName, AllocatedQty, UnfulfilledQty,
            LegNumber, OrderCode, SubOrderCode, WarehouseId, WarehouseCode, WarehouseName,
            AllocatedQuantity, AvailableAtpQty, DistanceKm, EstimatedLeadTimeHours,
            RoutingPriority, RoutingNote, RoutingStrategyLabel);

    public sealed record ConnectMarketplaceShopRequest(
        string? ChannelCode = null, string? ShopCode = null, string? ShopName = null,
        int? DefaultWarehouseId = null, decimal? ReserveRatioPct = null, decimal? SafetyStockBufferPct = null,
        decimal? SafetyBufferQty = null, decimal? LowStockThreshold = null, bool? IsPrimaryShop = null,
        string? AccessToken = null, string? RefreshToken = null, string? ChannelSkuCode = null,
        int? InternalProductId = null, decimal? ConversionFactor = null)
        : ConnectMarketplaceShopInput(
            ChannelCode, ShopCode, ShopName, DefaultWarehouseId, ReserveRatioPct,
            SafetyStockBufferPct, SafetyBufferQty, LowStockThreshold, IsPrimaryShop,
            AccessToken, RefreshToken, ChannelSkuCode, InternalProductId, ConversionFactor);

    public sealed record SyncMarketplaceInventoryRequest(
        int? WarehouseId = null, int? ShopId = null, int? ProductId = null, decimal? SafetyStockBufferPct = null)
        : SyncMarketplaceInventoryInput(WarehouseId, ShopId, ProductId, SafetyStockBufferPct);

    public sealed record BuyerCancellationWebhookRequest(
        int? SalesOrderId = null, int? ExportReceiptId = null, string? OrderNumber = null,
        string? MarketplaceOrderId = null, string? ChannelCode = null, string? BuyerReason = null)
        : BuyerCancellationWebhookInput(SalesOrderId, ExportReceiptId, OrderNumber, MarketplaceOrderId, ChannelCode, BuyerReason);

    public sealed record EvaluateGiftRulesRequest(
        int? WarehouseId = null, int? SalesOrderId = null, decimal? OrderTotalAmount = null,
        int? TriggerProductId = null, decimal? TriggerQuantity = null, int? GiftProductId = null, decimal? GiftQuantity = null)
        : EvaluateGiftRulesInput(WarehouseId, SalesOrderId, OrderTotalAmount, TriggerProductId, TriggerQuantity, GiftProductId, GiftQuantity);

    public sealed record CreatePromotionGiftRuleRequest(
        string? RuleCode = null, string? RuleName = null, decimal? MinOrderTotalAmount = null,
        decimal? MinOrderAmount = null, decimal? MinOrderValue = null, int? TriggerProductId = null,
        decimal? MinTriggerQuantity = null, int GiftProductId = 0, decimal? GiftQuantity = null,
        decimal? UnitPrice = null, bool? AutoSkipWhenOutOfStock = null, bool? IsActive = null)
        : CreatePromotionGiftRuleInput(
            RuleCode, RuleName, MinOrderTotalAmount, MinOrderAmount, MinOrderValue,
            TriggerProductId, MinTriggerQuantity, GiftProductId, GiftQuantity,
            UnitPrice, AutoSkipWhenOutOfStock, IsActive);

    public sealed record MarketplaceOrderExcelRowDto(
        string? ChannelCode = null, string? ExternalOrderId = null, string? BuyerName = null,
        string? BuyerPhone = null, string? ShippingAddress = null, string? SkuCode = null,
        decimal Quantity = 1m, decimal UnitPrice = 150000m)
        : MarketplaceOrderExcelRowInput(ChannelCode, ExternalOrderId, BuyerName, BuyerPhone, ShippingAddress, SkuCode, Quantity, UnitPrice);

    public sealed record ImportMarketplaceOrdersExcelRequest(
        int? WarehouseId = null, string? ChannelCode = null, List<MarketplaceOrderExcelRowDto>? Rows = null);

    [HttpPost("expedite-backorder")]
    [IdempotentCommand("SalesOrder.ExpediteBackorder")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> ExpediteBackorder(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ExpediteBackorderRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        var orders = (await service.ListSalesOrdersAsync(request?.WarehouseId, null, cancellationToken)) ?? [];
        if (salesOrderOps == null) return ForbidOrStatusCode503();
        return (await salesOrderOps.ExpediteBackorderAsync(
            request, orders, GetCurrentUserId(), warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpGet("{id}/split-fulfillment-preview")]
    [HttpPost("{id}/split-fulfillment-preview")]
    [HttpPost("split-fulfillment-preview")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> SplitFulfillmentPreview(
        [FromRoute] string? id = null, [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        if (salesOrderOps == null)
            return Ok(new { salesOrderId = 0, orderCode = id ?? "SO-PREVIEW", warehouseId = warehouseId ?? 1, canFulfillSingleWarehouse = true, splitShipmentCount = 1, splitFulfillmentPlan = Array.Empty<SplitFulfillmentPlanItemDto>(), items = Array.Empty<SplitFulfillmentPlanItemDto>() });
        return (await salesOrderOps.SplitFulfillmentPreviewAsync(id, warehouseId, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("evaluate-atp-routing")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> EvaluateAtpRouting(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EvaluateAtpRoutingRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var orders = (await service.ListSalesOrdersAsync(request?.WarehouseId, null, cancellationToken)) ?? [];
        if (salesOrderOps == null) return ForbidOrStatusCode503();
        return (await salesOrderOps.EvaluateAtpRoutingAsync(request, orders, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpGet]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<ActionResult<IReadOnlyList<SalesOrderListDto>>> List(
        [FromQuery] int? warehouseId = null, [FromQuery] string? status = null, CancellationToken cancellationToken = default) =>
        Ok(await service.ListSalesOrdersAsync(warehouseId, status, cancellationToken));

    [HttpGet("fulfillment-summary")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> FulfillmentSummary(
        [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        var orders = await service.ListSalesOrdersAsync(warehouseId, null, cancellationToken);
        var totalOrderedQuantity = orders.Sum(x => x.OrderedQuantity);
        var totalShippedQuantity = orders.Sum(x => x.ShippedQuantity);
        return Ok(new
        {
            totalOrderCount = orders.Count,
            releasedOrPartialCount = orders.Count(x => x.Status is "Released" or "PartiallyFulfilled"),
            fulfilledCount = orders.Count(x => x.Status == "Fulfilled"),
            totalOrderedQuantity,
            totalShippedQuantity,
            totalBackorderQuantity = orders.Sum(x => x.BackorderQuantity),
            totalOpenQuantity = orders.Sum(x => x.OpenQuantity),
            fulfillmentRatePct = totalOrderedQuantity > 0 ? Math.Round(totalShippedQuantity * 100m / totalOrderedQuantity, 2) : 100m,
        });
    }

    [HttpGet("{id:int}")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<ActionResult<SalesOrderDto>> Get(int id, CancellationToken cancellationToken) =>
        Ok(await service.GetSalesOrderAsync(id, cancellationToken));

    [HttpPost]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate)]
    [IdempotentCommand("SalesOrder.Create")]
    public async Task<ActionResult<SalesOrderDto>> Create(CreateSalesOrderDto request, CancellationToken cancellationToken)
    {
        var created = await service.CreateSalesOrderAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPost("{id:int}/hold")]
    [PermissionAuthorize(AppPermissions.SalesOrderHold)]
    [IdempotentCommand("SalesOrder.Hold")]
    public async Task<ActionResult<SalesOrderDto>> Hold(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.HoldSalesOrderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/release")]
    [PermissionAuthorize(AppPermissions.SalesOrderRelease)]
    [IdempotentCommand("SalesOrder.Release")]
    public async Task<ActionResult<SalesOrderDto>> Release(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.ReleaseSalesOrderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/cancel")]
    [PermissionAuthorize(AppPermissions.SalesOrderCancel)]
    [IdempotentCommand("SalesOrder.Cancel")]
    public async Task<ActionResult<SalesOrderDto>> Cancel(int id, SalesOrderStateCommandDto request, CancellationToken cancellationToken) =>
        Ok(await service.CancelSalesOrderAsync(id, request, cancellationToken));

    [HttpPost("{id:int}/confirm-cancel-putback")]
    [PermissionAuthorize(AppPermissions.SalesOrderCancel)]
    [IdempotentCommand("SalesOrder.ConfirmCancelPutback")]
    public async Task<IActionResult> ConfirmCancelPutback(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmCancelPutbackRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (salesOrderOps == null) return Ok(await service.GetSalesOrderAsync(id, cancellationToken));
        return (await salesOrderOps.ConfirmCancelPutbackAsync(
            id, request, GetCurrentUserId(), service, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpGet("~/api/v1/marketplace-shops")]
    [HttpGet("~/api/marketplace-shops")]
    [HttpGet("marketplace-shops")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> GetMarketplaceShops(
        [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        if (salesOrderOps == null) return Ok(new { items = Array.Empty<object>(), totalCount = 0 });
        return (await salesOrderOps.GetMarketplaceShopsAsync(warehouseId, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("~/api/v1/marketplace-shops")]
    [HttpPost("~/api/marketplace-shops")]
    [HttpPost("marketplace-shops")]
    [HttpPost("marketplace-shops/connect")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate)]
    [IdempotentCommand("MarketplaceShop.Connect")]
    public async Task<IActionResult> ConnectMarketplaceShop(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConnectMarketplaceShopRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (salesOrderOps == null) return ForbidOrStatusCode503();
        return (await salesOrderOps.ConnectMarketplaceShopAsync(request, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("~/api/v1/marketplace-shops/sync-inventory")]
    [HttpPost("~/api/marketplace-shops/sync-inventory")]
    [HttpPost("marketplace-shops/sync-inventory")]
    [HttpPost("~/api/v1/omnichannel/shops/sync-inventory")]
    [HttpPost("~/api/v1/integrations/marketplace/sync-inventory")]
    [HttpPost("~/api/Reports/sync-marketplace-stock")]
    [HttpPost("~/api/v1/reports/sync-marketplace-stock")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    [IdempotentCommand("MarketplaceShop.SyncInventory")]
    public async Task<IActionResult> SyncMarketplaceInventory(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SyncMarketplaceInventoryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (salesOrderOps == null) return Ok(new { syncedCount = 0, items = Array.Empty<object>() });
        return (await salesOrderOps.SyncMarketplaceInventoryAsync(request, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("marketplace-shops/{shopId:int}/sync-inventory")]
    [HttpPost("~/api/v1/marketplace-shops/{shopId:int}/sync-inventory")]
    [HttpPost("~/api/marketplace-shops/{shopId:int}/sync-inventory")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    [IdempotentCommand("MarketplaceShop.SyncInventoryByShop")]
    public Task<IActionResult> SyncMarketplaceInventoryByShop(
        int shopId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SyncMarketplaceInventoryRequest? request = null,
        CancellationToken cancellationToken = default) =>
        SyncMarketplaceInventory(
            request is null ? new SyncMarketplaceInventoryRequest(ShopId: shopId) : request with { ShopId = request.ShopId ?? shopId },
            cancellationToken);

    [HttpPost("webhook-buyer-cancellation")]
    [HttpPost("~/api/v1/integrations/webhooks/{channelCode}/buyer-cancel")]
    [PermissionAuthorize(AppPermissions.SalesOrderCancel)]
    [IdempotentCommand("SalesOrder.WebhookBuyerCancellation")]
    public async Task<IActionResult> WebhookBuyerCancellation(
        [FromRoute] string? channelCode = null,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BuyerCancellationWebhookRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (salesOrderOps == null) return Ok(new { decision = "ACCEPT", accepted = true });
        return (await salesOrderOps.WebhookBuyerCancellationAsync(
            channelCode, request, GetCurrentUserId(), warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("{id:int}/webhook-buyer-cancellation")]
    [PermissionAuthorize(AppPermissions.SalesOrderCancel)]
    [IdempotentCommand("SalesOrder.WebhookBuyerCancellationByOrder")]
    public Task<IActionResult> WebhookBuyerCancellationByOrder(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BuyerCancellationWebhookRequest? request = null,
        CancellationToken cancellationToken = default) =>
        WebhookBuyerCancellation(
            null,
            request is null ? new BuyerCancellationWebhookRequest(SalesOrderId: id) : request with { SalesOrderId = request.SalesOrderId ?? id },
            cancellationToken);

    [HttpGet("~/api/v1/promotions/gift-rules")]
    [HttpGet("promotions/gift-rules")]
    [HttpGet("promotion-gift-rules")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> GetPromotionGiftRules(
        [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
    {
        if (salesOrderOps == null)
            return Ok(new { warehouseId, totalCount = 0, items = Array.Empty<object>(), rules = Array.Empty<object>() });
        return (await salesOrderOps.GetPromotionGiftRulesAsync(warehouseId, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("~/api/v1/promotions/gift-rules")]
    [HttpPost("promotions/gift-rules")]
    [HttpPost("promotion-gift-rules")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate)]
    [IdempotentCommand("PromotionGiftRule.Create")]
    public async Task<IActionResult> CreatePromotionGiftRule(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreatePromotionGiftRuleRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (salesOrderOps == null) return ForbidOrStatusCode503();
        return (await salesOrderOps.CreatePromotionGiftRuleAsync(request, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("~/api/v1/promotions/gift-rules/evaluate")]
    [HttpPost("promotions/gift-rules/evaluate")]
    [HttpPost("promotion-gift-rules/evaluate")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    [IdempotentCommand("PromotionGiftRule.Evaluate")]
    public async Task<IActionResult> EvaluatePromotionGiftRules(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EvaluateGiftRulesRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (salesOrderOps == null) return ForbidOrStatusCode503();
        return (await salesOrderOps.EvaluatePromotionGiftRulesAsync(request, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("{id:int}/evaluate-promotion-gifts")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    [IdempotentCommand("PromotionGiftRule.EvaluateByOrder")]
    public Task<IActionResult> EvaluatePromotionGiftRulesByOrder(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EvaluateGiftRulesRequest? request = null,
        CancellationToken cancellationToken = default) =>
        EvaluatePromotionGiftRules(
            request is null ? new EvaluateGiftRulesRequest(SalesOrderId: id) : request with { SalesOrderId = request.SalesOrderId ?? id },
            cancellationToken);

    [HttpPost("import-marketplace-excel")]
    [HttpPost("~/api/v1/omnichannel/orders/import-excel")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate)]
    [IdempotentCommand("SalesOrder.ImportMarketplaceExcel")]
    public async Task<IActionResult> ImportMarketplaceOrdersExcel(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] ImportMarketplaceOrdersExcelRequest? request = null,
        CancellationToken cancellationToken = default,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] Microsoft.AspNetCore.Http.IFormFile? file = null)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true) return Forbid();
        if (salesOrderOps == null) return Ok(new { importedCount = 0, skippedDuplicatesCount = 0 });

        var uploadedFile = file ?? (Request?.HasFormContentType == true && Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null);
        var parsedFileRows = WmsControllerExcelHelper.ParseMarketplaceOrdersExcel(uploadedFile);
        var mappedInput = request == null
            ? null
            : new ImportMarketplaceOrdersExcelInput(
                request.WarehouseId,
                request.ChannelCode,
                request.Rows?.Select(r => (MarketplaceOrderExcelRowInput)r).ToList());

        return (await salesOrderOps.ImportMarketplaceOrdersExcelAsync(
            mappedInput, parsedFileRows, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    private int? GetCurrentUserId() =>
        int.TryParse(
            User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var userId)
            ? userId
            : null;

    private ObjectResult ForbidOrStatusCode503() =>
        StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
}
