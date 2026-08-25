using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("StockReservations", t =>
        {
            t.HasCheckConstraint("CK_StockReservations_Quantity", "[Quantity] > 0 AND [ConsumedQuantity] >= 0 AND [ReleasedQuantity] >= 0 AND [ConsumedQuantity] + [ReleasedQuantity] <= [Quantity]");
            t.HasCheckConstraint("CK_StockReservations_Expiry", "[ExpiresAt] > [CreatedAt]");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReservationCode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceCode).HasMaxLength(50);
        builder.Property(x => x.ReleaseReason).HasMaxLength(500);
        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.ConsumedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.ReleasedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.HasIndex(x => x.ReservationCode).IsUnique();
        builder.HasIndex(x => new { x.ProductId, x.WarehouseId, x.Status, x.ExpiresAt });
        builder.HasIndex(x => new { x.SourceType, x.SourceId, x.ProductId }).IsUnique().HasFilter("[SourceId] IS NOT NULL");
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReleasedByUser).WithMany().HasForeignKey(x => x.ReleasedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
