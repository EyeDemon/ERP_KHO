using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class InventoryTransaction
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int? LocationId { get; set; }
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public InventoryStatus InventoryStatus { get; set; } = InventoryStatus.Available;
    public InventoryStatus? FromInventoryStatus { get; set; }
    public InventoryStatus? ToInventoryStatus { get; set; }
    public TransactionType TransactionType { get; set; }
    public decimal Quantity { get; set; }
    public int? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public string? Note { get; set; }

    // Navigation
    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public WarehouseLocation? Location { get; set; }
    public WarehouseLocation? FromLocation { get; set; }
    public WarehouseLocation? ToLocation { get; set; }
    public InventoryLot? Lot { get; set; }
    public InventorySerial? Serial { get; set; }
    public InventoryStatusDefinition StatusDefinition { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
