using System.Text;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ERP.Api.Controllers;

public sealed partial class SalesOrdersController
{
    public sealed record ConnectMarketplaceShopRequest(
        string? ChannelCode = null, string? ShopCode = null, string? ShopName = null, int? DefaultWarehouseId = null,
        decimal? ReserveRatioPct = null, decimal? SafetyStockBufferPct = null, decimal? SafetyBufferQty = null,
        decimal? LowStockThreshold = null, bool? IsPrimaryShop = null, string? AccessToken = null,
        string? RefreshToken = null, string? ChannelSkuCode = null, int? InternalProductId = null, decimal? ConversionFactor = null)
        : ConnectMarketplaceShopInput(ChannelCode, ShopCode, ShopName, DefaultWarehouseId, ReserveRatioPct,
            SafetyStockBufferPct, SafetyBufferQty, LowStockThreshold, IsPrimaryShop, AccessToken, RefreshToken,
            ChannelSkuCode, InternalProductId, ConversionFactor);

    public sealed record SyncMarketplaceInventoryRequest(
        int? WarehouseId = null, int? ShopId = null, int? ProductId = null, decimal? SafetyStockBufferPct = null)
        : SyncMarketplaceInventoryInput(WarehouseId, ShopId, ProductId, SafetyStockBufferPct);

    public sealed record MarketplaceOAuthExchangeRequest(
        string? ChannelCode = null, string? ShopCode = null, string? ShopName = null,
        string? AuthCode = null, int? DefaultWarehouseId = null, string? WebhookSecret = null)
        : MarketplaceOAuthExchangeInput(ChannelCode, ShopCode, ShopName, AuthCode, DefaultWarehouseId, WebhookSecret);

    public sealed record BuyerCancellationWebhookRequest(
        int? SalesOrderId = null, int? ExportReceiptId = null, string? OrderNumber = null, string? MarketplaceOrderId = null,
        string? ChannelCode = null, string? BuyerReason = null, string? EventId = null, string? RawPayload = null, string? HmacSignature = null)
        : BuyerCancellationWebhookInput(SalesOrderId, ExportReceiptId, OrderNumber, MarketplaceOrderId, ChannelCode, BuyerReason, EventId, RawPayload, HmacSignature);

    public sealed record EvaluateGiftRulesRequest(
        int? WarehouseId = null, int? SalesOrderId = null, decimal? OrderTotalAmount = null,
        int? TriggerProductId = null, decimal? TriggerQuantity = null, int? GiftProductId = null, decimal? GiftQuantity = null)
        : EvaluateGiftRulesInput(WarehouseId, SalesOrderId, OrderTotalAmount, TriggerProductId, TriggerQuantity, GiftProductId, GiftQuantity);

    public sealed record CreatePromotionGiftRuleRequest(
        string? RuleCode = null, string? RuleName = null, decimal? MinOrderTotalAmount = null, decimal? MinOrderAmount = null,
        decimal? MinOrderValue = null, int? TriggerProductId = null, decimal? MinTriggerQuantity = null, int GiftProductId = 0,
        decimal? GiftQuantity = null, decimal? UnitPrice = null, bool? AutoSkipWhenOutOfStock = null, bool? IsActive = null)
        : CreatePromotionGiftRuleInput(RuleCode, RuleName, MinOrderTotalAmount, MinOrderAmount, MinOrderValue,
            TriggerProductId, MinTriggerQuantity, GiftProductId, GiftQuantity, UnitPrice, AutoSkipWhenOutOfStock, IsActive);

    public sealed record MarketplaceOrderExcelRowDto(
        string? ChannelCode = null, string? ExternalOrderId = null, string? BuyerName = null, string? BuyerPhone = null,
        string? ShippingAddress = null, string? SkuCode = null, decimal Quantity = 1m, decimal UnitPrice = 150000m)
        : MarketplaceOrderExcelRowInput(ChannelCode, ExternalOrderId, BuyerName, BuyerPhone, ShippingAddress, SkuCode, Quantity, UnitPrice);

