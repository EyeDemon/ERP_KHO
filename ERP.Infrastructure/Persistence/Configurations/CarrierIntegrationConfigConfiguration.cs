using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class CarrierIntegrationConfigConfiguration : IEntityTypeConfiguration<CarrierIntegrationConfig>
{
    public void Configure(EntityTypeBuilder<CarrierIntegrationConfig> builder)
    {
        builder.ToTable("CarrierIntegrationConfigs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CarrierCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ShopId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EncryptedApiToken).HasMaxLength(500);
        builder.Property(x => x.WebhookSecretHash).HasMaxLength(256).IsRequired();
        builder.Property(x => x.SortingHubPrefix).HasMaxLength(64);
        builder.Property(x => x.BaseShippingFeeVnd).HasPrecision(19, 4);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.WarehouseId, x.CarrierCode }).IsUnique();
    }
}
