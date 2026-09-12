namespace ERP.Application.DTOs
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public int? CategoryId { get; set; }
        public string? CategoryCode { get; set; }
        public string? CategoryName { get; set; }
        public IReadOnlyList<ProductBarcodeDto> Barcodes { get; set; } = [];
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
