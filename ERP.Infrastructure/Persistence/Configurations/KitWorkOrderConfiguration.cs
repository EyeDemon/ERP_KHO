using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class KitWorkOrderConfiguration : IEntityTypeConfiguration<KitWorkOrder>
{
    public void Configure(EntityTypeBuilder<KitWorkOrder> builder)
    {
        builder.ToTable("KitWorkOrders", t =>
        {
            t.HasCheckConstraint("CK_KitWorkOrders_PlannedKitQty", "[PlannedKitQty] > 0");
            t.HasCheckConstraint("CK_KitWorkOrders_CompletedKitQty", "[CompletedKitQty] >= 0");
            t.HasCheckConstraint("CK_KitWorkOrders_RolledUpUnitCost", "[RolledUpUnitCost] >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.WorkOrderCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.PlannedKitQty).HasPrecision(19, 4);
        builder.Property(x => x.CompletedKitQty).HasPrecision(19, 4);
        builder.Property(x => x.RolledUpUnitCost).HasPrecision(19, 4);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.WarehouseId, x.WorkOrderCode }).IsUnique();
        builder.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.KitProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.TargetLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryLot>().WithMany().HasForeignKey(x => x.TargetLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HandlingUnit>().WithMany().HasForeignKey(x => x.TargetHandlingUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class KitWorkOrderComponentLineConfiguration : IEntityTypeConfiguration<KitWorkOrderComponentLine>
{
    public void Configure(EntityTypeBuilder<KitWorkOrderComponentLine> builder)
    {
        builder.ToTable("KitWorkOrderComponentLines", t =>
        {
            t.HasCheckConstraint("CK_KitWorkOrderComponentLines_ComponentRatioQty", "[ComponentRatioQty] > 0");
            t.HasCheckConstraint("CK_KitWorkOrderComponentLines_RequiredBaseQty", "[RequiredBaseQty] > 0");
            t.HasCheckConstraint("CK_KitWorkOrderComponentLines_ConsumedBaseQty", "[ConsumedBaseQty] >= 0");
            t.HasCheckConstraint("CK_KitWorkOrderComponentLines_ComponentUnitCost", "[ComponentUnitCost] >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ComponentRatioQty).HasPrecision(19, 4);
        builder.Property(x => x.RequiredBaseQty).HasPrecision(19, 4);
        builder.Property(x => x.ConsumedBaseQty).HasPrecision(19, 4);
        builder.Property(x => x.ComponentUnitCost).HasPrecision(19, 4);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.KitWorkOrderId, x.ComponentProductId });
        builder.HasOne(x => x.KitWorkOrder).WithMany(x => x.ComponentLines).HasForeignKey(x => x.KitWorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Product>().WithMany().HasForeignKey(x => x.ComponentProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WarehouseLocation>().WithMany().HasForeignKey(x => x.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<InventoryLot>().WithMany().HasForeignKey(x => x.SourceLotId).OnDelete(DeleteBehavior.Restrict);
    }
}
