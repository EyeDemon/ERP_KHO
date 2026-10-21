using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class MarketplaceShopConfiguration : IEntityTypeConfiguration<MarketplaceShop>
{
    public void Configure(EntityTypeBuilder<MarketplaceShop> builder)
    {
        builder.ToTable("MarketplaceShops");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChannelCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ShopCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ShopName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ReserveRatioPct).HasPrecision(5, 2).HasDefaultValue(100m);
        builder.Property(x => x.SafetyBufferQty).HasPrecision(19, 4).HasDefaultValue(0m);
        builder.Property(x => x.LowStockThreshold).HasPrecision(19, 4).HasDefaultValue(5m);
        builder.Property(x => x.EncryptedAccessToken).HasMaxLength(500);
        builder.Property(x => x.EncryptedRefreshToken).HasMaxLength(500);
        builder.Property(x => x.WebhookSecret).HasMaxLength(128);
        builder.HasIndex(x => new { x.TenantId, x.ChannelCode, x.ShopCode })
               .IsUnique()
               .HasDatabaseName("UX_MarketplaceShops_TenantId_ChannelCode_ShopCode");
        builder.HasOne(x => x.DefaultWarehouse)
               .WithMany()
               .HasForeignKey(x => x.DefaultWarehouseId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ChannelSkuMappingConfiguration : IEntityTypeConfiguration<ChannelSkuMapping>
{
    public void Configure(EntityTypeBuilder<ChannelSkuMapping> builder)
    {
        builder.ToTable("ChannelSkuMappings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChannelItemId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ChannelVariationId).HasMaxLength(64);
        builder.Property(x => x.ChannelSkuCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ConversionFactor).HasPrecision(19, 4).HasDefaultValue(1m);
        builder.Property(x => x.LastPushedQty).HasPrecision(19, 4).HasDefaultValue(0m);
        builder.HasIndex(x => new { x.TenantId, x.ShopId, x.ChannelSkuCode })
               .IsUnique()
               .HasDatabaseName("UX_ChannelSkuMappings_TenantId_ShopId_ChannelSkuCode");
        builder.HasOne(x => x.Shop)
               .WithMany(s => s.SkuMappings)
               .HasForeignKey(x => x.ShopId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InternalProduct)
               .WithMany()
               .HasForeignKey(x => x.InternalProductId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class HandoverManifestConfiguration : IEntityTypeConfiguration<HandoverManifest>
{
    public void Configure(EntityTypeBuilder<HandoverManifest> builder)
    {
        builder.ToTable("HandoverManifests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ManifestCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CarrierCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.VehiclePlate).HasMaxLength(32);
        builder.Property(x => x.DriverName).HasMaxLength(128);
        builder.Property(x => x.DriverPhone).HasMaxLength(32);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ManifestCode })
               .IsUnique()
               .HasDatabaseName("UX_HandoverManifests_TenantId_ManifestCode");
        builder.HasOne(x => x.Warehouse)
               .WithMany()
               .HasForeignKey(x => x.WarehouseId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class HandoverManifestLineConfiguration : IEntityTypeConfiguration<HandoverManifestLine>
{
    public void Configure(EntityTypeBuilder<HandoverManifestLine> builder)
    {
        builder.ToTable("HandoverManifestLines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TrackingNumber).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MarketplaceOrderId).HasMaxLength(64);
        builder.HasIndex(x => new { x.TenantId, x.TrackingNumber })
               .IsUnique()
               .HasDatabaseName("UX_HandoverManifestLines_TenantId_TrackingNumber");
        builder.HasOne(x => x.Manifest)
               .WithMany(m => m.Lines)
               .HasForeignKey(x => x.ManifestId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ExportReceipt)
               .WithMany()
               .HasForeignKey(x => x.ExportReceiptId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SaasPlanConfiguration : IEntityTypeConfiguration<SaasPlan>
{
    public void Configure(EntityTypeBuilder<SaasPlan> builder)
    {
        builder.ToTable("SaasPlans");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlanCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PlanName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.MonthlyPriceVnd).HasPrecision(19, 4);
        builder.HasIndex(x => x.PlanCode)
               .IsUnique()
               .HasDatabaseName("UX_SaasPlans_PlanCode");
    }
}

public sealed class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> builder)
    {
        builder.ToTable("TenantSubscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlanCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.TenantId)
               .IsUnique()
               .HasDatabaseName("UX_TenantSubscriptions_TenantId");
    }
}

public sealed class SaasBillingInvoiceConfiguration : IEntityTypeConfiguration<SaasBillingInvoice>
{
    public void Configure(EntityTypeBuilder<SaasBillingInvoice> builder)
    {
        builder.ToTable("SaasBillingInvoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InvoiceCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PlanCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AmountVnd).HasPrecision(19, 4);
        builder.Property(x => x.PaymentProvider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.QrPayload).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.InvoiceCode })
               .IsUnique()
               .HasDatabaseName("UX_SaasBillingInvoices_TenantId_InvoiceCode");
    }
}

public sealed class ShipmentFinancialLedgerConfiguration : IEntityTypeConfiguration<ShipmentFinancialLedger>
{
    public void Configure(EntityTypeBuilder<ShipmentFinancialLedger> builder)
    {
        builder.ToTable("ShipmentFinancialLedgers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CarrierCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.TrackingNumber).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExpectedCodAmount).HasPrecision(19, 4);
        builder.Property(x => x.ActualCodCollected).HasPrecision(19, 4);
        builder.Property(x => x.EstimatedShippingFee).HasPrecision(19, 4);
        builder.Property(x => x.ActualShippingFee).HasPrecision(19, 4);
        builder.Property(x => x.PlatformCommissionFee).HasPrecision(19, 4);
        builder.Property(x => x.ReturnFee).HasPrecision(19, 4);
        builder.Property(x => x.NetPayoutAmount).HasPrecision(19, 4);
        builder.Property(x => x.FeeOverchargeVariance).HasPrecision(19, 4);
        builder.Property(x => x.ReconciliationStatus).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.TrackingNumber })
               .IsUnique()
               .HasDatabaseName("UX_ShipmentFinancialLedgers_TenantId_TrackingNumber");
        builder.HasOne(x => x.ExportReceipt)
               .WithMany()
               .HasForeignKey(x => x.ExportReceiptId)
               .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Shipment)
               .WithMany()
               .HasForeignKey(x => x.ShipmentId)
               .OnDelete(DeleteBehavior.NoAction);
    }
}
