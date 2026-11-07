using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class ProductBarcode : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int ProductId { get; set; }
    public string Value { get; set; } = string.Empty;
    public Product Product { get; set; } = null!;
}
