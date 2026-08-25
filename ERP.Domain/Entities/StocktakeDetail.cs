namespace ERP.Domain.Entities;

public class StocktakeDetail
{
    public int Id { get; set; }
    public int StocktakeId { get; set; }
    public int ProductId { get; set; }
    public decimal SystemQuantity { get; set; }
    public decimal? ActualQuantity { get; set; }
    public decimal DifferenceQuantity { get; set; }
    public string? Note { get; set; }

    // Navigation
    public Stocktake Stocktake { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
