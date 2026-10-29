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
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("UX_ExportReceipts_TenantId_Code");
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.Property(x => x.RowVersion).IsConcurrencyToken();
        builder.Property(x => x.DispatchMode);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.CustomerCodeSnapshot).HasMaxLength(50);
        builder.Property(x => x.CustomerNameSnapshot).HasMaxLength(200);
        builder.Property(x => x.MarketplaceOrderId).HasMaxLength(64);
        builder.Property(x => x.CarrierCode).HasMaxLength(32);
        builder.Property(x => x.TrackingNumber).HasMaxLength(64);
        builder.Property(x => x.AwbLabelUrl).HasMaxLength(500);
        builder.Property(x => x.AwbPrintCount).HasDefaultValue(0);
        builder.Property(x => x.EInvoiceStatus).HasMaxLength(32);
        builder.Property(x => x.EInvoiceProvider).HasMaxLength(32);
        builder.Property(x => x.EInvoiceNumber).HasMaxLength(64);
        builder.Property(x => x.BuyerTaxCode).HasMaxLength(32);
        builder.HasOne(x => x.Customer).WithMany(x => x.ExportReceipts).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SalesOrder).WithMany().HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Shipment).WithMany().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.SalesOrderId);
        builder.HasIndex(x => x.ShipmentId);

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
