using ERP.Application.Exceptions;
using ERP.Application.Interfaces;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;
using ERP.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Services;

public sealed class InventoryEligibilityService(ErpKhoDbContext context) : IInventoryEligibilityService
{
    public async Task EnsureBucketEligibleAsync(
        int productId,
        int warehouseId,
        int locationId,
        int? lotId,
        int? serialId,
        InventoryEligibilityOperation operation,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var exists = await context.InventoryStocks.AsNoTracking()
            .Where(x =>
                x.ProductId == productId &&
                x.WarehouseId == warehouseId &&
                x.LocationId == locationId &&
                x.LotId == lotId &&
                x.SerialId == serialId)
            .EligibleFor(context, operation, now)
            .AnyAsync(cancellationToken);

        if (exists) return;

        var locked = await HasActiveLockAsync(
            warehouseId,
            locationId,
            productId,
            lotId,
            serialId,
            null,
            cancellationToken);
        if (locked)
            throw Conflict("INV_STOCK_LOCKED", "Bucket tồn kho đang bị khóa bởi Inventory Lock.");

        var stock = await context.InventoryStocks.AsNoTracking()
            .Include(x => x.Lot)
            .Include(x => x.Serial)
            .SingleOrDefaultAsync(x =>
                x.ProductId == productId &&
                x.WarehouseId == warehouseId &&
                x.LocationId == locationId &&
                x.LotId == lotId &&
                x.SerialId == serialId,
                cancellationToken);

        if (stock is null)
            throw Conflict("INV_INSUFFICIENT_ON_HAND", "Không tìm thấy bucket tồn kho tương ứng.");

        if (stock.Lot?.ExpiryDate is DateTime expiry && expiry <= now)
            throw Conflict("LOT_EXPIRED", "Lot đã hết hạn và không còn đủ điều kiện thao tác.");

        var code = operation switch
        {
            InventoryEligibilityOperation.Reservation => "INV_STATUS_NOT_RESERVABLE",
            InventoryEligibilityOperation.Allocation => "INV_STATUS_NOT_ALLOCATABLE",
            InventoryEligibilityOperation.Picking => "INV_STATUS_NOT_PICKABLE",
            InventoryEligibilityOperation.Shipping => "INV_STATUS_NOT_SHIPPABLE",
            _ => "INV_STATUS_CHANGE_NOT_ALLOWED"
        };
        throw Conflict(code, $"Bucket tồn kho không đủ điều kiện cho thao tác {operation}.");
    }

    public Task<bool> HasActiveLockAsync(
        int warehouseId,
        int? locationId = null,
        int? productId = null,
        int? lotId = null,
        int? serialId = null,
        int? handlingUnitId = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return context.InventoryLocks.AsNoTracking()
            .ActiveAt(now)
            .AnyAsync(l =>
                l.WarehouseId == warehouseId &&
                (!l.LocationId.HasValue || l.LocationId == locationId) &&
                (!l.ProductId.HasValue || l.ProductId == productId) &&
                (!l.LotId.HasValue || l.LotId == lotId) &&
                (!l.SerialId.HasValue || l.SerialId == serialId) &&
                (!l.HandlingUnitId.HasValue || l.HandlingUnitId == handlingUnitId),
                cancellationToken);
    }

    private static BusinessRuleException Conflict(string code, string message)
    {
        var ex = new BusinessRuleException(message);
        ex.Data["HttpStatusCode"] = 409;
        ex.Data["ErrorCode"] = code;
        return ex;
    }
}
