using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class ImportReceipt
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    
    [System.ComponentModel.DataAnnotations.ConcurrencyCheck]
    public ReceiptStatus Status { get; set; } = ReceiptStatus.Draft;
    
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }

    // Navigation
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User? ApprovedByUser { get; set; }
    public ICollection<ImportReceiptDetail> Details { get; set; } = new List<ImportReceiptDetail>();
}
