namespace ERP.Application.DTOs
{
    public class UpdateStocktakeDetailDto
    {
        public decimal ActualQuantity { get; set; }
        public string? Note { get; set; }
    }

    public class RecountStocktakeDetailDto
    {
        public string? Reason { get; set; }
    }
}

