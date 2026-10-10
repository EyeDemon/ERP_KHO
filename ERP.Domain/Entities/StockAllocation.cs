using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class StockAllocation
{
    public int Id { get; set; }
    public string AllocationCode { get; set; } = string.Empty;
    public int ReservationId { get; set; }
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public int LocationId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public InventoryStatus InventoryStatus { get; set; } = InventoryStatus.Available;
    public decimal Quantity { get; set; }
    public StockAllocationStatus Status { get; set; } = StockAllocationStatus.Active;
    public AllocationStrategy Strategy { get; set; } = AllocationStrategy.LocationOrder;
    public string SelectionReason { get; set; } = string.Empty;
    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
    public int AllocatedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public int? ReleasedBy { get; set; }
    public string? ReleaseReason { get; set; }
    public int Version { get; set; }

    public StockReservation Reservation { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public WarehouseLocation Location { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public InventorySerial? Serial { get; set; }
    public User AllocatedByUser { get; set; } = null!;
    public User? ReleasedByUser { get; set; }
}
