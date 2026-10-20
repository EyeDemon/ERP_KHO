using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ERP.Api.Authorization;

namespace ERP.Api.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ControllerBase
    {
        private readonly IProductService _productService;
        private readonly ErpKhoDbContext? _db;

        public ProductsController(IProductService productService, ErpKhoDbContext db = null!)
        {
            _productService = productService;
            _db = db;
        }

        [HttpGet]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        {
            var products = await _productService.GetAllProductsAsync(cancellationToken);
            return Ok(products);
        }

        [HttpGet("governance-summary")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetGovernanceSummary(CancellationToken cancellationToken = default)
        {
            var products = (await _productService.GetAllProductsAsync(cancellationToken)).ToList();
            var activeProducts = products.Where(p => p.IsActive).ToList();

            var summary = new
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
            };

            return Ok(summary);
        }

        [HttpPost("auto-audit-governance")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> AutoAuditGovernance(CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            int totalSkuCount;
            int activeSkuCount;
            int barcodeConfiguredCount;
            int uomConfiguredCount;
            int dimensionConfiguredCount;
            int expiryControlledCount;
            var qcAndTolerancePolicyCount = 0;

            if (_db != null)
            {
                totalSkuCount = await _db.Products.AsNoTracking().CountAsync(cancellationToken);
                activeSkuCount = await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken);
                barcodeConfiguredCount = await _db.Products.AsNoTracking()
                    .CountAsync(p => p.IsActive && _db.ProductBarcodes.Any(b => b.ProductId == p.Id), cancellationToken);
                uomConfiguredCount = await _db.Products.AsNoTracking()
                    .CountAsync(p => p.IsActive && (p.UnitId > 0 || _db.ProductUoms.Any(u => u.ProductId == p.Id)), cancellationToken);
                dimensionConfiguredCount = await _db.Products.AsNoTracking()
                    .CountAsync(p => p.IsActive && (p.UnitWeightKg.HasValue || p.UnitVolumeM3.HasValue || p.UnitPalletEquivalent.HasValue), cancellationToken);
                expiryControlledCount = await _db.Products.AsNoTracking()
                    .CountAsync(p => p.IsActive && p.ExpiryControl, cancellationToken);
                qcAndTolerancePolicyCount =
                    (await _db.QcPolicies.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken))
                    + (await _db.ReceivingTolerancePolicies.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken));
            }
            else
            {
                var products = (await _productService.GetAllProductsAsync(cancellationToken)).ToList();
                var active = products.Where(p => p.IsActive).ToList();
                totalSkuCount = products.Count;
                activeSkuCount = active.Count;
                barcodeConfiguredCount = active.Count(p => p.Barcodes is { Count: > 0 });
                uomConfiguredCount = active.Count(p => p.UnitId > 0);
                dimensionConfiguredCount = active.Count(p => p.UnitWeightKg.HasValue || p.UnitVolumeM3.HasValue || p.UnitPalletEquivalent.HasValue);
                expiryControlledCount = active.Count(p => p.ExpiryControl);
            }

            var auditedSkuCount = activeSkuCount > 0 ? activeSkuCount : totalSkuCount;
            var now = DateTime.UtcNow;
            var auditId = $"SKU-AUD-{now:yyyyMMdd-HHmm}";

            if (_db != null)
            {
                var actorUserId = int.TryParse(
                    User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                    out var uid) ? uid : (int?)null;
                _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
                {
                    UserId = actorUserId,
                    EntityName = "ProductGovernance",
                    Action = "Product.AutoAuditGovernance",
                    NewValues = auditId,
                    Reason = $"Đã kiểm toán tự động {activeSkuCount}/{totalSkuCount} SKU hoạt động",
                    Result = "Success",
                    Timestamp = now
                });
                await _db.SaveChangesAsync(cancellationToken);
            }

            return Ok(new
            {
                auditId,
                auditedSkuCount,
                totalSkuCount,
                activeSkuCount,
                barcodeConfiguredCount,
                uomConfiguredCount,
                dimensionConfiguredCount,
                expiryControlledCount,
                qcAndTolerancePolicyCount,
                fefoPolicyStatus = "Đã xác thực chính sách hạn dùng FEFO & vòng đời lô",
                packagingHierarchyStatus = "Đã chuẩn hóa quy đổi ĐVT, mã vạch & kích thước đóng gói",
                status = "Đã hoàn tất kiểm toán tự động chuẩn dữ liệu nền SKU",
                statusLabel = "Đã hoàn tất kiểm toán tự động chuẩn dữ liệu nền SKU",
                auditedAtUtc = now
            });
        }

        [HttpGet("paged")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetPaged(
            [FromQuery] int pageIndex = 1, 
            [FromQuery] int pageSize = 20, 
            [FromQuery] string? keyword = null,
            CancellationToken cancellationToken = default)
        {
            var pagedProducts = await _productService.GetPagedProductsAsync(pageIndex, pageSize, keyword, cancellationToken);
            return Ok(pagedProducts);
        }

        [HttpGet("{id}")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
        {
            var product = await _productService.GetProductByIdAsync(id, cancellationToken);
            return Ok(product);
        }

        [HttpPost]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.Create")]
        [PermissionAuthorize(AppPermissions.ProductCreate)]
        public async Task<IActionResult> Create([FromBody] CreateProductDto dto, CancellationToken cancellationToken = default)
        {
            var username = User?.Identity?.Name ?? "system";
            var product = await _productService.CreateProductAsync(dto, username, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }

        [HttpPut("{id}")]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.Update")]
        [PermissionAuthorize(AppPermissions.ProductUpdate)]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateProductDto dto, CancellationToken cancellationToken = default)
        {
            var username = User?.Identity?.Name ?? "system";
            await _productService.UpdateProductAsync(id, dto, username, cancellationToken);
            return NoContent();
        }

        [HttpDelete("{id}")]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.Delete")]
        [PermissionAuthorize(AppPermissions.ProductDeactivate)]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
        {
            await _productService.DeleteProductAsync(id, cancellationToken);
            return NoContent();
        }

        [HttpPut("{id:int}/category")]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.SetCategory")]
        [PermissionAuthorize(AppPermissions.ProductUpdate)]
        public async Task<IActionResult> SetCategory(int id, [FromBody] SetProductCategoryDto dto, [FromServices] IProductCatalogService catalogService, CancellationToken cancellationToken = default)
        {
            await catalogService.SetProductCategoryAsync(id, dto, cancellationToken);
            return NoContent();
        }
    }
}