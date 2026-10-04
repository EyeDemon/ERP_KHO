using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class WarehouseLocationConfiguration : IEntityTypeConfiguration<WarehouseLocation>
{
    public void Configure(EntityTypeBuilder<WarehouseLocation> b)
    {
        b.ToTable("WarehouseLocations");
        b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(64); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.StructurePath).HasMaxLength(160); b.Property(x => x.StorageClass).HasMaxLength(32); b.Property(x => x.MaxWeightKg).HasPrecision(18,6); b.Property(x => x.MaxVolumeM3).HasPrecision(18,8); b.Property(x => x.MaxPalletEquivalent).HasPrecision(18,8); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.StructurePath });
        b.HasIndex(x => new { x.WarehouseId, x.StorageClass });
        b.HasOne(x => x.Warehouse).WithMany(x => x.Locations).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PutawayTaskConfiguration : IEntityTypeConfiguration<PutawayTask>
{
    public void Configure(EntityTypeBuilder<PutawayTask> b)
    {
        b.ToTable("PutawayTasks"); b.HasKey(x => x.Id); b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.ExceptionReason).HasMaxLength(500);
        b.HasIndex(x => x.ReceiptId).IsUnique();
        b.HasOne(x => x.Receipt).WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PutawayTaskItemConfiguration : IEntityTypeConfiguration<PutawayTaskItem>
{
    public void Configure(EntityTypeBuilder<PutawayTaskItem> b)
    {
        b.ToTable("PutawayTaskItems");
        b.HasKey(x => x.Id); b.Ignore(x => x.RemainingBaseQuantity); b.Property(x => x.RowVersion).IsRowVersion();
        foreach (var p in new[] { nameof(PutawayTaskItem.ConversionFactorSnapshot), nameof(PutawayTaskItem.RequiredOperationQuantity), nameof(PutawayTaskItem.RequiredBaseQuantity), nameof(PutawayTaskItem.MovedBaseQuantity) }) b.Property(p).HasPrecision(18,4);
        b.HasIndex(x => new { x.PutawayTaskId, x.ReceiptLineId, x.InventoryStatus }).IsUnique();
        b.HasOne(x => x.PutawayTask).WithMany(x => x.Items).HasForeignKey(x => x.PutawayTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ReceiptLine).WithMany().HasForeignKey(x => x.ReceiptLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SourceLocation).WithMany().HasForeignKey(x => x.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryLocationMovementConfiguration : IEntityTypeConfiguration<InventoryLocationMovement>
{
    public void Configure(EntityTypeBuilder<InventoryLocationMovement> b)
    {
        b.ToTable("InventoryLocationMovements");
        b.HasKey(x => x.Id); b.Property(x => x.BaseQuantity).HasPrecision(18,4); b.Property(x => x.EnteredQuantity).HasPrecision(18,4); b.Property(x => x.EnteredUnitCode).HasMaxLength(32);
        b.HasIndex(x => new { x.PutawayTaskItemId, x.CreatedAt });
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PutawayTask>().WithMany().HasForeignKey(x => x.PutawayTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PutawayTaskItem>().WithMany().HasForeignKey(x => x.PutawayTaskItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ImportReceipt>().WithMany().HasForeignKey(x => x.ReceiptId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ImportReceiptDetail>().WithMany().HasForeignKey(x => x.ReceiptLineId).OnDelete(DeleteBehavior.Restrict);
    }
}
