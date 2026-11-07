namespace ERP.Domain.Enums;

public enum PickingTaskStatus
{
    Open = 0,
    Assigned = 1,
    InProgress = 2,
    ShortPick = 3,
    Resolved = 4,
    Completed = 5,
    Cancelled = 6
}

public enum PickingTaskLineStatus
{
    Open = 0,
    InProgress = 1,
    Picked = 2,
    ShortPick = 3,
    Resolved = 4
}

public enum ShortPickExceptionStatus
{
    Open = 0,
    Resolved = 1
}

public enum ShortPickResolutionType
{
    AlternativeLocation = 0,
    AlternativeLot = 1,
    Replenish = 2,
    Backorder = 3,
    CancelRemainder = 4,
    SupervisorOverride = 5
}
