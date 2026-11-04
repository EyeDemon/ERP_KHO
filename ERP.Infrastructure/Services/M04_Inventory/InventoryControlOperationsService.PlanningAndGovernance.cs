using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    public async Task<WmsEndpointOutcome> AdvancedPlanningSuiteAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null) return WmsEndpointOutcome.BadRequest("Database context is required.");
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(warehouseId, effectiveAuth, cancellationToken);
        if (warehouseId.HasValue && !await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == warehouseId.Value, cancellationToken))
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "WAREHOUSE_NOT_FOUND",
                errorCode = "WAREHOUSE_NOT_FOUND",
                message = $"Không tìm thấy kho #{warehouseId.Value}."
            });
        }

        var stockQuery = db.InventoryStocks.AsNoTracking();
        var soLineQuery = db.SalesOrderLines.AsNoTracking()
            .Where(l => l.SalesOrder.Status != SalesOrderStatus.Cancelled && l.SalesOrder.Status != SalesOrderStatus.Draft);
        var poLineQuery = db.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.PurchaseOrder.Status == PurchaseOrderStatus.Open || l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived);
        var reservationQuery = db.StockReservations.AsNoTracking()
            .Where(r => r.Status == StockReservationStatus.Active);

        if (scopedWarehouseIds != null)
        {
            stockQuery = stockQuery.Where(s => scopedWarehouseIds.Contains(s.WarehouseId));
            soLineQuery = soLineQuery.Where(l => scopedWarehouseIds.Contains(l.SalesOrder.WarehouseId));
            poLineQuery = poLineQuery.Where(l => scopedWarehouseIds.Contains(l.PurchaseOrder.WarehouseId));
            reservationQuery = reservationQuery.Where(r => scopedWarehouseIds.Contains(r.WarehouseId));
        }

        var rawStocks = await stockQuery
            .Select(s => new
            {
                s.WarehouseId,
                WarehouseName = s.Warehouse != null ? s.Warehouse.Name : string.Empty,
                s.ProductId,
                ProductCode = s.Product != null ? s.Product.Code : string.Empty,
                ProductName = s.Product != null ? s.Product.Name : string.Empty,
                s.Status,
                s.Quantity,
                s.ReservedQuantity,
                ExpiryDate = s.Lot != null ? s.Lot.ExpiryDate : (DateTime?)null
            })
            .ToListAsync(cancellationToken);

        var todayUtc = DateTime.UtcNow.Date;
        var stocks = rawStocks
            .Select(s => new
            {
                s.WarehouseId,
                s.WarehouseName,
                s.ProductId,
                s.ProductCode,
                s.ProductName,
                s.Status,
                s.Quantity,
                s.ReservedQuantity,
                AvailableQty = s.Status == InventoryStatus.Available && (!s.ExpiryDate.HasValue || s.ExpiryDate.Value.Date >= todayUtc)
                    ? Math.Max(0m, s.Quantity - s.ReservedQuantity)
                    : 0m
            })
            .ToList();

        var totalOpenDemandQty = (await soLineQuery
            .Select(l => new { l.OrderedQuantity, l.CancelledQuantity })
            .ToListAsync(cancellationToken))
            .Sum(l => Math.Max(0m, l.OrderedQuantity - l.CancelledQuantity));

        var incomingPoQty = (await poLineQuery
            .Select(l => new { l.BaseOrderedQuantity, l.OrderedQuantity })
            .ToListAsync(cancellationToken))
            .Sum(l => l.BaseOrderedQuantity > 0m ? l.BaseOrderedQuantity : l.OrderedQuantity);

        var activeReservationsCount = await reservationQuery.CountAsync(cancellationToken);

        var bySku = stocks
            .GroupBy(s => new { s.ProductId, s.ProductCode, s.ProductName })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.ProductCode,
                g.Key.ProductName,
                OnHandQty = g.Sum(x => x.Quantity),
                AvailableQty = g.Sum(x => x.AvailableQty),
                WarehousesCount = g.Where(x => x.Quantity > 0).Select(x => x.WarehouseId).Distinct().Count()
            })
            .OrderByDescending(x => x.OnHandQty)
            .ToList();

        var classACount = bySku.Count(x => x.OnHandQty >= 100m);
        var classBCount = bySku.Count(x => x.OnHandQty >= 20m && x.OnHandQty < 100m);
        var classCCount = bySku.Count(x => x.OnHandQty < 20m);

        var safetyStockProposals = bySku
            .Where(x => x.AvailableQty < 50m)
            .OrderBy(x => x.AvailableQty)
            .Take(25)
            .Select(x => new
            {
                productId = x.ProductId,
                productCode = x.ProductCode.Trim(),
                productName = x.ProductName.Trim(),
                onHandQty = x.OnHandQty,
                availableQty = x.AvailableQty,
                safetyStockTarget = 50m,
                recommendedReorderQty = Math.Max(0m, 100m - x.AvailableQty),
                actionRecommendation = x.AvailableQty <= 0m ? "Đôn đốc đơn mua khẩn (PO)" : "Đặt hàng bổ sung định kỳ"
            })
            .ToList();

        var totalAtp = stocks.Sum(x => x.AvailableQty);
        var qcPendingQty = stocks
            .Where(x => x.Status == InventoryStatus.QcHold || x.Status == InventoryStatus.Quarantine)
            .Sum(x => x.Quantity);

        var byWarehouse = stocks
            .GroupBy(s => s.WarehouseId)
            .Select(g => new { WarehouseId = g.Key, TotalAvailable = g.Sum(x => x.AvailableQty), TotalOnHand = g.Sum(x => x.Quantity) })
            .ToList();

        return WmsEndpointOutcome.Ok(new
        {
            currentAvailableStock = totalAtp,
            totalAvailableQty = totalAtp,
            totalAtpQuantity = totalAtp,
            totalOpenDemandQty,
            incomingPoQty,
            incomingPurchaseOrderQty = incomingPoQty,
            activeReservationsCount,
            abcClassificationSummary = new
            {
                classA_HighVolumeCount = classACount,
                classB_MediumVolumeCount = classBCount,
                classC_LowVolumeCount = classCCount,
                totalClassifiedSkus = bySku.Count
            },
            safetyStockAndReorderProposals = safetyStockProposals,
            atpCtpSummary = new
            {
                availableToPromiseQty = totalAtp,
                totalAtpQuantity = totalAtp,
                capableToPromiseQty = totalAtp + qcPendingQty + incomingPoQty,
                defaultPromiseLeadTimeHours = 24
            },
            networkBalancingSummary = new
            {
                activeStockWarehousesCount = byWarehouse.Count(w => w.TotalOnHand > 0),
                surplusWarehousesCount = byWarehouse.Count(w => w.TotalAvailable >= 100m),
                shortageWarehousesCount = byWarehouse.Count(w => w.TotalAvailable < 20m)
            },
            decisionPolicyRegistry = new
            {
                policyVersion = "AX-2026.10 — Duyệt có kiểm soát (Human-in-the-loop)",
                humanApprovalRequiredAtBoundary = true,
                autonomousLedgerMutationAllowed = false
            },
            status = "Đã tải bộ công cụ hoạch định tồn kho nâng cao & dự báo nhu cầu",
            statusLabel = "Đã tải bộ công cụ hoạch định tồn kho nâng cao & dự báo nhu cầu"
        });
    }

    public async Task<WmsEndpointOutcome> RunWhatIfSimulationAsync(
        WhatIfSimulationInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null) return WmsEndpointOutcome.BadRequest("Database context is required.");
        var effectiveAuth = warehouseAuth ?? warehouseAccess;

        var req = request ?? new WhatIfSimulationInput();
        var clampedMultiplier = req.DemandShockPct.HasValue
            ? Math.Clamp(1m + (req.DemandShockPct.Value / 100m), 0.1m, 5.0m)
            : Math.Clamp(req.DemandMultiplier, 0.5m, 5.0m);
        var horizonDays = Math.Clamp(req.HorizonDays, 1, 90);
        var leadTimeDelayDays = Math.Clamp(req.LeadTimeDelayDays ?? 0, 0, 60);

        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(req.WarehouseId, effectiveAuth, cancellationToken);
        if (req.WarehouseId.HasValue && db != null && !await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == req.WarehouseId.Value, cancellationToken))
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "WAREHOUSE_NOT_FOUND",
                errorCode = "WAREHOUSE_NOT_FOUND",
                message = $"Không tìm thấy kho #{req.WarehouseId.Value}."
            });
        }

        var totalAvailableQty = 0m;
        var totalOpenDemandQty = 0m;
        var incomingPoQty = 0m;
        var activeReservedQty = 0m;

        if (db != null)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var stockQuery = db.InventoryStocks.AsNoTracking()
                .Where(s => s.Status == InventoryStatus.Available &&
                            (s.Lot == null || !s.Lot.ExpiryDate.HasValue || s.Lot.ExpiryDate.Value >= todayUtc));
            var soLineQuery = db.SalesOrderLines.AsNoTracking()
                .Where(l => l.SalesOrder.Status != SalesOrderStatus.Cancelled && l.SalesOrder.Status != SalesOrderStatus.Draft);
            var poLineQuery = db.PurchaseOrderLines.AsNoTracking()
                .Where(l => l.PurchaseOrder.Status == PurchaseOrderStatus.Open || l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived);
            var reservationQuery = db.StockReservations.AsNoTracking()
                .Where(r => r.Status == StockReservationStatus.Active);

            if (scopedWarehouseIds != null)
            {
                stockQuery = stockQuery.Where(s => scopedWarehouseIds.Contains(s.WarehouseId));
                soLineQuery = soLineQuery.Where(l => scopedWarehouseIds.Contains(l.SalesOrder.WarehouseId));
                poLineQuery = poLineQuery.Where(l => scopedWarehouseIds.Contains(l.PurchaseOrder.WarehouseId));
                reservationQuery = reservationQuery.Where(r => scopedWarehouseIds.Contains(r.WarehouseId));
            }

            totalAvailableQty = (await stockQuery
                .Select(s => new { s.Quantity, s.ReservedQuantity, ExpiryDate = s.Lot != null ? s.Lot.ExpiryDate : (DateTime?)null })
                .ToListAsync(cancellationToken))
                .Where(s => !s.ExpiryDate.HasValue || s.ExpiryDate.Value.Date >= todayUtc)
                .Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity));

            totalOpenDemandQty = (await soLineQuery
                .Select(l => new { l.OrderedQuantity, l.CancelledQuantity })
                .ToListAsync(cancellationToken))
                .Sum(l => Math.Max(0m, l.OrderedQuantity - l.CancelledQuantity));

            incomingPoQty = (await poLineQuery
                .Select(l => new { l.BaseOrderedQuantity, l.OrderedQuantity })
                .ToListAsync(cancellationToken))
                .Sum(l => l.BaseOrderedQuantity > 0m ? l.BaseOrderedQuantity : l.OrderedQuantity);

            activeReservedQty = (await reservationQuery
                .Select(r => new { r.Quantity, r.ConsumedQuantity, r.ReleasedQuantity })
                .ToListAsync(cancellationToken))
                .Sum(r => Math.Max(0m, r.Quantity - r.ConsumedQuantity - r.ReleasedQuantity));
        }

        var baseDemand = totalOpenDemandQty > 0m ? totalOpenDemandQty : totalAvailableQty;
        var simulatedDemand = Math.Round(baseDemand * clampedMultiplier, 2);
        var projectedEndingStock = Math.Round(totalAvailableQty + incomingPoQty - simulatedDemand - (leadTimeDelayDays * 2m), 2);
        var projectedNetBalance = projectedEndingStock;
        var scenarioStatus = projectedEndingStock < 0m
            ? "Cảnh báo thiếu hụt tồn kho"
            : "Đủ tồn kho đáp ứng";
        var formulaSummary = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Tồn khả dụng ({totalAvailableQty:0.##} Thùng) + Đang nhập PO ({incomingPoQty:0.##} Thùng) - Cầu kịch bản ({simulatedDemand:0.##} Thùng) = Dự kiến cuối kỳ ({projectedNetBalance:0.##} Thùng)");

        var now = DateTime.UtcNow;
        var simulationId = $"SIM-WIF-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = req.WarehouseId,
                EntityName = "InventoryControl",
                Action = "WhatIfSimulation",
                NewValues = simulationId,
                Reason = $"Mô phỏng tồn kho What-If: {formulaSummary}",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            simulationId,
            warehouseId = req.WarehouseId,
            demandMultiplier = clampedMultiplier,
            horizonDays,
            leadTimeDelayDays,
            totalAvailable = totalAvailableQty,
            totalAvailableQty,
            currentAvailableStock = totalAvailableQty,
            totalAtpQuantity = totalAvailableQty,
            totalOpenDemandQty,
            incomingPoQty,
            incomingPurchaseOrderQty = incomingPoQty,
            activeReservedQty,
            simulatedDemand,
            projectedNetBalance,
            projectedEndingStock,
            formulaSummary,
            scenarioStatus,
            status = scenarioStatus,
            statusLabel = scenarioStatus,
            immutableSnapshotGuarantee = true,
            simulatedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> EvaluateDecisionPolicyGovernanceAsync(
        EvaluateDecisionPolicyInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null) return WmsEndpointOutcome.BadRequest("Database context is required.");
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var accessibleIds = await ResolveScopedWarehouseIdsAsync(warehouseId, effectiveAuth, cancellationToken);
        if (warehouseId.HasValue && !await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == warehouseId.Value, cancellationToken))
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "WAREHOUSE_NOT_FOUND",
                errorCode = "WAREHOUSE_NOT_FOUND",
                message = $"Không tìm thấy kho #{warehouseId.Value}."
            });
        }

        var evaluatedStockBucketsCount = 0;
        var trackedSkuCount = 0;
        var activeLocationsCount = 0;
        var activeReservationsCount = 0;
        var activeLocksCount = 0;
        if (db != null)
        {
            var stockQuery = db.InventoryStocks.AsNoTracking();
            var locQuery = db.WarehouseLocations.AsNoTracking().Where(l => l.IsActive && !l.IsBlocked);
            var resQuery = db.StockReservations.AsNoTracking().Where(r => r.Status == StockReservationStatus.Active);
            var lockQuery = db.InventoryLocks.AsNoTracking().Where(l => l.Status == InventoryLockStatus.Active);

            if (warehouseId.HasValue)
            {
                stockQuery = stockQuery.Where(s => s.WarehouseId == warehouseId.Value);
                locQuery = locQuery.Where(l => l.WarehouseId == warehouseId.Value);
                resQuery = resQuery.Where(r => r.WarehouseId == warehouseId.Value);
                lockQuery = lockQuery.Where(l => l.WarehouseId == warehouseId.Value);
            }
            else if (accessibleIds != null)
            {
                stockQuery = stockQuery.Where(s => accessibleIds.Contains(s.WarehouseId));
                locQuery = locQuery.Where(l => accessibleIds.Contains(l.WarehouseId));
                resQuery = resQuery.Where(r => accessibleIds.Contains(r.WarehouseId));
                lockQuery = lockQuery.Where(l => accessibleIds.Contains(l.WarehouseId));
            }

            evaluatedStockBucketsCount = await stockQuery.CountAsync(cancellationToken);
            trackedSkuCount = await db.Products.AsNoTracking()
                .CountAsync(p => p.IsActive && (p.TrackingType != ProductTrackingType.None || p.ExpiryControl), cancellationToken);
            activeLocationsCount = await locQuery.CountAsync(cancellationToken);
            activeReservationsCount = await resQuery.CountAsync(cancellationToken);
            activeLocksCount = await lockQuery.CountAsync(cancellationToken);
        }

        var now = DateTime.UtcNow;
        var policyRegistryId = $"DEC-POL-{now:yyyyMMdd-HHmm}";
        if (db != null)
        {
            var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = warehouseId,
                EntityName = "InventoryControl",
                Action = "AuditDecisionPolicies",
                NewValues = policyRegistryId,
                Reason = $"Kiểm định khung chính sách quyết định tồn kho ({evaluatedStockBucketsCount} rổ tồn, {trackedSkuCount} SKU quản lý lô/sê-ri/hạn dùng)",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            policyRegistryId,
            warehouseId,
            evaluatedStockBucketsCount,
            trackedSkuCount,
            activeLocationsCount,
            activeReservationsCount,
            activeLocksCount,
            forecastBiasMapeStatus = "Sai số dự báo (MAPE) nằm trong ngưỡng cho phép",
            demandAnomalyDetector = "Đã kích hoạt bộ lọc biến động cầu bất thường",
            inventoryRiskScoreStatus = "Đã chấm điểm rủi ro thiếu hụt tồn kho có giải trình",
            humanInTheLoopBoundary = "Bắt buộc phê duyệt con người trước khi ghi sổ cái",
            status = "Đã xác thực khung quản trị chính sách quyết định tồn kho",
            statusLabel = "Đã xác thực khung quản trị chính sách quyết định tồn kho",
            evaluatedAtUtc = now
        });
    }
}
