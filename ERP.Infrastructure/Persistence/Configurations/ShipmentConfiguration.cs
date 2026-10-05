using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("Shipments");
        b.HasKey(x => x.Id);
        b.Property(x => x.ShipmentCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceType).HasMaxLength(40).IsRequired();
        b.Property(x => x.SourceCode).HasMaxLength(80);
        b.Property(x => x.VehiclePlate).HasMaxLength(30);
        b.Property(x => x.TrailerPlate).HasMaxLength(30);
        b.Property(x => x.SealNumber).HasMaxLength(80);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.PackingSessionId).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.ShipmentCode }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.SourceType, x.SourceId });
        b.HasOne(x => x.PackingSession).WithOne().HasForeignKey<Shipment>(x => x.PackingSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.DockAppointment).WithMany().HasForeignKey(x => x.DockAppointmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Dock).WithMany().HasForeignKey(x => x.DockId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ShipmentHandlingUnitConfiguration : IEntityTypeConfiguration<ShipmentHandlingUnit>
{
    public void Configure(EntityTypeBuilder<ShipmentHandlingUnit> b)
    {
        b.ToTable("ShipmentHandlingUnits");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.HandlingUnitId).IsUnique();
        b.HasIndex(x => new { x.ShipmentId, x.Sequence }).IsUnique();
        b.HasOne(x => x.Shipment).WithMany(x => x.HandlingUnits).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.HandlingUnit).WithMany().HasForeignKey(x => x.HandlingUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LoadedByUser).WithMany().HasForeignKey(x => x.LoadedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
