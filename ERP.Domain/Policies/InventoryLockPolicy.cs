using ERP.Domain.Enums;

namespace ERP.Domain.Policies;

public static class InventoryLockPolicy
{
    public static bool CanAutoExpire(InventoryLockType lockType) =>
        lockType is InventoryLockType.CountFreeze
            or InventoryLockType.MaintenanceFreeze
            or InventoryLockType.ManualOperationalLock;
}
