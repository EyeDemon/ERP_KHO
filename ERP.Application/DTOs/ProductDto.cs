namespace ERP.Application.DTOs
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? StorageClass { get; set; }
        public decimal? UnitWeightKg { get; set; }
        public decimal? UnitVolumeM3 { get; set; }
        public decimal? UnitPalletEquivalent { get; set; }
        public string TrackingType { get; set; } = "None";
        public bool ExpiryControl { get; set; }
        public int? ShelfLifeDays { get; set; }
        public int UnitId { get; set; }
        public string? UnitName { get; set; }
        public string? UnitCode { get; set; }
        public int UnitDecimalPlaces { get; set; }
        public IReadOnlyList<ProductUomDto> Uoms { get; set; } = [];
        public int? CategoryId { get; set; }
        public string? CategoryCode { get; set; }
        public string? CategoryName { get; set; }
        public IReadOnlyList<ProductBarcodeDto> Barcodes { get; set; } = [];
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ProductUomDto
    {
        public int UnitId { get; set; }
        public string UnitCode { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public int DecimalPlaces { get; set; }
        public decimal ConversionFactor { get; set; }
        public int Version { get; set; }
    }
}
