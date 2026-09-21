namespace ERP.Domain.Entities;

public class QcPolicy
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int? SupplierId { get; set; }
    public int Version { get; set; } = 1;
    public bool RequiresQc { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EffectiveFromUtc { get; set; }
    public string Rule { get; set; } = string.Empty;
    public Product Product { get; set; } = null!;
    public BusinessPartner? Supplier { get; set; }
}
