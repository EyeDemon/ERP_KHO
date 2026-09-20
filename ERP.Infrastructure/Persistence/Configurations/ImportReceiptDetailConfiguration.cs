using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class ImportReceiptDetailConfiguration : IEntityTypeConfiguration<ImportReceiptDetail>
{
    public void Configure(EntityTypeBuilder<ImportReceiptDetail> builder)
    {
        builder.ToTable("ImportReceiptDetails");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.ExpectedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.AcceptedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.DamagedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.RejectedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.PostedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.ConversionFactor).HasPrecision(18, 8);
        builder.Property(x => x.BaseExpectedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.BaseReceivedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.BaseAcceptedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.BaseDamagedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.BaseRejectedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.BasePostedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.OperationUnitCodeSnapshot).HasMaxLength(20);
        builder.Property(x => x.BaseUnitCodeSnapshot).HasMaxLength(20);
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.ImportReceipt)
               .WithMany(r => r.Details)
               .HasForeignKey(x => x.ImportReceiptId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
               .WithMany()
               .HasForeignKey(x => x.ProductId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
