using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class DockConfiguration : IEntityTypeConfiguration<Dock>
{
    public void Configure(EntityTypeBuilder<Dock> builder)
    {
        builder.ToTable("Docks", table => table.HasCheckConstraint("CK_Docks_Direction", "[SupportsInbound] = 1 OR [SupportsOutbound] = 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.AllowedVehicleType).HasMaxLength(50);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique().HasDatabaseName("UX_Docks_Warehouse_Code");
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class YardSlotConfiguration : IEntityTypeConfiguration<YardSlot>
{
    public void Configure(EntityTypeBuilder<YardSlot> builder)
    {
        builder.ToTable("YardSlots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(30);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique().HasDatabaseName("UX_YardSlots_Warehouse_Code");
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DockAppointmentConfiguration : IEntityTypeConfiguration<DockAppointment>
{
    public void Configure(EntityTypeBuilder<DockAppointment> builder)
    {
        builder.ToTable("DockAppointments", table => table.HasCheckConstraint("CK_DockAppointments_Window", "[PlannedStartUtc] < [PlannedEndUtc]"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.Property(x => x.CarrierCode).HasMaxLength(50);
        builder.Property(x => x.CarrierName).HasMaxLength(200);
        builder.Property(x => x.VehiclePlate).HasMaxLength(30);
        builder.Property(x => x.TrailerPlate).HasMaxLength(30);
        builder.Property(x => x.VehicleType).HasMaxLength(50);
        builder.Property(x => x.DriverName).HasMaxLength(150);
        builder.Property(x => x.DriverPhone).HasMaxLength(30);
        builder.Property(x => x.SealNumber).HasMaxLength(80);
        builder.Property(x => x.ExceptionCode).HasMaxLength(60);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique().HasDatabaseName("UX_DockAppointments_Warehouse_Code");
        builder.HasIndex(x => new { x.WarehouseId, x.PlannedStartUtc, x.PlannedEndUtc });
        builder.HasIndex(x => new { x.DockId, x.PlannedStartUtc, x.PlannedEndUtc, x.Status });
        builder.HasIndex(x => new { x.YardSlotId, x.Status });
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Dock).WithMany().HasForeignKey(x => x.DockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.YardSlot).WithMany().HasForeignKey(x => x.YardSlotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class DockAppointmentEventConfiguration : IEntityTypeConfiguration<DockAppointmentEvent>
{
    public void Configure(EntityTypeBuilder<DockAppointmentEvent> builder)
    {
        builder.ToTable("DockAppointmentEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasIndex(x => new { x.DockAppointmentId, x.EventAtUtc });
        builder.HasOne(x => x.Appointment).WithMany(x => x.Events).HasForeignKey(x => x.DockAppointmentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Dock>().WithMany().HasForeignKey(x => x.DockId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<YardSlot>().WithMany().HasForeignKey(x => x.YardSlotId).OnDelete(DeleteBehavior.Restrict);
    }
}
