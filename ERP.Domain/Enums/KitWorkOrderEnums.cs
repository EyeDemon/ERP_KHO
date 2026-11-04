namespace ERP.Domain.Enums;

public enum KitWorkOrderStatus
{
    Draft = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

public static class KitWorkOrderEnumExtensions
{
    public static string ToCode(this KitWorkOrderStatus status) => status switch
    {
        KitWorkOrderStatus.Draft => "Draft",
        KitWorkOrderStatus.InProgress => "InProgress",
        KitWorkOrderStatus.Completed => "Completed",
        KitWorkOrderStatus.Cancelled => "Cancelled",
        _ => "Draft"
    };

    public static KitWorkOrderStatus ParseStatus(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "INPROGRESS" or "IN_PROGRESS" => KitWorkOrderStatus.InProgress,
            "COMPLETED" => KitWorkOrderStatus.Completed,
            "CANCELLED" or "CANCELED" => KitWorkOrderStatus.Cancelled,
            _ => KitWorkOrderStatus.Draft
        };
}
