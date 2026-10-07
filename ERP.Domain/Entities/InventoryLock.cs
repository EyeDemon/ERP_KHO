using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class InventoryLock
{
    public int Id { get; set; }
    public InventoryLockType LockType { get; set; }
    public InventoryLockStatus Status { get; set; } = InventoryLockStatus.Active;
    public int WarehouseId { get; set; }
    public int? LocationId { get; set; }
    public int? ProductId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public int? HandlingUnitId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public int? ReleasedBy { get; set; }
    public string? ReleaseReason { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Warehouse Warehouse { get; set; } = null!;
    public WarehouseLocation? Location { get; set; }
    public Product? Product { get; set; }
    public InventoryLot? Lot { get; set; }
    public InventorySerial? Serial { get; set; }
    public HandlingUnit? HandlingUnit { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public User? ReleasedByUser { get; set; }
}
