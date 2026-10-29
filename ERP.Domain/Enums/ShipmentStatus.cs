namespace ERP.Domain.Enums;

public enum ShipmentStatus
{
    Draft = 0,
    Ready = 1,
    Staging = 2,
    Loading = 3,
    Loaded = 4,
    Dispatched = 5,
    InTransit = 6,
    Delivered = 7,
    DeliveryFailed = 8,
    Cancelled = 9,
    ReturnToWarehouse = 10,
    Completed = 11
}
