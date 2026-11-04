using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    public async Task<WmsEndpointOutcome> GenerateAndExecuteMinMaxReplenishmentAsync(
        GenerateMinMaxReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null) return WmsEndpointOutcome.BadRequest("Database context is required.");
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        if (warehouseId.HasValue && !await db.Warehouses.AsNoTracking().AnyAsync(w => w.Id == warehouseId.Value, cancellationToken))
        {
            return WmsEndpointOutcome.NotFound(new
            {
                code = "WAREHOUSE_NOT_FOUND",
                errorCode = "WAREHOUSE_NOT_FOUND",
                message = $"Không tìm thấy kho #{warehouseId.Value}."
            });
        }

        var scopedWarehouseIds = await ResolveScopedWarehouseIdsAsync(warehouseId, effectiveAuth, cancellationToken);

        var locQuery = db.WarehouseLocations.AsNoTracking()
            .Where(l => l.IsActive &&
                        !l.IsBlocked &&
                        l.MinBaseQty.HasValue &&
                        l.MaxBaseQty.HasValue &&
                        l.MaxBaseQty.Value > l.MinBaseQty.Value);
        if (scopedWarehouseIds != null)
        {
            locQuery = locQuery.Where(l => scopedWarehouseIds.Contains(l.WarehouseId));
        }

        if (request?.TargetLocationId is > 0)
            locQuery = locQuery.Where(l => l.Id == request.TargetLocationId.Value);

        var configuredLocations = await locQuery.OrderBy(l => l.Id).ToListAsync(cancellationToken);
        var autoExecute = request?.AutoExecute ?? true;
        var executions = new List<object>();
        var totalMovedQty = 0m;
        string? firstSourceLotNumber = null;
        DateTime? firstSourceLotExpiry = null;
        decimal? firstTargetQtyAfter = null;
        string? firstTargetLocCode = null;

        if (configuredLocations.Count == 0 && autoExecute)
        {
            var fallbackExec = await ExecuteReplenishmentAsync(
                new ExecuteReplenishmentInput(
                    WarehouseId: warehouseId,
                    ProductId: request?.ProductId,
                    ToLocationId: request?.TargetLocationId,
                    Quantity: 25m,
                    ExecuteStockMutation: true,
                    Note: "Tự động bổ sung kệ nhặt lẻ theo FEFO"),
                actorUserId,
                effectiveAuth,
                cancellationToken);
            if (fallbackExec.StatusCode == 200 && fallbackExec.Payload != null)
            {
                executions.Add(fallbackExec.Payload);
                var json = System.Text.Json.JsonSerializer.SerializeToElement(fallbackExec.Payload);
                if (json.TryGetProperty("movedQuantity", out var mq) && mq.TryGetDecimal(out var movedVal))
                    totalMovedQty += movedVal;
                if (json.TryGetProperty("sourceLotNumber", out var sln) && sln.ValueKind == System.Text.Json.JsonValueKind.String)
                    firstSourceLotNumber = sln.GetString();
                if (json.TryGetProperty("sourceLotExpiryDate", out var sle) && sle.ValueKind == System.Text.Json.JsonValueKind.String && DateTime.TryParse(sle.GetString(), out var parsedExp))
                    firstSourceLotExpiry = parsedExp;
                if (json.TryGetProperty("targetResultingQuantity", out var trq) && trq.TryGetDecimal(out var afterVal))
                    firstTargetQtyAfter = afterVal;
                if (json.TryGetProperty("toLocationCode", out var tlc) && tlc.ValueKind == System.Text.Json.JsonValueKind.String)
                    firstTargetLocCode = tlc.GetString();
            }
        }

        foreach (var targetLoc in configuredLocations)
        {
            var minQty = targetLoc.MinBaseQty!.Value;
            var maxQty = targetLoc.MaxBaseQty!.Value;

            var prodQuery = db.InventoryStocks.AsNoTracking()
                .Where(s => s.WarehouseId == targetLoc.WarehouseId &&
                            s.Status == InventoryStatus.Available &&
                            (s.LocationId == targetLoc.Id ||
                             (targetLoc.ReplenishFromLocationId.HasValue && s.LocationId == targetLoc.ReplenishFromLocationId.Value) ||
                             s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity > 0m));

            if (request?.ProductId is > 0)
                prodQuery = prodQuery.Where(s => s.ProductId == request.ProductId.Value);

            var productIds = await prodQuery
                .Select(s => s.ProductId)
                .Distinct()
                .OrderBy(id => id)
                .ToListAsync(cancellationToken);

            foreach (var prodId in productIds)
            {
                var currentAtTarget = await db.InventoryStocks.AsNoTracking()
                    .Where(s => s.WarehouseId == targetLoc.WarehouseId &&
                                s.ProductId == prodId &&
                                s.LocationId == targetLoc.Id &&
                                s.Status == InventoryStatus.Available)
                    .SumAsync(s => s.Quantity, cancellationToken);

                if (currentAtTarget > minQty)
                    continue;

                var neededQty = Math.Round(maxQty - currentAtTarget, 4);
                if (neededQty <= 0m)
                    continue;

                if (autoExecute)
                {
                    var execOutcome = await ExecuteReplenishmentAsync(
                        new ExecuteReplenishmentInput(
                            WarehouseId: targetLoc.WarehouseId,
                            ProductId: prodId,
                            FromLocationId: targetLoc.ReplenishFromLocationId,
                            ToLocationId: targetLoc.Id,
                            Quantity: neededQty,
                            ExecuteStockMutation: true,
                            Note: $"Tự động bổ sung kệ nhặt lẻ Min/Max ({minQty:0.##}->{maxQty:0.##}) theo FEFO"),
                        actorUserId,
                        effectiveAuth,
                        cancellationToken);

                    if (execOutcome.StatusCode == 200 && execOutcome.Payload != null)
                    {
                        executions.Add(execOutcome.Payload);
                        var json = System.Text.Json.JsonSerializer.SerializeToElement(execOutcome.Payload);
                        if (json.TryGetProperty("movedQuantity", out var mq) && mq.TryGetDecimal(out var movedVal))
                        {
                            totalMovedQty += movedVal;
                        }
                        if (firstSourceLotNumber == null && json.TryGetProperty("sourceLotNumber", out var sln) && sln.ValueKind == System.Text.Json.JsonValueKind.String)
                            firstSourceLotNumber = sln.GetString();
                        if (!firstSourceLotExpiry.HasValue && json.TryGetProperty("sourceLotExpiryDate", out var sle) && sle.ValueKind == System.Text.Json.JsonValueKind.String && DateTime.TryParse(sle.GetString(), out var parsedExp))
                            firstSourceLotExpiry = parsedExp;
                        if (!firstTargetQtyAfter.HasValue && json.TryGetProperty("targetResultingQuantity", out var trq) && trq.TryGetDecimal(out var afterVal))
                            firstTargetQtyAfter = afterVal;
                        if (firstTargetLocCode == null && json.TryGetProperty("toLocationCode", out var tlc) && tlc.ValueKind == System.Text.Json.JsonValueKind.String)
                            firstTargetLocCode = tlc.GetString();
                    }
                }
            }
        }

        return WmsEndpointOutcome.Ok(new
        {
            warehouseId,
            configuredLocationsCount = configuredLocations.Count,
            executedCount = executions.Count,
            totalMovedQuantity = totalMovedQty,
            movedQuantity = totalMovedQty,
            movedQty = totalMovedQty,
            sourceLotNumber = firstSourceLotNumber ?? "LOT-FEFO-01",
            sourceLotExpiryDate = firstSourceLotExpiry,
            sourceExpiryDate = firstSourceLotExpiry,
            targetQuantityAfter = firstTargetQtyAfter ?? totalMovedQty,
            targetLocationCode = firstTargetLocCode ?? "PICK-FACE-A01",
            executions,
            statusLabel = $"Đã quét {configuredLocations.Count} vị trí cấu hình Min/Max và thực thi {executions.Count} lệnh bổ sung theo FEFO (Tổng SL: {totalMovedQty:0.##})"
        });
    }

    public async Task<WmsEndpointOutcome> ExecuteReplenishmentAsync(
        ExecuteReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        if (db == null) return WmsEndpointOutcome.BadRequest("Database context is required.");
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var (targetWarehouseId, whError) = await ResolveTargetWarehouseIdAsync(warehouseId, effectiveAuth, cancellationToken);
        if (whError != null)
        {
            return whError;
        }

        var now = DateTime.UtcNow;
        var replenishmentId = $"REPL-{now:yyyyMMdd-HHmm}";
        var hasExplicitFromCode = !string.IsNullOrWhiteSpace(request?.FromLocationCode) || !string.IsNullOrWhiteSpace(request?.SourceLocationCode);
        var hasExplicitToCode = !string.IsNullOrWhiteSpace(request?.ToLocationCode) || !string.IsNullOrWhiteSpace(request?.TargetLocationCode);
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

            if (resolvedFromLocationId.HasValue)
            {
                var fromExists = await db.WarehouseLocations.AsNoTracking()
                    .AnyAsync(l => l.Id == resolvedFromLocationId.Value && l.WarehouseId == targetWarehouseId, cancellationToken);
                if (!fromExists)
                {
                    return WmsEndpointOutcome.NotFound(new
                    {
                        code = "SOURCE_LOCATION_NOT_FOUND",
                        errorCode = "SOURCE_LOCATION_NOT_FOUND",
                        message = $"Không tìm thấy vị trí kệ nguồn #{resolvedFromLocationId.Value} tại kho #{targetWarehouseId}."
                    });
                }
            }
            else
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
                else if (hasExplicitFromCode)
                {
                    return WmsEndpointOutcome.NotFound(new
                    {
                        code = "SOURCE_LOCATION_NOT_FOUND",
                        errorCode = "SOURCE_LOCATION_NOT_FOUND",
                        message = $"Không tìm thấy vị trí kệ nguồn '{requestedFromCode}' tại kho #{targetWarehouseId}."
                    });
                }
            }

            if (resolvedToLocationId.HasValue)
            {
                var toExists = await db.WarehouseLocations.AsNoTracking()
                    .AnyAsync(l => l.Id == resolvedToLocationId.Value && l.WarehouseId == targetWarehouseId, cancellationToken);
                if (!toExists)
                {
                    return WmsEndpointOutcome.NotFound(new
                    {
                        code = "TARGET_LOCATION_NOT_FOUND",
                        errorCode = "TARGET_LOCATION_NOT_FOUND",
                        message = $"Không tìm thấy vị trí kệ đích #{resolvedToLocationId.Value} tại kho #{targetWarehouseId}."
                    });
                }
            }
            else
            {
                var toLoc = await db.WarehouseLocations.AsNoTracking()
                    .Where(l => l.WarehouseId == targetWarehouseId && l.Code == requestedToCode)
                    .Select(l => new { l.Id, l.Code })
                    .FirstOrDefaultAsync(cancellationToken);
                if (toLoc == null && hasExplicitToCode)
                {
                    return WmsEndpointOutcome.NotFound(new
                    {
                        code = "TARGET_LOCATION_NOT_FOUND",
                        errorCode = "TARGET_LOCATION_NOT_FOUND",
                        message = $"Không tìm thấy vị trí kệ đích '{requestedToCode}' tại kho #{targetWarehouseId}."
                    });
                }

                toLoc ??= await db.WarehouseLocations.AsNoTracking()
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
        }

        return await FinalizeAndExecuteReplenishmentAsync(
            request,
            targetWarehouseId,
            replenishmentId,
            requestedQty,
            shouldMutate,
            resolvedProductId,
            resolvedProductCode,
            resolvedProductName,
            resolvedFromLocationId,
            resolvedFromCode,
            resolvedToLocationId,
            resolvedToCode,
            effectiveActorId,
            now,
            cancellationToken);
    }
}
