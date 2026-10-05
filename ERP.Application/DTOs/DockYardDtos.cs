using ERP.Domain.Enums;

namespace ERP.Application.DTOs;

public sealed class DockYardWarehouseDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "UTC";
    public bool CalendarConfigured { get; set; }
}

public sealed class DockDto
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool SupportsInbound { get; set; }
    public bool SupportsOutbound { get; set; }
    public string? AllowedVehicleType { get; set; }
    public bool IsTemperatureControlled { get; set; }
    public bool HazardAllowed { get; set; }
    public bool IsActive { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class UpsertDockDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool SupportsInbound { get; set; } = true;
    public bool SupportsOutbound { get; set; } = true;
    public string? AllowedVehicleType { get; set; }
    public bool IsTemperatureControlled { get; set; }
    public bool HazardAllowed { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class YardSlotDto
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool Occupied { get; set; }
    public string? OccupiedByAppointmentCode { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class UpsertYardSlotDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public sealed class DockAppointmentQueryDto
{
    public int? WarehouseId { get; set; }
    public DockAppointmentStatus? Status { get; set; }
    public DockAppointmentDirection? Direction { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public string? Search { get; set; }
}

public sealed class DockAppointmentDto
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DockAppointmentDirection Direction { get; set; }
    public DockAppointmentStatus Status { get; set; }
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
    public string? YardSlotCode { get; set; }
    public int? DockId { get; set; }
    public string? DockCode { get; set; }
    public DateTime? ArrivedAtUtc { get; set; }
    public DateTime? CheckedInAtUtc { get; set; }
    public DateTime? DockAssignedAtUtc { get; set; }
    public DateTime? ServiceStartedAtUtc { get; set; }
    public DateTime? ServiceCompletedAtUtc { get; set; }
    public DateTime? CheckedOutAtUtc { get; set; }
    public string? ExceptionCode { get; set; }
    public string? Note { get; set; }
    public string? RowVersion { get; set; }
    public IReadOnlyList<DockAppointmentEventDto> Events { get; set; } = [];
}

public sealed class DockAppointmentEventDto
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime EventAtUtc { get; set; }
    public int ActorUserId { get; set; }
    public int? DockId { get; set; }
    public int? YardSlotId { get; set; }
    public string? Note { get; set; }
}

public sealed class UpsertDockAppointmentDto
{
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public DockAppointmentDirection Direction { get; set; }
    public DateTimeOffset PlannedStart { get; set; }
    public DateTimeOffset PlannedEnd { get; set; }
    public string? CarrierCode { get; set; }
    public string? CarrierName { get; set; }
    public string? VehiclePlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? VehicleType { get; set; }
    public bool RequiresTemperatureControl { get; set; }
    public bool Hazardous { get; set; }
    public string? Note { get; set; }
    public string? RowVersion { get; set; }
}

public class DockAppointmentCommandDto
{
    public string? RowVersion { get; set; }
    public string? Note { get; set; }
}

public sealed class DockAppointmentArrivalDto : DockAppointmentCommandDto
{
    public DateTimeOffset? ArrivedAt { get; set; }
}

public sealed class DockAppointmentCheckInDto : DockAppointmentCommandDto
{
    public string? VehiclePlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public string? SealNumber { get; set; }
    public int? YardSlotId { get; set; }
}

public sealed class DockAppointmentAssignDockDto : DockAppointmentCommandDto
{
    public int DockId { get; set; }
}

public sealed class DockAppointmentExceptionDto : DockAppointmentCommandDto
{
    public string ExceptionCode { get; set; } = string.Empty;
}
