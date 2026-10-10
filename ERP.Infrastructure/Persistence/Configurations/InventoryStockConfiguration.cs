using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class InventoryStockConfiguration : IEntityTypeConfiguration<InventoryStock>
{
    public void Configure(EntityTypeBuilder<InventoryStock> builder)
    {
        builder.ToTable("InventoryStocks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.ReservedQuantity).HasPrecision(18, 4);
        builder.ToTable(t => t.HasCheckConstraint("CK_InventoryStocks_Reservation", "[ReservedQuantity] >= 0 AND [Quantity] >= [ReservedQuantity]"));
        builder.Property<int>("CanonicalLocationId")
               .HasComputedColumnSql("COALESCE([LocationId], 0)", stored: true);
        builder.Property<int>("CanonicalLotId")
               .HasComputedColumnSql("COALESCE([LotId], 0)", stored: true);
        builder.Property<int>("CanonicalSerialId")
               .HasComputedColumnSql("COALESCE([SerialId], 0)", stored: true);
        builder.HasIndex(
                "ProductId",
                "WarehouseId",
                "Status",
                "CanonicalLocationId",
                "CanonicalLotId",
                "CanonicalSerialId")
               .IsUnique()
               .HasDatabaseName("IX_InventoryStocks_CanonicalBucket")
               .HasFilter(null);

        builder.HasOne(x => x.Product)
               .WithMany(p => p.InventoryStocks)
               .HasForeignKey(x => x.ProductId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Serial).WithMany().HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StatusDefinition).WithMany().HasForeignKey(x => x.Status).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
               .WithMany(w => w.InventoryStocks)
               .HasForeignKey(x => x.WarehouseId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
