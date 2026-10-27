using ERP.Application.Common;
using ERP.Application.DTOs;

namespace ERP.Application.Interfaces;

public record ExpediteBackorderInput(int? WarehouseId, string? OrderNumber);

public record EvaluateAtpRoutingInput(
    int? WarehouseId = null,
    int? SalesOrderId = null,
    int? ProductId = null,
    decimal? RequestedQty = null,
    string? DestinationProvince = null,
    string? OrderCode = null,
    string? ProductCode = null,
    decimal? RequestedQuantity = null,
    string? CustomerProvince = null,
    string? FulfillmentMode = null);

public record ConfirmCancelPutbackInput(
    int? SourceLocationId = null,
    int? DestinationLocationId = null,
    string? Note = null);

public record SplitFulfillmentPlanItemDtoModel(
    int ProductId,
    string ProductCode,
    string ProductName,
    decimal RequestedQty,
    int AllocatedWarehouseId,
    string AllocatedWarehouseCode,
    string AllocatedWarehouseName,
    decimal AllocatedQty,
    decimal UnfulfilledQty,
    int LegNumber = 1,
    string OrderCode = "SO-QA-001",
    string SubOrderCode = "SO-QA-001",
    int WarehouseId = 1,
    string WarehouseCode = "WH01",
    string WarehouseName = "WH01 — Kho Chính TP.HCM",
    decimal AllocatedQuantity = 0m,
    decimal AvailableAtpQty = 0m,
    int DistanceKm = 12,
    int EstimatedLeadTimeHours = 4,
    int RoutingPriority = 1,
    string RoutingNote = "Đủ tồn 1 kho • WH01 — Kho Chính TP.HCM",
    string RoutingStrategyLabel = "Đủ tồn 1 kho • WH01 — Kho Chính TP.HCM");

public record ConnectMarketplaceShopInput(
    string? ChannelCode = null,
    string? ShopCode = null,
    string? ShopName = null,
    int? DefaultWarehouseId = null,
    decimal? ReserveRatioPct = null,
    decimal? SafetyStockBufferPct = null,
    decimal? SafetyBufferQty = null,
    decimal? LowStockThreshold = null,
    bool? IsPrimaryShop = null,
    string? AccessToken = null,
    string? RefreshToken = null,
    string? ChannelSkuCode = null,
    int? InternalProductId = null,
    decimal? ConversionFactor = null);

public record SyncMarketplaceInventoryInput(
    int? WarehouseId = null,
    int? ShopId = null,
    int? ProductId = null,
    decimal? SafetyStockBufferPct = null);

public record BuyerCancellationWebhookInput(
    int? SalesOrderId = null,
    int? ExportReceiptId = null,
    string? OrderNumber = null,
    string? MarketplaceOrderId = null,
    string? ChannelCode = null,
    string? BuyerReason = null);

public record EvaluateGiftRulesInput(
    int? WarehouseId = null,
    int? SalesOrderId = null,
    decimal? OrderTotalAmount = null,
    int? TriggerProductId = null,
    decimal? TriggerQuantity = null,
    int? GiftProductId = null,
    decimal? GiftQuantity = null);

public record CreatePromotionGiftRuleInput(
    string? RuleCode = null,
    string? RuleName = null,
    decimal? MinOrderTotalAmount = null,
    decimal? MinOrderAmount = null,
    decimal? MinOrderValue = null,
    int? TriggerProductId = null,
    decimal? MinTriggerQuantity = null,
    int GiftProductId = 0,
    decimal? GiftQuantity = null,
    decimal? UnitPrice = null,
    bool? AutoSkipWhenOutOfStock = null,
    bool? IsActive = null);

public record MarketplaceOrderExcelRowInput(
    string? ChannelCode = null,
    string? ExternalOrderId = null,
    string? BuyerName = null,
    string? BuyerPhone = null,
    string? ShippingAddress = null,
    string? SkuCode = null,
    decimal Quantity = 1m,
    decimal UnitPrice = 150000m);

public record ImportMarketplaceOrdersExcelInput(
    int? WarehouseId = null,
    string? ChannelCode = null,
    List<MarketplaceOrderExcelRowInput>? Rows = null);

public interface ISalesOrderOperationsService
{
    Task<WmsEndpointOutcome> ExpediteBackorderAsync(
        ExpediteBackorderInput? request,
        IReadOnlyList<SalesOrderListDto> orders,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> SplitFulfillmentPreviewAsync(
        string? id,
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> EvaluateAtpRoutingAsync(
        EvaluateAtpRoutingInput? request,
        IReadOnlyList<SalesOrderListDto> orders,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ConfirmCancelPutbackAsync(
        int id,
        ConfirmCancelPutbackInput? request,
        int? actorUserId,
        ISalesOrderBackorderService backorderService,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetMarketplaceShopsAsync(
        int? warehouseId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ConnectMarketplaceShopAsync(
        ConnectMarketplaceShopInput? request,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> SyncMarketplaceInventoryAsync(
        SyncMarketplaceInventoryInput? request,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> WebhookBuyerCancellationAsync(
        string? channelCode,
        BuyerCancellationWebhookInput? request,
        int? actorUserId,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetPromotionGiftRulesAsync(
        int? warehouseId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> CreatePromotionGiftRuleAsync(
        CreatePromotionGiftRuleInput? request,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> EvaluatePromotionGiftRulesAsync(
        EvaluateGiftRulesInput? request,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> ImportMarketplaceOrdersExcelAsync(
        ImportMarketplaceOrdersExcelInput? request,
        IReadOnlyList<MarketplaceOrderExcelRowInput> parsedFileRows,
        IWarehouseAuthorizationService? warehouseAccessOverride = null,
        CancellationToken cancellationToken = default);
}
