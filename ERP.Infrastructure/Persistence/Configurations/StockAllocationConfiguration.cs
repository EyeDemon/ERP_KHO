using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class StockAllocationConfiguration : IEntityTypeConfiguration<StockAllocation>
{
    public void Configure(EntityTypeBuilder<StockAllocation> builder)
    {
        builder.ToTable("StockAllocations", t =>
        {
            t.HasCheckConstraint("CK_StockAllocations_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_StockAllocations_Version", "[Version] >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AllocationCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.SelectionReason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ReleaseReason).HasMaxLength(500);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => x.AllocationCode).IsUnique();
        builder.HasIndex(x => x.ReservationId);
        builder.HasIndex(x => new { x.WarehouseId, x.LocationId, x.ProductId, x.Status });
        builder.HasIndex(x => new { x.ReservationId, x.LocationId, x.InventoryStatus, x.Status });

        builder.HasOne(x => x.Reservation).WithMany(x => x.Allocations).HasForeignKey(x => x.ReservationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AllocatedByUser).WithMany().HasForeignKey(x => x.AllocatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReleasedByUser).WithMany().HasForeignKey(x => x.ReleasedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
