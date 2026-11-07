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
    public async Task<WmsEndpointOutcome> GetKitWorkOrdersAsync(
        int? warehouseId = null,
        int? kitProductId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.KitWorkOrders
            .AsNoTracking()
            .Include(w => w.ComponentLines)
            .AsQueryable();

        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(warehouseId, cancellationToken);
        if (scopedWarehouseIds != null)
            query = query.Where(w => scopedWarehouseIds.Contains(w.WarehouseId));
        if (kitProductId is > 0)
            query = query.Where(w => w.KitProductId == kitProductId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim();
            query = query.Where(w => w.Status == s);
        }

        var orders = await query
            .OrderByDescending(w => w.CreatedAtUtc)
            .ThenByDescending(w => w.Id)
            .Take(100)
            .ToListAsync(cancellationToken);

        var productIds = orders.Select(o => o.KitProductId)
            .Concat(orders.SelectMany(o => o.ComponentLines.Select(l => l.ComponentProductId)))
            .Distinct()
            .ToList();
        var productMap = productIds.Count > 0
            ? await _db.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => new { p.Code, p.Name }, cancellationToken)
            : new();

        var canReadCost = CanReadInventoryCost();
        var items = orders.Select(w =>
        {
            productMap.TryGetValue(w.KitProductId, out var kitProd);
            var effectiveQty = w.CompletedKitQty > 0m ? w.CompletedKitQty : w.PlannedKitQty;
            var snapshotUnitCost = w.RolledUpUnitCost;
            var snapshotTotalCost = Math.Round(effectiveQty * snapshotUnitCost, 4);
            return new
            {
                id = w.Id,
                workOrderId = w.Id,
                workOrderCode = w.WorkOrderCode,
                warehouseId = w.WarehouseId,
                kitProductId = w.KitProductId,
                kitProductCode = kitProd?.Code ?? $"KIT-{w.KitProductId}",
                kitProductName = kitProd?.Name ?? $"Bộ KIT #{w.KitProductId}",
                workOrderType = w.WorkOrderType.ToString(),
                status = w.Status,
                plannedKitQty = w.PlannedKitQty,
                completedKitQty = w.CompletedKitQty,
                baseUomCode = NormalizeKitUomCode(w.BaseUomCode),
                conversionRateSnapshot = w.ConversionRateSnapshot > 0m ? w.ConversionRateSnapshot : 1m,
                targetLocationId = w.TargetLocationId,
                targetLotId = w.TargetLotId,
                targetHandlingUnitId = w.TargetHandlingUnitId,
                rolledUpUnitCost = canReadCost ? (decimal?)snapshotUnitCost : null,
                unitCost = canReadCost ? (decimal?)snapshotUnitCost : null,
                totalCost = canReadCost ? (decimal?)snapshotTotalCost : null,
                costHiddenByRbac = !canReadCost,
                earliestComponentExpiryDate = w.EarliestComponentExpiryDate,
                createdAtUtc = w.CreatedAtUtc,
                completedAtUtc = w.CompletedAtUtc,
                componentLines = w.ComponentLines.Select(l =>
                {
                    productMap.TryGetValue(l.ComponentProductId, out var compProd);
                    return new
                    {
                        id = l.Id,
                        componentProductId = l.ComponentProductId,
                        componentProductCode = compProd?.Code ?? $"COMP-{l.ComponentProductId}",
                        componentProductName = compProd?.Name ?? $"Thành phần #{l.ComponentProductId}",
                        componentRatioQty = l.ComponentRatioQty,
                        requiredBaseQty = l.RequiredBaseQty,
                        consumedBaseQty = l.ConsumedBaseQty,
                        baseUomCode = NormalizeKitUomCode(l.BaseUomCode),
                        conversionRateSnapshot = l.ConversionRateSnapshot > 0m ? l.ConversionRateSnapshot : 1m,
                        sourceLocationId = l.SourceLocationId,
                        sourceLotId = l.SourceLotId,
                        componentUnitCost = canReadCost ? (decimal?)l.ComponentUnitCost : null
                    };
                }).ToList()
            };
        }).ToList();

        return WmsEndpointOutcome.Ok(new
        {
            totalCount = items.Count,
            costHiddenByRbac = !canReadCost,
            items,
            workOrders = items
        });
    }

    public async Task<WmsEndpointOutcome> CreateKitWorkOrderAsync(
        CreateKitWorkOrderInput? request,
        int? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var plannedQty = Math.Round(request?.PlannedKitQty ?? 1m, 4);
        if (plannedQty <= 0m)
        {
            return WmsEndpointOutcome.Json(422, new
            {
                code = "VAS_KIT_INVALID_QTY",
                errorCode = "VAS_KIT_INVALID_QTY",
                message = "Số lượng lệnh lắp/rã bộ KIT (PlannedKitQty) phải lớn hơn 0."
            });
        }

        var warehouseId = request?.WarehouseId is > 0
            ? request.WarehouseId.Value
            : await _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.Id).Select(w => w.Id).FirstOrDefaultAsync(cancellationToken);
        if (warehouseId <= 0) warehouseId = 1;

        Product? kitProduct = null;
        if (request?.KitProductId is > 0)
        {
            kitProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.KitProductId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request?.KitProductCode))
        {
            var code = request.KitProductCode.Trim();
            kitProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, cancellationToken);
        }
        kitProduct ??= await _db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);
        if (kitProduct == null)
        {
            return WmsEndpointOutcome.Json(404, new
            {
                code = "PRODUCT_NOT_FOUND",
                errorCode = "PRODUCT_NOT_FOUND",
                message = "Không tìm thấy sản phẩm cha (KitProductId) để tạo lệnh gia công KIT."
            });
        }

        var typeRaw = (request?.WorkOrderType ?? "Assemble").Trim();
        var workOrderType = string.Equals(typeRaw, "Disassemble", StringComparison.OrdinalIgnoreCase) || typeRaw == "2"
            ? KitWorkOrderType.Disassemble
            : KitWorkOrderType.Assemble;

        var now = DateTime.UtcNow;
        var workOrderCode = !string.IsNullOrWhiteSpace(request?.WorkOrderCode)
            ? request.WorkOrderCode.Trim()
            : $"KWO-{now:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var lineInputs = request?.ComponentLines is { Count: > 0 }
            ? request.ComponentLines
            : request?.Lines;
        if (lineInputs == null || lineInputs.Count == 0)
        {
            var bomLines = await _db.ProductBundleComponents.AsNoTracking()
                .Where(b => b.ParentProductId == kitProduct.Id && b.IsActive)
                .OrderBy(b => b.Id)
                .ToListAsync(cancellationToken);

            if (bomLines.Count > 0)
            {
                lineInputs = bomLines.Select(b => new CreateKitWorkOrderLineInput(
                    b.ComponentProductId,
                    b.ComponentQuantity > 0m ? b.ComponentQuantity : 1m)).ToList();
            }
            else
            {
                var fallbackCompId = await _db.Products.AsNoTracking()
                    .Where(p => p.IsActive && p.Id != kitProduct.Id)
                    .OrderBy(p => p.Id)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(cancellationToken) ?? kitProduct.Id;
                lineInputs = [new CreateKitWorkOrderLineInput(fallbackCompId, 1m)];
            }
        }

        var kitBaseUomCode = await _db.Units.AsNoTracking()
            .Where(u => u.Id == kitProduct.UnitId)
            .Select(u => u.Code)
            .FirstOrDefaultAsync(cancellationToken) ?? "PCS";
        kitBaseUomCode = NormalizeKitUomCode(kitBaseUomCode);

        var workOrder = new KitWorkOrder
        {
            WarehouseId = warehouseId,
            WorkOrderCode = workOrderCode,
            KitProductId = kitProduct.Id,
            WorkOrderType = workOrderType,
            Status = "Released",
            PlannedKitQty = plannedQty,
            CompletedKitQty = 0m,
            BaseUomCode = kitBaseUomCode,
            ConversionRateSnapshot = 1m,
            TargetLocationId = request?.TargetLocationId,
            TargetLotId = request?.TargetLotId,
            TargetHandlingUnitId = request?.TargetHandlingUnitId,
            RolledUpUnitCost = 0m,
            CreatedByUserId = actorUserId is > 0 ? actorUserId : 1,
            CreatedAtUtc = now
        };

        foreach (var li in lineInputs)
        {
            var ratio = li.ComponentRatioQty > 0m ? Math.Round(li.ComponentRatioQty, 4) : 1m;
            var reqBase = Math.Round(plannedQty * ratio, 4);
            var compInfo = await _db.Products.AsNoTracking()
                .Where(p => p.Id == li.ComponentProductId)
                .Select(p => new
                {
                    UomCode = p.Unit != null && p.Unit.Code != null ? p.Unit.Code : "PCS",
                    p.MovingAverageCost
                })
                .FirstOrDefaultAsync(cancellationToken);
            var compBaseUomCode = NormalizeKitUomCode(compInfo?.UomCode);
            var resolvedCompUnitCost = li.ComponentUnitCost is > 0m
                ? Math.Round(li.ComponentUnitCost.Value, 4)
                : (compInfo?.MovingAverageCost is > 0m ? Math.Round(compInfo.MovingAverageCost, 4) : 0m);

            workOrder.ComponentLines.Add(new KitWorkOrderComponentLine
            {
                ComponentProductId = li.ComponentProductId,
                ComponentRatioQty = ratio,
                RequiredBaseQty = reqBase,
                ConsumedBaseQty = 0m,
                BaseUomCode = compBaseUomCode,
                ConversionRateSnapshot = 1m,
                SourceLocationId = li.SourceLocationId,
                SourceLotId = li.SourceLotId,
                ComponentUnitCost = resolvedCompUnitCost
            });
        }

        workOrder.RolledUpUnitCost = Math.Round(
            workOrder.ComponentLines.Sum(l => l.ComponentRatioQty * l.ComponentUnitCost),
            4);

        _db.KitWorkOrders.Add(workOrder);
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorUserId is > 0 ? actorUserId : 1,
            WarehouseId = warehouseId,
            Action = "KIT_WORK_ORDER_CREATED",
            EntityName = "KitWorkOrder",
            NewValues = $"Code={workOrderCode};Type={workOrderType};KitProductId={kitProduct.Id};PlannedQty={plannedQty:0.####}",
            Reason = $"Khởi tạo lệnh gia công {workOrderType} bộ KIT {kitProduct.Code}",
            Result = "Success",
            Timestamp = now
        });

        await _db.SaveChangesAsync(cancellationToken);

        if (request?.AutoComplete == true)
        {
            return await CompleteKitWorkOrderAsync(
                workOrder.Id,
                new CompleteKitWorkOrderInput(
                    CompletedKitQty: plannedQty,
                    TargetLocationId: request.TargetLocationId,
                    TargetLotId: request.TargetLotId,
                    TargetHandlingUnitId: request.TargetHandlingUnitId),
                actorUserId,
                cancellationToken);
        }

        var canReadCostCreate = CanReadInventoryCost();
        var createTotalCost = Math.Round(workOrder.PlannedKitQty * workOrder.RolledUpUnitCost, 4);
        return WmsEndpointOutcome.Ok(new
        {
            id = workOrder.Id,
            workOrderId = workOrder.Id,
            workOrderCode = workOrder.WorkOrderCode,
            warehouseId = workOrder.WarehouseId,
            kitProductId = workOrder.KitProductId,
            kitProductCode = kitProduct.Code,
            kitProductName = kitProduct.Name,
            workOrderType = workOrder.WorkOrderType.ToString(),
            status = workOrder.Status,
            plannedKitQty = workOrder.PlannedKitQty,
            completedKitQty = workOrder.CompletedKitQty,
            baseUomCode = NormalizeKitUomCode(workOrder.BaseUomCode),
            conversionRateSnapshot = workOrder.ConversionRateSnapshot,
            targetLocationId = workOrder.TargetLocationId,
            targetLotId = workOrder.TargetLotId,
            targetHandlingUnitId = workOrder.TargetHandlingUnitId,
            rolledUpUnitCost = canReadCostCreate ? (decimal?)workOrder.RolledUpUnitCost : null,
            unitCost = canReadCostCreate ? (decimal?)workOrder.RolledUpUnitCost : null,
            totalCost = canReadCostCreate ? (decimal?)createTotalCost : null,
            costHiddenByRbac = !canReadCostCreate,
            earliestComponentExpiryDate = workOrder.EarliestComponentExpiryDate,
            componentLines = workOrder.ComponentLines.Select(l => new
            {
                id = l.Id,
                componentProductId = l.ComponentProductId,
                componentRatioQty = l.ComponentRatioQty,
                requiredBaseQty = l.RequiredBaseQty,
                consumedBaseQty = l.ConsumedBaseQty,
                baseUomCode = NormalizeKitUomCode(l.BaseUomCode),
                conversionRateSnapshot = l.ConversionRateSnapshot,
                sourceLocationId = l.SourceLocationId,
                sourceLotId = l.SourceLotId,
                componentUnitCost = canReadCostCreate ? (decimal?)l.ComponentUnitCost : null
            }).ToList(),
            statusLabel = $"Đã phát hành lệnh gia công bộ KIT {workOrder.WorkOrderCode} ({workOrder.WorkOrderType})"
        });
    }
}
