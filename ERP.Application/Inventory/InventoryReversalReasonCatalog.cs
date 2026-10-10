using ERP.Application.DTOs;
using ERP.Domain.Enums;

namespace ERP.Application.Inventory;

/// <summary>
/// Fixed reason codes for current Move/StatusChange reversal sources.
/// Document-native reversal codes will be handled in their source workflows.
/// </summary>
public static class InventoryReversalReasonCatalog
{
    public static IReadOnlyList<InventoryReversalReasonDto> All { get; } =
    [
        new() { Code = "LOCATION_ERROR", Name = "Sai vị trí lưu kho", TransactionType = nameof(TransactionType.Move) },
        new() { Code = "STATUS_ERROR", Name = "Sai trạng thái tồn kho", TransactionType = nameof(TransactionType.StatusChange) },
        new() { Code = "OPERATION_CORRECTION", Name = "Hiệu chỉnh nghiệp vụ sau kiểm tra" },
        new() { Code = "DATA_ENTRY_ERROR", Name = "Sai sót nhập liệu" }
    ];

    public static bool IsAllowed(string? code, TransactionType originalType) =>
        !string.IsNullOrWhiteSpace(code) &&
        All.Any(x => x.Code == code &&
                     (x.TransactionType is null || x.TransactionType == originalType.ToString()));
}
