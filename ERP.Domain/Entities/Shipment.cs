using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class Shipment
{
    public int Id { get; set; }
    public string ShipmentCode { get; set; } = string.Empty;
    public int PackingSessionId { get; set; }
    public int WarehouseId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceCode { get; set; }
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Ready;
    public int? StagingLocationId { get; set; }
    public int? DockAppointmentId { get; set; }
    public int? DockId { get; set; }
    public string? VehiclePlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? SealNumber { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? StagedAt { get; set; }
    public DateTime? LoadingStartedAt { get; set; }
    public DateTime? LoadedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public int? DispatchedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public PackingSession PackingSession { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public WarehouseLocation? StagingLocation { get; set; }
    public DockAppointment? DockAppointment { get; set; }
    public Dock? Dock { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public User? DispatchedByUser { get; set; }
    public ICollection<ShipmentHandlingUnit> HandlingUnits { get; set; } = [];
}

public sealed class ShipmentHandlingUnit
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public int HandlingUnitId { get; set; }
    public int Sequence { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StagedAt { get; set; }
    public DateTime? LoadedAt { get; set; }
    public int? LoadedBy { get; set; }

    public Shipment Shipment { get; set; } = null!;
    public HandlingUnit HandlingUnit { get; set; } = null!;
    public User? LoadedByUser { get; set; }
}
