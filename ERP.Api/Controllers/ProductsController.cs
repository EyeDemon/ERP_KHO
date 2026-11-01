using System.Security.Claims;
using System.Text.Json;
using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
[Route("api/v1/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IProductCatalogService _catalogService;
    private readonly IProductOperationsService _productOps;
    private readonly ICurrentUser _currentUser;

    public ProductsController(
        IProductService productService,
        IProductCatalogService catalogService,
        IProductOperationsService productOps,
        ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(productService);
        ArgumentNullException.ThrowIfNull(catalogService);
        ArgumentNullException.ThrowIfNull(productOps);
        ArgumentNullException.ThrowIfNull(currentUser);
        _productService = productService;
        _catalogService = catalogService;
        _productOps = productOps;
        _currentUser = currentUser;
    }

    [HttpGet]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        => Ok(await _productService.GetAllProductsAsync(cancellationToken));

    [HttpGet("governance-summary")]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetGovernanceSummary(CancellationToken cancellationToken = default)
    {
        var products = (await _productService.GetAllProductsAsync(cancellationToken)).ToList();
        var activeProducts = products.Where(p => p.IsActive).ToList();

        return Ok(new
        {
            totalSkuCount = products.Count,
            activeSkuCount = activeProducts.Count,
            lotTrackedCount = products.Count(p => string.Equals(p.TrackingType, "Lot", StringComparison.OrdinalIgnoreCase)),
            serialTrackedCount = products.Count(p => string.Equals(p.TrackingType, "Serial", StringComparison.OrdinalIgnoreCase)),
            expiryControlledCount = products.Count(p => p.ExpiryControl),
            dimensionConfiguredCount = products.Count(p => p.UnitWeightKg.HasValue || p.UnitVolumeM3.HasValue || p.UnitPalletEquivalent.HasValue),
            barcodeConfiguredCount = products.Count(p => p.Barcodes is { Count: > 0 }),
            incompleteGovernanceSkus = activeProducts
                .Where(p => (p.UnitWeightKg == null && p.UnitVolumeM3 == null) || (p.ExpiryControl && p.ShelfLifeDays == null))
                .Take(20)
                .Select(p => new
                {
                    p.Id,
                    p.Code,
                    p.Name,
                    missingDimensions = p.UnitWeightKg == null && p.UnitVolumeM3 == null,
                    missingShelfLifeDays = p.ExpiryControl && p.ShelfLifeDays == null
                })
                .ToList()
        });
    }

    [HttpPost("auto-audit-governance")]
    [PermissionAuthorize(AppPermissions.ProductUpdate)]
    public async Task<IActionResult> AutoAuditGovernance(CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(_currentUser, AppPermissions.ProductUpdate))
            return Forbid();

        return (await _productOps.AutoAuditGovernanceAsync(ResolveActorUserId(), cancellationToken)).ToActionResult(this);
    }

    [HttpGet("paged")]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? keyword = null,
        CancellationToken cancellationToken = default)
        => Ok(await _productService.GetPagedProductsAsync(pageIndex, pageSize, keyword, cancellationToken));

    [HttpGet("{id}")]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
        => Ok(await _productService.GetProductByIdAsync(id, cancellationToken));

    [HttpPost]
    [IdempotentCommand("Product.Create")]
    [PermissionAuthorize(AppPermissions.ProductCreate)]
    public async Task<IActionResult> Create([FromBody] CreateProductDto dto, CancellationToken cancellationToken = default)
    {
        var product = await _productService.CreateProductAsync(dto, User?.Identity?.Name ?? "system", cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
    }

    [HttpPut("{id}")]
    [IdempotentCommand("Product.Update")]
    [PermissionAuthorize(AppPermissions.ProductUpdate)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProductDto dto, CancellationToken cancellationToken = default)
    {
        await _productService.UpdateProductAsync(id, dto, User?.Identity?.Name ?? "system", cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [IdempotentCommand("Product.Delete")]
    [PermissionAuthorize(AppPermissions.ProductDeactivate)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        await _productService.DeleteProductAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:int}/category")]
    [IdempotentCommand("Product.SetCategory")]
    [PermissionAuthorize(AppPermissions.ProductUpdate)]
    public async Task<IActionResult> SetCategory(
        int id,
        [FromBody] SetProductCategoryDto dto,
        [FromServices] IProductCatalogService? catalogService = null,
        CancellationToken cancellationToken = default)
    {
        var svc = catalogService ?? _catalogService;
        if (svc != null)
            await svc.SetProductCategoryAsync(id, dto, cancellationToken);
        return NoContent();
    }

    public sealed record UpsertProductUomRequest(int? UnitId = null, string? UnitCode = null, decimal ConversionFactor = 1m, bool IsPurchaseDefault = false, bool IsSalesDefault = false)
        : UpsertProductUomInput(UnitId, UnitCode, ConversionFactor, IsPurchaseDefault, IsSalesDefault);

    [HttpGet("{id:int}/uoms")]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetProductUoms(int id, CancellationToken cancellationToken = default)
        => (await _productOps.GetProductUomsAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:int}/uoms")]
    [IdempotentCommand("Product.UpsertUom")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.ProductCreate, AppPermissions.UomManage)]
    public async Task<IActionResult> UpsertProductUom(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpsertProductUomRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ProductUpdate, AppPermissions.ProductCreate, AppPermissions.UomManage))
            return Forbid();
        return (await _productOps.UpsertProductUomAsync(id, request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);
    }

    [HttpDelete("{id:int}/uoms/{uomId:int}")]
    [IdempotentCommand("Product.DeleteUom")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.ProductCreate, AppPermissions.UomManage)]
    public async Task<IActionResult> DeleteProductUom(int id, int uomId, CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ProductUpdate, AppPermissions.ProductCreate, AppPermissions.UomManage))
            return Forbid();
        return (await _productOps.DeleteProductUomAsync(id, uomId, ResolveActorUserId(), cancellationToken)).ToActionResult(this);
    }

    public sealed record UpsertQcPolicyRequest(int? ProductId = null, string? ProductCode = null, int? SupplierId = null, int? WarehouseId = null, string? Mode = null, decimal? SamplePercentage = null, bool RequireBeforePutaway = true, bool IsActive = true)
        : UpsertQcPolicyInput(ProductId, ProductCode, SupplierId, WarehouseId, Mode, SamplePercentage, RequireBeforePutaway, IsActive);

    [HttpGet("qc-policies")]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetQcPolicies([FromQuery] int? productId = null, [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
        => (await _productOps.GetQcPoliciesAsync(productId, warehouseId, cancellationToken)).ToActionResult(this);

    [HttpPost("qc-policies")]
    [IdempotentCommand("Product.UpsertQcPolicy")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.ProductCreate)]
    public async Task<IActionResult> UpsertQcPolicy(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpsertQcPolicyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ProductUpdate, AppPermissions.ProductCreate))
            return Forbid();
        return (await _productOps.UpsertQcPolicyAsync(request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);
    }

    public sealed record UpsertTolerancePolicyRequest(
        int? ProductId = null, string? ProductCode = null, int? SupplierId = null, int? WarehouseId = null,
        decimal OverReceiptTolerancePercent = 0m, decimal UnderReceiptTolerancePercent = 0m, decimal? PercentageTolerance = null,
        decimal? AbsoluteQuantityTolerance = null, bool AllowOverDelivery = true, bool RequireApprovalOnExceed = true, bool IsActive = true)
        : UpsertTolerancePolicyInput(ProductId, ProductCode, SupplierId, WarehouseId, OverReceiptTolerancePercent, UnderReceiptTolerancePercent, PercentageTolerance, AbsoluteQuantityTolerance, AllowOverDelivery, RequireApprovalOnExceed, IsActive);

    [HttpGet("tolerance-policies")]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetTolerancePolicies([FromQuery] int? productId = null, [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
        => (await _productOps.GetTolerancePoliciesAsync(productId, warehouseId, cancellationToken)).ToActionResult(this);

    [HttpPost("tolerance-policies")]
    [IdempotentCommand("Product.UpsertTolerancePolicy")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.ProductCreate)]
    public async Task<IActionResult> UpsertTolerancePolicy(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpsertTolerancePolicyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ProductUpdate, AppPermissions.ProductCreate))
            return Forbid();
        return (await _productOps.UpsertTolerancePolicyAsync(request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);
    }

    public sealed record BundleComponentItemInput(int? ComponentProductId = null, int? ChildProductId = null, decimal? ComponentQuantity = null, decimal? Quantity = null, decimal? CostAllocationRatioPct = null)
        : BundleComponentItemInputDto(ComponentProductId, ChildProductId, ComponentQuantity, Quantity, CostAllocationRatioPct);

    public sealed record UpsertBundleComponentRequest(
        int? ParentProductId = null, int? ComponentProductId = null, int? ChildProductId = null,
        decimal? ComponentQuantity = null, decimal? Quantity = null, decimal? CostAllocationRatioPct = null,
        List<BundleComponentItemInput>? Components = null);

    [HttpGet("{id:int}/bundle-components")]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetBundleComponents(int id, [FromQuery] int? warehouseId = null, CancellationToken cancellationToken = default)
        => (await _productOps.GetBundleComponentsAsync(id, warehouseId, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:int}/bundle-components")]
    [PermissionAuthorize(AppPermissions.ProductUpdate)]
    [IdempotentCommand("Product.UpsertBundleComponents")]
    public async Task<IActionResult> UpsertBundleComponent(
        int id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpsertBundleComponentRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ProductUpdate))
            return Forbid();

        var input = request == null
            ? null
            : new UpsertBundleComponentInput(
                request.ParentProductId, request.ComponentProductId, request.ChildProductId,
                request.ComponentQuantity, request.Quantity, request.CostAllocationRatioPct,
                request.Components?.Cast<BundleComponentItemInputDto>().ToList());
        return (await _productOps.UpsertBundleComponentAsync(id, input, cancellationToken)).ToActionResult(this);
    }

    public sealed record UpsertChannelSkuMappingRequest(
        JsonElement? ShopId = null, string? ChannelCode = null, string? ShopName = null, string? ChannelSkuCode = null,
        string? ChannelItemId = null, JsonElement? InternalProductId = null, int? ProductId = null,
        string? InternalProductCode = null, decimal? ConversionFactor = 1m, bool? IsActive = true)
        : UpsertChannelSkuMappingInput(ShopId, ChannelCode, ShopName, ChannelSkuCode, ChannelItemId, InternalProductId, ProductId, InternalProductCode, ConversionFactor, IsActive);

    [HttpGet("channel-sku-mappings")]
    [PermissionAuthorize(AppPermissions.ProductRead)]
    public async Task<IActionResult> GetChannelSkuMappings(
        [FromQuery] int? productId = null, [FromQuery] int? shopId = null, [FromQuery] string? channelCode = null,
        CancellationToken cancellationToken = default)
        => (await _productOps.GetChannelSkuMappingsAsync(productId, shopId, channelCode, cancellationToken)).ToActionResult(this);

    [HttpPost("channel-sku-mappings")]
    [PermissionAuthorize(AppPermissions.ProductUpdate)]
    [IdempotentCommand("Product.UpsertChannelSkuMapping")]
    public async Task<IActionResult> UpsertChannelSkuMapping(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UpsertChannelSkuMappingRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ProductUpdate))
            return Forbid();
        return (await _productOps.UpsertChannelSkuMappingAsync(request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);
    }

    public sealed record ProductDekitRequest(int? WarehouseId = null, int? LocationId = null, int? LotId = null, decimal? Quantity = null, string? Note = null)
        : ProductDekitInput(WarehouseId, LocationId, LotId, Quantity, Note);

    [HttpPost("{id:int}/dekit")]
    [HttpPost("dekit")]
    [IdempotentCommand("Product.Dekit")]
    [PermissionAuthorize(AppPermissions.ProductUpdate)]
    public async Task<IActionResult> Dekit(
        [FromRoute] int? id = null,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ProductDekitRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!this.HasActionPermission(null, AppPermissions.ProductUpdate))
            return Forbid();
        return (await _productOps.DekitAsync(id, request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);
    }

    [HttpGet("kit-work-orders")]
    [HttpGet("/api/v1/kit-work-orders")]
    [PermissionAuthorize(AppPermissions.ProductRead, AppPermissions.InventoryRead)]
    public async Task<IActionResult> GetKitWorkOrders(
        [FromQuery] int? warehouseId = null, [FromQuery] int? kitProductId = null, [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
        => (await _productOps.GetKitWorkOrdersAsync(warehouseId, kitProductId, status, cancellationToken)).ToActionResult(this);

    [HttpPost("kit-work-orders")]
    [HttpPost("/api/v1/kit-work-orders")]
    [IdempotentCommand("Product.CreateKitWorkOrder")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> CreateKitWorkOrder(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateKitWorkOrderInput? request = null,
        CancellationToken cancellationToken = default)
        => !this.HasActionPermission(_currentUser, AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)
            ? Forbid()
            : (await _productOps.CreateKitWorkOrderAsync(request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);

    [HttpPost("kit-work-orders/{id:int}/complete")]
    [HttpPost("/api/v1/kit-work-orders/{id:int}/complete")]
    [IdempotentCommand("Product.CompleteKitWorkOrder")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> CompleteKitWorkOrder(
        int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CompleteKitWorkOrderInput? request = null,
        CancellationToken cancellationToken = default)
        => !this.HasActionPermission(_currentUser, AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)
            ? Forbid()
            : (await _productOps.CompleteKitWorkOrderAsync(id, request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);

    [HttpPost("kit-work-orders/{id:int}/cancel")]
    [HttpPost("/api/v1/kit-work-orders/{id:int}/cancel")]
    [IdempotentCommand("Product.CancelKitWorkOrder")]
    [PermissionAuthorize(AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)]
    public async Task<IActionResult> CancelKitWorkOrder(
        int id, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelKitWorkOrderInput? request = null,
        CancellationToken cancellationToken = default)
        => !this.HasActionPermission(_currentUser, AppPermissions.ProductUpdate, AppPermissions.InventoryMovementCreate)
            ? Forbid()
            : (await _productOps.CancelKitWorkOrderAsync(id, request, ResolveActorUserId(), cancellationToken)).ToActionResult(this);

    private int? ResolveActorUserId()
        => int.TryParse(
            User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : null;
}
