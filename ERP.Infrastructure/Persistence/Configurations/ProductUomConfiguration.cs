using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class ProductUomConfiguration : IEntityTypeConfiguration<ProductUom>
{
    public void Configure(EntityTypeBuilder<ProductUom> builder)
    {
        builder.ToTable("ProductUoms", t => t.HasCheckConstraint("CK_ProductUoms_ConversionFactor", "[ConversionFactor] > 0"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.ProductId, x.UnitId, x.Version }).IsUnique();
        builder.Property(x => x.ConversionFactor).HasPrecision(18, 8);
        builder.HasOne(x => x.Product).WithMany(x => x.Uoms).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Unit).WithMany(x => x.ProductUoms).HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
