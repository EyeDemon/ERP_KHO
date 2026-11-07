using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class ThreePlBillingStatement : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int? WarehouseId { get; set; }
    public int PartnerId { get; set; }
    public string StatementCode { get; set; } = string.Empty;
    public DateTime PeriodFromUtc { get; set; }
    public DateTime PeriodToUtc { get; set; }
    public decimal TotalOnHandQty { get; set; }
    public decimal TotalInboundQty { get; set; }
    public decimal TotalOutboundQty { get; set; }
    public decimal StorageFeeAmount { get; set; }
    public decimal InboundHandlingFeeAmount { get; set; }
    public decimal OutboundHandlingFeeAmount { get; set; }
    public decimal TotalBillingAmount { get; set; }
    public string? ReceivableInvoiceCode { get; set; }
    public string Status { get; set; } = "ISSUED";
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];
}
