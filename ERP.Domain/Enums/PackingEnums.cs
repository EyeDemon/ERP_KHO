namespace ERP.Domain.Enums;

public enum PackingSessionStatus
{
    Open = 0,
    InProgress = 1,
    Packed = 2,
    Closed = 3,
    Exception = 4,
    Cancelled = 5
}

public enum HandlingUnitType
{
    Pallet = 0,
    Carton = 1,
    Tote = 2,
    Container = 3,
    Custom = 4
}

public enum HandlingUnitStatus
{
    Open = 0,
    InUse = 1,
    Closed = 2,
    Staged = 3,
    Loaded = 4,
    Shipped = 5,
    Cancelled = 6
}
