using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class InventoryStock
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int? LocationId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public InventoryStatus Status { get; set; } = InventoryStatus.Available;
    
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public decimal Quantity { get; set; }

    public decimal ReservedQuantity { get; set; }
    
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];

    // Navigation
    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public WarehouseLocation? Location { get; set; }
    public InventoryLot? Lot { get; set; }
    public InventorySerial? Serial { get; set; }
}
