namespace ERP.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int UnitId { get; set; }
    public int? CategoryId { get; set; }
    public string? Description { get; set; }
    public string? StorageClass { get; set; }
    public decimal? UnitWeightKg { get; set; }
    public decimal? UnitVolumeM3 { get; set; }
    public decimal? UnitPalletEquivalent { get; set; }
    public ERP.Domain.Enums.ProductTrackingType TrackingType { get; set; } = ERP.Domain.Enums.ProductTrackingType.None;
    public bool ExpiryControl { get; set; }
    public int? ShelfLifeDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Unit Unit { get; set; } = null!;
    public ProductCategory? Category { get; set; }
    public ICollection<ProductBarcode> Barcodes { get; set; } = new List<ProductBarcode>();
    public ICollection<ProductUom> Uoms { get; set; } = new List<ProductUom>();
    public ICollection<InventoryStock> InventoryStocks { get; set; } = new List<InventoryStock>();
    public ICollection<InventoryLot> InventoryLots { get; set; } = [];
    public ICollection<InventorySerial> InventorySerials { get; set; } = [];
    public ICollection<QcPolicy> QcPolicies { get; set; } = new List<QcPolicy>();
}
