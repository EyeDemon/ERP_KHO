using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class IntegrationOutboxMessageConfiguration : IEntityTypeConfiguration<IntegrationOutboxMessage>
{
    public void Configure(EntityTypeBuilder<IntegrationOutboxMessage> builder)
    {
        builder.ToTable("IntegrationOutboxMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.EventId }).IsUnique().HasDatabaseName("UX_IntegrationOutboxMessages_TenantId_EventId");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.NextRetryAt });
    }
}

public sealed class IntegrationInboxMessageConfiguration : IEntityTypeConfiguration<IntegrationInboxMessage>
{
    public void Configure(EntityTypeBuilder<IntegrationInboxMessage> builder)
    {
        builder.ToTable("IntegrationInboxMessages");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceSystem).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExternalMessageId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.SourceSystem, x.ExternalMessageId })
               .IsUnique()
               .HasDatabaseName("UX_IntegrationInboxMessages_TenantId_SourceSystem_ExternalMessageId");
    }
}

public sealed class ProductBundleComponentConfiguration : IEntityTypeConfiguration<ProductBundleComponent>
{
    public void Configure(EntityTypeBuilder<ProductBundleComponent> builder)
    {
        builder.ToTable("ProductBundleComponents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ComponentQuantity).HasPrecision(19, 4);
        builder.HasIndex(x => new { x.TenantId, x.ParentProductId, x.ComponentProductId })
               .IsUnique()
               .HasDatabaseName("UX_ProductBundleComponents_TenantId_Parent_Component");
        builder.HasOne(x => x.ParentProduct)
               .WithMany()
               .HasForeignKey(x => x.ParentProductId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ComponentProduct)
               .WithMany()
               .HasForeignKey(x => x.ComponentProductId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
