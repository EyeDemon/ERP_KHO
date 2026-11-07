namespace ERP.Domain.Enums;

public enum InventoryLockType
{
    CountFreeze = 0,
    InvestigationHold = 2,
    MaintenanceFreeze = 4,
    ManualOperationalLock = 5
}

public enum InventoryLockStatus
{
    Active = 0,
    Released = 1,
    Expired = 2
}
