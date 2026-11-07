using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Infrastructure.Persistence;

namespace ERP.Infrastructure.Queries;

public static class InventoryLockQueryExtensions
{
    public static IQueryable<InventoryLock> EffectiveAt(
        this IQueryable<InventoryLock> query,
        DateTime now) =>
        query.Where(x =>
            x.Status == InventoryLockStatus.Active &&
            (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > now));

    public static IQueryable<InventoryStock> UnlockedAt(
        this IQueryable<InventoryStock> query,
        ErpKhoDbContext context,
        DateTime now) =>
        query.Where(stock => !context.InventoryLocks
            .EffectiveAt(now)
            .Any(lockRow =>
                lockRow.WarehouseId == stock.WarehouseId &&
                (!lockRow.LocationId.HasValue || lockRow.LocationId == stock.LocationId) &&
                (!lockRow.ProductId.HasValue || lockRow.ProductId == stock.ProductId) &&
                (!lockRow.InventoryStatus.HasValue || lockRow.InventoryStatus == stock.Status) &&
                (!lockRow.LotId.HasValue || lockRow.LotId == stock.LotId) &&
                (!lockRow.SerialId.HasValue || lockRow.SerialId == stock.SerialId)));
}
