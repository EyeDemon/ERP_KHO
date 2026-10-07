using ERP.Domain.Enums;

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
    public bool IsActive { get; set; } = true;
    public ProductTrackingType TrackingType { get; set; } = ProductTrackingType.None;
    public bool RequiresExpiryDate { get; set; }
    public int MinimumRemainingShelfLifeDaysAtReceipt { get; set; }
    public int MinimumRemainingShelfLifeDaysAtAllocation { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Unit Unit { get; set; } = null!;
    public ProductCategory? Category { get; set; }
    public ICollection<ProductBarcode> Barcodes { get; set; } = new List<ProductBarcode>();
    public ICollection<ProductUom> Uoms { get; set; } = new List<ProductUom>();
    public ICollection<InventoryStock> InventoryStocks { get; set; } = new List<InventoryStock>();
    public ICollection<QcPolicy> QcPolicies { get; set; } = new List<QcPolicy>();
    public ICollection<InventoryLot> Lots { get; set; } = new List<InventoryLot>();
    public ICollection<InventorySerial> Serials { get; set; } = new List<InventorySerial>();
}
