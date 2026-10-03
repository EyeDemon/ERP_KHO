using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.ToTable("ProductBarcodes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Value).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.Value).IsUnique().HasDatabaseName("UX_ProductBarcodes_Value");
        builder.HasOne(x => x.Product).WithMany(x => x.Barcodes).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
