namespace ERP.Domain.Entities;

public sealed class InventoryLot
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? BestBeforeDate { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Product Product { get; set; } = null!;
    public User? CreatedByUser { get; set; }
    public ICollection<InventorySerial> Serials { get; set; } = [];
}
