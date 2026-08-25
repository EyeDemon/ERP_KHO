namespace ERP.Domain.Enums;

public enum StockReservationStatus
{
    Active = 0,
    PartiallyConsumed = 1,
    Consumed = 2,
    Released = 3,
    Expired = 4,
    Cancelled = 5
}
