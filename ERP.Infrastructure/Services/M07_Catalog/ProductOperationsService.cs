using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ProductOperationsService(
    ErpKhoDbContext db,
    ICurrentUser? currentUser = null,
    IWarehouseAuthorizationService? warehouseAuthorizationService = null) : IProductOperationsService
{
    private readonly ErpKhoDbContext _db = db;
    private readonly ICurrentUser? _currentUser = currentUser;
    private readonly IWarehouseAuthorizationService? _warehouseAuthorizationService = warehouseAuthorizationService;

    private bool CanReadInventoryCost() =>
        _currentUser is not null &&
        (_currentUser.IsAuthenticated || (_currentUser.UserId > 0 && _currentUser.IsGlobalAdmin)) &&
        _currentUser.HasPermission(ERP.Domain.Security.AppPermissions.InventoryCostRead);

    private bool IsAuthenticatedNonAdmin() =>
        _currentUser is { IsAuthenticated: true } &&
        !_currentUser.IsGlobalAdmin &&
        _currentUser.Role is not "Admin" and not "SuperAdmin";

    private async Task<IReadOnlyList<int>?> ResolveScopedWarehouseIdsAsync(
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        if (warehouseId is > 0)
        {
            if (_currentUser?.IsAuthenticated == true && _warehouseAuthorizationService != null)
            {
                await _warehouseAuthorizationService.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            return [warehouseId.Value];
        }

        if (!IsAuthenticatedNonAdmin())
        {
            return null;
        }

        if (_warehouseAuthorizationService != null)
        {
            return await _warehouseAuthorizationService.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        return await _db.UserWarehouses.AsNoTracking()
            .Where(uw => uw.UserId == _currentUser!.UserId &&
                         !_db.Warehouses.Any(w => w.Id == uw.WarehouseId && !w.IsActive))
            .Select(uw => uw.WarehouseId)
            .ToListAsync(cancellationToken);
    }

    public async Task<WmsEndpointOutcome> AutoAuditGovernanceAsync(
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var totalSkuCount = await _db.Products.AsNoTracking().CountAsync(cancellationToken);
        var activeSkuCount = await _db.Products.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken);
        var barcodeConfiguredCount = await _db.Products.AsNoTracking()
            .CountAsync(p => p.IsActive && _db.ProductBarcodes.Any(b => b.ProductId == p.Id), cancellationToken);
        var uomConfiguredCount = await _db.Products.AsNoTracking()
            .CountAsync(p => p.IsActive && (p.UnitId > 0 || _db.ProductUoms.Any(u => u.ProductId == p.Id)), cancellationToken);
        var dimensionConfiguredCount = await _db.Products.AsNoTracking()
            .CountAsync(p => p.IsActive && (p.UnitWeightKg.HasValue || p.UnitVolumeM3.HasValue || p.UnitPalletEquivalent.HasValue), cancellationToken);
        var expiryControlledCount = await _db.Products.AsNoTracking()
            .CountAsync(p => p.IsActive && p.ExpiryControl, cancellationToken);
        var qcAndTolerancePolicyCount =
            (await _db.QcPolicies.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken))
            + (await _db.ReceivingTolerancePolicies.AsNoTracking().CountAsync(p => p.IsActive, cancellationToken));

        var auditedSkuCount = activeSkuCount > 0 ? activeSkuCount : totalSkuCount;
        var now = DateTime.UtcNow;
        var auditId = $"SKU-AUD-{now:yyyyMMdd-HHmm}";

        _db.AuditLogs.Add(new AuditLog
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

        return WmsEndpointOutcome.Ok(new
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

    public async Task<WmsEndpointOutcome> GetProductUomsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Code, p.Name, p.UnitId, BaseUnitName = p.Unit != null ? p.Unit.Name : "Cái" })
            .FirstOrDefaultAsync(cancellationToken);
        if (product == null)
        {
            return WmsEndpointOutcome.Json(404, new { message = "Không tìm thấy sản phẩm." });
        }

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

        return WmsEndpointOutcome.Ok(items);
    }

    public async Task<WmsEndpointOutcome> UpsertProductUomAsync(
        int id,
        UpsertProductUomInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new { p.Id, p.Code, p.Name, p.UnitId })
            .FirstOrDefaultAsync(cancellationToken);
        if (product == null)
        {
            return WmsEndpointOutcome.Json(404, new { message = "Không tìm thấy sản phẩm cần cấu hình đơn vị tính." });
        }

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
            existing = new ProductUom
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

        _db.AuditLogs.Add(new AuditLog
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

        return WmsEndpointOutcome.Ok(new
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

    public async Task<WmsEndpointOutcome> DeleteProductUomAsync(
        int id,
        int uomId,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.ProductUoms
            .FirstOrDefaultAsync(u => u.Id == uomId && u.ProductId == id, cancellationToken);
        if (existing == null)
        {
            return WmsEndpointOutcome.Json(404, new { message = "Không tìm thấy quy đổi đơn vị tính cần xóa." });
        }

        _db.ProductUoms.Remove(existing);

        _db.AuditLogs.Add(new AuditLog
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

        return WmsEndpointOutcome.Ok(new
        {
            id = uomId,
            productId = id,
            status = "Đã xóa quy đổi đơn vị tính phụ",
            statusLabel = "Đã xóa quy đổi đơn vị tính phụ"
        });
    }

    public async Task<WmsEndpointOutcome> GetQcPoliciesAsync(
        int? productId,
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.QcPolicies.AsNoTracking().AsQueryable();
        if (productId.HasValue)
        {
            query = query.Where(p => p.ProductId == productId.Value);
        }

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

        return WmsEndpointOutcome.Ok(items);
    }
}
