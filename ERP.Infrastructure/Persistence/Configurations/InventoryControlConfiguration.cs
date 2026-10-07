using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
{
    public void Configure(EntityTypeBuilder<InventoryLot> b)
    {
        b.ToTable("InventoryLots", t =>
        {
            t.HasCheckConstraint(
                "CK_InventoryLots_Dates",
                "[ExpiryDate] IS NULL OR [ManufactureDate] IS NULL OR [ExpiryDate] >= [ManufactureDate]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.ProductId, x.LotNumber }).IsUnique();
        b.HasIndex(x => x.ExpiryDate);
        b.HasOne(x => x.Product).WithMany(x => x.Lots).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventorySerialConfiguration : IEntityTypeConfiguration<InventorySerial>
{
    public void Configure(EntityTypeBuilder<InventorySerial> b)
    {
        b.ToTable("InventorySerials");
        b.HasKey(x => x.Id);
        b.Property(x => x.SerialNumber).HasMaxLength(128).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.SerialNumber).IsUnique();
        b.HasIndex(x => new { x.ProductId, x.LotId });
        b.HasOne(x => x.Product).WithMany(x => x.Serials).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Lot).WithMany(x => x.Serials).HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryLockConfiguration : IEntityTypeConfiguration<InventoryLock>
{
    public void Configure(EntityTypeBuilder<InventoryLock> b)
    {
        b.ToTable("InventoryLocks", t =>
        {
            t.HasCheckConstraint("CK_InventoryLocks_Expiry", "[ExpiresAt] IS NULL OR [ExpiresAt] > [CreatedAt]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.ReleaseReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.WarehouseId, x.Status });
        b.HasIndex(x => new { x.LocationId, x.Status });
        b.HasIndex(x => new { x.ProductId, x.Status });
        b.HasIndex(x => new { x.LotId, x.Status });
        b.HasIndex(x => new { x.SerialId, x.Status });
        b.HasIndex(x => new { x.HandlingUnitId, x.Status });

        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Serial).WithMany().HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.HandlingUnit).WithMany().HasForeignKey(x => x.HandlingUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReleasedByUser).WithMany().HasForeignKey(x => x.ReleasedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
