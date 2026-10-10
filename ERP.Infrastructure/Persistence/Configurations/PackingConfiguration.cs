using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class PackingSessionConfiguration : IEntityTypeConfiguration<PackingSession>
{
    public void Configure(EntityTypeBuilder<PackingSession> b)
    {
        b.ToTable("PackingSessions");
        b.HasKey(x => x.Id);
        b.Property(x => x.SessionCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.PickingTaskId).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.SessionCode }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status, x.CreatedAt });
        b.HasOne(x => x.PickingTask).WithOne().HasForeignKey<PackingSession>(x => x.PickingTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class HandlingUnitConfiguration : IEntityTypeConfiguration<HandlingUnit>
{
    public void Configure(EntityTypeBuilder<HandlingUnit> b)
    {
        b.ToTable("HandlingUnits", t =>
        {
            t.HasCheckConstraint(
                "CK_HandlingUnits_Metrics",
                "([GrossWeightKg] IS NULL OR [GrossWeightKg] > 0) AND ([NetWeightKg] IS NULL OR [NetWeightKg] > 0) AND ([VolumeM3] IS NULL OR [VolumeM3] > 0) AND ([GrossWeightKg] IS NULL OR [NetWeightKg] IS NULL OR [GrossWeightKg] >= [NetWeightKg])");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.HuCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Barcode).HasMaxLength(64).IsRequired();
        b.Property(x => x.Sscc).HasMaxLength(32);
        b.Property(x => x.GrossWeightKg).HasPrecision(18, 4);
        b.Property(x => x.NetWeightKg).HasPrecision(18, 4);
        b.Property(x => x.VolumeM3).HasPrecision(18, 6);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.WarehouseId, x.HuCode }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Barcode }).IsUnique();
        b.HasIndex(x => x.Sscc).IsUnique().HasFilter("[Sscc] IS NOT NULL");
        b.HasIndex(x => new { x.PackingSessionId, x.Status });
        b.HasOne(x => x.PackingSession).WithMany(x => x.HandlingUnits).HasForeignKey(x => x.PackingSessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ParentHandlingUnit).WithMany(x => x.Children).HasForeignKey(x => x.ParentHandlingUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class HandlingUnitContentConfiguration : IEntityTypeConfiguration<HandlingUnitContent>
{
    public void Configure(EntityTypeBuilder<HandlingUnitContent> b)
    {
        b.ToTable("HandlingUnitContents", t =>
        {
            t.HasCheckConstraint("CK_HandlingUnitContents_Quantity", "[Quantity] > 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.HasIndex(x => new { x.HandlingUnitId, x.PickingTaskLineId }).IsUnique();
        b.HasIndex(x => x.ProductId);
        b.HasOne(x => x.HandlingUnit).WithMany(x => x.Contents).HasForeignKey(x => x.HandlingUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PickingTaskLine).WithMany().HasForeignKey(x => x.PickingTaskLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.PackedByUser).WithMany().HasForeignKey(x => x.PackedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
