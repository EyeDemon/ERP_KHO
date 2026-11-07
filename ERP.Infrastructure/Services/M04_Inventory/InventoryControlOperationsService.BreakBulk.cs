using ERP.Application.Common;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed partial class InventoryControlOperationsService
{
    public async Task<WmsEndpointOutcome> ExecuteBreakBulkReplenishmentAsync(
        ExecuteBreakBulkReplenishmentInput? request = null,
        int? actorUserId = null,
        IWarehouseAuthorizationService? warehouseAuth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveAuth = warehouseAuth ?? warehouseAccess;
        var warehouseId = request?.WarehouseId;
        var (targetWarehouseId, whError) = await ResolveTargetWarehouseIdAsync(warehouseId, effectiveAuth, cancellationToken);
        if (whError != null)
        {
            return whError;
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
            return await ReverseBreakBulkReplenishmentCoreAsync(
                request,
                targetWarehouseId,
                resolvedProductId,
                resolvedProductCode,
                resolvedProductName,
                resolvedSourceLocationId,
                resolvedTargetLocationId,
                resolvedSourceUomCode,
                baseUomCode,
                sourcePackQty,
                conversionFactor,
                movedBaseQty,
                effectiveActorId,
                now,
                tx,
                cancellationToken);
        }

        return await ExecuteForwardBreakBulkReplenishmentCoreAsync(
            request,
            targetWarehouseId,
            resolvedProductId,
            resolvedProductCode,
            resolvedProductName,
            resolvedUomId,
            resolvedSourceUomCode,
            baseUomCode,
            sourcePackQty,
            conversionFactor,
            movedBaseQty,
            resolvedSourceLocationId,
            resolvedSourceLocationCode,
            resolvedTargetLocationId,
            resolvedTargetLocationCode,
            effectiveActorId,
            now,
            tx,
            cancellationToken);
    }
}
