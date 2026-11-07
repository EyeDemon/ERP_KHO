namespace ERP.Domain.Enums;

public enum DockAppointmentDirection
{
    Inbound = 0,
    Outbound = 1
}

public enum DockAppointmentStatus
{
    Draft = 0,
    Confirmed = 1,
    Arrived = 2,
    CheckedIn = 3,
    DockAssigned = 4,
    InService = 5,
    Completed = 6,
    Cancelled = 7,
    NoShow = 8,
    Exception = 9
}
