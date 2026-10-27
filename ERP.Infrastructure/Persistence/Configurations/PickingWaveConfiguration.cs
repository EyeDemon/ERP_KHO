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
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.WarehouseId, x.WaveCode });
    }
}
