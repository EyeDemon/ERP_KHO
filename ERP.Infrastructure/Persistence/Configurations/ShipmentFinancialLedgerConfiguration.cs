using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class ShipmentFinancialLedgerConfiguration : IEntityTypeConfiguration<ShipmentFinancialLedger>
{
    public void Configure(EntityTypeBuilder<ShipmentFinancialLedger> builder)
    {
        builder.ToTable("ShipmentFinancialLedgers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CarrierCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.TrackingNumber).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExpectedCodAmount).HasPrecision(18, 4);
        builder.Property(x => x.ActualCodCollected).HasPrecision(18, 4);
        builder.Property(x => x.CodVariance).HasPrecision(18, 4);
        builder.Property(x => x.EstimatedShippingFee).HasPrecision(18, 4);
        builder.Property(x => x.ActualShippingFee).HasPrecision(18, 4);
        builder.Property(x => x.ShippingFeeVariance).HasPrecision(18, 4);
        builder.Property(x => x.PlatformCommissionFee).HasPrecision(18, 4);
        builder.Property(x => x.SellerVoucherAmount).HasPrecision(18, 4);
        builder.Property(x => x.ReturnFee).HasPrecision(18, 4);
        builder.Property(x => x.NetSettlementAmount).HasPrecision(18, 4);
        builder.Property(x => x.NetPayoutAmount).HasPrecision(18, 4);
        builder.Property(x => x.FeeOverchargeVariance).HasPrecision(18, 4);
        builder.Property(x => x.ReconciliationStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.TrackingNumber })
               .IsUnique()
               .HasDatabaseName("IX_ShipmentFinancialLedgers_TenantId_TrackingNumber");
        builder.HasOne(x => x.ExportReceipt)
               .WithMany()
               .HasForeignKey(x => x.ExportReceiptId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Shipment)
               .WithMany()
               .HasForeignKey(x => x.ShipmentId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
