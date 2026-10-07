using ERP.Domain.Entities;
using ERP.Domain.Enums;
using ERP.Domain.Policies;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Queries;

public static class InventoryEligibilityQuery
{
    public static IQueryable<InventoryStock> EligibleFor(
        this IQueryable<InventoryStock> query,
        ErpKhoDbContext context,
        InventoryEligibilityOperation operation,
        DateTime now)
    {
        var statuses = operation switch
        {
            InventoryEligibilityOperation.Reservation => InventoryStatusPolicy.ReservableStatuses,
            InventoryEligibilityOperation.Allocation => InventoryStatusPolicy.AllocatableStatuses,
            InventoryEligibilityOperation.Picking => InventoryStatusPolicy.PickableStatuses,
            InventoryEligibilityOperation.Shipping => InventoryStatusPolicy.ShippableStatuses,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

        return query.Where(x =>
            statuses.Contains(x.Status) &&
            x.LocationId.HasValue &&
            x.Location != null &&
            x.Location.IsActive &&
            !x.Location.IsBlocked &&
            x.Location.IsPickable &&
            (
                x.Product.TrackingType == ProductTrackingType.None ||
                (x.Product.TrackingType == ProductTrackingType.Lot && x.LotId.HasValue) ||
                (x.Product.TrackingType == ProductTrackingType.Serial && x.SerialId.HasValue)
            ) &&
            (
                !x.Product.RequiresExpiryDate ||
                (x.LotId.HasValue && x.Lot != null && x.Lot.ExpiryDate.HasValue)
            ) &&
            (
                !x.LotId.HasValue ||
                (x.Lot != null && (!x.Lot.ExpiryDate.HasValue || x.Lot.ExpiryDate.Value > now))
            ) &&
            (
                !x.SerialId.HasValue ||
                (x.Serial != null && !x.Serial.IsConsumed)
            ) &&
            !context.InventoryLocks.Any(l =>
                l.Status == InventoryLockStatus.Active &&
                l.WarehouseId == x.WarehouseId &&
                (!l.ExpiresAt.HasValue || l.ExpiresAt.Value > now) &&
                !l.HandlingUnitId.HasValue &&
                (!l.LocationId.HasValue || l.LocationId == x.LocationId) &&
                (!l.ProductId.HasValue || l.ProductId == x.ProductId) &&
                (!l.LotId.HasValue || l.LotId == x.LotId) &&
                (!l.SerialId.HasValue || l.SerialId == x.SerialId)));
    }

    public static IQueryable<InventoryLock> ActiveAt(
        this IQueryable<InventoryLock> query,
        DateTime now) =>
        query.Where(x =>
            x.Status == InventoryLockStatus.Active &&
            (!x.ExpiresAt.HasValue || x.ExpiresAt.Value > now));
}
