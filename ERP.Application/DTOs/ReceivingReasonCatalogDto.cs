namespace ERP.Application.DTOs;

public enum ReasonEffectivity { NotYetEffective, Effective, Expired }

public sealed class ReceivingReasonCatalogDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int Version { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset EffectiveFromUtc { get; set; }
    public DateTimeOffset? EffectiveToUtc { get; set; }
    public ReasonEffectivity Effectivity { get; set; }
    public bool RequiresNote { get; set; }
    public bool RequiresAttachment { get; set; }
    public bool RequiresApproval { get; set; }
}
