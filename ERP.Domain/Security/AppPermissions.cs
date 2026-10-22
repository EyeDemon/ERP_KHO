namespace ERP.Domain.Security;

public static class AppPermissions
{
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string InventoryRead = "inventory.read";
    public const string InventoryCostRead = "inventory.cost.read";
    public const string ExportReceiptReprintAwb = "export_receipt.reprint_awb";

    public static readonly IReadOnlyList<PermissionMetadata> Definitions =
    [
        new(UsersManage, "Quản lý người dùng và phân quyền kho"),
        new(RolesManage, "Quản lý vai trò và quyền hệ thống"),
        new(InventoryRead, "Xem tồn kho và thẻ kho"),
        new(InventoryCostRead, "Xem đơn giá, giá vốn bình quân và giá trị tồn kho"),
        new(ExportReceiptReprintAwb, "In lại vận đơn AWB từ lần thứ 2 trở đi")
    ];

    public static readonly string[] All =
    [
        UsersManage,
        RolesManage,
        InventoryRead,
        InventoryCostRead,
        ExportReceiptReprintAwb
    ];

    public static IReadOnlyList<string> GetDefaultPermissionsForRole(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return [];
        }

        return roleName.Trim() switch
        {
            "Admin" => All,
            "Manager" => [InventoryRead, InventoryCostRead, ExportReceiptReprintAwb],
            "Accountant" => [InventoryRead, InventoryCostRead],
            "WarehouseStaff" => [InventoryRead],
            "Viewer" => [InventoryRead],
            _ => []
        };
    }
}

public sealed record PermissionMetadata(string Code, string Description);
