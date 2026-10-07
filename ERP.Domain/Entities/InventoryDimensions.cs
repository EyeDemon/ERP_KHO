using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class InventoryStatusDefinition
{
    public InventoryStatus Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsReservable { get; set; }
    public bool IsAllocatable { get; set; }
    public bool IsPickable { get; set; }
    public bool IsShippable { get; set; }
    public bool IsSystem { get; set; } = true;
}

public sealed class InventoryLot
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Product Product { get; set; } = null!;
    public ICollection<InventorySerial> Serials { get; set; } = [];
}

public sealed class InventorySerial
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Product Product { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
}

public sealed class ImportReceiptInventoryIdentity
{
    public int Id { get; set; }
    public int ImportReceiptDetailId { get; set; }
    public int ProductId { get; set; }
    public InventoryStatus TargetStatus { get; set; }
    public decimal BaseQuantity { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? SerialNumber { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ImportReceiptDetail ImportReceiptDetail { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
