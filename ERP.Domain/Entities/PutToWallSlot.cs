using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class PutToWallSlot : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int WarehouseId { get; set; }
    public int PickingTaskId { get; set; }
    public int? PickingTaskLineId { get; set; }
    public int SlotIndex { get; set; }
    public string SlotCode { get; set; } = string.Empty;
    public string ToteBarcode { get; set; } = string.Empty;
    public string? OrderReference { get; set; }
    public int? ProductId { get; set; }
    public string? ProductCode { get; set; }
    public decimal RequiredQuantity { get; set; }
    public decimal SortedQuantity { get; set; }
    public string SlotStatus { get; set; } = "WAITING";
    public int? LastSortedByUserId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}
