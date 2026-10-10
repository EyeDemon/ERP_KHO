using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class Dock
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool SupportsInbound { get; set; } = true;
    public bool SupportsOutbound { get; set; } = true;
    public string? AllowedVehicleType { get; set; }
    public bool IsTemperatureControlled { get; set; }
    public bool HazardAllowed { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];

    public Warehouse Warehouse { get; set; } = null!;
}

public sealed class YardSlot
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];

    public Warehouse Warehouse { get; set; } = null!;
}

public sealed class DockAppointment
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DockAppointmentDirection Direction { get; set; }
    public DockAppointmentStatus Status { get; set; } = DockAppointmentStatus.Draft;
    public DateTime PlannedStartUtc { get; set; }
    public DateTime PlannedEndUtc { get; set; }
    public string? CarrierCode { get; set; }
    public string? CarrierName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? VehicleType { get; set; }
    public bool RequiresTemperatureControl { get; set; }
    public bool Hazardous { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? SealNumber { get; set; }
    public int? YardSlotId { get; set; }
    public int? DockId { get; set; }
    public DateTime? ArrivedAtUtc { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }
    public DateTime? DockAssignedAtUtc { get; set; }
    public DateTime? ServiceStartedAtUtc { get; set; }
    public DateTime? ServiceCompletedAtUtc { get; set; }
    public DateTime? CheckedOutAtUtc { get; set; }
    public string? ExceptionCode { get; set; }
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[] RowVersion { get; set; } = [];

    public Warehouse Warehouse { get; set; } = null!;
    public Dock? Dock { get; set; }
    public YardSlot? YardSlot { get; set; }
    public User CreatedByUser { get; set; } = null!;
    public ICollection<DockAppointmentEvent> Events { get; set; } = new List<DockAppointmentEvent>();
}

public sealed class DockAppointmentEvent
{
    public int Id { get; set; }
    public int DockAppointmentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime EventAtUtc { get; set; }
    public int ActorUserId { get; set; }
    public int? DockId { get; set; }
    public int? YardSlotId { get; set; }
    public string? Note { get; set; }

    public DockAppointment Appointment { get; set; } = null!;
    public User Actor { get; set; } = null!;
}
