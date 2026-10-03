namespace ERP.Domain.Entities;

public sealed class WarehouseZone
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ZoneType { get; set; } = "STORAGE";
    public int? PickPriority { get; set; }
    public int? PutawayPriority { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<WarehouseAisle> Aisles { get; set; } = new List<WarehouseAisle>();
    public ICollection<WarehouseLocation> Locations { get; set; } = new List<WarehouseLocation>();
}

public sealed class WarehouseAisle
{
    public int Id { get; set; }
    public int ZoneId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public WarehouseZone Zone { get; set; } = null!;
    public ICollection<WarehouseRack> Racks { get; set; } = new List<WarehouseRack>();
}

public sealed class WarehouseRack
{
    public int Id { get; set; }
    public int AisleId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public WarehouseAisle Aisle { get; set; } = null!;
    public ICollection<WarehouseRackLevel> Levels { get; set; } = new List<WarehouseRackLevel>();
}

public sealed class WarehouseRackLevel
{
    public int Id { get; set; }
    public int RackId { get; set; }
    public int LevelNo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public WarehouseRack Rack { get; set; } = null!;
    public ICollection<WarehouseLocation> Locations { get; set; } = new List<WarehouseLocation>();
}
