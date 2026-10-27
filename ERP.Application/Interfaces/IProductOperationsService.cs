using System.Text.Json;
using ERP.Application.Common;

namespace ERP.Application.Interfaces;

public record UpsertProductUomInput(
    int? UnitId = null,
    string? UnitCode = null,
    decimal ConversionFactor = 1m,
    bool IsPurchaseDefault = false,
    bool IsSalesDefault = false);

public record UpsertQcPolicyInput(
    int? ProductId = null,
    string? ProductCode = null,
    int? SupplierId = null,
    int? WarehouseId = null,
    string? Mode = null,
    decimal? SamplePercentage = null,
    bool RequireBeforePutaway = true,
    bool IsActive = true);

public record UpsertTolerancePolicyInput(
    int? ProductId = null,
    string? ProductCode = null,
    int? SupplierId = null,
    int? WarehouseId = null,
    decimal OverReceiptTolerancePercent = 0m,
    decimal UnderReceiptTolerancePercent = 0m,
    decimal? PercentageTolerance = null,
    decimal? AbsoluteQuantityTolerance = null,
    bool AllowOverDelivery = true,
    bool RequireApprovalOnExceed = true,
    bool IsActive = true);

public record BundleComponentItemInputDto(
    int? ComponentProductId = null,
    int? ChildProductId = null,
    decimal? ComponentQuantity = null,
    decimal? Quantity = null,
    decimal? CostAllocationRatioPct = null);

public record UpsertBundleComponentInput(
    int? ParentProductId = null,
    int? ComponentProductId = null,
    int? ChildProductId = null,
    decimal? ComponentQuantity = null,
    decimal? Quantity = null,
    decimal? CostAllocationRatioPct = null,
    List<BundleComponentItemInputDto>? Components = null);

public record UpsertChannelSkuMappingInput(
    JsonElement? ShopId = null,
    string? ChannelCode = null,
    string? ShopName = null,
    string? ChannelSkuCode = null,
    string? ChannelItemId = null,
    JsonElement? InternalProductId = null,
    int? ProductId = null,
    string? InternalProductCode = null,
    decimal? ConversionFactor = 1m,
    bool? IsActive = true);

public record ProductDekitInput(
    int? WarehouseId = null,
    int? LocationId = null,
    int? LotId = null,
    decimal? Quantity = null,
    string? Note = null);

public interface IProductOperationsService
{
    Task<WmsEndpointOutcome> AutoAuditGovernanceAsync(
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetProductUomsAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> UpsertProductUomAsync(
        int id,
        UpsertProductUomInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> DeleteProductUomAsync(
        int id,
        int uomId,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetQcPoliciesAsync(
        int? productId,
        int? warehouseId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> UpsertQcPolicyAsync(
        UpsertQcPolicyInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetTolerancePoliciesAsync(
        int? productId,
        int? warehouseId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> UpsertTolerancePolicyAsync(
        UpsertTolerancePolicyInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetBundleComponentsAsync(
        int id,
        int? warehouseId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> UpsertBundleComponentAsync(
        int id,
        UpsertBundleComponentInput? request,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> GetChannelSkuMappingsAsync(
        int? productId,
        int? shopId,
        string? channelCode,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> UpsertChannelSkuMappingAsync(
        UpsertChannelSkuMappingInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);

    Task<WmsEndpointOutcome> DekitAsync(
        int? id,
        ProductDekitInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default);
}
