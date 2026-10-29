using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public sealed class ReceivingReasonCode : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
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

public sealed class ReceivingTolerancePolicy : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
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
