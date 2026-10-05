using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class PickingTaskConfiguration : IEntityTypeConfiguration<PickingTask>
{
    public void Configure(EntityTypeBuilder<PickingTask> b)
    {
        b.ToTable("PickingTasks", t => t.HasCheckConstraint("CK_PickingTasks_Priority", "[Priority] >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.TaskCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
        b.Property(x => x.SourceCode).HasMaxLength(50);
        b.Property(x => x.PickingType).HasMaxLength(30).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.WarehouseId, x.TaskCode }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status, x.Priority });
        b.HasIndex(x => new { x.SourceType, x.SourceId })
            .IsUnique()
            .HasFilter("[SourceId] IS NOT NULL");
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PickingTaskLineConfiguration : IEntityTypeConfiguration<PickingTaskLine>
{
    public void Configure(EntityTypeBuilder<PickingTaskLine> b)
    {
        b.ToTable("PickingTaskLines", t =>
        {
            t.HasCheckConstraint("CK_PickingTaskLines_Requested", "[RequestedQuantity] > 0");
            t.HasCheckConstraint("CK_PickingTaskLines_Picked", "[PickedQuantity] >= 0 AND [PickedQuantity] <= [RequestedQuantity]");
            t.HasCheckConstraint("CK_PickingTaskLines_Sequence", "[Sequence] > 0");
        });
        b.HasKey(x => x.Id);
        b.Ignore(x => x.RemainingQuantity);
        b.Property(x => x.RequestedQuantity).HasPrecision(18, 4);
        b.Property(x => x.PickedQuantity).HasPrecision(18, 4);
        b.HasIndex(x => x.AllocationId).IsUnique();
        b.HasIndex(x => new { x.PickingTaskId, x.Sequence }).IsUnique();
        b.HasOne(x => x.PickingTask).WithMany(x => x.Lines).HasForeignKey(x => x.PickingTaskId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Allocation).WithMany().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SourceLocation).WithMany().HasForeignKey(x => x.SourceLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ShortPickExceptionConfiguration : IEntityTypeConfiguration<ShortPickException>
{
    public void Configure(EntityTypeBuilder<ShortPickException> b)
    {
        b.ToTable("ShortPickExceptions", t =>
        {
            t.HasCheckConstraint(
                "CK_ShortPickExceptions_Quantities",
                "[ExpectedQuantity] > 0 AND [PickedQuantity] >= 0 AND [ShortageQuantity] > 0 AND [PickedQuantity] + [ShortageQuantity] = [ExpectedQuantity]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.ExpectedQuantity).HasPrecision(18, 4);
        b.Property(x => x.PickedQuantity).HasPrecision(18, 4);
        b.Property(x => x.ShortageQuantity).HasPrecision(18, 4);
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.ResolutionNote).HasMaxLength(500);
        b.HasIndex(x => new { x.PickingTaskLineId, x.Status });
        b.HasOne(x => x.PickingTaskLine).WithMany(x => x.ShortPicks).HasForeignKey(x => x.PickingTaskLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ResolvedByUser).WithMany().HasForeignKey(x => x.ResolvedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
