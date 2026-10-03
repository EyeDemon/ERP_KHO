using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> builder)
    {
        builder.ToTable("StockTransfers", t => t.HasCheckConstraint("CK_StockTransfers_DifferentWarehouses", "[SourceWarehouseId] <> [DestinationWarehouseId]"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.HasIndex(x => x.Code).IsUnique();
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.SourceWarehouseId);
        builder.HasIndex(x => x.DestinationWarehouseId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasOne(x => x.SourceWarehouse).WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.DestinationWarehouse).WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.DispatchedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ReceivedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CompletedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.CancelledBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockTransferDetailConfiguration : IEntityTypeConfiguration<StockTransferDetail>
{
    public void Configure(EntityTypeBuilder<StockTransferDetail> builder)
    {
        builder.ToTable("StockTransferDetails", t =>
        {
            t.HasCheckConstraint("CK_StockTransferDetails_Quantities_NonNegative", "[RequestedQuantity] > 0 AND [DispatchedQuantity] >= 0 AND [ReceivedQuantity] >= 0 AND [MissingQuantity] >= 0 AND [DamagedQuantity] >= 0");
            t.HasCheckConstraint("CK_StockTransferDetails_ReceiptAllocation", "[ReceivedQuantity] + [MissingQuantity] + [DamagedQuantity] <= [DispatchedQuantity]");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.DispatchedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.MissingQuantity).HasPrecision(18, 4);
        builder.Property(x => x.DamagedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasIndex(x => new { x.StockTransferId, x.ProductId }).IsUnique();
        builder.HasOne(x => x.StockTransfer).WithMany(x => x.Details).HasForeignKey(x => x.StockTransferId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
