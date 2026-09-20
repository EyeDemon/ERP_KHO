namespace ERP.Domain.Entities;

public class Unit
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DecimalPlaces { get; set; } = 4;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<ProductUom> ProductUoms { get; set; } = new List<ProductUom>();
}
