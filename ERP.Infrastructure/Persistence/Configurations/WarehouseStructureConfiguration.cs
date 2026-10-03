using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class WarehouseZoneConfiguration : IEntityTypeConfiguration<WarehouseZone>
{
    public void Configure(EntityTypeBuilder<WarehouseZone> b)
    {
        b.ToTable("WarehouseZones");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(30);
        b.Property(x => x.Name).IsRequired().HasMaxLength(100);
        b.Property(x => x.ZoneType).IsRequired().HasMaxLength(30);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasOne(x => x.Warehouse).WithMany(x => x.Zones).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WarehouseAisleConfiguration : IEntityTypeConfiguration<WarehouseAisle>
{
    public void Configure(EntityTypeBuilder<WarehouseAisle> b)
    {
        b.ToTable("WarehouseAisles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.ZoneId, x.Code }).IsUnique();
        b.HasOne(x => x.Zone).WithMany(x => x.Aisles).HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WarehouseRackConfiguration : IEntityTypeConfiguration<WarehouseRack>
{
    public void Configure(EntityTypeBuilder<WarehouseRack> b)
    {
        b.ToTable("WarehouseRacks");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.AisleId, x.Code }).IsUnique();
        b.HasOne(x => x.Aisle).WithMany(x => x.Racks).HasForeignKey(x => x.AisleId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class WarehouseRackLevelConfiguration : IEntityTypeConfiguration<WarehouseRackLevel>
{
    public void Configure(EntityTypeBuilder<WarehouseRackLevel> b)
    {
        b.ToTable("WarehouseRackLevels");
        b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.RackId, x.LevelNo }).IsUnique();
        b.HasOne(x => x.Rack).WithMany(x => x.Levels).HasForeignKey(x => x.RackId).OnDelete(DeleteBehavior.Restrict);
    }
}
