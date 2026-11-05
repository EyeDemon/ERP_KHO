using System.Text.Json;
using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class ProductOperationsService
{
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
        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(warehouseId, cancellationToken);
        if (warehouseId.HasValue)
        {
            query = query.Where(p => p.WarehouseId == warehouseId.Value);
        }
        else if (scopedWarehouseIds != null)
        {
            query = query.Where(p => !p.WarehouseId.HasValue || scopedWarehouseIds.Contains(p.WarehouseId.Value));
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
                toleranceSummary = $"Dung sai ±{displayPct:0.##}% (Tối đa ±{r.AbsoluteQuantityTolerance:0.##} đơn vị)",
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
}
