namespace ERP.Domain.Entities;

public class ExportReceiptDetail
{
    public int Id { get; set; }
    public int ExportReceiptId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Note { get; set; }

    // Navigation
    public ExportReceipt ExportReceipt { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
