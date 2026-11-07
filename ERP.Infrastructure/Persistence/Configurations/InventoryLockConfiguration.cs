using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class InventoryLockConfiguration : IEntityTypeConfiguration<InventoryLock>
{
    public void Configure(EntityTypeBuilder<InventoryLock> b)
    {
        b.ToTable("InventoryLocks", t =>
        {
            t.HasCheckConstraint("CK_InventoryLocks_Expiry", "[ExpiresAt] IS NULL OR [ExpiresAt] > [CreatedAt]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.LockType).HasConversion<int>();
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.ReleaseReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasIndex(x => new { x.WarehouseId, x.Status });
        b.HasIndex(x => new { x.LocationId, x.Status });
        b.HasIndex(x => new { x.ProductId, x.Status });
        b.HasIndex(x => new { x.InventoryStatus, x.Status });
        b.HasIndex(x => new { x.LotId, x.Status });
        b.HasIndex(x => new { x.SerialId, x.Status });

        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Serial).WithMany().HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReleasedByUser).WithMany().HasForeignKey(x => x.ReleasedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
