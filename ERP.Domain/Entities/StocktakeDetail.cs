using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class StocktakeDetail : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int StocktakeId { get; set; }
    public int ProductId { get; set; }
    public int? LocationId { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public decimal DifferenceQuantity { get; set; }
    public string? Note { get; set; }

    // Navigation
    public Stocktake Stocktake { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public WarehouseLocation? Location { get; set; }
}
