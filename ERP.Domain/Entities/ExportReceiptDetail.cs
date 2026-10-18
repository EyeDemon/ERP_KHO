using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class ExportReceiptDetail : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int ExportReceiptId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxPercent { get; set; }
    public string? Note { get; set; }
    public int? BaseUomIdSnapshot { get; set; }
    public string? BaseUomCodeSnapshot { get; set; }
    public string? BaseUomNameSnapshot { get; set; }
    public int? BaseUomDecimalPlacesSnapshot { get; set; }

    // Navigation
    public ExportReceipt ExportReceipt { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
