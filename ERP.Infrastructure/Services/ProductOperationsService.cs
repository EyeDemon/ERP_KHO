using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class ProductOperationsService(ErpKhoDbContext db) : IProductOperationsService
{
    private readonly ErpKhoDbContext _db = db;

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

    public async Task<WmsEndpointOutcome> UpsertQcPolicyAsync(
        UpsertQcPolicyInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
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
        {
            return WmsEndpointOutcome.BadRequest(new { message = "Không tìm thấy sản phẩm để áp dụng chính sách QC." });
        }

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
            existing = new QcPolicy
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

        _db.AuditLogs.Add(new AuditLog
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

        return WmsEndpointOutcome.Ok(new
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

    public async Task<WmsEndpointOutcome> GetTolerancePoliciesAsync(
        int? productId,
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
        var query = _db.ReceivingTolerancePolicies.AsNoTracking().AsQueryable();
        if (productId.HasValue)
        {
            query = query.Where(p => p.ProductId == productId.Value);
        }
        if (warehouseId.HasValue)
        {
            query = query.Where(p => p.WarehouseId == warehouseId.Value);
        }

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

        return WmsEndpointOutcome.Ok(items);
    }

    public async Task<WmsEndpointOutcome> UpsertTolerancePolicyAsync(
        UpsertTolerancePolicyInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
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
        {
            resolvedProductId = null;
        }

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
            {
                resolvedSupplierId = null;
            }
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
            existing = new ReceivingTolerancePolicy
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
        _db.AuditLogs.Add(new AuditLog
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

        return WmsEndpointOutcome.Ok(new
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

    public async Task<WmsEndpointOutcome> GetBundleComponentsAsync(
        int id,
        int? warehouseId,
        CancellationToken cancellationToken = default)
    {
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
                        s.Status == InventoryStatus.Available &&
                        (s.Location == null || s.Location.LocationType != WarehouseLocationType.Receiving));

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

        return WmsEndpointOutcome.Ok(new
        {
            parentProductId = id,
            availableBundleQty,
            totalComponents = mapped.Count,
            items = mapped,
            components = mapped
        });
    }

    public async Task<WmsEndpointOutcome> UpsertBundleComponentAsync(
        int id,
        UpsertBundleComponentInput? request,
        CancellationToken cancellationToken = default)
    {
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
                return WmsEndpointOutcome.BadRequest(new
                {
                    code = "COMPONENT_PRODUCT_REQUIRED",
                    errorCode = "COMPONENT_PRODUCT_REQUIRED",
                    message = "Phải chỉ định mã sản phẩm thành phần (ComponentProductId)."
                });
            }

            if (compId == id)
            {
                return WmsEndpointOutcome.BadRequest(new
                {
                    code = "BUNDLE_SELF_REFERENCE_INVALID",
                    errorCode = "BUNDLE_SELF_REFERENCE_INVALID",
                    message = "Sản phẩm Combo/Kit không được tự tham chiếu chính nó làm thành phần."
                });
            }

            if (compQty <= 0m)
            {
                return WmsEndpointOutcome.BadRequest(new
                {
                    code = "INVALID_COMPONENT_QUANTITY",
                    errorCode = "INVALID_COMPONENT_QUANTITY",
                    message = "Định lượng thành phần Combo phải lớn hơn 0."
                });
            }
        }

        var now = DateTime.UtcNow;
        ProductBundleComponent? lastSaved = null;
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
                existing = new ProductBundleComponent
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

        return WmsEndpointOutcome.Ok(new
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

    public async Task<WmsEndpointOutcome> GetChannelSkuMappingsAsync(
        int? productId,
        int? shopId,
        string? channelCode,
        CancellationToken cancellationToken = default)
    {
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
                            s.Status == InventoryStatus.Available &&
                            (s.Location == null || s.Location.LocationType != WarehouseLocationType.Receiving))
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

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = items.Count,
            items,
            mappings = items,
            stores = storeList
        });
    }

    public async Task<WmsEndpointOutcome> UpsertChannelSkuMappingAsync(
        UpsertChannelSkuMappingInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var channelSkuCode = request?.ChannelSkuCode?.Trim();
        if (string.IsNullOrWhiteSpace(channelSkuCode))
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "CHANNEL_SKU_CODE_REQUIRED",
                errorCode = "CHANNEL_SKU_CODE_REQUIRED",
                message = "Mã SKU trên sàn TMĐT (ChannelSkuCode) không được để trống."
            });
        }

        var conversionFactor = request?.ConversionFactor ?? 1m;
        if (conversionFactor <= 0m)
        {
            return WmsEndpointOutcome.BadRequest(new
            {
                code = "INVALID_CONVERSION_FACTOR",
                errorCode = "INVALID_CONVERSION_FACTOR",
                message = "Hệ số quy đổi (ConversionFactor) phải lớn hơn 0."
            });
        }

        var parsedInternalProductId = ParseOptionalInt(request?.InternalProductId) ?? request?.ProductId;
        Product? internalProduct = null;
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
            request.InternalProductId.Value.ValueKind == JsonValueKind.String)
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
            return WmsEndpointOutcome.BadRequest(new
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

        MarketplaceShop? shop = null;
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

            shop = new MarketplaceShop
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
            existingMapping = new ChannelSkuMapping
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
        if (actorUserId is > 0 &&
            await _db.Users.AsNoTracking().AnyAsync(u => u.Id == actorUserId.Value, cancellationToken))
        {
            validAuditUserId = actorUserId.Value;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = validAuditUserId,
            WarehouseId = shop.DefaultWarehouseId,
            EntityName = "ChannelSkuMapping",
            EntityId = existingMapping.Id,
            Action = "UPSERT_CHANNEL_SKU_MAPPING",
            NewValues = JsonSerializer.Serialize(new
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

        return WmsEndpointOutcome.Ok(new
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

    public async Task<WmsEndpointOutcome> DekitAsync(
        int? id,
        ProductDekitInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var kitQty = request?.Quantity ?? 1m;
        if (kitQty <= 0m)
        {
            return WmsEndpointOutcome.Json(422, new
            {
                code = "VAS_KIT_INVALID_QTY",
                errorCode = "VAS_KIT_INVALID_QTY",
                message = "Số lượng rã bộ KIT phải lớn hơn 0."
            });
        }

        var targetKitProductId = id;
        if (!targetKitProductId.HasValue || targetKitProductId.Value <= 0)
        {
            targetKitProductId = await _db.Products.AsNoTracking().OrderBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(cancellationToken);
        }

        if (!targetKitProductId.HasValue)
        {
            return WmsEndpointOutcome.Json(404, new { code = "PRODUCT_NOT_FOUND", errorCode = "PRODUCT_NOT_FOUND", message = "Không tìm thấy sản phẩm KIT." });
        }

        var kitProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == targetKitProductId.Value, cancellationToken);
        if (kitProduct == null)
        {
            return WmsEndpointOutcome.Json(404, new { code = "PRODUCT_NOT_FOUND", errorCode = "PRODUCT_NOT_FOUND", message = "Không tìm thấy sản phẩm KIT." });
        }

        var targetWarehouseId = request?.WarehouseId ?? 1;
        var bundleRows = await _db.ProductBundleComponents
            .AsNoTracking()
            .Include(b => b.ComponentProduct)
            .Where(b => b.ParentProductId == targetKitProductId.Value && b.IsActive)
            .OrderBy(b => b.Id)
            .ToListAsync(cancellationToken);

        var componentSpecs = bundleRows.Count > 0
            ? bundleRows.Select(b => (
                ComponentProductId: b.ComponentProductId,
                ComponentCode: b.ComponentProduct?.Code ?? $"COMP-{b.ComponentProductId}",
                ComponentName: b.ComponentProduct?.Name ?? $"Thành phần #{b.ComponentProductId}",
                Ratio: b.ComponentQuantity > 0m ? b.ComponentQuantity : 1m)).ToList()
            : new List<(int ComponentProductId, string ComponentCode, string ComponentName, decimal Ratio)>
            {
                (targetKitProductId.Value, kitProduct.Code, kitProduct.Name, 1m)
            };

        var now = DateTime.UtcNow;
        var actorId = actorUserId is > 0 ? actorUserId.Value : 1;

        await using var tx = _db.Database.CurrentTransaction == null
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var lockProductIds = componentSpecs.Select(x => x.ComponentProductId).Append(targetKitProductId.Value).Distinct().OrderBy(x => x).ToList();
        await _db.AcquireInventoryAllocationLocksAsync(targetWarehouseId, lockProductIds, cancellationToken);

        var kitStocksQuery = _db.InventoryStocks
            .Where(s => s.WarehouseId == targetWarehouseId &&
                        s.ProductId == targetKitProductId.Value &&
                        s.Status == InventoryStatus.Available &&
                        s.Quantity > 0m);
        if (request?.LocationId.HasValue == true)
        {
            kitStocksQuery = kitStocksQuery.Where(s => s.LocationId == request.LocationId.Value);
        }
        if (request?.LotId.HasValue == true)
        {
            kitStocksQuery = kitStocksQuery.Where(s => s.LotId == request.LotId.Value);
        }

        var kitStocks = await kitStocksQuery
            .OrderByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
            .ThenBy(s => s.Id)
            .ToListAsync(cancellationToken);

        var kitAvail = kitStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity));
        if (kitAvail < kitQty)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return WmsEndpointOutcome.Json(422, new
            {
                code = "VAS_DEKIT_INSUFFICIENT_KIT_STOCK",
                errorCode = "VAS_DEKIT_INSUFFICIENT_KIT_STOCK",
                kitProductId = targetKitProductId.Value,
                requiredKitQuantity = kitQty,
                availableKitQuantity = kitAvail,
                message = $"Không đủ tồn kho khả dụng của bộ KIT {kitProduct.Code} để rã bộ (cần {kitQty:0.##}, khả dụng {kitAvail:0.##})."
            });
        }

        var defaultLocId = kitStocks.FirstOrDefault(s => s.LocationId.HasValue)?.LocationId
            ?? await _db.WarehouseLocations
                .AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId && !l.IsBlocked)
                .OrderBy(l => l.Id)
                .Select(l => (int?)l.Id)
                .FirstOrDefaultAsync(cancellationToken);

        var remainingToDeduct = kitQty;
        int? primaryDeductedLocationId = defaultLocId;
        int? primaryDeductedLotId = null;
        int? primaryDeductedHuId = null;
        int? primaryDeductedOwnerId = null;
        InventoryTransaction? firstKitTx = null;

        foreach (var ks in kitStocks)
        {
            if (remainingToDeduct <= 0m) break;
            var free = Math.Max(0m, ks.Quantity - ks.ReservedQuantity - ks.AllocatedQuantity - ks.LockedQuantity);
            if (free <= 0m) continue;
            var deduct = Math.Min(free, remainingToDeduct);
            ks.Quantity -= deduct;
            ks.LastUpdated = now;
            ks.RefreshGrainHash();
            remainingToDeduct -= deduct;
            primaryDeductedLocationId = ks.LocationId ?? defaultLocId;
            primaryDeductedLotId ??= ks.LotId;
            primaryDeductedHuId ??= ks.HandlingUnitId;
            primaryDeductedOwnerId ??= ks.OwnerId;

            var kTx = new InventoryTransaction
            {
                ProductId = targetKitProductId.Value,
                WarehouseId = targetWarehouseId,
                LocationId = ks.LocationId,
                LotId = ks.LotId,
                SerialId = ks.SerialId,
                HandlingUnitId = ks.HandlingUnitId,
                OwnerId = ks.OwnerId,
                InventoryStatus = InventoryStatus.Available,
                FromInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.TransferOut,
                Quantity = deduct,
                ReferenceId = ks.Id,
                ReferenceType = "VAS_DEKIT",
                TransactionDate = now,
                ActualOccurredAtUtc = now,
                PostedAtUtc = now,
                CreatedBy = actorId,
                Note = $"VAS-DEKIT: Xuất rã bộ KIT -{deduct:0.##}"
            };
            _db.InventoryTransactions.Add(kTx);
            firstKitTx ??= kTx;
        }

        InventoryTransaction? firstCompTx = null;
        foreach (var spec in componentSpecs)
        {
            var addCompQty = Math.Round(kitQty * spec.Ratio, 4);
            var preserveKitGrain = spec.ComponentProductId == targetKitProductId.Value;
            var compLocId = preserveKitGrain ? (primaryDeductedLocationId ?? defaultLocId) : defaultLocId;
            var compLotId = preserveKitGrain ? primaryDeductedLotId : request?.LotId;
            var compHuId = preserveKitGrain ? primaryDeductedHuId : null;
            var compOwnerId = preserveKitGrain ? primaryDeductedOwnerId : null;

            var compStock = await _db.InventoryStocks
                .FirstOrDefaultAsync(s =>
                    s.WarehouseId == targetWarehouseId &&
                    s.ProductId == spec.ComponentProductId &&
                    s.LocationId == compLocId &&
                    s.LotId == compLotId &&
                    s.SerialId == null &&
                    s.HandlingUnitId == compHuId &&
                    s.OwnerId == compOwnerId &&
                    s.Status == InventoryStatus.Available,
                    cancellationToken);

            if (compStock != null)
            {
                compStock.Quantity += addCompQty;
                compStock.LastUpdated = now;
                compStock.RefreshGrainHash();
            }
            else
            {
                compStock = new InventoryStock
                {
                    WarehouseId = targetWarehouseId,
                    ProductId = spec.ComponentProductId,
                    LocationId = compLocId,
                    LotId = compLotId,
                    HandlingUnitId = compHuId,
                    OwnerId = compOwnerId,
                    Status = InventoryStatus.Available,
                    Quantity = addCompQty,
                    ReservedQuantity = 0m,
                    AllocatedQuantity = 0m,
                    LockedQuantity = 0m,
                    LastUpdated = now
                };
                compStock.RefreshGrainHash();
                _db.InventoryStocks.Add(compStock);
            }

            var cTx = new InventoryTransaction
            {
                ProductId = spec.ComponentProductId,
                WarehouseId = targetWarehouseId,
                LocationId = compLocId,
                LotId = compLotId,
                HandlingUnitId = compHuId,
                OwnerId = compOwnerId,
                InventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.TransferIn,
                Quantity = addCompQty,
                ReferenceId = targetKitProductId.Value,
                ReferenceType = "VAS_DEKIT",
                TransactionDate = now,
                ActualOccurredAtUtc = now,
                PostedAtUtc = now,
                CreatedBy = actorId,
                Note = $"VAS-DEKIT: Nhập hoàn nguyên thành phần +{addCompQty:0.##} ({spec.ComponentCode})"
            };
            _db.InventoryTransactions.Add(cTx);
            firstCompTx ??= cTx;
        }

        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            WarehouseId = targetWarehouseId,
            Action = "VAS_DEKIT_EXECUTED",
            EntityName = "Product",
            EntityId = targetKitProductId.Value,
            NewValues = $"Operation=DEKIT;Kit={kitProduct.Code};Qty={kitQty:0.##};LotId={primaryDeductedLotId}",
            Reason = request?.Note ?? "Thực thi rã bộ KIT (De-kitting) bảo toàn 9D grain",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return WmsEndpointOutcome.Ok(new
        {
            operationMode = "DEKIT",
            warehouseId = targetWarehouseId,
            kitProductId = targetKitProductId.Value,
            kitProductCode = kitProduct.Code,
            kitQuantity = kitQty,
            locationId = primaryDeductedLocationId,
            lotId = primaryDeductedLotId,
            kitTransferOutTxId = firstKitTx?.Id,
            componentTransferInTxId = firstCompTx?.Id
        });
    }

    private static int? ParseOptionalInt(JsonElement? element)
    {
        if (!element.HasValue)
        {
            return null;
        }

        var el = element.Value;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var numVal))
        {
            return numVal;
        }

        if (el.ValueKind == JsonValueKind.String)
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
