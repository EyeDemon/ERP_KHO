using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class ReceivingDiscrepancy
{
    public int Id { get; set; }
    public int ImportReceiptId { get; set; }
    public int ImportReceiptDetailId { get; set; }
    public ReceivingDiscrepancyStatus Status { get; set; } = ReceivingDiscrepancyStatus.Pending;
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ImportReceipt ImportReceipt { get; set; } = null!;
    public ImportReceiptDetail ImportReceiptDetail { get; set; } = null!;
    public ICollection<ReceivingObservationVersion> Observations { get; set; } = [];
    public ICollection<ReceivingResolutionVersion> Resolutions { get; set; } = [];
}

public sealed class ReceivingObservationVersion
{
    public int Id { get; set; }
    public int ReceivingDiscrepancyId { get; set; }
    public int Version { get; set; }
    public int? PreviousObservationVersionId { get; set; }
    public decimal ObservedQuantity { get; set; }
    public decimal BaseObservedQuantity { get; set; }
    public int ObservedUnitId { get; set; }
    public string ObservedUnitCodeSnapshot { get; set; } = string.Empty;
    public decimal ConversionFactorSnapshot { get; set; }
    public int ConversionVersionSnapshot { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ReceivingDiscrepancy ReceivingDiscrepancy { get; set; } = null!;
    public ReceivingObservationVersion? PreviousObservationVersion { get; set; }
    public ICollection<ReceivingObservationItem> Items { get; set; } = [];
}

public sealed class ReceivingObservationItem
{
    public int Id { get; set; }
    public int ReceivingObservationVersionId { get; set; }
    public decimal Quantity { get; set; }
    public string? ScanReference { get; set; }
    public bool IsVoided { get; set; }
    public int? VoidedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public int? SupersededByObservationVersionId { get; set; }
    public ReceivingObservationVersion? SupersededByObservationVersion { get; set; }
    public ReceivingObservationVersion ReceivingObservationVersion { get; set; } = null!;
}

public sealed class ReceivingResolutionVersion
{
    public int Id { get; set; }
    public int ReceivingDiscrepancyId { get; set; }
    public int Version { get; set; }
    public int? PreviousResolutionVersionId { get; set; }
    public ReceivingResolutionAction Action { get; set; }
    public decimal DoorRejectedQuantity { get; set; }
    public decimal BaseDoorRejectedQuantity { get; set; }
    public decimal FinalReceivedQuantity { get; set; }
    public decimal BaseFinalReceivedQuantity { get; set; }
    public int ReasonCodeId { get; set; }
    public string ReasonCodeSnapshot { get; set; } = string.Empty;
    public string ReasonNameSnapshot { get; set; } = string.Empty;
    public string ReasonCategorySnapshot { get; set; } = string.Empty;
    public int ReasonVersionSnapshot { get; set; }
    public DateTime ReasonEffectiveAtUtcSnapshot { get; set; }
    public bool ReasonRequiresNoteSnapshot { get; set; }
    public bool ReasonRequiresAttachmentSnapshot { get; set; }
    public bool ReasonRequiresApprovalSnapshot { get; set; }
    public int? TolerancePolicyId { get; set; }
    public int? TolerancePolicyVersionSnapshot { get; set; }
    public string TolerancePolicySourceSnapshot { get; set; } = "SystemDefault";
    public DateTime? TolerancePolicyEffectiveAtUtcSnapshot { get; set; }
    public decimal AbsoluteToleranceSnapshot { get; set; }
    public decimal PercentageToleranceSnapshot { get; set; }
    public decimal AllowedBaseToleranceSnapshot { get; set; }
    public decimal? ValueToleranceSnapshot { get; set; }
    public bool RequiresApproval { get; set; }
    public ResponsibleParty ResponsibleParty { get; set; }
    public bool SupplierClaimRequired { get; set; }
    public string? Note { get; set; }
    public string? EvidenceReference { get; set; }
    public int SubmittedBy { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? RejectedBy { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public int? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public ReceivingDiscrepancy ReceivingDiscrepancy { get; set; } = null!;
    public ReceivingResolutionVersion? PreviousResolutionVersion { get; set; }
}
