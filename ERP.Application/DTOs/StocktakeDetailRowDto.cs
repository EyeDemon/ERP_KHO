namespace ERP.Application.DTOs
{
    public class StocktakeDetailRowDto
    {
        public int Id { get; set; }
        public int StocktakeId { get; set; }
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public decimal SystemQuantity { get; set; }
        public decimal? ActualQuantity { get; set; }
        public decimal DifferenceQuantity { get; set; }
        public string? Note { get; set; }
    }
}
