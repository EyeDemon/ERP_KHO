using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class StocktakeDetailConfiguration : IEntityTypeConfiguration<StocktakeDetail>
{
    public void Configure(EntityTypeBuilder<StocktakeDetail> builder)
    {
        builder.ToTable("StocktakeDetails");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SystemQuantity).HasPrecision(18, 4);
        builder.Property(x => x.ActualQuantity).HasPrecision(18, 4);
        builder.Property(x => x.DifferenceQuantity).HasPrecision(18, 4);
        builder.Property(x => x.CountAttempt1Qty).HasPrecision(19, 4);
        builder.Property(x => x.CountAttempt2Qty).HasPrecision(19, 4);
        builder.Property(x => x.CountAttempt3Qty).HasPrecision(19, 4);
        builder.Property(x => x.CountAttemptCount).HasDefaultValue(0);
        builder.Property(x => x.RecountReason).HasMaxLength(500);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasOne(x => x.Stocktake)
               .WithMany(s => s.Details)
               .HasForeignKey(x => x.StocktakeId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Product)
               .WithMany()
               .HasForeignKey(x => x.ProductId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Location)
               .WithMany()
               .HasForeignKey(x => x.LocationId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.StocktakeId, x.ProductId, x.LocationId })
               .IsUnique()
               .HasFilter("[LocationId] IS NOT NULL");
    }
}
