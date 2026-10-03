namespace ERP.Domain.Entities;

public class ProductUom
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int UnitId { get; set; }
    public decimal ConversionFactor { get; set; }
    public int Version { get; set; } = 1;
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public Product Product { get; set; } = null!;
    public Unit Unit { get; set; } = null!;
}
