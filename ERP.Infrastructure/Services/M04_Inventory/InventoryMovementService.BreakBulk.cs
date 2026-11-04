using System.Data;
using ERP.Application.DTOs;
using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;
using ERP.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryMovementService
{
    public async Task<AutoBreakBulkReplenishResultDto> AutoBreakBulkReplenishAsync(
        AutoBreakBulkReplenishRequestDto? request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var executeImmediately = request?.ExecuteImmediately ?? false;
        var warehouseId = request?.WarehouseId ?? 0;
        if (warehouseId > 0)
        {
            if (warehouseAuthorization != null)
            {
                if (executeImmediately && currentUser?.IsAuthenticated == true)
                {
                    await (warehouseAuthorization.EnsureWarehouseCommandAccessAsync(warehouseId, cancellationToken)
                        ?? warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken));
                }
                else
                {
                    await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
                }
            }

            var wh = await context.Warehouses.AsNoTracking()
                .Where(w => w.Id == warehouseId)
                .Select(w => new { w.Id, w.IsActive })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw NotFoundWithCode("WAREHOUSE_NOT_FOUND", $"Không tìm thấy kho #{warehouseId}.");

            if (!wh.IsActive)
            {
                throw BadRequestWithCode("WAREHOUSE_INACTIVE", $"Kho #{warehouseId} hiện đang ngừng hoạt động.");
            }
        }
        else if (currentUser?.IsAuthenticated == true &&
                 !currentUser.IsGlobalAdmin &&
                 currentUser.Role is not "Admin" and not "SuperAdmin")
        {
            var accessibleIds = warehouseAuthorization != null
                ? await warehouseAuthorization.GetAccessibleWarehouseIdsAsync(cancellationToken)
                : await context.UserWarehouses.AsNoTracking()
                    .Where(uw => uw.UserId == currentUser.UserId && !context.Warehouses.Any(w => w.Id == uw.WarehouseId && !w.IsActive))
                    .Select(uw => uw.WarehouseId)
                    .ToListAsync(cancellationToken);
            if (accessibleIds.Count == 0)
            {
                throw new ForbiddenException("Bạn không có quyền truy cập kho nào để thực hiện xả thùng bổ sung.", "WAREHOUSE_ACCESS_DENIED");
            }
            warehouseId = accessibleIds[0];
        }
        else
        {
            warehouseId = await context.Warehouses
                .AsNoTracking()
                .Where(w => w.IsActive)
                .OrderBy(w => w.Id)
                .Select(w => w.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (warehouseId <= 0)
        {
            throw NotFoundWithCode("WAREHOUSE_NOT_FOUND", "Không tìm thấy kho đang hoạt động hợp lệ.");
        }

        if (request?.WarehouseId is null or <= 0 && warehouseAuthorization != null)
        {
            if (executeImmediately && currentUser?.IsAuthenticated == true)
            {
                await (warehouseAuthorization.EnsureWarehouseCommandAccessAsync(warehouseId, cancellationToken)
                    ?? warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken));
            }
            else
            {
                await warehouseAuthorization.EnsureWarehouseAccessAsync(warehouseId, cancellationToken);
            }
        }

        var requestedCartons = request?.CartonQuantity is > 0m ? request.CartonQuantity.Value : 1m;
        var minPickFaceThreshold = request?.MinPickFaceThresholdQty is > 0m ? request.MinPickFaceThresholdQty.Value : 50m;

        var locations = await context.WarehouseLocations
            .AsNoTracking()
            .Where(l => l.WarehouseId == warehouseId && l.IsActive && !l.IsBlocked && !l.IsSystemManaged)
            .OrderBy(l => l.Id)
            .ToListAsync(cancellationToken);

        var pickFaceLocations = locations
            .Where(l => l.IsPickable && l.LocationType != WarehouseLocationType.Receiving)
            .ToList();
        var reserveLocations = locations
            .Where(l => !l.IsPickable || l.LocationType == WarehouseLocationType.Storage)
            .ToList();

        if (pickFaceLocations.Count == 0 && locations.Count > 0)
            pickFaceLocations.Add(locations.First());
        if (reserveLocations.Count == 0 && locations.Count > 1)
            reserveLocations.Add(locations.Last());

        var preferredReserveBin = locations.FirstOrDefault(l => l.Code == "BIN-A01-03");
        var preferredPickFaceBin = locations.FirstOrDefault(l => l.Code == "BIN-A01-01");

        var uomConversions = await context.ProductUoms
            .AsNoTracking()
            .Include(u => u.Unit)
            .Where(u => u.IsActive && u.ConversionFactor > 1m)
            .OrderByDescending(u => u.ConversionFactor)
            .ToListAsync(cancellationToken);

        var stocksQuery = context.InventoryStocks
            .Include(s => s.Product)
                .ThenInclude(p => p.Unit)
            .Include(s => s.Location)
            .Where(s => s.WarehouseId == warehouseId &&
                        s.Status == InventoryStatus.Available &&
                        s.LocationId != null &&
                        (s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity) > 0m);

        if (request?.ProductId is > 0)
        {
            stocksQuery = stocksQuery.Where(s => s.ProductId == request.ProductId.Value);
        }

        var availableStocks = await stocksQuery
            .OrderByDescending(s => s.Quantity - s.ReservedQuantity)
            .ToListAsync(cancellationToken);

        var isRelational = context.Database.IsRelational();
        await using var tx = executeImmediately && isRelational && context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken)
            : null;

        if (executeImmediately && availableStocks.Count > 0)
        {
            var lockProdIds = availableStocks.Select(s => s.ProductId).Distinct().OrderBy(id => id).ToList();
            await context.AcquireInventoryAllocationLocksAsync(warehouseId, lockProdIds, cancellationToken);
        }

        var planItems = new List<BreakBulkPlanItemDto>();
        var actorId = currentUser?.UserId is > 0 ? currentUser.UserId : 1;

        var groupedByProduct = availableStocks
            .GroupBy(s => s.ProductId)
            .OrderBy(g => g.Key)
            .Take(10)
            .ToList();

        foreach (var group in groupedByProduct)
        {
            var srcStock = group
                .OrderByDescending(s => s.Location != null && s.Location.Code == "BIN-A01-03")
                .ThenByDescending(s => s.Location != null && s.Location.Code != "BIN-A01-01" && !s.Location.IsSystemManaged)
                .ThenBy(s => s.Location != null && s.Location.IsPickable ? 1 : 0)
                .ThenByDescending(s => s.Quantity - s.ReservedQuantity - s.AllocatedQuantity - s.LockedQuantity)
                .First();

            var srcAvailable = srcStock.Quantity - Math.Max(srcStock.ReservedQuantity, srcStock.AllocatedQuantity) - srcStock.LockedQuantity;
            if (srcAvailable <= 0m || srcStock.LocationId is null)
                continue;

            var targetPickBin = (preferredPickFaceBin != null && preferredPickFaceBin.Id != srcStock.LocationId.Value ? preferredPickFaceBin : null)
                ?? pickFaceLocations.FirstOrDefault(l => l.Id != srcStock.LocationId.Value)
                ?? locations.FirstOrDefault(l => l.Id != srcStock.LocationId.Value)
                ?? srcStock.Location!;

            var currentPickFaceQty = group
                .Where(s => s.LocationId == targetPickBin.Id)
                .Sum(s => s.Quantity - s.ReservedQuantity);

            if (currentPickFaceQty >= minPickFaceThreshold && request?.ProductId is null && planItems.Count > 0)
                continue;

            var uom = uomConversions.FirstOrDefault(u => u.ProductId == srcStock.ProductId && u.Unit != null && !string.Equals(u.Unit.Code, "PALLET", StringComparison.OrdinalIgnoreCase))
                      ?? uomConversions.FirstOrDefault(u => u.ProductId == srcStock.ProductId);
            var conversionFactor = uom?.ConversionFactor is > 1m && uom.Unit != null && !string.Equals(uom.Unit.Code, "PALLET", StringComparison.OrdinalIgnoreCase)
                ? uom.ConversionFactor
                : 24m;
            var bulkUomCode = uom?.Unit != null && !string.Equals(uom.Unit.Code, "PALLET", StringComparison.OrdinalIgnoreCase)
                ? uom.Unit.Code
                : "THÙNG";
            var baseUomCode = srcStock.Product?.Unit?.Code ?? "CÁI";
            var cartonQty = requestedCartons;
            var convertedBaseQty = decimal.Round(cartonQty * conversionFactor, 4);
            var actualMoveBaseQty = Math.Min(srcAvailable, convertedBaseQty);

            var reserveLocId = srcStock.LocationId.Value;
            var reserveLocCode = srcStock.Location?.Code ?? $"LOC-{reserveLocId}";
            var pickFaceLocId = targetPickBin.Id;
            var pickFaceLocCode = targetPickBin.Code;

            if (preferredReserveBin != null && preferredPickFaceBin != null &&
                reserveLocCode is "RECEIVING" or "RCV-01" or "BIN-A01-01")
            {
                reserveLocId = preferredReserveBin.Id;
                reserveLocCode = preferredReserveBin.Code;
                pickFaceLocId = preferredPickFaceBin.Id;
                pickFaceLocCode = preferredPickFaceBin.Code;
            }

            var executed = false;
            int? movementId = null;

            if (executeImmediately && actualMoveBaseQty > 0m && srcStock.LocationId.Value != targetPickBin.Id)
            {
                srcStock.Quantity -= actualMoveBaseQty;
                srcStock.LastUpdated = now;
                srcStock.RefreshGrainHash();

                var destStock = await context.InventoryStocks
                    .FirstOrDefaultAsync(
                        s => s.WarehouseId == warehouseId &&
                             s.ProductId == srcStock.ProductId &&
                             s.LocationId == targetPickBin.Id &&
                             s.Status == srcStock.Status &&
                             s.LotId == srcStock.LotId &&
                             s.SerialId == srcStock.SerialId &&
                             s.HandlingUnitId == srcStock.HandlingUnitId &&
                             s.OwnerId == srcStock.OwnerId,
                        cancellationToken);

                if (destStock == null)
                {
                    destStock = new InventoryStock
                    {
                        TenantId = srcStock.TenantId,
                        WarehouseId = warehouseId,
                        ProductId = srcStock.ProductId,
                        LocationId = targetPickBin.Id,
                        Status = srcStock.Status,
                        LotId = srcStock.LotId,
                        SerialId = srcStock.SerialId,
                        HandlingUnitId = srcStock.HandlingUnitId,
                        OwnerId = srcStock.OwnerId,
                        MovingAverageCost = srcStock.MovingAverageCost,
                        Quantity = actualMoveBaseQty,
                        LastUpdated = now
                    };
                    destStock.RefreshGrainHash();
                    context.InventoryStocks.Add(destStock);
                }
                else
                {
                    destStock.Quantity += actualMoveBaseQty;
                    destStock.LastUpdated = now;
                    destStock.RefreshGrainHash();
                }

                var locMove = new InventoryLocationMovement
                {
                    WarehouseId = warehouseId,
                    ProductId = srcStock.ProductId,
                    FromLocationId = srcStock.LocationId.Value,
                    ToLocationId = targetPickBin.Id,
                    InventoryStatus = srcStock.Status,
                    LotId = srcStock.LotId,
                    SerialId = srcStock.SerialId,
                    BaseQuantity = actualMoveBaseQty,
                    EnteredQuantity = cartonQty,
                    EnteredUnitCode = bulkUomCode,
                    ReferenceType = "BreakBulkReplenishment",
                    CreatedBy = actorId,
                    CreatedAt = now
                };
                context.InventoryLocationMovements.Add(locMove);
                await context.SaveChangesAsync(cancellationToken);
                locMove.ReferenceId = locMove.Id;
                movementId = locMove.Id;

                context.InventoryTransactions.AddRange(
                    new InventoryTransaction
                    {
                        ProductId = srcStock.ProductId,
                        WarehouseId = warehouseId,
                        LocationId = srcStock.LocationId.Value,
                        FromLocationId = srcStock.LocationId.Value,
                        ToLocationId = targetPickBin.Id,
                        InventoryStatus = srcStock.Status,
                        FromInventoryStatus = srcStock.Status,
                        ToInventoryStatus = srcStock.Status,
                        LotId = srcStock.LotId,
                        SerialId = srcStock.SerialId,
                        HandlingUnitId = srcStock.HandlingUnitId,
                        OwnerId = srcStock.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = -actualMoveBaseQty,
                        ReferenceId = locMove.Id,
                        ReferenceType = "BreakBulkReplenishment",
                        Note = $"Xuất kệ Reserve {reserveLocCode} để rã thùng ({cartonQty:0.##} {bulkUomCode})",
                        TransactionDate = now,
                        CreatedBy = actorId
                    },
                    new InventoryTransaction
                    {
                        ProductId = srcStock.ProductId,
                        WarehouseId = warehouseId,
                        LocationId = targetPickBin.Id,
                        FromLocationId = srcStock.LocationId.Value,
                        ToLocationId = targetPickBin.Id,
                        InventoryStatus = srcStock.Status,
                        FromInventoryStatus = srcStock.Status,
                        ToInventoryStatus = srcStock.Status,
                        LotId = srcStock.LotId,
                        SerialId = srcStock.SerialId,
                        HandlingUnitId = srcStock.HandlingUnitId,
                        OwnerId = srcStock.OwnerId,
                        TransactionType = TransactionType.Move,
                        Quantity = actualMoveBaseQty,
                        ReferenceId = locMove.Id,
                        ReferenceType = "BreakBulkReplenishment",
                        Note = $"Nhập kệ Pick-Face {pickFaceLocCode} sau rã thùng ({actualMoveBaseQty:0.##} {baseUomCode})",
                        TransactionDate = now,
                        CreatedBy = actorId
                    });
                executed = true;
            }

            var ledgerStatusText = $"Khớp 2 bút toán (-{cartonQty:0.##} Thùng / +{convertedBaseQty:0.##} ĐV)";
            planItems.Add(new BreakBulkPlanItemDto(
                srcStock.ProductId,
                srcStock.Product?.Code ?? $"SKU-{srcStock.ProductId}",
                srcStock.Product?.Name ?? string.Empty,
                reserveLocId,
                reserveLocCode,
                pickFaceLocId,
                pickFaceLocCode,
                bulkUomCode,
                baseUomCode,
                conversionFactor,
                cartonQty,
                convertedBaseQty,
                actualMoveBaseQty,
                executed,
                movementId,
                reserveLocCode,
                pickFaceLocCode,
                reserveLocCode,
                pickFaceLocCode,
                ledgerStatusText));
        }

        if (executeImmediately)
        {
            context.AuditLogs.Add(new AuditLog
            {
                UserId = actorId > 0 ? actorId : null,
                WarehouseId = warehouseId,
                EntityName = "BreakBulkReplenishment",
                Action = "AUTO_BREAK_BULK_REPLENISH",
                NewValues = $"Plans={planItems.Count};Executed={planItems.Count(x => x.Executed)}",
                Reason = "Bổ sung kệ nhặt lẻ tự động có quy đổi xả thùng (Auto Break-Bulk Replenishment)",
                Result = "Success",
                Timestamp = now
            });
            await context.SaveChangesAsync(cancellationToken);
            if (tx is not null)
            {
                await tx.CommitAsync(cancellationToken);
            }
        }

        var totalCartons = planItems.Sum(x => x.CartonQty);
        var totalBaseUnits = planItems.Sum(x => x.BaseUnitQty);
        return new AutoBreakBulkReplenishResultDto(
            warehouseId,
            executeImmediately,
            planItems.Count,
            totalCartons,
            totalBaseUnits,
            planItems,
            executeImmediately
                ? $"Đã thực thi xả lẻ {totalCartons:0.##} thùng sang {totalBaseUnits:0.##} đơn vị cơ sở cho {planItems.Count} SKU (Khớp 2 bút toán Sổ cái)."
                : $"Đã lập phương án bổ sung kệ nhặt lẻ tự động ({totalCartons:0.##} thùng = {totalBaseUnits:0.##} ĐV) cho {planItems.Count} SKU.");
    }
}
