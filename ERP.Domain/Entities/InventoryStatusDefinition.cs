using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class InventoryStatusDefinition
{
    public InventoryStatus Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsReservable { get; set; }
    public bool IsAllocatable { get; set; }
    public bool IsPickable { get; set; }
    public bool IsShippable { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
