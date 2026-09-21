using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class ImportReceiptDetail
{
    public int Id { get; set; }
    public int ImportReceiptId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal AcceptedQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal RejectedQuantity { get; set; }
    public decimal PostedQuantity { get; set; }
    public int OperationUnitId { get; set; }
    public string OperationUnitCodeSnapshot { get; set; } = string.Empty;
    public int OperationUnitDecimalPlaces { get; set; } = 4;
    public int BaseUnitId { get; set; }
    public string BaseUnitCodeSnapshot { get; set; } = string.Empty;
    public int BaseUnitDecimalPlaces { get; set; } = 4;
    public decimal ConversionFactor { get; set; } = 1;
    public int ConversionVersion { get; set; } = 1;
    public decimal BaseExpectedQuantity { get; set; }
    public decimal BaseReceivedQuantity { get; set; }
    public decimal BaseAcceptedQuantity { get; set; }
    public decimal BaseDamagedQuantity { get; set; }
    public decimal BaseRejectedQuantity { get; set; }
    public decimal BasePostedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Note { get; set; }
    public bool RequiresQc { get; set; }
    public int? QcPolicyId { get; set; }
    public int? QcPolicyVersion { get; set; }
    public string? QcPolicySourceSnapshot { get; set; }
    public DateTime? QcPolicyEffectiveAtUtc { get; set; }
    public string? QcRuleSnapshot { get; set; }
    public ReceiptLineQcState QcState { get; set; }
    public string? QcDispositionReasonCode { get; set; }
    public string? QcDispositionNote { get; set; }
    public int? QcCompletedBy { get; set; }
    public DateTime? QcCompletedAt { get; set; }

    // Navigation
    public ImportReceipt ImportReceipt { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
