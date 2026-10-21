using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class ShipmentFinancialLedger : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int? ExportReceiptId { get; set; }
    public int? ShipmentId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public decimal ExpectedCodAmount { get; set; }
    public decimal ActualCodCollected { get; set; }
    public decimal EstimatedShippingFee { get; set; }
    public decimal ActualShippingFee { get; set; }
    public decimal PlatformCommissionFee { get; set; }
    public decimal ReturnFee { get; set; }
    public decimal NetPayoutAmount { get; set; }
    public decimal FeeOverchargeVariance { get; set; }
    public string ReconciliationStatus { get; set; } = "PENDING"; // PENDING | MATCHED | DISCREPANCY
    public DateTime? SettledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ExportReceipt? ExportReceipt { get; set; }
    public Shipment? Shipment { get; set; }
}
