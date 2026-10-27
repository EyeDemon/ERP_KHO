using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class PutToWallSlotConfiguration : IEntityTypeConfiguration<PutToWallSlot>
{
    public void Configure(EntityTypeBuilder<PutToWallSlot> builder)
    {
        builder.ToTable("PutToWallSlots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SlotCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ToteBarcode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OrderReference).HasMaxLength(64);
        builder.Property(x => x.ProductCode).HasMaxLength(64);
        builder.Property(x => x.RequiredQuantity).HasPrecision(19, 4);
        builder.Property(x => x.SortedQuantity).HasPrecision(19, 4);
        builder.Property(x => x.SlotStatus).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.PickingTaskId, x.SlotCode }).IsUnique();
    }
}
