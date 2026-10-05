namespace ERP.Domain.Entities;

public sealed class WarehouseCalendar
{
    public int WarehouseId { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<WarehouseCalendarDay> Days { get; set; } = new List<WarehouseCalendarDay>();
    public ICollection<WarehouseShift> Shifts { get; set; } = new List<WarehouseShift>();
}

public sealed class WarehouseCalendarDay
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public int DayOfWeek { get; set; }
    public bool IsOpen { get; set; }
    public TimeSpan? OpensAtLocal { get; set; }
    public TimeSpan? ClosesAtLocal { get; set; }
    public TimeSpan? InboundCutoffLocal { get; set; }
    public TimeSpan? OutboundCutoffLocal { get; set; }

    public WarehouseCalendar Calendar { get; set; } = null!;
}

public sealed class WarehouseShift
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTimeLocal { get; set; }
    public TimeSpan EndTimeLocal { get; set; }
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
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public WarehouseCalendar Calendar { get; set; } = null!;
}
