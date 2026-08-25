using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class ExportReceiptConfiguration : IEntityTypeConfiguration<ExportReceipt>
{
    public void Configure(EntityTypeBuilder<ExportReceipt> builder)
    {
        builder.ToTable("ExportReceipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.Property(x => x.DispatchMode);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.Warehouse)
               .WithMany(w => w.ExportReceipts)
               .HasForeignKey(x => x.WarehouseId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedBy)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ApprovedByUser)
               .WithMany()
               .HasForeignKey(x => x.ApprovedBy)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.DispatchedByUser)
               .WithMany()
               .HasForeignKey(x => x.DispatchedBy)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
