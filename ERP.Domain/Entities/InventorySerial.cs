namespace ERP.Domain.Entities;

public sealed class InventorySerial
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? LotId { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public bool IsConsumed { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Product Product { get; set; } = null!;
    public InventoryLot? Lot { get; set; }
    public User? CreatedByUser { get; set; }
}
