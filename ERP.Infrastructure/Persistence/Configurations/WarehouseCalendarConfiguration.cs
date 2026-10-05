using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class WarehouseCalendarConfiguration : IEntityTypeConfiguration<WarehouseCalendar>
{
    public void Configure(EntityTypeBuilder<WarehouseCalendar> builder)
    {
        builder.ToTable("WarehouseCalendars");
        builder.HasKey(x => x.WarehouseId);
        builder.Property(x => x.TimeZoneId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasOne(x => x.Warehouse).WithOne().HasForeignKey<WarehouseCalendar>(x => x.WarehouseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WarehouseCalendarDayConfiguration : IEntityTypeConfiguration<WarehouseCalendarDay>
{
    public void Configure(EntityTypeBuilder<WarehouseCalendarDay> builder)
    {
        builder.ToTable("WarehouseCalendarDays", table =>
        {
            table.HasCheckConstraint("CK_WarehouseCalendarDays_DayOfWeek", "[DayOfWeek] BETWEEN 0 AND 6");
            table.HasCheckConstraint("CK_WarehouseCalendarDays_OpenHours", "([IsOpen] = 0 AND [OpensAtLocal] IS NULL AND [ClosesAtLocal] IS NULL AND [InboundCutoffLocal] IS NULL AND [OutboundCutoffLocal] IS NULL) OR ([IsOpen] = 1 AND [OpensAtLocal] IS NOT NULL AND [ClosesAtLocal] IS NOT NULL AND [OpensAtLocal] <> [ClosesAtLocal])");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.WarehouseId, x.DayOfWeek }).IsUnique().HasDatabaseName("UX_WarehouseCalendarDays_Warehouse_Day");
        builder.HasOne(x => x.Calendar).WithMany(x => x.Days).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class WarehouseShiftConfiguration : IEntityTypeConfiguration<WarehouseShift>
{
    public void Configure(EntityTypeBuilder<WarehouseShift> builder)
    {
        builder.ToTable("WarehouseShifts", table =>
        {
            table.HasCheckConstraint("CK_WarehouseShifts_Time", "[StartTimeLocal] <> [EndTimeLocal]");
            table.HasCheckConstraint("CK_WarehouseShifts_NonNegative", "[BreakMinutes] >= 0 AND [PlannedHeadcount] >= 0 AND ([InboundPalletsPerHour] IS NULL OR [InboundPalletsPerHour] >= 0) AND ([OutboundOrdersPerHour] IS NULL OR [OutboundOrdersPerHour] >= 0) AND ([DockSlots] IS NULL OR [DockSlots] >= 0) AND ([LaborHours] IS NULL OR [LaborHours] >= 0) AND ([StagingCapacity] IS NULL OR [StagingCapacity] >= 0) AND ([PackingStations] IS NULL OR [PackingStations] >= 0) AND ([EquipmentAvailable] IS NULL OR [EquipmentAvailable] >= 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.InboundPalletsPerHour).HasPrecision(18, 4);
        builder.Property(x => x.OutboundOrdersPerHour).HasPrecision(18, 4);
        builder.Property(x => x.LaborHours).HasPrecision(18, 4);
        builder.Property(x => x.StagingCapacity).HasPrecision(18, 4);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique().HasDatabaseName("UX_WarehouseShifts_Warehouse_Code");
        builder.HasOne(x => x.Calendar).WithMany(x => x.Shifts).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Cascade);
    }
}
