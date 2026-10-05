using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class PackingSession
{
    public int Id { get; set; }
    public string SessionCode { get; set; } = string.Empty;
    public int PickingTaskId { get; set; }
    public int WarehouseId { get; set; }
    public PackingSessionStatus Status { get; set; } = PackingSessionStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? PackedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public PickingTask PickingTask { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<HandlingUnit> HandlingUnits { get; set; } = [];
}

public sealed class HandlingUnit
{
    public int Id { get; set; }
    public string HuCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string? Sscc { get; set; }
    public int WarehouseId { get; set; }
    public int PackingSessionId { get; set; }
    public int? ParentHandlingUnitId { get; set; }
    public HandlingUnitType Type { get; set; }
    public HandlingUnitStatus Status { get; set; } = HandlingUnitStatus.Open;
    public decimal? GrossWeightKg { get; set; }
    public decimal? NetWeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public DateTime? SealedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public PackingSession PackingSession { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public HandlingUnit? ParentHandlingUnit { get; set; }
    public ICollection<HandlingUnit> Children { get; set; } = [];
    public ICollection<HandlingUnitContent> Contents { get; set; } = [];
    public User CreatedByUser { get; set; } = null!;
}

public sealed class HandlingUnitContent
{
    public int Id { get; set; }
    public int HandlingUnitId { get; set; }
    public int PickingTaskLineId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime PackedAt { get; set; } = DateTime.UtcNow;
    public int PackedBy { get; set; }

    public HandlingUnit HandlingUnit { get; set; } = null!;
    public PickingTaskLine PickingTaskLine { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public User PackedByUser { get; set; } = null!;
}
