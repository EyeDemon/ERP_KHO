namespace ERP.Domain.Enums;

public enum PurchaseOrderStatus
{
    Draft = 0,
    Open = 1,
    PartiallyReceived = 2,
    Received = 3,
    Closed = 4,
    Cancelled = 5
}

public enum AsnStatus
{
    Draft = 0,
    Confirmed = 1,
    InTransit = 2,
    Arrived = 3,
    Receiving = 4,
    Completed = 5,
    Cancelled = 6
}
