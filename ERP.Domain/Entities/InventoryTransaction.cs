using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class InventoryTransaction
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int? LocationId { get; set; }
    public InventoryStatus InventoryStatus { get; set; } = InventoryStatus.Available;
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
    public User CreatedByUser { get; set; } = null!;
}
