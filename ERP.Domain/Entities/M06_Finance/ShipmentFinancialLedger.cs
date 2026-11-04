using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class ShipmentFinancialLedger : ITenantEntity
{
    private decimal _shippingFeeVariance;
    private decimal _netSettlementAmount;

    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int? ExportReceiptId { get; set; }
    public int? ShipmentId { get; set; }
    public string CarrierCode { get; set; } = string.Empty;
    public string TrackingNumber { get; set; } = string.Empty;
    public decimal ExpectedCodAmount { get; set; }
    public decimal ActualCodCollected { get; set; }
    public decimal CodVariance { get; set; }
    public decimal EstimatedShippingFee { get; set; }
    public decimal ActualShippingFee { get; set; }
    public decimal ShippingFeeVariance
    {
        get => _shippingFeeVariance;
        set => _shippingFeeVariance = value;
    }
    public decimal PlatformCommissionFee { get; set; }
    public decimal SellerVoucherAmount { get; set; }
    public decimal ReturnFee { get; set; }
    public decimal NetSettlementAmount
    {
        get => _netSettlementAmount;
        set => _netSettlementAmount = value;
    }
    public decimal NetPayoutAmount
    {
        get => _netSettlementAmount;
        set => _netSettlementAmount = value;
    }
    public decimal FeeOverchargeVariance
    {
        get => _shippingFeeVariance;
        set => _shippingFeeVariance = value;
    }
    public string ReconciliationStatus { get; set; } = "PENDING"; // PENDING | MATCHED | DISCREPANCY
    public DateTime? SettledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public ExportReceipt? ExportReceipt { get; set; }
    public Shipment? Shipment { get; set; }
}
