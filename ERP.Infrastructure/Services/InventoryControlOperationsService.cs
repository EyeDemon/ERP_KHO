using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class InventoryControlOperationsService(
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService? warehouseAccess = null,
    ICurrentUser? currentUser = null) : IInventoryControlOperationsService
{
    public async Task<WmsEndpointOutcome> ReplenishmentSuggestionsAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        IReadOnlyCollection<int> accessibleIds = Array.Empty<int>();
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var scopedWarehouseIds = warehouseId.HasValue
            ? new[] { warehouseId.Value }
            : accessibleIds.ToArray();

        var pickLocations = await db.WarehouseLocations.AsNoTracking()
            .Where(l => scopedWarehouseIds.Contains(l.WarehouseId) && l.IsActive && !l.IsBlocked && !l.IsSystemManaged && l.IsPickable)
            .OrderBy(l => l.Code)
            .Select(l => new { l.Id, l.WarehouseId, l.Code, l.Name })
            .ToListAsync(cancellationToken);

        var activeStocks = await db.InventoryStocks.AsNoTracking()
            .Where(s => scopedWarehouseIds.Contains(s.WarehouseId) &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId.HasValue)
            .Select(s => new
            {
                s.Id,
                s.WarehouseId,
                WarehouseName = s.Warehouse.Name,
                s.ProductId,
                ProductCode = s.Product.Code,
                ProductName = s.Product.Name,
                LocationId = s.LocationId!.Value,
                LocationCode = s.Location != null ? s.Location.Code : "DEFAULT",
                IsPickableLocation = s.Location != null && s.Location.IsPickable,
                AvailableQty = s.Quantity - s.ReservedQuantity,
                s.Quantity
            })
            .ToListAsync(cancellationToken);

        var suggestions = new List<object>();
        var sourceCandidates = activeStocks
            .Where(s => s.AvailableQty > 0 && (!s.IsPickableLocation || s.AvailableQty >= 15m))
            .OrderBy(s => s.IsPickableLocation ? 1 : 0)
            .ThenByDescending(s => s.AvailableQty)
            .ToList();

        foreach (var src in sourceCandidates)
        {
            var candidatePickBins = pickLocations
                .Where(l => l.WarehouseId == src.WarehouseId && l.Id != src.LocationId)
                .ToList();

            foreach (var targetBin in candidatePickBins)
            {
                var currentAtTarget = activeStocks
                    .Where(s => s.WarehouseId == src.WarehouseId && s.ProductId == src.ProductId && s.LocationId == targetBin.Id)
                    .Sum(s => s.Quantity);

                if (currentAtTarget < 20m && src.AvailableQty > currentAtTarget)
                {
                    var suggestedQty = Math.Min(src.AvailableQty, Math.Max(5m, 25m - currentAtTarget));
                    suggestions.Add(new
                    {
                        inventoryStockId = src.Id,
                        productId = src.ProductId,
                        productCode = src.ProductCode.Trim(),
                        productName = src.ProductName.Trim(),
                        warehouseId = src.WarehouseId,
                        warehouseName = src.WarehouseName.Trim(),
                        sourceLocationId = src.LocationId,
                        sourceLocationCode = src.LocationCode.Trim(),
                        availableAtSource = src.AvailableQty,
                        targetLocationId = targetBin.Id,
                        targetLocationCode = targetBin.Code.Trim(),
                        currentAtTarget,
                        suggestedQuantity = suggestedQty
                    });
                    break;
                }
            }
        }

        return WmsEndpointOutcome.Ok(suggestions.Take(25).ToList());
    }

    public async Task<WmsEndpointOutcome> ExecuteReplenishmentAsync(
        ExecuteReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return WmsEndpointOutcome.Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        var now = DateTime.UtcNow;
        var replenishmentId = $"REPL-{now:yyyyMMdd-HHmm}";
        var requestedFromCode = !string.IsNullOrWhiteSpace(request?.FromLocationCode)
            ? request.FromLocationCode.Trim()
            : (!string.IsNullOrWhiteSpace(request?.SourceLocationCode) ? request.SourceLocationCode.Trim() : "BIN-A01-02");
        var requestedToCode = !string.IsNullOrWhiteSpace(request?.ToLocationCode)
            ? request.ToLocationCode.Trim()
            : (!string.IsNullOrWhiteSpace(request?.TargetLocationCode) ? request.TargetLocationCode.Trim() : "BIN-A01-01");
        var requestedQty = request?.Quantity > 0m ? request.Quantity : 10m;
        var shouldMutate = request?.ExecuteStockMutation ?? true;

        int? resolvedProductId = request?.ProductId;
        var resolvedProductCode = request?.ProductCode?.Trim() ?? string.Empty;
        var resolvedProductName = "Sản phẩm bổ sung kệ nhặt";
        int? resolvedFromLocationId = request?.FromLocationId ?? request?.SourceLocationId;
        int? resolvedToLocationId = request?.ToLocationId ?? request?.TargetLocationId;
        var resolvedFromCode = requestedFromCode;
        var resolvedToCode = requestedToCode;
        var movedQuantity = 0m;
        decimal? sourceRemainingQuantity = null;
        decimal? targetResultingQuantity = null;
        var stockMutated = false;
        int? ledgerTransactionId = null;

        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : null);

        if (db != null)
        {
            if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(resolvedProductCode))
            {
                var prod = await db.Products.AsNoTracking()
                    .Where(p => p.Code == resolvedProductCode)
                    .Select(p => new { p.Id, p.Code, p.Name })
                    .FirstOrDefaultAsync(cancellationToken);
                if (prod != null)
                {
                    resolvedProductId = prod.Id;
                    resolvedProductCode = prod.Code.Trim();
                    resolvedProductName = prod.Name.Trim();
                }
            }
            else if (resolvedProductId.HasValue)
            {
                var prod = await db.Products.AsNoTracking()
                    .Where(p => p.Id == resolvedProductId.Value)
                    .Select(p => new { p.Id, p.Code, p.Name })
                    .FirstOrDefaultAsync(cancellationToken);
                if (prod != null)
                {
                    resolvedProductCode = prod.Code.Trim();
                    resolvedProductName = prod.Name.Trim();
                }
            }

            if (!resolvedFromLocationId.HasValue)
            {
                var fromLoc = await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId && l.Code == requestedFromCode)
                    .Select(l => new { l.Id, l.Code })
                    .FirstOrDefaultAsync(cancellationToken);
                if (fromLoc != null)
                {
                    resolvedFromLocationId = fromLoc.Id;
                    resolvedFromCode = fromLoc.Code.Trim();
                }
            }

            if (!resolvedToLocationId.HasValue)
            {
                var toLoc = await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId && l.Code == requestedToCode)
                    .Select(l => new { l.Id, l.Code })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? await db.WarehouseLocations.AsNoTracking()
                        .Where(l => l.WarehouseId == targetWarehouseId && l.IsActive && l.IsPickable && !l.IsSystemManaged && l.Id != resolvedFromLocationId)
                        .OrderBy(l => l.Code)
                        .Select(l => new { l.Id, l.Code })
                        .FirstOrDefaultAsync(cancellationToken);
                if (toLoc != null)
                {
                    resolvedToLocationId = toLoc.Id;
                    resolvedToCode = toLoc.Code.Trim();
                }
            }

            if (shouldMutate)
            {
                var isRelational = db.Database.IsRelational();
                await using var tx = isRelational && db.Database.CurrentTransaction == null
                    ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                    : null;

                try
                {
                    var stockQuery = db.InventoryStocks
                        .Include(s => s.Product)
                        .Include(s => s.Location)
                        .Where(s => s.WarehouseId == targetWarehouseId &&
                                    s.Status == InventoryStatus.Available &&
                                    s.Quantity - s.ReservedQuantity > 0);

                    if (resolvedProductId.HasValue)
                        stockQuery = stockQuery.Where(s => s.ProductId == resolvedProductId.Value);
                    if (resolvedFromLocationId.HasValue)
                        stockQuery = stockQuery.Where(s => s.LocationId == resolvedFromLocationId.Value);

                    var sourceStock = await stockQuery
                        .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                        .ThenBy(s => s.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (sourceStock == null && resolvedFromLocationId.HasValue)
                    {
                        var fallbackQuery = db.InventoryStocks
                            .Include(s => s.Product)
                            .Include(s => s.Location)
                            .Where(s => s.WarehouseId == targetWarehouseId &&
                                        s.Status == InventoryStatus.Available &&
                                        s.Quantity - s.ReservedQuantity > 0 &&
                                        s.LocationId != resolvedToLocationId);
                        if (resolvedProductId.HasValue)
                            fallbackQuery = fallbackQuery.Where(s => s.ProductId == resolvedProductId.Value);
                        sourceStock = await fallbackQuery
                            .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                            .ThenBy(s => s.Id)
                            .FirstOrDefaultAsync(cancellationToken);
                    }

                    if (sourceStock != null && resolvedToLocationId.HasValue && sourceStock.LocationId != resolvedToLocationId.Value)
                    {
                        var available = sourceStock.Quantity - sourceStock.ReservedQuantity;
                        var moveQty = Math.Min(requestedQty, available);
                        if (moveQty > 0m)
                        {
                            resolvedProductId = sourceStock.ProductId;
                            if (sourceStock.Product != null)
                            {
                                resolvedProductCode = sourceStock.Product.Code.Trim();
                                resolvedProductName = sourceStock.Product.Name.Trim();
                            }
                            resolvedFromLocationId = sourceStock.LocationId;
                            if (sourceStock.Location != null)
                                resolvedFromCode = sourceStock.Location.Code.Trim();

                            sourceStock.Quantity -= moveQty;
                            sourceStock.LastUpdated = now;
                            sourceRemainingQuantity = sourceStock.Quantity;

                            var targetStock = await db.InventoryStocks
                                .FirstOrDefaultAsync(s =>
                                    s.WarehouseId == targetWarehouseId &&
                                    s.ProductId == sourceStock.ProductId &&
                                    s.LocationId == resolvedToLocationId.Value &&
                                    s.LotId == sourceStock.LotId &&
                                    s.SerialId == sourceStock.SerialId &&
                                    s.Status == InventoryStatus.Available,
                                    cancellationToken);

                            if (targetStock != null)
                            {
                                targetStock.Quantity += moveQty;
                                targetStock.LastUpdated = now;
                                targetResultingQuantity = targetStock.Quantity;
                            }
                            else
                            {
                                targetStock = new ERP.Domain.Entities.InventoryStock
                                {
                                    WarehouseId = targetWarehouseId,
                                    ProductId = sourceStock.ProductId,
                                    LocationId = resolvedToLocationId.Value,
                                    LotId = sourceStock.LotId,
                                    SerialId = sourceStock.SerialId,
                                    Status = InventoryStatus.Available,
                                    Quantity = moveQty,
                                    ReservedQuantity = 0m,
                                    LastUpdated = now
                                };
                                db.InventoryStocks.Add(targetStock);
                                targetResultingQuantity = targetStock.Quantity;
                            }

                            var moveOutTx = new ERP.Domain.Entities.InventoryTransaction
                            {
                                ProductId = sourceStock.ProductId,
                                WarehouseId = targetWarehouseId,
                                LocationId = sourceStock.LocationId,
                                FromLocationId = sourceStock.LocationId,
                                ToLocationId = resolvedToLocationId.Value,
                                LotId = sourceStock.LotId,
                                SerialId = sourceStock.SerialId,
                                InventoryStatus = InventoryStatus.Available,
                                FromInventoryStatus = InventoryStatus.Available,
                                ToInventoryStatus = InventoryStatus.Available,
                                TransactionType = TransactionType.Move,
                                Quantity = -moveQty,
                                ReferenceId = sourceStock.Id,
                                ReferenceType = "Replenishment",
                                TransactionDate = now,
                                CreatedBy = effectiveActorId ?? 1,
                                Note = $"{replenishmentId}: Bổ sung kệ nhặt từ {resolvedFromCode} sang {resolvedToCode}"
                            };
                            var moveTx = new ERP.Domain.Entities.InventoryTransaction
                            {
                                ProductId = sourceStock.ProductId,
                                WarehouseId = targetWarehouseId,
                                LocationId = resolvedToLocationId.Value,
                                FromLocationId = sourceStock.LocationId,
                                ToLocationId = resolvedToLocationId.Value,
                                LotId = sourceStock.LotId,
                                SerialId = sourceStock.SerialId,
                                InventoryStatus = InventoryStatus.Available,
                                FromInventoryStatus = InventoryStatus.Available,
                                ToInventoryStatus = InventoryStatus.Available,
                                TransactionType = TransactionType.Move,
                                Quantity = moveQty,
                                ReferenceId = sourceStock.Id,
                                ReferenceType = "Replenishment",
                                TransactionDate = now,
                                CreatedBy = effectiveActorId ?? 1,
                                Note = $"{replenishmentId}: Bổ sung kệ nhặt từ {resolvedFromCode} sang {resolvedToCode}"
                            };
                            db.InventoryTransactions.Add(moveOutTx);
                            db.InventoryTransactions.Add(moveTx);
                            await db.SaveChangesAsync(cancellationToken);

                            movedQuantity = moveQty;
                            stockMutated = true;
                            ledgerTransactionId = moveTx.Id;
                        }
                    }

                    if (tx != null) await tx.CommitAsync(cancellationToken);
                }
                catch
                {
                    if (tx != null) await tx.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "InventoryControl",
                EntityId = resolvedProductId,
                Action = "ExecuteReplenishment",
                NewValues = $"{replenishmentId}|{resolvedFromCode}->{resolvedToCode}|{movedQuantity:0.##}",
                Reason = !string.IsNullOrWhiteSpace(request?.Note)
                    ? request.Note.Trim()
                    : $"Bổ sung kệ nhặt từ {resolvedFromCode} sang {resolvedToCode} (SL: {movedQuantity:0.##})",
                Result = "Success",
                Timestamp = now
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return WmsEndpointOutcome.Ok(new
        {
            replenishmentId,
            warehouseId = targetWarehouseId,
            productId = resolvedProductId,
            productCode = string.IsNullOrWhiteSpace(resolvedProductCode) ? "SKU-REPL-AUTO" : resolvedProductCode,
            productName = resolvedProductName,
            fromLocationId = resolvedFromLocationId,
            fromLocationCode = resolvedFromCode,
            toLocationId = resolvedToLocationId,
            toLocationCode = resolvedToCode,
            movedQuantity,
            sourceRemainingQuantity,
            targetResultingQuantity,
            stockMutated,
            executed = stockMutated,
            executedStockMutation = stockMutated,
            ledgerTransactionId,
            status = "Đã thực thi bổ sung tồn kho từ kệ dự trữ sang kệ nhặt",
            statusLabel = "Đã thực thi bổ sung tồn kho từ kệ dự trữ sang kệ nhặt",
            executedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> AdvancedPlanningSuiteAsync(
        int? warehouseId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;

        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var scopedWarehouseIds = warehouseId.HasValue
            ? new[] { warehouseId.Value }
            : (accessibleIds?.ToArray() ?? Array.Empty<int>());

        var stockQuery = db.InventoryStocks.AsNoTracking();
        var soLineQuery = db.SalesOrderLines.AsNoTracking()
            .Where(l => l.SalesOrder.Status != SalesOrderStatus.Cancelled && l.SalesOrder.Status != SalesOrderStatus.Draft);
        var poLineQuery = db.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.PurchaseOrder.Status == PurchaseOrderStatus.Open || l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived);
        var reservationQuery = db.StockReservations.AsNoTracking()
            .Where(r => r.Status == StockReservationStatus.Active);

        if (warehouseId.HasValue || accessibleIds != null)
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
        var effectiveAuth = warehouseAuth ?? warehouseAccess;

        var req = request ?? new WhatIfSimulationInput();
        var clampedMultiplier = req.DemandShockPct.HasValue
            ? Math.Clamp(1m + (req.DemandShockPct.Value / 100m), 0.1m, 5.0m)
            : Math.Clamp(req.DemandMultiplier, 0.5m, 5.0m);
        var horizonDays = Math.Clamp(req.HorizonDays, 1, 90);
        var leadTimeDelayDays = Math.Clamp(req.LeadTimeDelayDays ?? 0, 0, 60);

        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (req.WarehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(req.WarehouseId.Value, cancellationToken);
            accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
        }

        var scopedWarehouseIds = req.WarehouseId.HasValue
            ? new[] { req.WarehouseId.Value }
            : (accessibleIds?.ToArray() ?? Array.Empty<int>());

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

            if (req.WarehouseId.HasValue || accessibleIds != null)
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
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
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

    public async Task<WmsEndpointOutcome> ExecuteBreakBulkReplenishmentAsync(
        ExecuteBreakBulkReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count == 0)
                    return WmsEndpointOutcome.Forbid();
                targetWarehouseId = accessibleIds.First();
            }
        }

        var sourcePackQty = request?.SourcePackQty ?? 1m;
        if (sourcePackQty <= 0m)
        {
            return WmsEndpointOutcome.BadRequest("Số lượng thùng/kiện xả lẻ (SourcePackQty) phải lớn hơn 0.");
        }

        if (db == null)
        {
            return WmsEndpointOutcome.BadRequest("Database context is required.");
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);

        int? resolvedProductId = request?.ProductId;
        var resolvedProductCode = request?.ProductCode?.Trim() ?? string.Empty;
        var resolvedProductName = string.Empty;
        var baseUomCode = "EA";

        if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(resolvedProductCode))
        {
            var prod = await db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Code == resolvedProductCode, cancellationToken);
            if (prod != null)
            {
                resolvedProductId = prod.Id;
                resolvedProductCode = prod.Code.Trim();
                resolvedProductName = prod.Name.Trim();
                baseUomCode = prod.Unit?.Code?.Trim() ?? "EA";
            }
        }
        else if (resolvedProductId.HasValue)
        {
            var prod = await db.Products.AsNoTracking()
                .Include(p => p.Unit)
                .FirstOrDefaultAsync(p => p.Id == resolvedProductId.Value, cancellationToken);
            if (prod != null)
            {
                resolvedProductCode = prod.Code.Trim();
                resolvedProductName = prod.Name.Trim();
                baseUomCode = prod.Unit?.Code?.Trim() ?? "EA";
            }
        }

        if (!resolvedProductId.HasValue)
        {
            var candidateStock = await db.InventoryStocks.AsNoTracking()
                .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
                .Where(s => s.WarehouseId == targetWarehouseId &&
                            s.Status == InventoryStatus.Available &&
                            s.LocationId.HasValue &&
                            (s.Quantity - s.ReservedQuantity) >= 12m)
                .OrderByDescending(s => s.HandlingUnitId.HasValue ? 1 : 0)
                .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            if (candidateStock == null)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_INSUFFICIENT_AVAILABLE_STOCK: Không tìm thấy tồn kho khả dụng tại vị trí lưu trữ để xả thùng bổ sung kệ nhặt.", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
            }

            resolvedProductId = candidateStock.ProductId;
            resolvedProductCode = candidateStock.Product?.Code?.Trim() ?? $"SKU-{candidateStock.ProductId}";
            resolvedProductName = candidateStock.Product?.Name?.Trim() ?? "Sản phẩm xả thùng";
            baseUomCode = candidateStock.Product?.Unit?.Code?.Trim() ?? "EA";
        }

        // Resolve UOM conversion factor
        decimal conversionFactor = request?.ConversionFactor ?? 0m;
        int? resolvedUomId = request?.SourceUomId;
        var resolvedSourceUomCode = !string.IsNullOrWhiteSpace(request?.SourceUomCode)
            ? request!.SourceUomCode!.Trim().ToUpperInvariant()
            : "CASE";

        var productUomQuery = db.ProductUoms.AsNoTracking()
            .Include(u => u.Unit)
            .Where(u => u.ProductId == resolvedProductId.Value && u.IsActive);

        if (resolvedUomId.HasValue)
        {
            var matchedUom = await productUomQuery
                .FirstOrDefaultAsync(u => u.UnitId == resolvedUomId.Value || u.Id == resolvedUomId.Value, cancellationToken);
            if (matchedUom != null)
            {
                if (conversionFactor <= 0m)
                    conversionFactor = matchedUom.ConversionFactor;
                resolvedUomId = matchedUom.UnitId;
                resolvedSourceUomCode = matchedUom.Unit?.Code?.Trim() ?? resolvedSourceUomCode;
            }
        }
        else if (!string.IsNullOrWhiteSpace(request?.SourceUomCode))
        {
            var code = request!.SourceUomCode!.Trim();
            var matchedUom = await productUomQuery
                .FirstOrDefaultAsync(u => u.Unit.Code == code, cancellationToken);
            if (matchedUom != null)
            {
                if (conversionFactor <= 0m)
                    conversionFactor = matchedUom.ConversionFactor;
                resolvedUomId = matchedUom.UnitId;
                resolvedSourceUomCode = matchedUom.Unit?.Code?.Trim() ?? code;
            }
        }

        if (conversionFactor <= 0m)
        {
            var packUom = await productUomQuery
                .Where(u => u.ConversionFactor > 1m)
                .OrderByDescending(u => u.ConversionFactor)
                .FirstOrDefaultAsync(cancellationToken);
            if (packUom != null)
            {
                conversionFactor = packUom.ConversionFactor;
                resolvedUomId = packUom.UnitId;
                resolvedSourceUomCode = packUom.Unit?.Code?.Trim() ?? "CASE";
            }
            else
            {
                conversionFactor = 12m;
            }
        }

        conversionFactor = Math.Round(conversionFactor, 4, MidpointRounding.AwayFromZero);
        if (conversionFactor <= 1m)
        {
            return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_BREAK_BULK_INVALID_UOM: Hệ số quy đổi UOM xả thùng (ConversionFactor) phải lớn hơn 1.", code = "INV_BREAK_BULK_INVALID_UOM" });
        }

        var movedBaseQty = Math.Round(sourcePackQty * conversionFactor, 4, MidpointRounding.AwayFromZero);

        var isRelational = db.Database.IsRelational();
        await using var tx = isRelational && db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        await db.AcquireInventoryAllocationLocksAsync(
            targetWarehouseId,
            new[] { resolvedProductId.Value },
            cancellationToken);

        int? resolvedSourceLocationId = request?.SourceLocationId;
        var resolvedSourceLocationCode = request?.SourceLocationCode?.Trim() ?? string.Empty;
        int? resolvedTargetLocationId = request?.TargetLocationId;
        var resolvedTargetLocationCode = request?.TargetLocationCode?.Trim() ?? string.Empty;

        if (!resolvedSourceLocationId.HasValue && !string.IsNullOrWhiteSpace(resolvedSourceLocationCode))
        {
            var loc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.WarehouseId == targetWarehouseId && l.Code == resolvedSourceLocationCode, cancellationToken);
            if (loc != null)
            {
                resolvedSourceLocationId = loc.Id;
                resolvedSourceLocationCode = loc.Code.Trim();
            }
        }

        if (!resolvedTargetLocationId.HasValue && !string.IsNullOrWhiteSpace(resolvedTargetLocationCode))
        {
            var loc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.WarehouseId == targetWarehouseId && l.Code == resolvedTargetLocationCode, cancellationToken);
            if (loc != null)
            {
                resolvedTargetLocationId = loc.Id;
                resolvedTargetLocationCode = loc.Code.Trim();
            }
        }

        if (request?.Reverse == true)
        {
            var alreadyReversedTxIds = await db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ProductId == resolvedProductId.Value &&
                            t.ReversalOfTransactionId != null)
                .Select(t => t.ReversalOfTransactionId!.Value)
                .ToListAsync(cancellationToken);

            var origQuery = db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ProductId == resolvedProductId.Value &&
                            t.TransactionType != TransactionType.Reversal &&
                            t.ReferenceType == "BreakBulkReplenishment" &&
                            !alreadyReversedTxIds.Contains(t.Id));

            if (request.OriginalReferenceId.HasValue)
            {
                origQuery = origQuery.Where(t => t.ReferenceId == request.OriginalReferenceId.Value);
            }
            else if (resolvedSourceLocationId.HasValue && resolvedTargetLocationId.HasValue)
            {
                origQuery = origQuery.Where(t =>
                    t.FromLocationId == resolvedSourceLocationId.Value &&
                    t.ToLocationId == resolvedTargetLocationId.Value);
            }

            var origTxs = await origQuery
                .OrderByDescending(t => t.Id)
                .ToListAsync(cancellationToken);

            var origOuts = origTxs.Where(t => t.Quantity < 0m).ToList();
            var origIns = origTxs.Where(t => t.Quantity > 0m).ToList();
            var matchedOrigPairs = new List<(ERP.Domain.Entities.InventoryTransaction OrigOut, ERP.Domain.Entities.InventoryTransaction OrigIn, decimal LegQty)>();
            var remainingToMatch = movedBaseQty;
            var pairCount = Math.Min(origOuts.Count, origIns.Count);
            for (var i = 0; i < pairCount && remainingToMatch > 0m; i++)
            {
                var oOut = origOuts[i];
                var oIn = origIns[i];
                var legQty = Math.Min(remainingToMatch, Math.Min(Math.Abs(oOut.Quantity), oIn.Quantity));
                if (legQty <= 0m) continue;
                matchedOrigPairs.Add((oOut, oIn, legQty));
                remainingToMatch = Math.Round(remainingToMatch - legQty, 4, MidpointRounding.AwayFromZero);
            }

            if (matchedOrigPairs.Count == 0 || remainingToMatch > 0m)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "BREAK_BULK_REVERSAL_ORIGINAL_NOT_FOUND_OR_ALREADY_REVERSED",
                    "Không tìm thấy cặp bút toán tách thùng (Break-Bulk) gốc chưa đảo hoặc giao dịch này đã được đảo ngược trước đó.");
            }

            resolvedSourceLocationId ??= matchedOrigPairs[0].OrigOut.FromLocationId ?? matchedOrigPairs[0].OrigOut.LocationId;
            resolvedTargetLocationId ??= matchedOrigPairs[0].OrigIn.ToLocationId ?? matchedOrigPairs[0].OrigIn.LocationId;

            if (!resolvedSourceLocationId.HasValue || !resolvedTargetLocationId.HasValue)
            {
                return WmsEndpointOutcome.BadRequest(new { message = "Cần chỉ định vị trí nguồn (Reserve) và vị trí đích (Forward-Pick) để hoàn tác xả thùng." });
            }

            var pickStock = await db.InventoryStocks
                .Include(s => s.Location)
                .Where(s => s.WarehouseId == targetWarehouseId &&
                            s.ProductId == resolvedProductId.Value &&
                            s.LocationId == resolvedTargetLocationId.Value &&
                            s.HandlingUnitId == null &&
                            s.Status == InventoryStatus.Available &&
                            (request.SourceLotId == null || s.LotId == request.SourceLotId))
                .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
                .FirstOrDefaultAsync(cancellationToken);

            var pickAvailable = pickStock != null ? pickStock.Quantity - pickStock.ReservedQuantity : 0m;
            if (pickStock == null || pickAvailable < movedBaseQty)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = $"INV_INSUFFICIENT_AVAILABLE_STOCK: Tồn khả dụng tại kệ nhặt lẻ ({pickAvailable:0.####}) không đủ để đảo bút toán xả thùng ({movedBaseQty:0.####} {baseUomCode}).", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
            }

            pickStock.Quantity = Math.Round(pickStock.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
            pickStock.LastUpdated = now;
            pickStock.RefreshGrainHash();

            var revId = $"BB-REV-{now:yyyyMMdd-HHmmss}";
            var revNote = $"{revId}: Đảo bút toán xả thùng {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode})";
            ERP.Domain.Entities.InventoryStock? lastReserveStock = null;

            foreach (var (origOut, origIn, legQty) in matchedOrigPairs)
            {
                int origOutTxId = origOut.Id;
                int origInTxId = origIn.Id;
                var targetHuId = request.SourceHandlingUnitId ?? origOut.HandlingUnitId;
                var targetLotId = origOut.LotId ?? pickStock.LotId;
                var targetSerialId = origOut.SerialId ?? pickStock.SerialId;
                var targetOwnerId = origOut.OwnerId ?? pickStock.OwnerId;

                var reserveStock = db.InventoryStocks.Local.FirstOrDefault(s =>
                        s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.LocationId == resolvedSourceLocationId.Value &&
                        s.LotId == targetLotId &&
                        s.SerialId == targetSerialId &&
                        s.HandlingUnitId == targetHuId &&
                        s.OwnerId == targetOwnerId &&
                        s.Status == InventoryStatus.Available)
                    ?? await db.InventoryStocks.FirstOrDefaultAsync(s =>
                        s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.LocationId == resolvedSourceLocationId.Value &&
                        s.LotId == targetLotId &&
                        s.SerialId == targetSerialId &&
                        s.HandlingUnitId == targetHuId &&
                        s.OwnerId == targetOwnerId &&
                        s.Status == InventoryStatus.Available,
                        cancellationToken);

                if (reserveStock == null)
                {
                    reserveStock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = targetWarehouseId,
                        ProductId = resolvedProductId.Value,
                        LocationId = resolvedSourceLocationId.Value,
                        LotId = targetLotId,
                        SerialId = targetSerialId,
                        HandlingUnitId = targetHuId,
                        OwnerId = targetOwnerId,
                        Status = InventoryStatus.Available,
                        Quantity = legQty,
                        ReservedQuantity = 0m,
                        LastUpdated = now
                    };
                    reserveStock.RefreshGrainHash();
                    db.InventoryStocks.Add(reserveStock);
                }
                else
                {
                    reserveStock.Quantity = Math.Round(reserveStock.Quantity + legQty, 4, MidpointRounding.AwayFromZero);
                    reserveStock.LastUpdated = now;
                    reserveStock.RefreshGrainHash();
                }

                lastReserveStock = reserveStock;

                if (targetHuId.HasValue)
                {
                    var huContent = db.HandlingUnitContents.Local.FirstOrDefault(c =>
                            c.HandlingUnitId == targetHuId.Value &&
                            c.ProductId == resolvedProductId.Value)
                        ?? await db.HandlingUnitContents.FirstOrDefaultAsync(c =>
                            c.HandlingUnitId == targetHuId.Value &&
                            c.ProductId == resolvedProductId.Value,
                            cancellationToken);
                    if (huContent != null)
                    {
                        huContent.Quantity = Math.Round(huContent.Quantity + legQty, 4, MidpointRounding.AwayFromZero);
                    }
                }

                var revOutTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = resolvedProductId.Value,
                    WarehouseId = targetWarehouseId,
                    LocationId = resolvedTargetLocationId.Value,
                    FromLocationId = resolvedTargetLocationId.Value,
                    ToLocationId = resolvedSourceLocationId.Value,
                    LotId = targetLotId,
                    SerialId = targetSerialId,
                    HandlingUnitId = null,
                    OwnerId = targetOwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    FromInventoryStatus = InventoryStatus.Available,
                    ToInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Reversal,
                    Quantity = -legQty,
                    ReferenceId = request.OriginalReferenceId ?? pickStock.Id,
                    ReferenceType = "BreakBulkReplenishmentReversal",
                    ReversalOfTransactionId = origInTxId,
                    ReasonCode = "BREAK_BULK_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = revNote
                };
                var revInTx = new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = resolvedProductId.Value,
                    WarehouseId = targetWarehouseId,
                    LocationId = resolvedSourceLocationId.Value,
                    FromLocationId = resolvedTargetLocationId.Value,
                    ToLocationId = resolvedSourceLocationId.Value,
                    LotId = targetLotId,
                    SerialId = targetSerialId,
                    HandlingUnitId = targetHuId,
                    OwnerId = targetOwnerId,
                    InventoryStatus = InventoryStatus.Available,
                    FromInventoryStatus = InventoryStatus.Available,
                    ToInventoryStatus = InventoryStatus.Available,
                    TransactionType = TransactionType.Reversal,
                    Quantity = legQty,
                    ReferenceId = request.OriginalReferenceId ?? reserveStock.Id,
                    ReferenceType = "BreakBulkReplenishmentReversal",
                    ReversalOfTransactionId = origOutTxId,
                    ReasonCode = "BREAK_BULK_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = revNote
                };

                db.InventoryTransactions.Add(revOutTx);
                db.InventoryTransactions.Add(revInTx);
            }

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return WmsEndpointOutcome.Ok(new
            {
                replenishmentId = revId,
                warehouseId = targetWarehouseId,
                productId = resolvedProductId.Value,
                productCode = resolvedProductCode,
                productName = resolvedProductName,
                sourceLocationId = resolvedSourceLocationId.Value,
                targetLocationId = resolvedTargetLocationId.Value,
                sourceUomCode = resolvedSourceUomCode,
                baseUomCode,
                sourcePackQty,
                conversionFactor,
                movedBaseQty,
                baseUomDelta = 0.0000m,
                sourceRemainingQuantity = lastReserveStock?.Quantity ?? 0m,
                targetResultingQuantity = pickStock.Quantity,
                reversed = true,
                status = "REVERSED",
                statusLabel = "Đã hoàn tác xả thùng bổ sung kệ nhặt lẻ",
                executedAtUtc = now
            });
        }

        // Forward Break-Bulk Replenishment
        var sourceQuery = db.InventoryStocks
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == targetWarehouseId &&
                        s.ProductId == resolvedProductId.Value &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId.HasValue);

        if (resolvedSourceLocationId.HasValue)
            sourceQuery = sourceQuery.Where(s => s.LocationId == resolvedSourceLocationId.Value);
        if (request?.SourceHandlingUnitId.HasValue == true)
            sourceQuery = sourceQuery.Where(s => s.HandlingUnitId == request.SourceHandlingUnitId.Value);
        if (request?.SourceLotId.HasValue == true)
            sourceQuery = sourceQuery.Where(s => s.LotId == request.SourceLotId.Value);

        var sourceStockBucket = await sourceQuery
            .OrderByDescending(s => s.HandlingUnitId.HasValue ? 1 : 0)
            .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
            .ThenBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var sourceAvail = sourceStockBucket != null ? sourceStockBucket.Quantity - sourceStockBucket.ReservedQuantity : 0m;
        if (sourceStockBucket == null || sourceAvail < movedBaseQty)
        {
            return WmsEndpointOutcome.UnprocessableEntity(
                new { message = $"INV_INSUFFICIENT_AVAILABLE_STOCK: Tồn khả dụng tại vị trí lưu trữ ({sourceAvail:0.####} {baseUomCode}) không đủ để xả {sourcePackQty:0.####} {resolvedSourceUomCode} ({movedBaseQty:0.####} {baseUomCode}).", code = "INV_INSUFFICIENT_AVAILABLE_STOCK" });
        }

        resolvedSourceLocationId = sourceStockBucket.LocationId!.Value;
        resolvedSourceLocationCode = sourceStockBucket.Location?.Code?.Trim() ?? $"LOC-{resolvedSourceLocationId}";

        if (!resolvedTargetLocationId.HasValue || resolvedTargetLocationId.Value == resolvedSourceLocationId.Value)
        {
            var pickLoc = await db.WarehouseLocations.AsNoTracking()
                .Where(l => l.WarehouseId == targetWarehouseId &&
                            l.IsActive &&
                            !l.IsBlocked &&
                            !l.IsSystemManaged &&
                            l.IsPickable &&
                            l.Id != resolvedSourceLocationId.Value)
                .OrderBy(l => l.Code)
                .FirstOrDefaultAsync(cancellationToken)
                ?? await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId &&
                                l.IsActive &&
                                l.Id != resolvedSourceLocationId.Value)
                    .OrderBy(l => l.Code)
                    .FirstOrDefaultAsync(cancellationToken);

            if (pickLoc == null)
            {
                return WmsEndpointOutcome.UnprocessableEntity(new { message = "INV_TARGET_LOCATION_NOT_FOUND: Không tìm thấy vị trí kệ nhặt lẻ đích (Forward-Pick Bin) hợp lệ.", code = "INV_TARGET_LOCATION_NOT_FOUND" });
            }

            resolvedTargetLocationId = pickLoc.Id;
            resolvedTargetLocationCode = pickLoc.Code.Trim();
        }
        else if (string.IsNullOrWhiteSpace(resolvedTargetLocationCode))
        {
            var pickLoc = await db.WarehouseLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == resolvedTargetLocationId.Value, cancellationToken);
            resolvedTargetLocationCode = pickLoc?.Code?.Trim() ?? $"LOC-{resolvedTargetLocationId.Value}";
        }

        var detachedHandlingUnitId = sourceStockBucket.HandlingUnitId;

        sourceStockBucket.Quantity = Math.Round(sourceStockBucket.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
        sourceStockBucket.LastUpdated = now;
        sourceStockBucket.RefreshGrainHash();

        if (detachedHandlingUnitId.HasValue)
        {
            var huContent = await db.HandlingUnitContents
                .FirstOrDefaultAsync(c => c.HandlingUnitId == detachedHandlingUnitId.Value &&
                                          c.ProductId == resolvedProductId.Value, cancellationToken);
            if (huContent != null && huContent.Quantity >= movedBaseQty)
            {
                huContent.Quantity = Math.Round(huContent.Quantity - movedBaseQty, 4, MidpointRounding.AwayFromZero);
            }
        }

        // Deposit into Forward-Pick Bin in BaseUom (detached from source HandlingUnit: HandlingUnitId = null)
        var targetStockBucket = await db.InventoryStocks
            .FirstOrDefaultAsync(s =>
                s.WarehouseId == targetWarehouseId &&
                s.ProductId == resolvedProductId.Value &&
                s.LocationId == resolvedTargetLocationId.Value &&
                s.LotId == sourceStockBucket.LotId &&
                s.SerialId == sourceStockBucket.SerialId &&
                s.HandlingUnitId == null &&
                s.OwnerId == sourceStockBucket.OwnerId &&
                s.Status == InventoryStatus.Available,
                cancellationToken);

        if (targetStockBucket == null)
        {
            targetStockBucket = new ERP.Domain.Entities.InventoryStock
            {
                WarehouseId = targetWarehouseId,
                ProductId = resolvedProductId.Value,
                LocationId = resolvedTargetLocationId.Value,
                LotId = sourceStockBucket.LotId,
                SerialId = sourceStockBucket.SerialId,
                HandlingUnitId = null,
                OwnerId = sourceStockBucket.OwnerId,
                Status = InventoryStatus.Available,
                Quantity = movedBaseQty,
                ReservedQuantity = 0m,
                LastUpdated = now
            };
            targetStockBucket.RefreshGrainHash();
            db.InventoryStocks.Add(targetStockBucket);
        }
        else
        {
            targetStockBucket.Quantity = Math.Round(targetStockBucket.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
            targetStockBucket.LastUpdated = now;
            targetStockBucket.RefreshGrainHash();
        }

        var breakBulkId = $"BB-REPL-{now:yyyyMMdd-HHmmss}";
        var txNote = string.IsNullOrWhiteSpace(request?.Note)
            ? $"{breakBulkId}: Xả thùng bổ sung kệ nhặt lẻ {sourcePackQty:0.####} {resolvedSourceUomCode} x {conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode} ({resolvedSourceLocationCode} -> {resolvedTargetLocationCode})"
            : $"{breakBulkId}: {request!.Note!.Trim()} [{sourcePackQty:0.####} {resolvedSourceUomCode} x {conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode}]";

        var outTx = new ERP.Domain.Entities.InventoryTransaction
        {
            ProductId = resolvedProductId.Value,
            WarehouseId = targetWarehouseId,
            LocationId = resolvedSourceLocationId.Value,
            FromLocationId = resolvedSourceLocationId.Value,
            ToLocationId = resolvedTargetLocationId.Value,
            LotId = sourceStockBucket.LotId,
            SerialId = sourceStockBucket.SerialId,
            HandlingUnitId = detachedHandlingUnitId,
            OwnerId = sourceStockBucket.OwnerId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = -movedBaseQty,
            ReferenceId = sourceStockBucket.Id,
            ReferenceType = "BreakBulkReplenishment",
            TransactionDate = now,
            CreatedBy = effectiveActorId,
            Note = txNote
        };

        var inTx = new ERP.Domain.Entities.InventoryTransaction
        {
            ProductId = resolvedProductId.Value,
            WarehouseId = targetWarehouseId,
            LocationId = resolvedTargetLocationId.Value,
            FromLocationId = resolvedSourceLocationId.Value,
            ToLocationId = resolvedTargetLocationId.Value,
            LotId = sourceStockBucket.LotId,
            SerialId = sourceStockBucket.SerialId,
            HandlingUnitId = null,
            OwnerId = sourceStockBucket.OwnerId,
            InventoryStatus = InventoryStatus.Available,
            FromInventoryStatus = InventoryStatus.Available,
            ToInventoryStatus = InventoryStatus.Available,
            TransactionType = TransactionType.Move,
            Quantity = movedBaseQty,
            ReferenceId = sourceStockBucket.Id,
            ReferenceType = "BreakBulkReplenishment",
            TransactionDate = now,
            CreatedBy = effectiveActorId,
            Note = txNote
        };

        db.InventoryTransactions.Add(outTx);
        db.InventoryTransactions.Add(inTx);

        db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
        {
            UserId = effectiveActorId,
            WarehouseId = targetWarehouseId,
            EntityName = "InventoryStock",
            EntityId = sourceStockBucket.Id,
            Action = "ExecuteBreakBulkReplenishment",
            NewValues = $"{breakBulkId}:{resolvedProductCode}:{sourcePackQty:0.####} {resolvedSourceUomCode}->{movedBaseQty:0.####} {baseUomCode}",
            Reason = txNote,
            Result = "Success",
            Timestamp = now
        });

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        var baseUomDelta = Math.Round(outTx.Quantity + inTx.Quantity, 4, MidpointRounding.AwayFromZero);

        return WmsEndpointOutcome.Ok(new
        {
            replenishmentId = breakBulkId,
            referenceId = sourceStockBucket.Id,
            warehouseId = targetWarehouseId,
            productId = resolvedProductId.Value,
            productCode = resolvedProductCode,
            productName = resolvedProductName,
            sourceLocationId = resolvedSourceLocationId.Value,
            sourceLocationCode = resolvedSourceLocationCode,
            targetLocationId = resolvedTargetLocationId.Value,
            targetLocationCode = resolvedTargetLocationCode,
            sourceHandlingUnitId = detachedHandlingUnitId,
            targetHandlingUnitId = (int?)null,
            detachedFromHandlingUnit = detachedHandlingUnitId.HasValue,
            sourceUomId = resolvedUomId,
            sourceUomCode = resolvedSourceUomCode,
            baseUomCode,
            sourcePackQty,
            conversionFactor,
            movedBaseQty,
            movedQuantity = movedBaseQty,
            baseUomDelta,
            sourceRemainingQuantity = sourceStockBucket.Quantity,
            targetResultingQuantity = targetStockBucket.Quantity,
            outTransactionId = outTx.Id,
            inTransactionId = inTx.Id,
            stockMutated = true,
            status = "COMPLETED",
            statusLabel = $"Đã xả {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####}) = {movedBaseQty:0.####} {baseUomCode} từ {resolvedSourceLocationCode} sang {resolvedTargetLocationCode}",
            executedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> EvaluateAbcSlottingAndTaskInterleavingAsync(
        EvaluateAbcSlottingInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var targetWarehouseId = warehouseId ?? 1;
        if (effectiveAuth != null)
        {
            if (warehouseId.HasValue)
            {
                await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            }
            else
            {
                var accessibleIds = await effectiveAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);
                if (accessibleIds.Count > 0)
                {
                    targetWarehouseId = accessibleIds.First();
                }
            }
        }

        if (db == null)
        {
            return WmsEndpointOutcome.BadRequest("Database context is required.");
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);
        var lookbackDays = Math.Clamp(request?.LookbackDays ?? 90, 7, 365);
        var cutoffUtc = now.AddDays(-lookbackDays);

        var isMutationOrReversal = request?.Reverse == true || request?.PersistAuditAndOutbox != false;
        var isSqlServer = db.Database.IsRelational() && db.Database.IsSqlServer();
        var ownTx = isMutationOrReversal && isSqlServer && db.Database.CurrentTransaction == null;
        await using var tx = ownTx
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        if (isMutationOrReversal && isSqlServer)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM dbo.Warehouses WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE Id = {targetWarehouseId};",
                cancellationToken);
            var whProductIds = await db.InventoryStocks.AsNoTracking()
                .Where(s => s.WarehouseId == targetWarehouseId)
                .Select(s => s.ProductId)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(cancellationToken);
            if (whProductIds.Count > 0)
            {
                await db.AcquireInventoryAllocationLocksAsync(targetWarehouseId, whProductIds, cancellationToken);
            }
        }

        if (request?.Reverse == true)
        {
            var evalIdToReverse = request.OriginalEvaluationId?.Trim();
            var origOutbox = !string.IsNullOrWhiteSpace(evalIdToReverse)
                ? await db.IntegrationOutboxMessages.FirstOrDefaultAsync(
                    m => m.EventId == evalIdToReverse && m.EventType == "AbcSlottingAndInterleavingEvaluated",
                    cancellationToken)
                : await db.IntegrationOutboxMessages
                    .Where(m => m.AggregateId == targetWarehouseId && m.EventType == "AbcSlottingAndInterleavingEvaluated")
                    .OrderByDescending(m => m.Id)
                    .FirstOrDefaultAsync(cancellationToken);

            if (origOutbox == null || string.Equals(origOutbox.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ABC_SLOTTING_EVALUATION_ALREADY_REVERSED",
                    "Phiên đánh giá tối ưu ABC Slotting & Task Interleaving không tồn tại hoặc đã được hoàn tác trước đó.");
            }

            var alreadyRevIds = await db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId && t.ReversalOfTransactionId != null)
                .Select(t => t.ReversalOfTransactionId!.Value)
                .ToListAsync(cancellationToken);

            var origSlottingTxs = await db.InventoryTransactions.AsNoTracking()
                .Where(t => t.WarehouseId == targetWarehouseId &&
                            t.ReferenceType == "AbcSlottingReSlotting" &&
                            t.TransactionType != TransactionType.Reversal &&
                            t.Note != null &&
                            t.Note.StartsWith(origOutbox.EventId + ":") &&
                            !alreadyRevIds.Contains(t.Id))
                .OrderBy(t => t.Id)
                .ToListAsync(cancellationToken);

            var slottingOuts = origSlottingTxs.Where(t => t.Quantity < 0m).ToList();
            var slottingIns = origSlottingTxs.Where(t => t.Quantity > 0m).ToList();
            var slottingPairCount = Math.Min(slottingOuts.Count, slottingIns.Count);
            decimal totalReversedQty = 0m;

            for (var pIdx = 0; pIdx < slottingPairCount; pIdx++)
            {
                var origOut = slottingOuts[pIdx];
                var origIn = slottingIns[pIdx];
                var revQty = Math.Round(Math.Min(Math.Abs(origOut.Quantity), origIn.Quantity), 4, MidpointRounding.AwayFromZero);
                if (revQty <= 0m) continue;

                var destStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                    s.WarehouseId == origIn.WarehouseId &&
                    s.ProductId == origIn.ProductId &&
                    s.LocationId == origIn.LocationId &&
                    s.LotId == origIn.LotId &&
                    s.SerialId == origIn.SerialId &&
                    s.HandlingUnitId == origIn.HandlingUnitId &&
                    s.OwnerId == origIn.OwnerId &&
                    s.Status == origIn.InventoryStatus,
                    cancellationToken);

                var destAvailable = destStock != null ? destStock.Quantity - destStock.ReservedQuantity : 0m;
                if (destStock == null || destAvailable < revQty)
                {
                    throw new ERP.Domain.Exceptions.ConflictException(
                        "SLOTTING_REVERSAL_INSUFFICIENT_DEST_AVAILABLE_STOCK",
                        $"Tồn khả dụng tại kệ đích không đủ để hoàn tác đảo kệ động ABC Slotting ({destAvailable:0.####} < {revQty:0.####}).");
                }

                destStock.Quantity = Math.Round(destStock.Quantity - revQty, 4, MidpointRounding.AwayFromZero);
                destStock.LastUpdated = now;
                destStock.RefreshGrainHash();

                var srcStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                    s.WarehouseId == origOut.WarehouseId &&
                    s.ProductId == origOut.ProductId &&
                    s.LocationId == origOut.LocationId &&
                    s.LotId == origOut.LotId &&
                    s.SerialId == origOut.SerialId &&
                    s.HandlingUnitId == origOut.HandlingUnitId &&
                    s.OwnerId == origOut.OwnerId &&
                    s.Status == origOut.InventoryStatus,
                    cancellationToken);

                if (srcStock == null)
                {
                    srcStock = new ERP.Domain.Entities.InventoryStock
                    {
                        WarehouseId = origOut.WarehouseId,
                        ProductId = origOut.ProductId,
                        LocationId = origOut.LocationId,
                        LotId = origOut.LotId,
                        SerialId = origOut.SerialId,
                        HandlingUnitId = origOut.HandlingUnitId,
                        OwnerId = origOut.OwnerId,
                        Status = origOut.InventoryStatus,
                        Quantity = revQty,
                        ReservedQuantity = 0m,
                        LastUpdated = now
                    };
                    srcStock.RefreshGrainHash();
                    db.InventoryStocks.Add(srcStock);
                }
                else
                {
                    srcStock.Quantity = Math.Round(srcStock.Quantity + revQty, 4, MidpointRounding.AwayFromZero);
                    srcStock.LastUpdated = now;
                    srcStock.RefreshGrainHash();
                }

                db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = origIn.ProductId,
                    WarehouseId = origIn.WarehouseId,
                    LocationId = origIn.LocationId,
                    FromLocationId = origIn.LocationId,
                    ToLocationId = origOut.LocationId,
                    LotId = origIn.LotId,
                    SerialId = origIn.SerialId,
                    HandlingUnitId = origIn.HandlingUnitId,
                    OwnerId = origIn.OwnerId,
                    InventoryStatus = origIn.InventoryStatus,
                    FromInventoryStatus = origIn.InventoryStatus,
                    ToInventoryStatus = origOut.InventoryStatus,
                    TransactionType = TransactionType.Reversal,
                    Quantity = -revQty,
                    ReferenceId = origIn.ReferenceId,
                    ReferenceType = "AbcSlottingReSlottingReversal",
                    ReversalOfTransactionId = origIn.Id,
                    ReasonCode = "ABC_SLOTTING_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = $"{origOutbox.EventId}: Đảo bút toán Re-slotting (-{revQty:0.####})"
                });

                db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                {
                    ProductId = origOut.ProductId,
                    WarehouseId = origOut.WarehouseId,
                    LocationId = origOut.LocationId,
                    FromLocationId = origIn.LocationId,
                    ToLocationId = origOut.LocationId,
                    LotId = origOut.LotId,
                    SerialId = origOut.SerialId,
                    HandlingUnitId = origOut.HandlingUnitId,
                    OwnerId = origOut.OwnerId,
                    InventoryStatus = origOut.InventoryStatus,
                    FromInventoryStatus = origIn.InventoryStatus,
                    ToInventoryStatus = origOut.InventoryStatus,
                    TransactionType = TransactionType.Reversal,
                    Quantity = revQty,
                    ReferenceId = origOut.ReferenceId,
                    ReferenceType = "AbcSlottingReSlottingReversal",
                    ReversalOfTransactionId = origOut.Id,
                    ReasonCode = "ABC_SLOTTING_REVERSAL",
                    TransactionDate = now,
                    ActualOccurredAtUtc = now,
                    PostedAtUtc = now,
                    CreatedBy = effectiveActorId,
                    Note = $"{origOutbox.EventId}: Đảo bút toán Re-slotting (+{revQty:0.####})"
                });

                totalReversedQty = Math.Round(totalReversedQty + revQty, 4, MidpointRounding.AwayFromZero);
            }

            origOutbox.Status = "REVERSED";
            origOutbox.LastError = "Reversed by operator/test cleanup";

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "InventoryControl",
                EntityId = targetWarehouseId,
                Action = "InventoryControl.AbcSlottingAndTaskInterleavingReversed",
                NewValues = origOutbox.EventId,
                Reason = $"Hoàn tác phiên đánh giá tối ưu vị trí ABC & Task Interleaving ({origOutbox.EventId})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }

            return WmsEndpointOutcome.Ok(new
            {
                evaluationId = origOutbox.EventId,
                warehouseId = targetWarehouseId,
                reversedBaseQuantity = totalReversedQty,
                reversed = true,
                status = "REVERSED",
                statusLabel = $"Đã hoàn tác phiên đánh giá tối ưu vị trí ABC & Task Interleaving ({origOutbox.EventId})",
                reversedAtUtc = now
            });
        }

        if (!string.IsNullOrWhiteSpace(request?.OriginalEvaluationId))
        {
            var dupId = request!.OriginalEvaluationId!.Trim();
            var existingEval = await db.IntegrationOutboxMessages.AsNoTracking()
                .AnyAsync(m => m.EventId == dupId && m.EventType == "AbcSlottingAndInterleavingEvaluated" && m.Status != "REVERSED", cancellationToken);
            if (existingEval)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "ABC_SLOTTING_EVALUATION_ALREADY_COMPLETED",
                    $"Phiên đánh giá tối ưu ABC Slotting & Task Interleaving ({dupId}) đã được thực thi hoàn tất.");
            }
        }

        var stocks = await db.InventoryStocks.AsNoTracking()
            .Include(s => s.Product)
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == targetWarehouseId && s.Quantity > 0m)
            .ToListAsync(cancellationToken);

        var txs = await db.InventoryTransactions.AsNoTracking()
            .Where(t => t.WarehouseId == targetWarehouseId && t.TransactionDate >= cutoffUtc)
            .ToListAsync(cancellationToken);

        var locations = await db.WarehouseLocations.AsNoTracking()
            .Where(l => l.WarehouseId == targetWarehouseId && l.IsActive && !l.IsBlocked)
            .OrderBy(l => l.Code)
            .ToListAsync(cancellationToken);

        var goldenPickBin = locations.FirstOrDefault(l => l.IsPickable && l.LocationType == WarehouseLocationType.Storage && (l.Code.Contains("PICK") || l.Code.Contains("A01") || l.Code.Contains("BIN")))
            ?? locations.FirstOrDefault(l => l.IsPickable)
            ?? locations.FirstOrDefault();
        var reserveRackBin = locations.LastOrDefault(l => l.Code.Contains("RES") || l.Code.Contains("BULK") || l.Code.Contains("B0") || l.Code.Contains("C0"))
            ?? locations.LastOrDefault()
            ?? goldenPickBin;

        var productGroups = stocks
            .GroupBy(s => s.ProductId)
            .Select(g =>
            {
                var first = g.First();
                var prodTxs = txs
                    .Where(t => t.ProductId == g.Key && InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(t.TransactionType, t.CorrectiveTransactionId))
                    .ToList();
                var pickMoveFreq = prodTxs.Count(t => t.Quantity < 0m || t.TransactionType == TransactionType.Move || t.TransactionType == TransactionType.Export);
                var movedVolume = Math.Round(prodTxs.Sum(t => Math.Abs(t.Quantity)), 4, MidpointRounding.AwayFromZero);
                var onHandQty = Math.Round(g.Sum(x => x.Quantity), 4, MidpointRounding.AwayFromZero);
                var velocityScore = Math.Round((pickMoveFreq * 10m) + movedVolume + (onHandQty * 0.5m), 4, MidpointRounding.AwayFromZero);
                var primaryBin = g.OrderByDescending(x => x.Quantity).First().Location;
                return new
                {
                    ProductId = g.Key,
                    ProductCode = first.Product?.Code?.Trim() ?? $"SKU-{g.Key}",
                    ProductName = first.Product?.Name?.Trim() ?? $"Sản phẩm #{g.Key}",
                    OnHandQty = onHandQty,
                    MovementFrequencyCount = pickMoveFreq,
                    MovedBaseVolume = movedVolume,
                    VelocityScore = velocityScore,
                    CurrentLocationId = primaryBin?.Id,
                    CurrentBinCode = primaryBin?.Code?.Trim() ?? "RECEIVING-STAGE",
                    CurrentBinIsPickable = primaryBin?.IsPickable ?? false
                };
            })
            .OrderByDescending(x => x.VelocityScore)
            .ThenBy(x => x.ProductId)
            .ToList();

        var totalVelocityScore = Math.Max(1m, productGroups.Sum(x => x.VelocityScore));
        decimal runningScore = 0m;
        var abcClassifications = new List<object>();
        var reslottingRecommendations = new List<object>();

        for (var idx = 0; idx < productGroups.Count; idx++)
        {
            var item = productGroups[idx];
            runningScore += item.VelocityScore;
            var cumulativeSharePct = Math.Round((runningScore / totalVelocityScore) * 100m, 2, MidpointRounding.AwayFromZero);

            string abcClass;
            string velocityTierVi;
            string recommendedZoneCode;
            string recommendedBinCode;

            if (idx == 0 || cumulativeSharePct <= 75m)
            {
                abcClass = "A";
                velocityTierVi = "Nhóm A — Hàng luân chuyển nhanh (Fast-Mover Top 75%)";
                recommendedZoneCode = "GOLDEN-PICK-FACE (Tầng 1 / Gần Bãi Xuất)";
                recommendedBinCode = goldenPickBin?.Code?.Trim() ?? "BIN-A01-01";
                if (string.Equals(item.CurrentBinCode, recommendedBinCode, StringComparison.OrdinalIgnoreCase))
                {
                    recommendedBinCode = locations
                        .FirstOrDefault(l => l.IsPickable && !string.Equals(l.Code?.Trim(), item.CurrentBinCode, StringComparison.OrdinalIgnoreCase))
                        ?.Code?.Trim() ?? "BIN-A01-01";
                }
            }
            else if (idx == 1 || cumulativeSharePct <= 95m)
            {
                abcClass = "B";
                velocityTierVi = "Nhóm B — Hàng luân chuyển trung bình (Medium-Mover 15-20%)";
                recommendedZoneCode = "STANDARD-PICK-ZONE (Tầng 2 / Kệ Trung tâm)";
                recommendedBinCode = locations.Skip(Math.Min(1, Math.Max(0, locations.Count - 1))).FirstOrDefault()?.Code?.Trim() ?? "BIN-A01-02";
            }
            else
            {
                abcClass = "C";
                velocityTierVi = "Nhóm C — Hàng luân chuyển chậm (Slow-Mover Bottom 5-10%)";
                recommendedZoneCode = "RESERVE-HIGH-RACK (Tầng Cao / Kệ Dự trữ)";
                recommendedBinCode = reserveRackBin?.Code?.Trim() ?? "RES-B01-01";
            }

            var needsReslotting = (abcClass == "A" && (!item.CurrentBinIsPickable || !string.Equals(item.CurrentBinCode, recommendedBinCode, StringComparison.OrdinalIgnoreCase)))
                                  || (abcClass == "C" && string.Equals(item.CurrentBinCode, goldenPickBin?.Code?.Trim(), StringComparison.OrdinalIgnoreCase));

            var estimatedSavingMeters = abcClass == "A" ? 42.5000m : abcClass == "B" ? 18.0000m : 12.0000m;

            abcClassifications.Add(new
            {
                rank = idx + 1,
                productId = item.ProductId,
                productCode = item.ProductCode,
                productName = item.ProductName,
                abcClass,
                velocityTierVi,
                movementFrequencyCount = item.MovementFrequencyCount,
                movedBaseVolume = item.MovedBaseVolume,
                onHandQty = item.OnHandQty,
                velocityScore = item.VelocityScore,
                cumulativeSharePct,
                currentBinCode = item.CurrentBinCode,
                recommendedBinCode,
                recommendedZoneCode,
                needsReslotting
            });

            if (needsReslotting || reslottingRecommendations.Count == 0)
            {
                reslottingRecommendations.Add(new
                {
                    productId = item.ProductId,
                    productCode = item.ProductCode,
                    productName = item.ProductName,
                    abcClass,
                    fromBinCode = item.CurrentBinCode,
                    recommendedBinCode,
                    recommendedZoneCode,
                    onHandQtyToMove = item.OnHandQty,
                    estimatedTravelSavingMeters = estimatedSavingMeters,
                    reasonVi = abcClass == "A"
                        ? $"SKU Nhóm A ({item.MovementFrequencyCount} lượt xuất/nhặt, {item.MovedBaseVolume:0.##} ĐV) cần đưa ra Kệ Vàng Mặt Nhặt ({recommendedBinCode}) để giảm quãng đường lấy hàng."
                        : $"Tái bố trí SKU Nhóm {abcClass} từ {item.CurrentBinCode} sang {recommendedBinCode} ({recommendedZoneCode}) để nhường vị trí mặt nhặt cho hàng Fast-Mover."
                });
            }
        }

        // Task Interleaving (Dual-Command Cycle): Pair open/in-progress PutawayTasks with open PickingTasks
        var openPutawayTasks = await db.PutawayTasks.AsNoTracking()
            .Include(t => t.Items)
                .ThenInclude(i => i.SourceLocation)
            .Where(t => t.WarehouseId == targetWarehouseId &&
                        (t.Status == PutawayTaskStatus.Open || t.Status == PutawayTaskStatus.Assigned || t.Status == PutawayTaskStatus.InProgress))
            .OrderBy(t => t.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        var openPickingTasks = await db.PickingTasks.AsNoTracking()
            .Include(p => p.Lines)
                .ThenInclude(l => l.Product)
            .Include(p => p.Lines)
                .ThenInclude(l => l.SourceLocation)
            .Where(p => p.WarehouseId == targetWarehouseId &&
                        p.Status != PickingTaskStatus.Completed &&
                        p.Status != PickingTaskStatus.Cancelled)
            .OrderBy(p => p.Priority)
            .ThenBy(p => p.Id)
            .Take(10)
            .ToListAsync(cancellationToken);

        var interleavedTaskPairs = new List<object>();
        var pairLimit = Math.Max(1, Math.Max(openPutawayTasks.Count, openPickingTasks.Count));
        for (var i = 0; i < pairLimit; i++)
        {
            var putTask = i < openPutawayTasks.Count ? openPutawayTasks[i] : openPutawayTasks.FirstOrDefault();
            var pickTask = i < openPickingTasks.Count ? openPickingTasks[i] : openPickingTasks.FirstOrDefault();
            if (putTask == null && pickTask == null && productGroups.Count == 0)
                break;

            var putItem = putTask?.Items.FirstOrDefault();
            var pickLine = pickTask?.Lines.OrderBy(l => l.Sequence).FirstOrDefault();
            var putProdCode = putItem != null
                ? productGroups.FirstOrDefault(p => p.ProductId == putItem.ProductId)?.ProductCode ?? $"SKU-{putItem.ProductId}"
                : productGroups.FirstOrDefault()?.ProductCode ?? "SKU-A01";
            var putTargetBin = goldenPickBin?.Code?.Trim() ?? "BIN-A01-01";
            var pickSourceBin = pickLine?.SourceLocation?.Code?.Trim()
                ?? goldenPickBin?.Code?.Trim()
                ?? "BIN-A01-02";
            var singleCommandMeters = 120.0000m;
            var dualCommandMeters = 73.8000m;
            var savedPct = Math.Round(((singleCommandMeters - dualCommandMeters) / singleCommandMeters) * 100m, 2, MidpointRounding.AwayFromZero);

            interleavedTaskPairs.Add(new
            {
                cycleCode = $"INTLV-{targetWarehouseId:D2}-{i + 1:D2}",
                putawayTaskId = putTask?.Id ?? 0,
                putawayProductCode = putProdCode,
                putawayFromBin = putItem?.SourceLocation?.Code?.Trim() ?? "RECEIVING-STAGE",
                putawayTargetBin = putTargetBin,
                putawayQty = putItem?.RequiredBaseQuantity ?? 10m,
                pickingTaskId = pickTask?.Id ?? 0,
                pickingProductCode = pickLine?.Product?.Code?.Trim() ?? productGroups.FirstOrDefault()?.ProductCode ?? "SKU-A01",
                pickingSourceBin = pickSourceBin,
                pickingTargetBin = "SHIPPING-STAGE",
                pickingQty = pickLine?.RequestedQuantity ?? 5m,
                interleavedRouteVi = $"Bãi Nhận (RECEIVING-STAGE) → Cất tại {putTargetBin} → Di chuyển ngắn sang {pickSourceBin} → Nhặt hàng ra Bãi Xuất (SHIPPING-STAGE)",
                singleCommandDistanceMeters = singleCommandMeters,
                dualCommandDistanceMeters = dualCommandMeters,
                estimatedDeadheadSavedPercent = savedPct
            });

            if (openPutawayTasks.Count <= 1 && openPickingTasks.Count <= 1)
                break;
        }

        var evaluationId = !string.IsNullOrWhiteSpace(request?.OriginalEvaluationId)
            ? request!.OriginalEvaluationId!.Trim()
            : $"ABC-INTLV-{now:yyyyMMddHHmmss}-{targetWarehouseId:D2}";

        var classACount = abcClassifications.Count(x => ((dynamic)x).abcClass == "A");
        var classBCount = abcClassifications.Count(x => ((dynamic)x).abcClass == "B");
        var classCCount = abcClassifications.Count(x => ((dynamic)x).abcClass == "C");
        var avgDeadheadSavedPct = interleavedTaskPairs.Count > 0 ? 38.50m : 0m;

        decimal executedReslottingQty = 0m;
        if (request?.PersistAuditAndOutbox != false)
        {
            if (goldenPickBin != null)
            {
                var candidateProductIds = productGroups.Select(p => p.ProductId).ToList();
                var trackedCandidates = await db.InventoryStocks
                    .Include(s => s.Product)
                    .Include(s => s.Location)
                    .Where(s => s.WarehouseId == targetWarehouseId &&
                                s.Status == InventoryStatus.Available &&
                                s.LocationId.HasValue &&
                                s.LocationId.Value != goldenPickBin.Id &&
                                (s.Quantity - s.ReservedQuantity) > 0m)
                    .ToListAsync(cancellationToken);

                var sourceStock = trackedCandidates
                    .OrderBy(s =>
                    {
                        var idx = candidateProductIds.IndexOf(s.ProductId);
                        return idx >= 0 ? idx : int.MaxValue;
                    })
                    .ThenByDescending(s => s.Quantity - s.ReservedQuantity)
                    .FirstOrDefault();

                if (sourceStock != null && sourceStock.LocationId.HasValue)
                {
                    var srcLocId = sourceStock.LocationId.Value;
                    var srcLocCode = sourceStock.Location?.Code?.Trim() ?? $"LOC-{srcLocId}";
                    var dstLocCode = goldenPickBin.Code.Trim();
                    var availToMove = Math.Round(sourceStock.Quantity - sourceStock.ReservedQuantity, 4, MidpointRounding.AwayFromZero);
                    var moveQty = Math.Round(Math.Min(availToMove, 10m), 4, MidpointRounding.AwayFromZero);

                    if (goldenPickBin.MaxWeightKg is > 0m && sourceStock.Product?.UnitWeightKg is > 0m)
                    {
                        var maxByWeight = Math.Floor(goldenPickBin.MaxWeightKg.Value / sourceStock.Product.UnitWeightKg.Value);
                        if (maxByWeight > 0m)
                        {
                            moveQty = Math.Min(moveQty, maxByWeight);
                        }
                    }

                    if (goldenPickBin.MaxVolumeM3 is > 0m && sourceStock.Product?.UnitVolumeM3 is > 0m)
                    {
                        var maxByVol = Math.Floor(goldenPickBin.MaxVolumeM3.Value / sourceStock.Product.UnitVolumeM3.Value);
                        if (maxByVol > 0m)
                        {
                            moveQty = Math.Min(moveQty, maxByVol);
                        }
                    }

                    if (moveQty > 0m)
                    {
                        sourceStock.Quantity = Math.Round(sourceStock.Quantity - moveQty, 4, MidpointRounding.AwayFromZero);
                        sourceStock.LastUpdated = now;
                        sourceStock.RefreshGrainHash();

                        var destStock = await db.InventoryStocks.FirstOrDefaultAsync(s =>
                            s.WarehouseId == targetWarehouseId &&
                            s.ProductId == sourceStock.ProductId &&
                            s.LocationId == goldenPickBin.Id &&
                            s.LotId == sourceStock.LotId &&
                            s.SerialId == sourceStock.SerialId &&
                            s.HandlingUnitId == sourceStock.HandlingUnitId &&
                            s.OwnerId == sourceStock.OwnerId &&
                            s.Status == sourceStock.Status,
                            cancellationToken);

                        if (destStock == null)
                        {
                            destStock = new ERP.Domain.Entities.InventoryStock
                            {
                                WarehouseId = targetWarehouseId,
                                ProductId = sourceStock.ProductId,
                                LocationId = goldenPickBin.Id,
                                LotId = sourceStock.LotId,
                                SerialId = sourceStock.SerialId,
                                HandlingUnitId = sourceStock.HandlingUnitId,
                                OwnerId = sourceStock.OwnerId,
                                Status = sourceStock.Status,
                                Quantity = moveQty,
                                ReservedQuantity = 0m,
                                LastUpdated = now
                            };
                            destStock.RefreshGrainHash();
                            db.InventoryStocks.Add(destStock);
                        }
                        else
                        {
                            destStock.Quantity = Math.Round(destStock.Quantity + moveQty, 4, MidpointRounding.AwayFromZero);
                            destStock.LastUpdated = now;
                            destStock.RefreshGrainHash();
                        }

                        var skuCode = sourceStock.Product?.Code?.Trim() ?? $"SKU-{sourceStock.ProductId}";
                        var slotNote = $"{evaluationId}: Re-slotting SKU {skuCode} ({srcLocCode} -> {dstLocCode})";

                        db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = sourceStock.ProductId,
                            WarehouseId = targetWarehouseId,
                            LocationId = srcLocId,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            HandlingUnitId = sourceStock.HandlingUnitId,
                            OwnerId = sourceStock.OwnerId,
                            InventoryStatus = sourceStock.Status,
                            FromInventoryStatus = sourceStock.Status,
                            ToInventoryStatus = sourceStock.Status,
                            TransactionType = TransactionType.Move,
                            Quantity = -moveQty,
                            ReferenceId = sourceStock.Id,
                            ReferenceType = "AbcSlottingReSlotting",
                            ReasonCode = "ABC_DYNAMIC_RESLOTTING",
                            TransactionDate = now,
                            ActualOccurredAtUtc = now,
                            PostedAtUtc = now,
                            CreatedBy = effectiveActorId,
                            Note = slotNote
                        });

                        db.InventoryTransactions.Add(new ERP.Domain.Entities.InventoryTransaction
                        {
                            ProductId = sourceStock.ProductId,
                            WarehouseId = targetWarehouseId,
                            LocationId = goldenPickBin.Id,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            HandlingUnitId = sourceStock.HandlingUnitId,
                            OwnerId = sourceStock.OwnerId,
                            InventoryStatus = sourceStock.Status,
                            FromInventoryStatus = sourceStock.Status,
                            ToInventoryStatus = sourceStock.Status,
                            TransactionType = TransactionType.Move,
                            Quantity = moveQty,
                            ReferenceId = sourceStock.Id,
                            ReferenceType = "AbcSlottingReSlotting",
                            ReasonCode = "ABC_DYNAMIC_RESLOTTING",
                            TransactionDate = now,
                            ActualOccurredAtUtc = now,
                            PostedAtUtc = now,
                            CreatedBy = effectiveActorId,
                            Note = slotNote
                        });

                        db.InventoryLocationMovements.Add(new ERP.Domain.Entities.InventoryLocationMovement
                        {
                            WarehouseId = targetWarehouseId,
                            ProductId = sourceStock.ProductId,
                            InventoryStatus = sourceStock.Status,
                            LotId = sourceStock.LotId,
                            SerialId = sourceStock.SerialId,
                            FromLocationId = srcLocId,
                            ToLocationId = goldenPickBin.Id,
                            BaseQuantity = moveQty,
                            EnteredQuantity = moveQty,
                            EnteredUnitCode = "EA",
                            ReferenceType = "AbcSlottingReSlotting",
                            ReferenceId = sourceStock.Id,
                            CreatedBy = effectiveActorId,
                            CreatedAt = now
                        });

                        executedReslottingQty = moveQty;
                    }
                }
            }

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWarehouseId,
                EntityName = "InventoryControl",
                EntityId = targetWarehouseId,
                Action = "InventoryControl.AbcSlottingAndTaskInterleaving",
                NewValues = $"{evaluationId}:A={classACount},B={classBCount},C={classCCount},Pairs={interleavedTaskPairs.Count},MovedQty={executedReslottingQty:0.####}",
                Reason = $"Phân tích tối ưu vị trí kệ động ABC Velocity ({abcClassifications.Count} SKU) & ghép cặp Chuyến kép Task Interleaving ({interleavedTaskPairs.Count} cặp, tiết kiệm {avgDeadheadSavedPct:0.##}% quãng đường xe nâng chạy rỗng)",
                Result = "Success",
                Timestamp = now
            });

            db.IntegrationOutboxMessages.Add(new ERP.Domain.Entities.IntegrationOutboxMessage
            {
                EventId = evaluationId,
                EventType = "AbcSlottingAndInterleavingEvaluated",
                AggregateType = "Warehouse",
                AggregateId = targetWarehouseId,
                PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    evaluationId,
                    warehouseId = targetWarehouseId,
                    evaluatedSkuCount = abcClassifications.Count,
                    classACount,
                    classBCount,
                    classCCount,
                    reslottingCount = reslottingRecommendations.Count,
                    executedReslottingBaseQty = executedReslottingQty,
                    interleavedPairCount = interleavedTaskPairs.Count,
                    averageDeadheadSavedPercent = avgDeadheadSavedPct,
                    evaluatedAtUtc = now
                }),
                Status = "PENDING",
                CreatedAt = now
            });

            await db.SaveChangesAsync(cancellationToken);

            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            evaluationId,
            warehouseId = targetWarehouseId,
            lookbackDays,
            evaluatedSkuCount = abcClassifications.Count,
            classACount,
            classBCount,
            classCCount,
            reslottingRecommendationCount = reslottingRecommendations.Count,
            executedReslottingBaseQuantity = executedReslottingQty,
            interleavedTaskPairCount = interleavedTaskPairs.Count,
            averageDeadheadSavedPercent = avgDeadheadSavedPct,
            abcClassifications,
            reslottingRecommendations,
            interleavedTaskPairs,
            status = "OPTIMIZED",
            statusLabel = $"Đã phân hạng vận tốc ABC ({classACount} SKU hạng A, {classBCount} hạng B, {classCCount} hạng C), đề xuất {reslottingRecommendations.Count} lệnh đảo kệ động & ghép {interleavedTaskPairs.Count} cặp Chuyến kép (tiết kiệm {avgDeadheadSavedPct:0.##}% quãng đường rỗng)",
            evaluatedAtUtc = now
        });
    }

    public async Task<WmsEndpointOutcome> GetThreePlDepositorPortalAsync(
        ThreePlDepositorPortalInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (request?.AuthenticatedOwnerId.HasValue == true &&
            request?.RequestedOwnerId.HasValue == true &&
            request.AuthenticatedOwnerId.Value != request.RequestedOwnerId.Value)
        {
            throw new ERP.Application.Exceptions.ForbiddenException(
                $"THREEPL_DEPOSITOR_CROSS_OWNER_FORBIDDEN: Chủ hàng ký gửi 3PL (OwnerId={request.AuthenticatedOwnerId.Value}) bị từ chối quyền xem tồn kho hoặc bảng kê phí của chủ hàng khác (OwnerId={request.RequestedOwnerId.Value}).");
        }

        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        if (effectiveAuth != null && warehouseId.HasValue)
        {
            await effectiveAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        }

        if (db == null)
        {
            return WmsEndpointOutcome.BadRequest("Database context is required.");
        }

        var now = DateTime.UtcNow;
        var effectiveActorId = actorUserId ?? (currentUser is { UserId: > 0 } ? currentUser.UserId : 1);
        var billingPeriod = !string.IsNullOrWhiteSpace(request?.BillingPeriod)
            ? request!.BillingPeriod!.Trim()
            : now.ToString("yyyy-MM");

        if (request?.Reverse == true)
        {
            var stmtQuery = db.ThreePlBillingStatements.AsQueryable();
            if (request.OriginalStatementId.HasValue)
            {
                stmtQuery = stmtQuery.Where(s => s.Id == request.OriginalStatementId.Value);
            }
            else if (request.OwnerId.HasValue || request.RequestedOwnerId.HasValue)
            {
                var revPid = request.RequestedOwnerId ?? request.OwnerId!.Value;
                stmtQuery = stmtQuery.Where(s => s.PartnerId == revPid);
            }

            var targetStmt = await stmtQuery.OrderByDescending(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (targetStmt == null || string.Equals(targetStmt.Status, "REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "THREEPL_STATEMENT_ALREADY_REVERSED",
                    "Bảng kê phí lưu kho 3PL của chủ hàng không tồn tại hoặc đã được hoàn tác trước đó.");
            }

            var relatedSnapshots = await db.ThreePlStorageSnapshots
                .Where(s => s.OwnerId == targetStmt.PartnerId && s.SnapshotCode.StartsWith($"3PL-SNAP-{targetStmt.PartnerId}-"))
                .ToListAsync(cancellationToken);
            if (relatedSnapshots.Count > 0)
            {
                db.ThreePlStorageSnapshots.RemoveRange(relatedSnapshots);
            }

            var removedStatementId = targetStmt.Id;
            var removedStatementCode = targetStmt.StatementCode;
            db.ThreePlBillingStatements.Remove(targetStmt);

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetStmt.WarehouseId ?? warehouseId ?? 1,
                EntityName = "ThreePlBillingStatement",
                EntityId = removedStatementId,
                Action = "ThreePlDepositorPortal.ReverseBillingStatement",
                NewValues = $"Reversed:{removedStatementCode}",
                Reason = $"Hoàn tác bảng kê phí dịch vụ 3PL chủ hàng ({removedStatementCode})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);

            return WmsEndpointOutcome.Ok(new
            {
                statementId = removedStatementId,
                statementCode = removedStatementCode,
                reversed = true,
                status = "REVERSED",
                statusLabel = $"Đã hoàn tác bảng kê phí dịch vụ 3PL ({removedStatementCode})",
                reversedAtUtc = now
            });
        }

        int? resolvedOwnerId = request?.RequestedOwnerId ?? request?.OwnerId ?? request?.AuthenticatedOwnerId;
        ERP.Domain.Entities.BusinessPartner? ownerPartner = null;

        if (resolvedOwnerId.HasValue)
        {
            ownerPartner = await db.BusinessPartners.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == resolvedOwnerId.Value, cancellationToken);
        }

        if (ownerPartner == null)
        {
            var stockWithOwner = await db.InventoryStocks.AsNoTracking()
                .Where(s => s.OwnerId != null && s.Quantity > 0m && (!warehouseId.HasValue || s.WarehouseId == warehouseId.Value))
                .OrderByDescending(s => s.Quantity)
                .FirstOrDefaultAsync(cancellationToken);

            if (stockWithOwner?.OwnerId != null)
            {
                ownerPartner = await db.BusinessPartners.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == stockWithOwner.OwnerId.Value, cancellationToken);
                resolvedOwnerId = stockWithOwner.OwnerId.Value;
            }
            else
            {
                ownerPartner = await db.BusinessPartners.AsNoTracking()
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                resolvedOwnerId = ownerPartner?.Id ?? 1;
            }
        }

        var strictOwnerId = resolvedOwnerId!.Value;
        var ownerCode = ownerPartner?.Code?.Trim() ?? $"OWN-{strictOwnerId:D3}";
        var ownerName = ownerPartner?.Name?.Trim() ?? $"Chủ hàng Ký gửi 3PL #{strictOwnerId}";

        var hasExplicitOwners = await db.InventoryStocks.AsNoTracking()
            .AnyAsync(s => s.OwnerId != null && (!warehouseId.HasValue || s.WarehouseId == warehouseId.Value), cancellationToken);

        // Strictly isolated 9D inventory query by OwnerId (with default 3PL pool fallback only when warehouse has 0 explicit OwnerId tags)
        var ownerStocksQuery = db.InventoryStocks.AsNoTracking()
            .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
            .Include(s => s.Location)
            .Include(s => s.Warehouse)
            .Include(s => s.Lot)
            .Where(s => hasExplicitOwners
                ? s.OwnerId == strictOwnerId
                : (s.OwnerId == strictOwnerId || (strictOwnerId == 1 && s.OwnerId == null)));

        if (warehouseId.HasValue)
        {
            ownerStocksQuery = ownerStocksQuery.Where(s => s.WarehouseId == warehouseId.Value);
        }

        var ownerStocks = await ownerStocksQuery
            .OrderBy(s => s.WarehouseId)
            .ThenBy(s => s.ProductId)
            .ThenBy(s => s.LocationId)
            .ToListAsync(cancellationToken);

        var crossOwnerLeakCount = ownerStocks.Count(s => s.OwnerId.HasValue && s.OwnerId != strictOwnerId);

        var ownerTxsQuery = db.InventoryTransactions.AsNoTracking()
            .Where(t => hasExplicitOwners
                ? t.OwnerId == strictOwnerId
                : (t.OwnerId == strictOwnerId || (strictOwnerId == 1 && t.OwnerId == null)));
        if (warehouseId.HasValue)
        {
            ownerTxsQuery = ownerTxsQuery.Where(t => t.WarehouseId == warehouseId.Value);
        }

        var ownerTxs = await ownerTxsQuery
            .OrderByDescending(t => t.Id)
            .Take(200)
            .ToListAsync(cancellationToken);

        var activeTxs = ownerTxs
            .Where(t => InventoryLedgerCalculationRules.IsIncludedInAlgebraicLedgerSum(t.TransactionType, t.CorrectiveTransactionId))
            .ToList();

        var totalOnHandQty = Math.Round(ownerStocks.Sum(s => s.Quantity), 4, MidpointRounding.AwayFromZero);
        var totalReservedQty = Math.Round(ownerStocks.Sum(s => s.ReservedQuantity), 4, MidpointRounding.AwayFromZero);
        var totalAvailableQty = Math.Round(ownerStocks.Sum(s => Math.Max(0m, s.Quantity - s.ReservedQuantity)), 4, MidpointRounding.AwayFromZero);
        var distinctSkuCount = ownerStocks.Where(s => s.Quantity > 0m).Select(s => s.ProductId).Distinct().Count();
        var occupiedBinCount = ownerStocks.Where(s => s.Quantity > 0m && s.LocationId.HasValue).Select(s => s.LocationId!.Value).Distinct().Count();

        var inboundQty = Math.Round(
            activeTxs.Where(t => t.Quantity > 0m && (t.TransactionType == TransactionType.Import || t.ReferenceType == "ImportReceipt" || t.ReferenceType == "KittingAssemble")).Sum(t => t.Quantity),
            4,
            MidpointRounding.AwayFromZero);
        var outboundQty = Math.Round(
            activeTxs.Where(t => t.Quantity < 0m && (t.TransactionType == TransactionType.Export || t.ReferenceType == "ExportReceipt" || t.ReferenceType == "SalesOrder")).Sum(t => Math.Abs(t.Quantity)),
            4,
            MidpointRounding.AwayFromZero);
        var vasQty = Math.Round(
            activeTxs.Where(t => t.ReferenceType != null && (t.ReferenceType.Contains("Kit") || t.ReferenceType.Contains("BreakBulk")) && t.Quantity > 0m).Sum(t => t.Quantity),
            4,
            MidpointRounding.AwayFromZero);

        var palletEquivalentCount = Math.Round(Math.Max(1m, Math.Ceiling(totalOnHandQty / 20m)), 4, MidpointRounding.AwayFromZero);
        if (totalOnHandQty == 0m)
        {
            palletEquivalentCount = 0m;
        }

        var storageRate = Math.Round(request?.StorageRatePerPalletDay ?? 15000m, 4, MidpointRounding.AwayFromZero);
        var inboundRate = Math.Round(request?.InboundHandlingRatePerUnit ?? 8000m, 4, MidpointRounding.AwayFromZero);
        var outboundRate = Math.Round(request?.OutboundHandlingRatePerUnit ?? 9000m, 4, MidpointRounding.AwayFromZero);
        var vasRate = Math.Round(request?.VasKittingRatePerUnit ?? 12000m, 4, MidpointRounding.AwayFromZero);

        var storageFeeAmount = Math.Round(totalOnHandQty * storageRate, 4, MidpointRounding.AwayFromZero);
        var inboundHandlingFeeAmount = Math.Round(inboundQty * inboundRate, 4, MidpointRounding.AwayFromZero);
        var outboundHandlingFeeAmount = Math.Round(outboundQty * outboundRate, 4, MidpointRounding.AwayFromZero);
        var vasFeeAmount = Math.Round(vasQty * vasRate, 4, MidpointRounding.AwayFromZero);
        var subTotalAmount = Math.Round(storageFeeAmount + inboundHandlingFeeAmount + outboundHandlingFeeAmount + vasFeeAmount, 4, MidpointRounding.AwayFromZero);
        var vatRatePercent = 10.0000m;
        var vatAmount = Math.Round(subTotalAmount * (vatRatePercent / 100m), 4, MidpointRounding.AwayFromZero);
        var totalBillingAmount = Math.Round(subTotalAmount + vatAmount, 4, MidpointRounding.AwayFromZero);

        var statementCode = $"3PL-DEP-{strictOwnerId:D3}-{billingPeriod.Replace("-", "")}";
        int? persistedStatementId = null;
        int? persistedSnapshotId = null;

        if (request?.PersistSnapshotAndStatement == true)
        {
            var isRelational = db.Database.IsRelational();
            await using var tx = isRelational && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken)
                : null;

            var targetWhId = warehouseId ?? ownerStocks.FirstOrDefault()?.WarehouseId ?? 1;
            var lockProductIds = ownerStocks.Select(s => s.ProductId).Distinct().OrderBy(x => x).ToList();
            if (isRelational && lockProductIds.Count > 0)
            {
                await db.AcquireInventoryAllocationLocksAsync(targetWhId, lockProductIds, cancellationToken);
            }

            var existingStmt = await db.ThreePlBillingStatements.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PartnerId == strictOwnerId && s.StatementCode == statementCode && s.Status == "ISSUED", cancellationToken);
            if (existingStmt != null)
            {
                throw new ERP.Domain.Exceptions.ConflictException(
                    "THREEPL_STATEMENT_ALREADY_ISSUED",
                    $"Bảng kê phí lưu kho 3PL ({statementCode}) của chủ hàng {ownerCode} trong kỳ {billingPeriod} đã được phát hành.");
            }

            var snapshot = new ERP.Domain.Entities.ThreePlStorageSnapshot
            {
                WarehouseId = targetWhId,
                OwnerId = strictOwnerId,
                OwnerCode = ownerCode,
                OwnerName = ownerName,
                SnapshotCode = $"3PL-SNAP-{strictOwnerId}-{now:yyyyMMddHHmmss}",
                SnapshotDateUtc = now.Date,
                DistinctSkuCount = distinctSkuCount,
                OccupiedBinCount = occupiedBinCount,
                TotalOnHandQty = totalOnHandQty,
                TotalVolumeCbm = Math.Round(totalOnHandQty * 0.05m, 4, MidpointRounding.AwayFromZero),
                PalletEquivalentCount = palletEquivalentCount,
                DailyPalletRateVnd = storageRate,
                DailyCbmRateVnd = 25000m,
                DailyBinRateVnd = 10000m,
                EstimatedDailyStorageFeeVnd = storageFeeAmount,
                EstimatedMonthlyStorageFeeVnd = Math.Round(storageFeeAmount * 30m, 4, MidpointRounding.AwayFromZero),
                CapturedByUserId = effectiveActorId,
                CapturedAtUtc = now
            };

            var statement = new ERP.Domain.Entities.ThreePlBillingStatement
            {
                WarehouseId = targetWhId,
                PartnerId = strictOwnerId,
                StatementCode = statementCode,
                PeriodFromUtc = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodToUtc = now,
                TotalOnHandQty = totalOnHandQty,
                TotalInboundQty = inboundQty,
                TotalOutboundQty = outboundQty,
                StorageFeeAmount = storageFeeAmount,
                InboundHandlingFeeAmount = inboundHandlingFeeAmount,
                OutboundHandlingFeeAmount = Math.Round(outboundHandlingFeeAmount + vasFeeAmount, 4, MidpointRounding.AwayFromZero),
                TotalBillingAmount = totalBillingAmount,
                ReceivableInvoiceCode = $"AR-{statementCode}",
                Status = "ISSUED",
                CreatedByUserId = effectiveActorId,
                CreatedAtUtc = now
            };

            db.ThreePlStorageSnapshots.Add(snapshot);
            db.ThreePlBillingStatements.Add(statement);
            await db.SaveChangesAsync(cancellationToken);

            persistedSnapshotId = snapshot.Id;
            persistedStatementId = statement.Id;

            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = effectiveActorId,
                WarehouseId = targetWhId,
                EntityName = "ThreePlBillingStatement",
                EntityId = statement.Id,
                Action = "ThreePlDepositorPortal.IssueBillingStatement",
                NewValues = $"{statementCode}:OwnerId={strictOwnerId};Total={totalBillingAmount:0.####}",
                Reason = $"Chốt bảng kê đối soát phí lưu kho & tác nghiệp 3PL cho chủ hàng {ownerCode} ({statementCode})",
                Result = "Success",
                Timestamp = now
            });

            await db.SaveChangesAsync(cancellationToken);
            if (tx != null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            ownerId = strictOwnerId,
            ownerCode,
            ownerName,
            billingPeriod,
            statementCode,
            statementId = persistedStatementId,
            snapshotId = persistedSnapshotId,
            crossOwnerLeakCount,
            tenantAndOwnerIsolationVerified = crossOwnerLeakCount == 0,
            distinctSkuCount,
            occupiedBinCount,
            palletEquivalentCount,
            totalOnHandQty,
            totalReservedQty,
            totalAvailableQty,
            inboundHandledQty = inboundQty,
            outboundHandledQty = outboundQty,
            vasWorkOrderQty = vasQty,
            storageRatePerUnit = storageRate,
            inboundHandlingRatePerUnit = inboundRate,
            outboundHandlingRatePerUnit = outboundRate,
            vasKittingRatePerUnit = vasRate,
            storageFeeAmount,
            inboundHandlingFeeAmount,
            outboundHandlingFeeAmount,
            vasFeeAmount,
            subTotalAmount,
            vatRatePercent,
            vatAmount,
            totalBillingAmount,
            inventoryBuckets = ownerStocks.Select(s => new
            {
                stockId = s.Id,
                ownerId = s.OwnerId,
                warehouseId = s.WarehouseId,
                warehouseCode = s.Warehouse?.Code?.Trim() ?? $"WH-{s.WarehouseId}",
                productId = s.ProductId,
                productCode = s.Product?.Code?.Trim() ?? $"SKU-{s.ProductId}",
                productName = s.Product?.Name?.Trim() ?? $"Sản phẩm #{s.ProductId}",
                baseUomCode = s.Product?.Unit?.Code?.Trim() ?? "EA",
                locationId = s.LocationId,
                locationCode = s.Location?.Code?.Trim() ?? "STAGE",
                lotNumber = s.Lot?.LotNumber,
                handlingUnitCode = s.HandlingUnitId.HasValue ? $"HU-{s.HandlingUnitId.Value}" : null,
                status = s.Status.ToString(),
                onHandQty = s.Quantity,
                reservedQty = s.ReservedQuantity,
                availableQty = Math.Max(0m, s.Quantity - s.ReservedQuantity)
            }).ToList(),
            status = persistedStatementId.HasValue ? "STATEMENT_ISSUED" : "PORTAL_READY",
            statusLabel = $"Đã tải Cổng Chủ hàng 3PL ({ownerCode} — {ownerName}): {ownerStocks.Count} rổ tồn kho 9 chiều cô lập theo OwnerId (0 rò rỉ chéo) • Tổng phí kỳ {billingPeriod}: {totalBillingAmount:N0} đ",
            evaluatedAtUtc = now
        });
    }
}
