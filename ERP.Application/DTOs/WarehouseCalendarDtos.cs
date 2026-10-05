namespace ERP.Application.DTOs;

public sealed class WarehouseCalendarDto
{
    public int WarehouseId { get; set; }
    public string WarehouseCode { get; set; } = string.Empty;
    public string WarehouseName { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public DateTimeOffset LocalNow { get; set; }
    public bool IsOpenNow { get; set; }
    public string? CurrentShiftCode { get; set; }
    public string? RowVersion { get; set; }
    public IReadOnlyList<WarehouseCalendarDayDto> Days { get; set; } = [];
    public IReadOnlyList<WarehouseShiftDto> Shifts { get; set; } = [];
}

public sealed class WarehouseCalendarDayDto
{
    public int DayOfWeek { get; set; }
    public string DayName { get; set; } = string.Empty;
    public bool IsOpen { get; set; }
    public string? OpensAtLocal { get; set; }
    public string? ClosesAtLocal { get; set; }
    public string? InboundCutoffLocal { get; set; }
    public string? OutboundCutoffLocal { get; set; }
    public bool Overnight { get; set; }
}

public sealed class UpdateWarehouseCalendarDto
{
    public string TimeZoneId { get; set; } = string.Empty;
    public string? RowVersion { get; set; }
    public IReadOnlyList<UpdateWarehouseCalendarDayDto> Days { get; set; } = [];
}

public sealed class UpdateWarehouseCalendarDayDto
{
    public int DayOfWeek { get; set; }
    public bool IsOpen { get; set; }
    public string? OpensAtLocal { get; set; }
    public string? ClosesAtLocal { get; set; }
    public string? InboundCutoffLocal { get; set; }
    public string? OutboundCutoffLocal { get; set; }
}

public sealed class WarehouseShiftDto
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StartTimeLocal { get; set; } = string.Empty;
    public string EndTimeLocal { get; set; } = string.Empty;
    public bool Overnight { get; set; }
    public int BreakMinutes { get; set; }
    public int PlannedHeadcount { get; set; }
    public decimal? InboundPalletsPerHour { get; set; }
    public decimal? OutboundOrdersPerHour { get; set; }
    public int? DockSlots { get; set; }
    public decimal? LaborHours { get; set; }
    public decimal? StagingCapacity { get; set; }
    public int? PackingStations { get; set; }
    public int? EquipmentAvailable { get; set; }
    public bool IsActive { get; set; }
    public string? RowVersion { get; set; }
}

public sealed class UpsertWarehouseShiftDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StartTimeLocal { get; set; } = string.Empty;
    public string EndTimeLocal { get; set; } = string.Empty;
    public int BreakMinutes { get; set; }
    public int PlannedHeadcount { get; set; }
    public decimal? InboundPalletsPerHour { get; set; }
    public decimal? OutboundOrdersPerHour { get; set; }
    public int? DockSlots { get; set; }
    public decimal? LaborHours { get; set; }
    public decimal? StagingCapacity { get; set; }
    public int? PackingStations { get; set; }
    public int? EquipmentAvailable { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
