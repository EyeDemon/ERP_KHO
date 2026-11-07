namespace ERP.Domain.Enums;

public enum InventoryStatus
{
    Available = 0,
    QcHold = 1,
    Quarantine = 2,
    Damaged = 3,
    Rejected = 4,
    Blocked = 5,
    Expired = 6,
    RecallBlocked = 7
}
