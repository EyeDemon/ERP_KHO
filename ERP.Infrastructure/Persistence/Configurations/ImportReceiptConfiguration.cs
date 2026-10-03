  using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class ImportReceiptConfiguration : IEntityTypeConfiguration<ImportReceipt>
{
    public void Configure(EntityTypeBuilder<ImportReceipt> builder)
    {
        builder.ToTable("ImportReceipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.SupplierCodeSnapshot).HasMaxLength(50);
        builder.Property(x => x.SupplierNameSnapshot).HasMaxLength(200);
        builder.HasOne(x => x.Supplier).WithMany(x => x.ImportReceipts).HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
               .WithMany(w => w.ImportReceipts)
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
    }
}
