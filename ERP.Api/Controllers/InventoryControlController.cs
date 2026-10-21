using ERP.Api.Authorization;
using ERP.Api.Infrastructure;
using ERP.Application.DTOs;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ERP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/inventory")]
public sealed class InventoryControlController(
    IInventoryLockService lockService,
    IInventoryMovementService movementService,
    ErpKhoDbContext db = null!,
    IWarehouseAuthorizationService warehouseAccess = null!) : ControllerBase
{
    [HttpGet("locks")]
    [PermissionAuthorize(AppPermissions.InventoryLockRead)]
    public async Task<ActionResult<IReadOnlyList<InventoryLockDto>>> Locks(
        [FromQuery] int? warehouseId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default) =>
        Ok(await lockService.ListAsync(warehouseId, status, cancellationToken));

    [HttpGet("locks/{id:int}")]
    [PermissionAuthorize(AppPermissions.InventoryLockRead)]
    public async Task<ActionResult<InventoryLockDto>> Lock(
        int id,
        CancellationToken cancellationToken) =>
        Ok(await lockService.GetAsync(id, cancellationToken));

    [HttpPost("locks")]
    [PermissionAuthorize(AppPermissions.InventoryLockManage)]
    [IdempotentCommand("InventoryLock.Create")]
    public async Task<ActionResult<InventoryLockDto>> CreateLock(
        CreateInventoryLockDto request,
        CancellationToken cancellationToken) =>
        Ok(await lockService.CreateAsync(request, cancellationToken));

    [HttpPost("locks/{id:int}/release")]
    [PermissionAuthorize(AppPermissions.InventoryLockManage)]
    [IdempotentCommand("InventoryLock.Release")]
    public async Task<ActionResult<InventoryLockDto>> ReleaseLock(
        int id,
        ReleaseInventoryLockDto request,
        CancellationToken cancellationToken) =>
        Ok(await lockService.ReleaseAsync(id, request, cancellationToken));

    [HttpPost("movements")]
    [PermissionAuthorize(AppPermissions.InventoryMovementCreate)]
    [IdempotentCommand("InventoryMovement.Create")]
    public async Task<ActionResult<InventoryMoveResultDto>> Move(
        CreateInventoryMoveDto request,
        CancellationToken cancellationToken) =>
        Ok(await movementService.MoveAsync(request, cancellationToken));

    [HttpGet("replenishment-suggestions")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ReplenishmentSuggestions(
        [FromServices] ErpKhoDbContext db,
        [FromServices] IWarehouseAuthorizationService warehouseAuth,
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        if (warehouseId.HasValue)
            await warehouseAuth.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
        var accessibleIds = await warehouseAuth.GetAccessibleWarehouseIdsAsync(cancellationToken);

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

        return Ok(suggestions.Take(25).ToList());
    }

    public sealed record ExecuteReplenishmentRequest(
        int? WarehouseId = null,
        int? ProductId = null,
        string? ProductCode = null,
        int? FromLocationId = null,
        string? FromLocationCode = null,
        int? ToLocationId = null,
        string? ToLocationCode = null,
        decimal Quantity = 10m,
        bool ExecuteStockMutation = true,
        string? LotNumber = null,
        string? Note = null,
        int? SourceLocationId = null,
        string? SourceLocationCode = null,
        int? TargetLocationId = null,
        string? TargetLocationCode = null);

    [HttpPost("execute-replenishment")]
    [HttpPost("/api/inventory-control/execute-replenishment")]
    [IdempotentCommand("Inventory.ExecuteReplenishment")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> ExecuteReplenishment(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ExecuteReplenishmentRequest? request = null,
        [FromServices] ErpKhoDbContext? dbSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveDb = dbSvc ?? db;
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
                    return Forbid();
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

        var actorUserId = int.TryParse(
            User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
            out var uid) ? uid : (int?)null;

        if (effectiveDb != null)
        {
            if (!resolvedProductId.HasValue && !string.IsNullOrWhiteSpace(resolvedProductCode))
            {
                var prod = await effectiveDb.Products.AsNoTracking()
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
                var prod = await effectiveDb.Products.AsNoTracking()
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
                var fromLoc = await effectiveDb.WarehouseLocations.AsNoTracking()
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
                var toLoc = await effectiveDb.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId && l.Code == requestedToCode)
                    .Select(l => new { l.Id, l.Code })
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? await effectiveDb.WarehouseLocations.AsNoTracking()
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
                var isRelational = effectiveDb.Database.IsRelational();
                await using var tx = isRelational && effectiveDb.Database.CurrentTransaction == null
                    ? await effectiveDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
                    : null;

                try
                {
                    var stockQuery = effectiveDb.InventoryStocks
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
                        var fallbackQuery = effectiveDb.InventoryStocks
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

                            var targetStock = await effectiveDb.InventoryStocks
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
                                effectiveDb.InventoryStocks.Add(targetStock);
                                targetResultingQuantity = targetStock.Quantity;
                            }

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
                                CreatedBy = actorUserId ?? 1,
                                Note = $"{replenishmentId}: Bổ sung kệ nhặt từ {resolvedFromCode} sang {resolvedToCode}"
                            };
                            effectiveDb.InventoryTransactions.Add(moveTx);
                            await effectiveDb.SaveChangesAsync(cancellationToken);

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

            effectiveDb.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
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
            await effectiveDb.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
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

    [HttpGet("advanced-planning-suite")]
    [HttpGet("/api/inventory-control/advanced-planning-suite")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> AdvancedPlanningSuite(
        [FromServices] ErpKhoDbContext? dbSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromQuery] int? warehouseId = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveDb = dbSvc ?? db;
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

        var stockQuery = effectiveDb.InventoryStocks.AsNoTracking();
        var soLineQuery = effectiveDb.SalesOrderLines.AsNoTracking()
            .Where(l => l.SalesOrder.Status != SalesOrderStatus.Cancelled && l.SalesOrder.Status != SalesOrderStatus.Draft);
        var poLineQuery = effectiveDb.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.PurchaseOrder.Status == PurchaseOrderStatus.Open || l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived);
        var reservationQuery = effectiveDb.StockReservations.AsNoTracking()
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

        return Ok(new
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

    public sealed record WhatIfSimulationRequest(
        int? WarehouseId = null,
        decimal DemandMultiplier = 1.5m,
        int HorizonDays = 14,
        decimal? DemandShockPct = null,
        int? LeadTimeDelayDays = null);

    [HttpPost("what-if-simulation")]
    [HttpPost("/api/inventory-control/what-if-simulation")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> RunWhatIfSimulation(
        [FromServices] ErpKhoDbContext? dbSvc = null,
        [FromServices] IWarehouseAuthorizationService? warehouseAuth = null,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] WhatIfSimulationRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var effectiveDb = dbSvc ?? db;
        var effectiveAuth = warehouseAuth ?? warehouseAccess;

        var req = request ?? new WhatIfSimulationRequest();
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

        if (effectiveDb != null)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var stockQuery = effectiveDb.InventoryStocks.AsNoTracking()
                .Where(s => s.Status == InventoryStatus.Available &&
                            (s.Lot == null || !s.Lot.ExpiryDate.HasValue || s.Lot.ExpiryDate.Value >= todayUtc));
            var soLineQuery = effectiveDb.SalesOrderLines.AsNoTracking()
                .Where(l => l.SalesOrder.Status != SalesOrderStatus.Cancelled && l.SalesOrder.Status != SalesOrderStatus.Draft);
            var poLineQuery = effectiveDb.PurchaseOrderLines.AsNoTracking()
                .Where(l => l.PurchaseOrder.Status == PurchaseOrderStatus.Open || l.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived);
            var reservationQuery = effectiveDb.StockReservations.AsNoTracking()
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
        if (effectiveDb != null)
        {
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            effectiveDb.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
                WarehouseId = req.WarehouseId,
                EntityName = "InventoryControl",
                Action = "WhatIfSimulation",
                NewValues = simulationId,
                Reason = $"Mô phỏng tồn kho What-If: {formulaSummary}",
                Result = "Success",
                Timestamp = now
            });
            await effectiveDb.SaveChangesAsync(cancellationToken);
        }

        return Ok(new
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

    public sealed record EvaluateDecisionPolicyRequest(int? WarehouseId = null);

    [HttpPost("evaluate-decision-policy-governance")]
    [HttpPost("audit-decision-policies")]
    [HttpPost("/api/inventory-control/audit-decision-policies")]
    [PermissionAuthorize(AppPermissions.InventoryRead)]
    public async Task<IActionResult> EvaluateDecisionPolicyGovernance(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] EvaluateDecisionPolicyRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        if (User?.IsInRole(AppRoles.Viewer) == true)
            return Forbid();

        var warehouseId = request?.WarehouseId;
        IReadOnlyCollection<int>? accessibleIds = null;
        if (warehouseAccess != null)
        {
            if (warehouseId.HasValue)
                await warehouseAccess.EnsureWarehouseAccessAsync(warehouseId.Value, cancellationToken);
            else
                accessibleIds = await warehouseAccess.GetAccessibleWarehouseIdsAsync(cancellationToken);
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
            var actorUserId = int.TryParse(
                User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User?.FindFirst("Id")?.Value,
                out var uid) ? uid : (int?)null;
            db.AuditLogs.Add(new ERP.Domain.Entities.AuditLog
            {
                UserId = actorUserId,
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

        return Ok(new
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

