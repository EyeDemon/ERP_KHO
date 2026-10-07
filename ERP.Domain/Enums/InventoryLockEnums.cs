namespace ERP.Domain.Enums;

public enum InventoryLockType
{
    CountFreeze = 0,
    QualityHold = 1,
    InvestigationHold = 2,
    RecallHold = 3,
    MaintenanceFreeze = 4,
    ManualOperationalLock = 5
}

public enum InventoryLockStatus
{
    Active = 0,
    Released = 1,
    Expired = 2
}
