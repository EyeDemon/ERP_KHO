namespace ERP.Application.Inventory;

public sealed record StockTransferReturnReasonDto(string Code, string Name);

public static class StockTransferReturnReasonCatalog
{
    public static IReadOnlyList<StockTransferReturnReasonDto> All { get; } =
    [
        new("TRANSFER_DISPATCH_ERROR", "Xuất điều chuyển sai"),
        new("TRANSFER_DESTINATION_UNAVAILABLE", "Kho đích không thể tiếp nhận"),
        new("TRANSFER_ROUTE_ERROR", "Sai tuyến điều chuyển")
    ];

    public static bool IsAllowed(string? code) =>
        !string.IsNullOrWhiteSpace(code) &&
        All.Any(item => string.Equals(item.Code, code, StringComparison.Ordinal));
}
