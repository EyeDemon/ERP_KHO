using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class ExportReceiptDetailConfiguration : IEntityTypeConfiguration<ExportReceiptDetail>
{
    public void Configure(EntityTypeBuilder<ExportReceiptDetail> builder)
    {
        builder.ToTable("ExportReceiptDetails");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.ExportReceipt)
               .WithMany(r => r.Details)
               .HasForeignKey(x => x.ExportReceiptId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
               .WithMany()
               .HasForeignKey(x => x.ProductId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
