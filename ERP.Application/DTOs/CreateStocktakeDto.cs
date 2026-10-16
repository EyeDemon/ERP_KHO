namespace ERP.Application.DTOs
{
    public class CreateStocktakeDto
    {
        public int WarehouseId { get; set; }
        public int? LocationId { get; set; }
        public bool IsBlindCount { get; set; }
        public string? Note { get; set; }
    }
}

