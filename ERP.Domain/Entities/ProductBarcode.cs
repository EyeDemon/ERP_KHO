namespace ERP.Domain.Entities;

public class ProductBarcode
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string Value { get; set; } = string.Empty;
    public Product Product { get; set; } = null!;
}
