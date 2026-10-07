using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_ProductCode");
        builder.HasIndex(x => x.StorageClass);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.StorageClass).HasMaxLength(32);
        builder.Property(x => x.UnitWeightKg).HasPrecision(18, 6);
        builder.Property(x => x.UnitVolumeM3).HasPrecision(18, 8);
        builder.Property(x => x.UnitPalletEquivalent).HasPrecision(18, 8);
        builder.Property(x => x.TrackingType).HasConversion<int>();

        builder.HasOne(x => x.Unit)
               .WithMany(u => u.Products)
               .HasForeignKey(x => x.UnitId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Category)
               .WithMany(x => x.Products)
               .HasForeignKey(x => x.CategoryId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