    public sealed record ImportMarketplaceOrdersExcelRequest(
        int? WarehouseId = null, string? ChannelCode = null, List<MarketplaceOrderExcelRowDto>? Rows = null);

    [HttpGet("~/api/v1/marketplace-shops")]
    [HttpGet("~/api/marketplace-shops")]
    [HttpGet("marketplace-shops")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> GetMarketplaceShops(
        [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        (await salesOrderOps.GetMarketplaceShopsAsync(warehouseId, warehouseAccess, cancellationToken)).ToActionResult(this);

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
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderCreate)) return Forbid();
        return (await salesOrderOps.ConnectMarketplaceShopAsync(request, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("~/api/v1/marketplace-shops/oauth/exchange")]
    [HttpPost("~/api/marketplace-shops/oauth/exchange")]
    [HttpPost("marketplace-shops/oauth/exchange")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate)]
    [IdempotentCommand("MarketplaceShop.OAuthExchange")]
    public async Task<IActionResult> ExchangeMarketplaceOAuth(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MarketplaceOAuthExchangeRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderCreate)) return Forbid();
        return (await salesOrderOps.ExchangeMarketplaceOAuthAsync(request, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("~/api/v1/marketplace-shops/{shopId:int}/oauth/refresh")]
    [HttpPost("~/api/marketplace-shops/{shopId:int}/oauth/refresh")]
    [HttpPost("marketplace-shops/{shopId:int}/oauth/refresh")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate)]
    [IdempotentCommand("MarketplaceShop.OAuthRefresh")]
    public async Task<IActionResult> RefreshMarketplaceOAuthToken(int shopId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderCreate)) return Forbid();
        return (await salesOrderOps.RefreshMarketplaceOAuthTokenAsync(shopId, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("~/api/v1/marketplace-shops/sync-inventory")]
    [HttpPost("~/api/marketplace-shops/sync-inventory")]
    [HttpPost("marketplace-shops/sync-inventory")]
    [HttpPost("~/api/v1/omnichannel/shops/sync-inventory")]
    [HttpPost("~/api/v1/integrations/marketplace/sync-inventory")]
    [HttpPost("~/api/Reports/sync-marketplace-stock")]
    [HttpPost("~/api/v1/reports/sync-marketplace-stock")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate, AppPermissions.SalesOrderRelease)]
    [IdempotentCommand("MarketplaceShop.SyncInventory")]
    public async Task<IActionResult> SyncMarketplaceInventory(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SyncMarketplaceInventoryRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderCreate, AppPermissions.SalesOrderRelease)) return Forbid();
        return (await salesOrderOps.SyncMarketplaceInventoryAsync(request, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("marketplace-shops/{shopId:int}/sync-inventory")]
    [HttpPost("~/api/v1/marketplace-shops/{shopId:int}/sync-inventory")]
    [HttpPost("~/api/marketplace-shops/{shopId:int}/sync-inventory")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate, AppPermissions.SalesOrderRelease)]
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
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderCancel)) return Forbid();
        var hdrSig = Request?.Headers["X-Marketplace-Signature"].FirstOrDefault()
            ?? Request?.Headers["X-Shopee-Signature"].FirstOrDefault()
            ?? Request?.Headers["X-TikTok-Signature"].FirstOrDefault();
        var hdrEvtId = Request?.Headers["X-Webhook-Event-Id"].FirstOrDefault();
        var rawBody = await ReadSeekableBodyAsync(cancellationToken);
        var effectiveReq = (request ?? new BuyerCancellationWebhookRequest()) with
        {
            HmacSignature = !string.IsNullOrWhiteSpace(request?.HmacSignature) ? request.HmacSignature : hdrSig,
            EventId = !string.IsNullOrWhiteSpace(request?.EventId) ? request.EventId : hdrEvtId,
            RawPayload = !string.IsNullOrWhiteSpace(request?.RawPayload) ? request.RawPayload : rawBody
        };
        return (await salesOrderOps.WebhookBuyerCancellationAsync(
            channelCode, effectiveReq, GetCurrentUserId(), warehouseAccess, cancellationToken)).ToActionResult(this);
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

    [HttpGet("~/api/v1/promotions/gift-rules"), HttpGet("promotions/gift-rules"), HttpGet("promotion-gift-rules")]
    [PermissionAuthorize(AppPermissions.SalesOrderRead)]
    public async Task<IActionResult> GetPromotionGiftRules(
        [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default) =>
        (await salesOrderOps.GetPromotionGiftRulesAsync(warehouseId, cancellationToken)).ToActionResult(this);

    [HttpPost("~/api/v1/promotions/gift-rules"), HttpPost("promotions/gift-rules"), HttpPost("promotion-gift-rules")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate), IdempotentCommand("PromotionGiftRule.Create")]
    public async Task<IActionResult> CreatePromotionGiftRule(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreatePromotionGiftRuleRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderCreate)) return Forbid();
        return (await salesOrderOps.CreatePromotionGiftRuleAsync(request, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("~/api/v1/promotions/gift-rules/evaluate"), HttpPost("promotions/gift-rules/evaluate"), HttpPost("promotion-gift-rules/evaluate")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate, AppPermissions.SalesOrderRelease), IdempotentCommand("PromotionGiftRule.Evaluate")]
    public async Task<IActionResult> EvaluatePromotionGiftRules(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EvaluateGiftRulesRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderCreate, AppPermissions.SalesOrderRelease)) return Forbid();
        return (await salesOrderOps.EvaluatePromotionGiftRulesAsync(request, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    [HttpPost("{id:int}/evaluate-promotion-gifts")]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate, AppPermissions.SalesOrderRelease), IdempotentCommand("PromotionGiftRule.EvaluateByOrder")]
    public Task<IActionResult> EvaluatePromotionGiftRulesByOrder(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] EvaluateGiftRulesRequest? request = null,
        CancellationToken cancellationToken = default) =>
        EvaluatePromotionGiftRules(
            request is null ? new EvaluateGiftRulesRequest(SalesOrderId: id) : request with { SalesOrderId = request.SalesOrderId ?? id },
            cancellationToken);

    [HttpPost("import-marketplace-excel"), HttpPost("~/api/v1/omnichannel/orders/import-excel")]
    [RequestSizeLimit(FileUploadSecurityValidator.MaxSpreadsheetOrDocBytes)]
    [PermissionAuthorize(AppPermissions.SalesOrderCreate), IdempotentCommand("SalesOrder.ImportMarketplaceExcel")]
    public async Task<IActionResult> ImportMarketplaceOrdersExcel(
        [ModelBinder(BinderType = typeof(FlexibleBodyOrFormModelBinder))] ImportMarketplaceOrdersExcelRequest? request = null,
        CancellationToken cancellationToken = default,
        [ModelBinder(BinderType = typeof(FlexibleFormFileModelBinder))] IFormFile? file = null)
    {
        if (!this.HasActionPermission(null, AppPermissions.SalesOrderCreate)) return Forbid();

        var uploadedFile = file ?? (Request?.HasFormContentType == true && Request.Form.Files.Count > 0 ? Request.Form.Files[0] : null);
        if (uploadedFile != null)
        {
            var fileValidation = FileUploadSecurityValidator.ValidateSpreadsheetOrCsvFile(uploadedFile);
            if (!fileValidation.IsValid) return fileValidation.ToActionResult(this);
        }

        var parsedFileRows = WmsControllerExcelHelper.ParseMarketplaceOrdersExcel(uploadedFile);
        var mappedInput = request == null ? null : new ImportMarketplaceOrdersExcelInput(
            request.WarehouseId, request.ChannelCode, request.Rows?.Select(r => (MarketplaceOrderExcelRowInput)r).ToList());

        return (await salesOrderOps.ImportMarketplaceOrdersExcelAsync(
            mappedInput, parsedFileRows, warehouseAccess, cancellationToken)).ToActionResult(this);
    }

    private async Task<string?> ReadSeekableBodyAsync(CancellationToken cancellationToken)
    {
        if (Request?.Body == null || !Request.Body.CanSeek)
            return null;

        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }
}
