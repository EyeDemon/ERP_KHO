using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class WarehouseLocation
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? StructurePath { get; set; }
    public string? StorageClass { get; set; }
    public decimal? MaxWeightKg { get; set; }
    public decimal? MaxVolumeM3 { get; set; }
    public decimal? MaxPalletEquivalent { get; set; }
    public decimal? MapX { get; set; }
    public decimal? MapY { get; set; }
    public decimal? MapWidth { get; set; }
    public decimal? MapHeight { get; set; }
    public WarehouseLocationType LocationType { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsBlocked { get; set; }
    public bool IsPickable { get; set; }
    public bool IsReceivable { get; set; }
    public bool IsSystemManaged { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Warehouse Warehouse { get; set; } = null!;
}
