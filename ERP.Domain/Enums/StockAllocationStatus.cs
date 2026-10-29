namespace ERP.Domain.Enums;

public enum StockAllocationStatus
{
    Active = 0,
    Picking = 1,
    Picked = 2,
    Consumed = 3,
    Released = 4,
    Reallocated = 5
}
