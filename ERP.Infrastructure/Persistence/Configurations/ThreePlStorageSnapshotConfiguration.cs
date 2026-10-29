using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class ThreePlStorageSnapshotConfiguration : IEntityTypeConfiguration<ThreePlStorageSnapshot>
{
    public void Configure(EntityTypeBuilder<ThreePlStorageSnapshot> builder)
    {
        builder.ToTable("ThreePlStorageSnapshots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OwnerCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OwnerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SnapshotCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TotalOnHandQty).HasPrecision(19, 4);
        builder.Property(x => x.TotalVolumeCbm).HasPrecision(19, 4);
        builder.Property(x => x.PalletEquivalentCount).HasPrecision(19, 4);
        builder.Property(x => x.DailyPalletRateVnd).HasPrecision(19, 4);
        builder.Property(x => x.DailyCbmRateVnd).HasPrecision(19, 4);
        builder.Property(x => x.DailyBinRateVnd).HasPrecision(19, 4);
        builder.Property(x => x.EstimatedDailyStorageFeeVnd).HasPrecision(19, 4);
        builder.Property(x => x.EstimatedMonthlyStorageFeeVnd).HasPrecision(19, 4);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.WarehouseId, x.SnapshotDateUtc });
    }
}
