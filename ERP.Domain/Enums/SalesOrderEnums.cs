namespace ERP.Domain.Enums;

public enum SalesOrderStatus
{
    Draft = 0,
    Hold = 1,
    Released = 2,
    PartiallyFulfilled = 3,
    Fulfilled = 4,
    Cancelled = 5
}

public enum BackorderStatus
{
    Open = 0,
    PartiallyAllocated = 1,
    Allocated = 2,
    Fulfilled = 3,
    Cancelled = 4
}
