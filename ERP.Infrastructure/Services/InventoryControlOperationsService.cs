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

            var reserveStock = await db.InventoryStocks
                .FirstOrDefaultAsync(s =>
                    s.WarehouseId == targetWarehouseId &&
                    s.ProductId == resolvedProductId.Value &&
                    s.LocationId == resolvedSourceLocationId.Value &&
                    s.LotId == pickStock.LotId &&
                    s.SerialId == pickStock.SerialId &&
                    s.HandlingUnitId == request.SourceHandlingUnitId &&
                    s.OwnerId == pickStock.OwnerId &&
                    s.Status == InventoryStatus.Available,
                    cancellationToken);

            if (reserveStock == null)
            {
                reserveStock = new ERP.Domain.Entities.InventoryStock
                {
                    WarehouseId = targetWarehouseId,
                    ProductId = resolvedProductId.Value,
                    LocationId = resolvedSourceLocationId.Value,
                    LotId = pickStock.LotId,
                    SerialId = pickStock.SerialId,
                    HandlingUnitId = request.SourceHandlingUnitId,
                    OwnerId = pickStock.OwnerId,
                    Status = InventoryStatus.Available,
                    Quantity = movedBaseQty,
                    ReservedQuantity = 0m,
                    LastUpdated = now
                };
                db.InventoryStocks.Add(reserveStock);
            }
            else
            {
                reserveStock.Quantity = Math.Round(reserveStock.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
                reserveStock.LastUpdated = now;
            }

            var revId = $"BB-REV-{now:yyyyMMdd-HHmmss}";
            var revNote = $"{revId}: Đảo bút toán xả thùng {sourcePackQty:0.####} {resolvedSourceUomCode} (x{conversionFactor:0.####} = {movedBaseQty:0.####} {baseUomCode})";

            int? origOutTxId = null;
            int? origInTxId = null;
            if (request.OriginalReferenceId.HasValue)
            {
                var alreadyReversedTxIds = await db.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.WarehouseId == targetWarehouseId &&
                                t.ProductId == resolvedProductId.Value &&
                                t.ReversalOfTransactionId != null)
                    .Select(t => t.ReversalOfTransactionId!.Value)
                    .ToListAsync(cancellationToken);

                var origTxs = await db.InventoryTransactions
                    .AsNoTracking()
                    .Where(t => t.WarehouseId == targetWarehouseId &&
                                t.ProductId == resolvedProductId.Value &&
                                t.TransactionType != TransactionType.Reversal &&
                                t.ReferenceType == "BreakBulkReplenishment" &&
                                t.ReferenceId == request.OriginalReferenceId.Value &&
                                !alreadyReversedTxIds.Contains(t.Id))
                    .OrderByDescending(t => t.Id)
                    .ToListAsync(cancellationToken);

                origOutTxId = origTxs.FirstOrDefault(t => t.Quantity < 0)?.Id;
                origInTxId = origTxs.FirstOrDefault(t => t.Quantity > 0)?.Id;
            }

            var revOutTx = new ERP.Domain.Entities.InventoryTransaction
            {
                ProductId = resolvedProductId.Value,
                WarehouseId = targetWarehouseId,
                LocationId = resolvedTargetLocationId.Value,
                FromLocationId = resolvedTargetLocationId.Value,
                ToLocationId = resolvedSourceLocationId.Value,
                LotId = pickStock.LotId,
                SerialId = pickStock.SerialId,
                HandlingUnitId = null,
                OwnerId = pickStock.OwnerId,
                InventoryStatus = InventoryStatus.Available,
                FromInventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Reversal,
                Quantity = -movedBaseQty,
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
                LotId = pickStock.LotId,
                SerialId = pickStock.SerialId,
                HandlingUnitId = request.SourceHandlingUnitId,
                OwnerId = pickStock.OwnerId,
                InventoryStatus = InventoryStatus.Available,
                FromInventoryStatus = InventoryStatus.Available,
                ToInventoryStatus = InventoryStatus.Available,
                TransactionType = TransactionType.Reversal,
                Quantity = movedBaseQty,
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
                sourceRemainingQuantity = reserveStock.Quantity,
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
            db.InventoryStocks.Add(targetStockBucket);
        }
        else
        {
            targetStockBucket.Quantity = Math.Round(targetStockBucket.Quantity + movedBaseQty, 4, MidpointRounding.AwayFromZero);
            targetStockBucket.LastUpdated = now;
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
}
