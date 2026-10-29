using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class PickingWaveConfiguration : IEntityTypeConfiguration<PickingWave>
{
    public void Configure(EntityTypeBuilder<PickingWave> builder)
    {
        builder.ToTable("PickingWaves");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.WaveCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PickingType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.TotalRequestedQuantity).HasPrecision(19, 4);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.TemplateCode).HasMaxLength(64);
        builder.Property(x => x.CarrierCutoffWindow).HasMaxLength(32);
        builder.Property(x => x.SlaTierFilter).HasMaxLength(32);
        builder.Property(x => x.ZoneCode).HasMaxLength(64);
        builder.Property(x => x.ClusterToteBarcodesCsv).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.WarehouseId, x.WaveCode });
    }
}

public class PickingWaveTemplateConfiguration : IEntityTypeConfiguration<PickingWaveTemplate>
{
    public void Configure(EntityTypeBuilder<PickingWaveTemplate> builder)
    {
        builder.ToTable("PickingWaveTemplates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TemplateCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TemplateName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CarrierCutoffWindow).HasMaxLength(32);
        builder.Property(x => x.SlaTierFilter).HasMaxLength(32);
        builder.Property(x => x.ZoneCode).HasMaxLength(64);
        builder.Property(x => x.PickingType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.WarehouseId, x.TemplateCode }).IsUnique();
    }
}
