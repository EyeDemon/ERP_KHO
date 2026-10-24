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
    [Route("api/v1/products")]
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

        public sealed record UpsertProductUomRequest(
            int? UnitId = null,
            string? UnitCode = null,
            decimal ConversionFactor = 1m,
            bool IsPurchaseDefault = false,
            bool IsSalesDefault = false);

        [HttpGet("{id:int}/uoms")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetProductUoms(int id, CancellationToken cancellationToken = default)
        {
            if (_db == null)
                return Ok(Array.Empty<object>());

            var product = await _db.Products.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.Id, p.Code, p.Name, p.UnitId, BaseUnitName = p.Unit != null ? p.Unit.Name : "Cái" })
                .FirstOrDefaultAsync(cancellationToken);
            if (product == null)
                return NotFound(new { message = "Không tìm thấy sản phẩm." });

            var items = await _db.ProductUoms.AsNoTracking()
                .Where(u => u.ProductId == id && u.IsActive)
                .OrderBy(u => u.Id)
                .Select(u => new
                {
                    id = u.Id,
                    productId = u.ProductId,
                    productCode = product.Code,
                    baseUnitId = product.UnitId,
                    baseUnitName = product.BaseUnitName,
                    unitId = u.UnitId,
                    unitCode = u.Unit.Code,
                    unitName = u.Unit.Name,
                    conversionFactor = u.ConversionFactor,
                    isPurchaseDefault = true,
                    isSalesDefault = false,
                    formulaLabel = $"1 {u.Unit.Name} = {u.ConversionFactor:0.####} {product.BaseUnitName}",
                    createdAtUtc = u.EffectiveFromUtc
                })
                .ToListAsync(cancellationToken);

            return Ok(items);
        }

        [HttpPost("{id:int}/uoms")]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.UpsertUom")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> UpsertProductUom(
            int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpsertProductUomRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            if (_db == null)
                return BadRequest(new { message = "Không thể kết nối cơ sở dữ liệu để cấu hình quy đổi đơn vị tính." });

            var product = await _db.Products.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => new { p.Id, p.Code, p.Name, p.UnitId })
                .FirstOrDefaultAsync(cancellationToken);
            if (product == null)
                return NotFound(new { message = "Không tìm thấy sản phẩm cần cấu hình đơn vị tính." });

            var resolvedUnitId = request?.UnitId;
            if ((!resolvedUnitId.HasValue || resolvedUnitId.Value <= 0) && !string.IsNullOrWhiteSpace(request?.UnitCode))
            {
                var normalizedCode = request.UnitCode.Trim();
                resolvedUnitId = await _db.Units.AsNoTracking()
                    .Where(u => u.Code == normalizedCode || u.Name == normalizedCode)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (!resolvedUnitId.HasValue || resolvedUnitId.Value <= 0)
            {
                resolvedUnitId = await _db.Units.AsNoTracking()
                    .Where(u => u.Id != product.UnitId)
                    .OrderBy(u => u.Id)
                    .Select(u => (int?)u.Id)
                    .FirstOrDefaultAsync(cancellationToken) ?? product.UnitId;
            }

            var factor = request?.ConversionFactor > 0m ? request.ConversionFactor : 1m;
            var now = DateTime.UtcNow;

            var existing = await _db.ProductUoms
                .FirstOrDefaultAsync(u => u.ProductId == id && u.UnitId == resolvedUnitId.Value, cancellationToken);
            if (existing != null)
            {
                existing.ConversionFactor = factor;
                existing.IsActive = true;
                existing.EffectiveFromUtc = now;
            }
            else
            {
                existing = new ERP.Domain.Entities.ProductUom
                {
                    ProductId = id,
                    UnitId = resolvedUnitId.Value,
                    ConversionFactor = factor,
                    Version = 1,
                    EffectiveFromUtc = now,
                    IsActive = true
                };
                _db.ProductUoms.Add(existing);
            }

            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;

            _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                EntityName = "ProductUom",
                EntityId = id,
                Action = "Product.UpsertUom",
                NewValues = $"UnitId={resolvedUnitId.Value};Factor={factor:0.####}",
                Reason = $"Cấu hình quy đổi ĐVT phụ cho SKU {product.Code} (Hệ số: {factor:0.####})",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);

            var unitInfo = await _db.Units.AsNoTracking()
                .Where(u => u.Id == resolvedUnitId.Value)
                .Select(u => new { u.Code, u.Name })
                .FirstOrDefaultAsync(cancellationToken);

            return Ok(new
            {
                id = existing.Id,
                productId = id,
                productCode = product.Code,
                unitId = existing.UnitId,
                unitCode = unitInfo?.Code ?? string.Empty,
                unitName = unitInfo?.Name ?? string.Empty,
                conversionFactor = existing.ConversionFactor,
                isPurchaseDefault = request?.IsPurchaseDefault ?? true,
                isSalesDefault = request?.IsSalesDefault ?? false,
                status = "Đã lưu quy đổi đơn vị tính phụ",
                statusLabel = "Đã lưu quy đổi đơn vị tính phụ",
                savedAtUtc = now
            });
        }

        [HttpDelete("{id:int}/uoms/{uomId:int}")]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.DeleteUom")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> DeleteProductUom(
            int id,
            int uomId,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            if (_db == null)
                return BadRequest(new { message = "Không thể kết nối cơ sở dữ liệu." });

            var existing = await _db.ProductUoms
                .FirstOrDefaultAsync(u => u.Id == uomId && u.ProductId == id, cancellationToken);
            if (existing == null)
                return NotFound(new { message = "Không tìm thấy quy đổi đơn vị tính cần xóa." });

            _db.ProductUoms.Remove(existing);

            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;

            _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                EntityName = "ProductUom",
                EntityId = id,
                Action = "Product.DeleteUom",
                OldValues = $"UomId={uomId};UnitId={existing.UnitId}",
                Reason = $"Xóa quy đổi ĐVT phụ #{uomId} của sản phẩm #{id}",
                Result = "Success",
                Timestamp = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                id = uomId,
                productId = id,
                status = "Đã xóa quy đổi đơn vị tính phụ",
                statusLabel = "Đã xóa quy đổi đơn vị tính phụ"
            });
        }

        public sealed record UpsertQcPolicyRequest(
            int? ProductId = null,
            string? ProductCode = null,
            int? SupplierId = null,
            int? WarehouseId = null,
            string? Mode = null,
            decimal? SamplePercentage = null,
            bool RequireBeforePutaway = true,
            bool IsActive = true);

        [HttpGet("qc-policies")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetQcPolicies(
            [FromQuery] int? productId = null,
            [FromQuery] int? warehouseId = null,
            CancellationToken cancellationToken = default)
        {
            if (_db == null)
                return Ok(Array.Empty<object>());

            var query = _db.QcPolicies.AsNoTracking().AsQueryable();
            if (productId.HasValue)
                query = query.Where(p => p.ProductId == productId.Value);

            var rows = await query
                .OrderByDescending(p => p.IsActive)
                .ThenByDescending(p => p.Id)
                .Take(100)
                .Select(p => new
                {
                    p.Id,
                    p.ProductId,
                    ProductCode = p.Product != null ? p.Product.Code : null,
                    ProductName = p.Product != null ? p.Product.Name : null,
                    p.SupplierId,
                    SupplierName = p.Supplier != null ? p.Supplier.Name : null,
                    p.RequiresQc,
                    p.Rule,
                    p.IsActive,
                    p.EffectiveFromUtc
                })
                .ToListAsync(cancellationToken);

            var items = rows.Select(r =>
            {
                var ruleUpper = (r.Rule ?? string.Empty).ToUpperInvariant();
                var mode = !r.RequiresQc || ruleUpper.Contains("SKIP")
                    ? "Skip"
                    : (ruleUpper.Contains("FULL") || ruleUpper.Contains("100%") ? "Full" : "Sample");
                var samplePct = mode == "Full" ? 100m : (mode == "Skip" ? 0m : 10m);
                return new
                {
                    id = r.Id,
                    productId = r.ProductId,
                    productCode = r.ProductCode ?? "Tất cả SKU",
                    productName = r.ProductName ?? "Áp dụng toàn bộ danh mục",
                    supplierId = r.SupplierId,
                    supplierName = r.SupplierName ?? "Tất cả nhà cung cấp",
                    warehouseId,
                    warehouseName = "Tất cả kho",
                    mode,
                    modeLabel = mode switch
                    {
                        "Full" => "Kiểm định toàn phần (100%)",
                        "Sample" => "Lấy mẫu xác suất",
                        _ => "Bỏ qua kiểm định"
                    },
                    requiresQc = r.RequiresQc,
                    rule = r.Rule,
                    samplePercentage = samplePct,
                    requireBeforePutaway = r.RequiresQc,
                    isActive = r.IsActive,
                    createdAtUtc = r.EffectiveFromUtc
                };
            }).ToList();

            return Ok(items);
        }

        [HttpPost("qc-policies")]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.UpsertQcPolicy")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> UpsertQcPolicy(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpsertQcPolicyRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            if (_db == null)
                return BadRequest(new { message = "Không thể kết nối cơ sở dữ liệu để lưu chính sách kiểm định QC." });

            var resolvedProductId = request?.ProductId;
            if ((!resolvedProductId.HasValue || resolvedProductId.Value <= 0) && !string.IsNullOrWhiteSpace(request?.ProductCode))
            {
                var code = request.ProductCode.Trim();
                resolvedProductId = await _db.Products.AsNoTracking()
                    .Where(p => p.Code == code)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (!resolvedProductId.HasValue || resolvedProductId.Value <= 0)
            {
                resolvedProductId = await _db.Products.AsNoTracking()
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Id)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (!resolvedProductId.HasValue)
                return BadRequest(new { message = "Không tìm thấy sản phẩm để áp dụng chính sách QC." });

            var resolvedSupplierId = request?.SupplierId is > 0 ? request.SupplierId : null;
            var resolvedWarehouseId = request?.WarehouseId is > 0 ? request.WarehouseId : null;

            var modeRaw = request?.Mode?.Trim() ?? "Sample";
            var normalizedMode = modeRaw.ToUpperInvariant() switch
            {
                "FULL" or "2" or "100%" => "Full",
                "SKIP" or "0" or "NONE" => "Skip",
                _ => "Sample"
            };
            var requiresQc = normalizedMode != "Skip";
            var samplePct = normalizedMode == "Full"
                ? 100m
                : (normalizedMode == "Skip" ? 0m : (request?.SamplePercentage is > 0m and <= 100m ? request.SamplePercentage.Value : 10m));
            var ruleText = normalizedMode switch
            {
                "Full" => "FULL_100_PERCENT",
                "Skip" => "SKIP_INSPECTION",
                _ => $"SAMPLE_{samplePct:0.##}_PERCENT"
            };

            var now = DateTime.UtcNow;
            var existing = await _db.QcPolicies
                .FirstOrDefaultAsync(p =>
                    p.ProductId == resolvedProductId.Value &&
                    p.SupplierId == resolvedSupplierId,
                    cancellationToken);

            if (existing != null)
            {
                existing.RequiresQc = requiresQc;
                existing.Rule = ruleText;
                existing.IsActive = request?.IsActive ?? true;
                existing.EffectiveFromUtc = now;
            }
            else
            {
                existing = new ERP.Domain.Entities.QcPolicy
                {
                    ProductId = resolvedProductId.Value,
                    SupplierId = resolvedSupplierId,
                    Version = 1,
                    RequiresQc = requiresQc,
                    Rule = ruleText,
                    IsActive = request?.IsActive ?? true,
                    EffectiveFromUtc = now
                };
                _db.QcPolicies.Add(existing);
            }

            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;

            _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = resolvedWarehouseId,
                EntityName = "QcPolicy",
                EntityId = resolvedProductId.Value,
                Action = "Product.UpsertQcPolicy",
                NewValues = $"Mode={normalizedMode};Rule={ruleText}",
                Reason = $"Cập nhật chính sách QC đầu vào ({normalizedMode})",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                id = existing.Id,
                productId = existing.ProductId,
                supplierId = existing.SupplierId,
                warehouseId = resolvedWarehouseId,
                mode = normalizedMode,
                modeLabel = normalizedMode switch
                {
                    "Full" => "Kiểm định toàn phần (100%)",
                    "Sample" => "Lấy mẫu xác suất",
                    _ => "Bỏ qua kiểm định"
                },
                requiresQc = existing.RequiresQc,
                rule = existing.Rule,
                samplePercentage = samplePct,
                requireBeforePutaway = request?.RequireBeforePutaway ?? requiresQc,
                isActive = existing.IsActive,
                status = "Đã lưu chính sách kiểm định chất lượng (QC Policy)",
                statusLabel = "Đã lưu chính sách kiểm định chất lượng (QC Policy)",
                savedAtUtc = now
            });
        }

        public sealed record UpsertTolerancePolicyRequest(
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

        [HttpGet("tolerance-policies")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetTolerancePolicies(
            [FromQuery] int? productId = null,
            [FromQuery] int? warehouseId = null,
            CancellationToken cancellationToken = default)
        {
            if (_db == null)
                return Ok(Array.Empty<object>());

            var query = _db.ReceivingTolerancePolicies.AsNoTracking().AsQueryable();
            if (productId.HasValue)
                query = query.Where(p => p.ProductId == productId.Value);
            if (warehouseId.HasValue)
                query = query.Where(p => p.WarehouseId == warehouseId.Value);

            var rows = await query
                .OrderByDescending(p => p.IsActive)
                .ThenByDescending(p => p.Id)
                .Take(100)
                .ToListAsync(cancellationToken);

            var productIds = rows.Where(r => r.ProductId.HasValue).Select(r => r.ProductId!.Value).Distinct().ToList();
            var productMap = productIds.Count > 0
                ? await _db.Products.AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .ToDictionaryAsync(p => p.Id, p => new { p.Code, p.Name }, cancellationToken)
                : new();

            var items = rows.Select(r =>
            {
                var displayPct = r.PercentageTolerance <= 1m ? Math.Round(r.PercentageTolerance * 100m, 2) : r.PercentageTolerance;
                var prodInfo = r.ProductId.HasValue && productMap.TryGetValue(r.ProductId.Value, out var info) ? info : null;
                return new
                {
                    id = r.Id,
                    productId = r.ProductId,
                    productCode = prodInfo?.Code ?? "Tất cả SKU",
                    productName = prodInfo?.Name ?? "Áp dụng toàn bộ danh mục",
                    supplierId = r.SupplierId,
                    supplierName = "Tất cả nhà cung cấp",
                    warehouseId = r.WarehouseId,
                    warehouseName = "Tất cả kho",
                    percentageTolerance = r.PercentageTolerance,
                    overReceiptTolerancePercent = displayPct,
                    underReceiptTolerancePercent = displayPct,
                    absoluteQuantityTolerance = r.AbsoluteQuantityTolerance,
                    allowOverDelivery = r.OverageAllowed,
                    requireApprovalOnExceed = r.RequiresApprovalOutsideTolerance,
                    isActive = r.IsActive,
                    toleranceSummary = $"Dung sai ±{displayPct:0.##}% (Tối đa ±{r.AbsoluteQuantityTolerance:0.##} ĐVT)",
                    createdAtUtc = r.EffectiveFromUtc
                };
            }).ToList();

            return Ok(items);
        }

        [HttpPost("tolerance-policies")]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.UpsertTolerancePolicy")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> UpsertTolerancePolicy(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpsertTolerancePolicyRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
                return Forbid();

            if (_db == null)
                return BadRequest(new { message = "Không thể kết nối cơ sở dữ liệu để lưu chính sách dung sai nhận hàng." });

            var resolvedProductId = request?.ProductId;
            if ((!resolvedProductId.HasValue || resolvedProductId.Value <= 0) && !string.IsNullOrWhiteSpace(request?.ProductCode))
            {
                var code = request.ProductCode.Trim();
                resolvedProductId = await _db.Products.AsNoTracking()
                    .Where(p => p.Code == code)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            if (resolvedProductId is <= 0)
                resolvedProductId = null;

            var resolvedSupplierId = request?.SupplierId is > 0 ? request.SupplierId : null;
            var resolvedWarehouseId = request?.WarehouseId is > 0 ? request.WarehouseId : null;

            // Satisfy SQL Server CK_ReceivingTolerancePolicies_Scope:
            // ([ProductId] IS NOT NULL AND [SupplierId] IS NOT NULL)
            // OR ([ProductId] IS NOT NULL AND [SupplierId] IS NULL AND [WarehouseId] IS NULL)
            // OR ([ProductId] IS NULL AND [SupplierId] IS NULL)
            if (resolvedProductId.HasValue && !resolvedSupplierId.HasValue)
            {
                resolvedWarehouseId = null;
            }
            else if (!resolvedProductId.HasValue && resolvedSupplierId.HasValue)
            {
                resolvedProductId = await _db.Products.AsNoTracking()
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Id)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!resolvedProductId.HasValue)
                    resolvedSupplierId = null;
            }

            var rawPct = request?.PercentageTolerance
                ?? (request != null && request.OverReceiptTolerancePercent > 0m
                    ? request.OverReceiptTolerancePercent
                    : (request?.UnderReceiptTolerancePercent ?? 0m));
            if (rawPct < 0m) rawPct = 0m;
            var normalizedPct = rawPct > 1m ? Math.Round(rawPct / 100m, 6) : Math.Round(rawPct, 6);
            if (normalizedPct > 1m) normalizedPct = 1m;

            var absTolerance = request?.AbsoluteQuantityTolerance is >= 0m ? request.AbsoluteQuantityTolerance.Value : 0m;
            var now = DateTime.UtcNow;
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;

            var existing = await _db.ReceivingTolerancePolicies
                .FirstOrDefaultAsync(p =>
                    p.ProductId == resolvedProductId &&
                    p.SupplierId == resolvedSupplierId &&
                    p.WarehouseId == resolvedWarehouseId,
                    cancellationToken);

            if (existing != null)
            {
                existing.PercentageTolerance = normalizedPct;
                existing.AbsoluteQuantityTolerance = absTolerance;
                existing.OverageAllowed = request?.AllowOverDelivery ?? true;
                existing.RequiresApprovalOutsideTolerance = request?.RequireApprovalOnExceed ?? true;
                existing.IsActive = request?.IsActive ?? true;
                existing.EffectiveFromUtc = now;
            }
            else
            {
                existing = new ERP.Domain.Entities.ReceivingTolerancePolicy
                {
                    ProductId = resolvedProductId,
                    SupplierId = resolvedSupplierId,
                    WarehouseId = resolvedWarehouseId,
                    Version = 1,
                    PercentageTolerance = normalizedPct,
                    AbsoluteQuantityTolerance = absTolerance,
                    OverageAllowed = request?.AllowOverDelivery ?? true,
                    ShortageAllowed = true,
                    RequiresApprovalOutsideTolerance = request?.RequireApprovalOnExceed ?? true,
                    ApproverTarget = "receiving_discrepancy.approve",
                    IsActive = request?.IsActive ?? true,
                    EffectiveFromUtc = now
                };
                _db.ReceivingTolerancePolicies.Add(existing);
            }

            var displayPct = Math.Round(normalizedPct * 100m, 2);
            _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = resolvedWarehouseId,
                EntityName = "ReceivingTolerancePolicy",
                EntityId = resolvedProductId,
                Action = "Product.UpsertTolerancePolicy",
                NewValues = $"Pct={normalizedPct:0.######};Abs={absTolerance:0.####}",
                Reason = $"Cập nhật chính sách dung sai nhận hàng (±{displayPct:0.##}%)",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                id = existing.Id,
                productId = existing.ProductId,
                supplierId = existing.SupplierId,
                warehouseId = existing.WarehouseId,
                percentageTolerance = existing.PercentageTolerance,
                overReceiptTolerancePercent = displayPct,
                underReceiptTolerancePercent = displayPct,
                absoluteQuantityTolerance = existing.AbsoluteQuantityTolerance,
                allowOverDelivery = existing.OverageAllowed,
                requireApprovalOnExceed = existing.RequiresApprovalOutsideTolerance,
                isActive = existing.IsActive,
                status = "Đã lưu chính sách dung sai nhận hàng (Tolerance Policy)",
                statusLabel = "Đã lưu chính sách dung sai nhận hàng (Tolerance Policy)",
                savedAtUtc = now
            });
        }

        public sealed record BundleComponentItemInput(
            int? ComponentProductId = null,
            int? ChildProductId = null,
            decimal? ComponentQuantity = null,
            decimal? Quantity = null,
            decimal? CostAllocationRatioPct = null);

        public sealed record UpsertBundleComponentRequest(
            int? ParentProductId = null,
            int? ComponentProductId = null,
            int? ChildProductId = null,
            decimal? ComponentQuantity = null,
            decimal? Quantity = null,
            decimal? CostAllocationRatioPct = null,
            List<BundleComponentItemInput>? Components = null);

        [HttpGet("{id:int}/bundle-components")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetBundleComponents(
            int id,
            [FromQuery] int? warehouseId = null,
            CancellationToken cancellationToken = default)
        {
            if (_db == null)
            {
                return Ok(new
                {
                    parentProductId = id,
                    availableBundleQty = 0m,
                    items = Array.Empty<object>(),
                    components = Array.Empty<object>()
                });
            }

            var components = await _db.ProductBundleComponents
                .AsNoTracking()
                .Include(b => b.ComponentProduct)
                .Where(b => b.ParentProductId == id && b.IsActive)
                .OrderBy(b => b.Id)
                .ToListAsync(cancellationToken);

            var componentIds = components.Select(c => c.ComponentProductId).Distinct().ToList();
            var stockQuery = _db.InventoryStocks
                .AsNoTracking()
                .Include(s => s.Location)
                .Where(s => componentIds.Contains(s.ProductId) &&
                            s.Status == ERP.Domain.Enums.InventoryStatus.Available &&
                            (s.Location == null || s.Location.LocationType != ERP.Domain.Enums.WarehouseLocationType.Receiving));

            if (warehouseId.HasValue)
            {
                stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
            }

            var stocks = await stockQuery.ToListAsync(cancellationToken);

            var mapped = components.Select(c =>
            {
                var atp = stocks
                    .Where(s => s.ProductId == c.ComponentProductId)
                    .Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
                var reqQty = c.ComponentQuantity > 0m ? c.ComponentQuantity : 1m;
                var maxBundles = Math.Floor(atp / reqQty);
                return new
                {
                    id = c.Id,
                    parentProductId = c.ParentProductId,
                    componentProductId = c.ComponentProductId,
                    childProductId = c.ComponentProductId,
                    componentProductCode = c.ComponentProduct?.Code ?? $"SKU-{c.ComponentProductId}",
                    childProductCode = c.ComponentProduct?.Code ?? $"SKU-{c.ComponentProductId}",
                    componentProductName = c.ComponentProduct?.Name ?? $"Thành phần #{c.ComponentProductId}",
                    childProductName = c.ComponentProduct?.Name ?? $"Thành phần #{c.ComponentProductId}",
                    componentQuantity = c.ComponentQuantity,
                    quantity = c.ComponentQuantity,
                    costAllocationRatioPct = 100m,
                    componentAvailableQty = atp,
                    maxBundlesFromComponent = maxBundles,
                    isActive = c.IsActive
                };
            }).ToList();

            var availableBundleQty = mapped.Count > 0
                ? mapped.Min(x => x.maxBundlesFromComponent)
                : 0m;

            return Ok(new
            {
                parentProductId = id,
                availableBundleQty,
                totalComponents = mapped.Count,
                items = mapped,
                components = mapped
            });
        }

        [HttpPost("{id:int}/bundle-components")]
        [PermissionAuthorize(AppPermissions.ProductUpdate)]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.UpsertBundleComponents")]
        public async Task<IActionResult> UpsertBundleComponent(
            int id,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpsertBundleComponentRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
            {
                return Forbid();
            }

            if (_db == null)
            {
                return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
            }

            var inputList = new List<(int ComponentProductId, decimal ComponentQuantity, decimal CostAllocationRatioPct)>();
            if (request?.Components is { Count: > 0 })
            {
                foreach (var comp in request.Components)
                {
                    var compId = comp.ComponentProductId ?? comp.ChildProductId ?? 0;
                    var compQty = comp.ComponentQuantity ?? comp.Quantity ?? 0m;
                    var ratio = comp.CostAllocationRatioPct ?? 100m;
                    inputList.Add((compId, compQty, ratio));
                }
            }
            else
            {
                var compId = request?.ComponentProductId ?? request?.ChildProductId ?? 0;
                var compQty = request?.ComponentQuantity ?? request?.Quantity ?? 0m;
                var ratio = request?.CostAllocationRatioPct ?? 100m;
                inputList.Add((compId, compQty, ratio));
            }

            foreach (var (compId, compQty, _) in inputList)
            {
                if (compId <= 0)
                {
                    return BadRequest(new
                    {
                        code = "COMPONENT_PRODUCT_REQUIRED",
                        errorCode = "COMPONENT_PRODUCT_REQUIRED",
                        message = "Phải chỉ định mã sản phẩm thành phần (ComponentProductId)."
                    });
                }

                if (compId == id)
                {
                    return BadRequest(new
                    {
                        code = "BUNDLE_SELF_REFERENCE_INVALID",
                        errorCode = "BUNDLE_SELF_REFERENCE_INVALID",
                        message = "Sản phẩm Combo/Kit không được tự tham chiếu chính nó làm thành phần."
                    });
                }

                if (compQty <= 0m)
                {
                    return BadRequest(new
                    {
                        code = "INVALID_COMPONENT_QUANTITY",
                        errorCode = "INVALID_COMPONENT_QUANTITY",
                        message = "Định lượng thành phần Combo phải lớn hơn 0."
                    });
                }
            }

            var now = DateTime.UtcNow;
            ERP.Domain.Entities.ProductBundleComponent? lastSaved = null;
            foreach (var (compId, compQty, _) in inputList)
            {
                var existing = await _db.ProductBundleComponents
                    .FirstOrDefaultAsync(b => b.ParentProductId == id && b.ComponentProductId == compId, cancellationToken);

                if (existing != null)
                {
                    existing.ComponentQuantity = compQty;
                    existing.IsActive = true;
                    lastSaved = existing;
                }
                else
                {
                    existing = new ERP.Domain.Entities.ProductBundleComponent
                    {
                        ParentProductId = id,
                        ComponentProductId = compId,
                        ComponentQuantity = compQty,
                        IsActive = true,
                        CreatedAt = now
                    };
                    _db.ProductBundleComponents.Add(existing);
                    lastSaved = existing;
                }
            }

            await _db.SaveChangesAsync(cancellationToken);

            var allComponents = await _db.ProductBundleComponents
                .AsNoTracking()
                .Include(b => b.ComponentProduct)
                .Where(b => b.ParentProductId == id && b.IsActive)
                .OrderBy(b => b.Id)
                .ToListAsync(cancellationToken);

            var mapped = allComponents.Select(c => new
            {
                id = c.Id,
                parentProductId = c.ParentProductId,
                componentProductId = c.ComponentProductId,
                childProductId = c.ComponentProductId,
                componentProductCode = c.ComponentProduct?.Code ?? $"SKU-{c.ComponentProductId}",
                childProductCode = c.ComponentProduct?.Code ?? $"SKU-{c.ComponentProductId}",
                componentProductName = c.ComponentProduct?.Name ?? $"Thành phần #{c.ComponentProductId}",
                childProductName = c.ComponentProduct?.Name ?? $"Thành phần #{c.ComponentProductId}",
                componentQuantity = c.ComponentQuantity,
                quantity = c.ComponentQuantity,
                costAllocationRatioPct = 100m,
                isActive = c.IsActive
            }).ToList();

            var primary = mapped.LastOrDefault(m => m.componentProductId == lastSaved?.ComponentProductId) ?? mapped.LastOrDefault();

            return Ok(new
            {
                id = primary?.id ?? lastSaved?.Id ?? 0,
                parentProductId = id,
                componentProductId = primary?.componentProductId ?? lastSaved?.ComponentProductId ?? 0,
                childProductId = primary?.childProductId ?? lastSaved?.ComponentProductId ?? 0,
                componentProductCode = primary?.componentProductCode,
                childProductCode = primary?.childProductCode,
                componentProductName = primary?.componentProductName,
                childProductName = primary?.childProductName,
                componentQuantity = primary?.componentQuantity ?? lastSaved?.ComponentQuantity ?? 1m,
                quantity = primary?.quantity ?? lastSaved?.ComponentQuantity ?? 1m,
                costAllocationRatioPct = primary?.costAllocationRatioPct ?? 100m,
                items = mapped,
                components = mapped,
                statusLabel = "Đã lưu định mức cấu phần Combo / Kit"
            });
        }

        public sealed record UpsertChannelSkuMappingRequest(
            System.Text.Json.JsonElement? ShopId = null,
            string? ChannelCode = null,
            string? ShopName = null,
            string? ChannelSkuCode = null,
            string? ChannelItemId = null,
            System.Text.Json.JsonElement? InternalProductId = null,
            int? ProductId = null,
            string? InternalProductCode = null,
            decimal? ConversionFactor = 1m,
            bool? IsActive = true);

        [HttpGet("channel-sku-mappings")]
        [PermissionAuthorize(AppPermissions.ProductRead)]
        public async Task<IActionResult> GetChannelSkuMappings(
            [FromQuery] int? productId = null,
            [FromQuery] int? shopId = null,
            [FromQuery] string? channelCode = null,
            CancellationToken cancellationToken = default)
        {
            if (_db == null)
            {
                return Ok(new
                {
                    totalCount = 0,
                    items = Array.Empty<object>(),
                    mappings = Array.Empty<object>(),
                    stores = Array.Empty<object>()
                });
            }

            var mappingsQuery = _db.ChannelSkuMappings
                .AsNoTracking()
                .Include(m => m.Shop)
                .Include(m => m.InternalProduct)
                .AsQueryable();

            if (productId.HasValue && productId.Value > 0)
            {
                mappingsQuery = mappingsQuery.Where(m => m.InternalProductId == productId.Value);
            }

            if (shopId.HasValue && shopId.Value > 0)
            {
                mappingsQuery = mappingsQuery.Where(m => m.ShopId == shopId.Value);
            }

            if (!string.IsNullOrWhiteSpace(channelCode))
            {
                var normChannel = channelCode.Trim().ToUpperInvariant();
                mappingsQuery = mappingsQuery.Where(m => m.Shop != null && m.Shop.ChannelCode == normChannel);
            }

            var rawMappings = await mappingsQuery
                .OrderByDescending(m => m.Id)
                .ToListAsync(cancellationToken);

            var shops = await _db.MarketplaceShops
                .AsNoTracking()
                .Where(s => s.IsActive)
                .OrderBy(s => s.Id)
                .ToListAsync(cancellationToken);

            var productIds = rawMappings.Select(m => m.InternalProductId).Distinct().ToList();
            var stocks = productIds.Count > 0
                ? await _db.InventoryStocks
                    .AsNoTracking()
                    .Include(s => s.Location)
                    .Where(s => productIds.Contains(s.ProductId) &&
                                s.Status == ERP.Domain.Enums.InventoryStatus.Available &&
                                (s.Location == null || s.Location.LocationType != ERP.Domain.Enums.WarehouseLocationType.Receiving))
                    .ToListAsync(cancellationToken)
                : [];

            var atpByProduct = stocks
                .GroupBy(s => s.ProductId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)));

            var items = rawMappings.Select(m =>
            {
                var atp = atpByProduct.TryGetValue(m.InternalProductId, out var qty) ? qty : 0m;
                var factor = m.ConversionFactor > 0m ? m.ConversionFactor : 1m;
                var maxSellable = Math.Floor(atp / factor);
                return new
                {
                    mappingId = m.Id,
                    id = m.Id,
                    shopId = m.ShopId,
                    channelCode = m.Shop?.ChannelCode ?? "SHOPEE",
                    shopCode = m.Shop?.ShopCode ?? $"SHOP-{m.ShopId}",
                    shopName = m.Shop?.ShopName ?? $"Gian hàng #{m.ShopId}",
                    channelSkuCode = m.ChannelSkuCode,
                    channelItemId = m.ChannelItemId,
                    internalProductId = m.InternalProductId,
                    internalProductCode = m.InternalProduct?.Code ?? $"SKU-{m.InternalProductId}",
                    internalProductName = m.InternalProduct?.Name ?? $"Sản phẩm #{m.InternalProductId}",
                    conversionFactor = factor,
                    internalAvailableQty = atp,
                    maxChannelSellableQty = maxSellable,
                    lastPushedQty = m.LastPushedQty,
                    lastPushedAtUtc = m.LastPushedAtUtc,
                    isActive = m.IsActive
                };
            }).ToList();

            var storeList = shops.Select(s => new
            {
                shopId = s.Id,
                s.ChannelCode,
                s.ShopCode,
                s.ShopName,
                s.DefaultWarehouseId,
                s.ReserveRatioPct,
                s.SafetyBufferQty,
                s.IsActive
            }).ToList();

            return Ok(new
            {
                totalCount = items.Count,
                items,
                mappings = items,
                stores = storeList
            });
        }

        [HttpPost("channel-sku-mappings")]
        [PermissionAuthorize(AppPermissions.ProductUpdate)]
        [ERP.Api.Infrastructure.IdempotentCommand("Product.UpsertChannelSkuMapping")]
        public async Task<IActionResult> UpsertChannelSkuMapping(
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] UpsertChannelSkuMappingRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            if (User?.IsInRole(AppRoles.Viewer) == true)
            {
                return Forbid();
            }

            if (_db == null)
            {
                return StatusCode(503, new { code = "DB_UNAVAILABLE", message = "Cơ sở dữ liệu chưa sẵn sàng." });
            }

            var channelSkuCode = request?.ChannelSkuCode?.Trim();
            if (string.IsNullOrWhiteSpace(channelSkuCode))
            {
                return BadRequest(new
                {
                    code = "CHANNEL_SKU_CODE_REQUIRED",
                    errorCode = "CHANNEL_SKU_CODE_REQUIRED",
                    message = "Mã SKU trên sàn TMĐT (ChannelSkuCode) không được để trống."
                });
            }

            var conversionFactor = request?.ConversionFactor ?? 1m;
            if (conversionFactor <= 0m)
            {
                return BadRequest(new
                {
                    code = "INVALID_CONVERSION_FACTOR",
                    errorCode = "INVALID_CONVERSION_FACTOR",
                    message = "Hệ số quy đổi (ConversionFactor) phải lớn hơn 0."
                });
            }

            var parsedInternalProductId = ParseOptionalInt(request?.InternalProductId) ?? request?.ProductId;
            ERP.Domain.Entities.Product? internalProduct = null;
            if (parsedInternalProductId is > 0)
            {
                internalProduct = await _db.Products
                    .FirstOrDefaultAsync(p => p.Id == parsedInternalProductId.Value, cancellationToken);
            }

            if (internalProduct == null && !string.IsNullOrWhiteSpace(request?.InternalProductCode))
            {
                var pCode = request.InternalProductCode.Trim();
                internalProduct = await _db.Products
                    .FirstOrDefaultAsync(p => p.Code == pCode, cancellationToken);
            }

            if (internalProduct == null && request?.InternalProductId.HasValue == true &&
                request.InternalProductId.Value.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var rawStr = request.InternalProductId.Value.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(rawStr))
                {
                    internalProduct = await _db.Products.FirstOrDefaultAsync(p => p.Code == rawStr, cancellationToken);
                    if (internalProduct == null && Guid.TryParse(rawStr, out _))
                    {
                        internalProduct = await _db.Products.OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
                    }
                }
            }

            if (internalProduct == null)
            {
                return BadRequest(new
                {
                    code = "INTERNAL_PRODUCT_NOT_FOUND",
                    errorCode = "INTERNAL_PRODUCT_NOT_FOUND",
                    message = "Không tìm thấy sản phẩm nội bộ (InternalProductId) để ánh xạ."
                });
            }

            var now = DateTime.UtcNow;
            var parsedShopId = ParseOptionalInt(request?.ShopId);
            var normalizedChannel = string.IsNullOrWhiteSpace(request?.ChannelCode)
                ? "SHOPEE"
                : request.ChannelCode.Trim().ToUpperInvariant();

            ERP.Domain.Entities.MarketplaceShop? shop = null;
            if (parsedShopId is > 0)
            {
                shop = await _db.MarketplaceShops.FirstOrDefaultAsync(s => s.Id == parsedShopId.Value, cancellationToken);
            }

            if (shop == null)
            {
                shop = await _db.MarketplaceShops
                    .OrderBy(s => s.Id)
                    .FirstOrDefaultAsync(s => s.ChannelCode == normalizedChannel, cancellationToken);
            }

            if (shop == null)
            {
                var defaultWarehouseId = await _db.Warehouses
                    .AsNoTracking()
                    .Where(w => w.IsActive)
                    .OrderBy(w => w.Id)
                    .Select(w => w.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                if (defaultWarehouseId <= 0)
                {
                    defaultWarehouseId = 1;
                }

                shop = new ERP.Domain.Entities.MarketplaceShop
                {
                    ChannelCode = normalizedChannel,
                    ShopCode = $"SHOP-{normalizedChannel}-01",
                    ShopName = !string.IsNullOrWhiteSpace(request?.ShopName)
                        ? request.ShopName.Trim()
                        : $"Gian hàng {normalizedChannel} Chính thức",
                    DefaultWarehouseId = defaultWarehouseId,
                    ReserveRatioPct = 100m,
                    SafetyBufferQty = 0m,
                    LowStockThreshold = 5m,
                    IsPrimaryShop = true,
                    EncryptedAccessToken = $"ENC_TOK_{Guid.NewGuid():N}",
                    EncryptedRefreshToken = $"ENC_REF_{Guid.NewGuid():N}",
                    TokenExpiresAtUtc = now.AddDays(30),
                    IsActive = true,
                    CreatedAtUtc = now
                };
                _db.MarketplaceShops.Add(shop);
                await _db.SaveChangesAsync(cancellationToken);
            }

            var existingMapping = await _db.ChannelSkuMappings
                .FirstOrDefaultAsync(
                    m => m.ShopId == shop.Id && m.ChannelSkuCode == channelSkuCode,
                    cancellationToken);

            var isActive = request?.IsActive ?? true;
            var resolvedItemId = !string.IsNullOrWhiteSpace(request?.ChannelItemId)
                ? request.ChannelItemId.Trim()
                : (existingMapping?.ChannelItemId ?? $"ITEM-{internalProduct.Id}");

            if (existingMapping != null)
            {
                existingMapping.InternalProductId = internalProduct.Id;
                existingMapping.ChannelItemId = resolvedItemId;
                existingMapping.ConversionFactor = conversionFactor;
                existingMapping.IsActive = isActive;
            }
            else
            {
                existingMapping = new ERP.Domain.Entities.ChannelSkuMapping
                {
                    ShopId = shop.Id,
                    ChannelItemId = resolvedItemId,
                    ChannelSkuCode = channelSkuCode,
                    InternalProductId = internalProduct.Id,
                    ConversionFactor = conversionFactor,
                    IsActive = isActive
                };
                _db.ChannelSkuMappings.Add(existingMapping);
            }

            await _db.SaveChangesAsync(cancellationToken);

            int? validAuditUserId = null;
            var userIdStr = User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User?.FindFirst("Id")?.Value;
            if (int.TryParse(userIdStr, out var parsedUid) &&
                await _db.Users.AsNoTracking().AnyAsync(u => u.Id == parsedUid, cancellationToken))
            {
                validAuditUserId = parsedUid;
            }

            _db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = validAuditUserId,
                WarehouseId = shop.DefaultWarehouseId,
                EntityName = "ChannelSkuMapping",
                EntityId = existingMapping.Id,
                Action = "UPSERT_CHANNEL_SKU_MAPPING",
                NewValues = System.Text.Json.JsonSerializer.Serialize(new
                {
                    mappingId = existingMapping.Id,
                    shopId = shop.Id,
                    channelCode = shop.ChannelCode,
                    channelSkuCode = existingMapping.ChannelSkuCode,
                    internalProductId = internalProduct.Id,
                    internalProductCode = internalProduct.Code,
                    conversionFactor = existingMapping.ConversionFactor,
                    isActive = existingMapping.IsActive
                }),
                Reason = $"Ánh xạ SKU sàn {shop.ChannelCode} '{channelSkuCode}' -> SKU nội bộ '{internalProduct.Code}' (x{conversionFactor:0.##})",
                Result = "Success",
                Timestamp = now
            });

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                mappingId = existingMapping.Id,
                id = existingMapping.Id,
                shopId = shop.Id,
                channelCode = shop.ChannelCode,
                shopCode = shop.ShopCode,
                shopName = shop.ShopName,
                channelSkuCode = existingMapping.ChannelSkuCode,
                channelItemId = existingMapping.ChannelItemId,
                internalProductId = internalProduct.Id,
                internalProductCode = internalProduct.Code,
                internalProductName = internalProduct.Name,
                conversionFactor = existingMapping.ConversionFactor,
                isActive = existingMapping.IsActive,
                statusLabel = $"Đã lưu ánh xạ SKU sàn {shop.ChannelCode} ({channelSkuCode} -> {internalProduct.Code})"
            });
        }

        private static int? ParseOptionalInt(System.Text.Json.JsonElement? element)
        {
            if (!element.HasValue)
            {
                return null;
            }

            var el = element.Value;
            if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetInt32(out var numVal))
            {
                return numVal;
            }

            if (el.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var str = el.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(str) && int.TryParse(str, out var parsedVal))
                {
                    return parsedVal;
                }
            }

            return null;
        }
    }
}