using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class StocktakeAdjustmentProposal : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string ProposalCode { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public int? StocktakeId { get; set; }
    public int ProductId { get; set; }
    public int? LocationId { get; set; }
    public string ReasonCode { get; set; } = string.Empty; // DAMAGED | EXPIRED | FOUND | LOST | SYSTEM_VARIANCE
    public decimal SystemQuantity { get; set; }
    public decimal CountedQuantity { get; set; }
    public decimal DeltaQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal VariancePct { get; set; }
    public decimal VarianceValue { get; set; }
    public bool IsAutoApproved { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING | APPROVED | EXECUTED | REJECTED
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? Note { get; set; }
}
