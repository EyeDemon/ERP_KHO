namespace ERP.Domain.Enums;

public enum CustomerReturnStatus
{
    Quarantined = 0,
    Inspected = 1,
    Completed = 2,
    Cancelled = 3
}

public enum ReturnItemCondition
{
    Good = 0,
    DamagedBox = 1,
    Defective = 2
}

public enum ReturnDisposition
{
    Pending = 0,
    RestockAvailable = 1,
    RefurbishRepack = 2,
    RtvVendor = 3,
    ScrapDisposal = 4
}

public static class CustomerReturnEnumExtensions
{
    public static string ToCode(this CustomerReturnStatus status) => status switch
    {
        CustomerReturnStatus.Quarantined => "QUARANTINED",
        CustomerReturnStatus.Inspected => "INSPECTED",
        CustomerReturnStatus.Completed => "COMPLETED",
        CustomerReturnStatus.Cancelled => "CANCELLED",
        _ => "QUARANTINED"
    };

    public static CustomerReturnStatus ParseStatus(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "INSPECTED" => CustomerReturnStatus.Inspected,
            "COMPLETED" => CustomerReturnStatus.Completed,
            "CANCELLED" => CustomerReturnStatus.Cancelled,
            _ => CustomerReturnStatus.Quarantined
        };

    public static string ToCode(this ReturnItemCondition condition) => condition switch
    {
        ReturnItemCondition.Good => "GOOD",
        ReturnItemCondition.DamagedBox => "DAMAGED_BOX",
        ReturnItemCondition.Defective => "DEFECTIVE",
        _ => "GOOD"
    };

    public static ReturnItemCondition ParseCondition(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "DAMAGED_BOX" => ReturnItemCondition.DamagedBox,
            "DEFECTIVE" => ReturnItemCondition.Defective,
            _ => ReturnItemCondition.Good
        };

    public static string ToCode(this ReturnDisposition disposition) => disposition switch
    {
        ReturnDisposition.Pending => "PENDING",
        ReturnDisposition.RestockAvailable => "RESTOCK_AVAILABLE",
        ReturnDisposition.RefurbishRepack => "REFURBISH_REPACK",
        ReturnDisposition.RtvVendor => "RTV_VENDOR",
        ReturnDisposition.ScrapDisposal => "SCRAP_DISPOSAL",
        _ => "PENDING"
    };

    public static ReturnDisposition ParseDisposition(string? value) =>
        value?.Trim().ToUpperInvariant() switch
        {
            "RESTOCK_AVAILABLE" => ReturnDisposition.RestockAvailable,
            "REFURBISH_REPACK" => ReturnDisposition.RefurbishRepack,
            "RTV_VENDOR" => ReturnDisposition.RtvVendor,
            "SCRAP_DISPOSAL" => ReturnDisposition.ScrapDisposal,
            _ => ReturnDisposition.Pending
        };
}
