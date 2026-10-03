namespace ERP.Domain.Entities;

public sealed class ReceivingReasonCode
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Receiving";
    public int Version { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public bool RequiresNote { get; set; }
    public bool RequiresAttachment { get; set; }
    public bool RequiresApproval { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ReceivingTolerancePolicy
{
    public int Id { get; set; }
    public int? ProductId { get; set; }
    public int? SupplierId { get; set; }
    public int? WarehouseId { get; set; }
    public int Version { get; set; }
    public DateTime EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public decimal AbsoluteQuantityTolerance { get; set; }
    public decimal PercentageTolerance { get; set; }
    public bool OverageAllowed { get; set; }
    public bool ShortageAllowed { get; set; }
    public decimal? ValueTolerance { get; set; }
    public bool RequiresApprovalOutsideTolerance { get; set; }
    public string? ApproverTarget { get; set; }
    public bool IsActive { get; set; }
}
