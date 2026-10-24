using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class StocktakeAdjustmentProposalConfiguration : IEntityTypeConfiguration<StocktakeAdjustmentProposal>
{
    public void Configure(EntityTypeBuilder<StocktakeAdjustmentProposal> builder)
    {
        builder.ToTable("StocktakeAdjustmentProposals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProposalCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReasonCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SystemQuantity).HasPrecision(18, 4);
        builder.Property(x => x.CountedQuantity).HasPrecision(18, 4);
        builder.Property(x => x.DeltaQuantity).HasPrecision(18, 4);
        builder.Property(x => x.VarianceQuantity).HasPrecision(18, 4);
        builder.Property(x => x.UnitCost).HasPrecision(18, 4);
        builder.Property(x => x.VariancePct).HasPrecision(18, 4);
        builder.Property(x => x.VarianceValue).HasPrecision(18, 4);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.ProposalCode })
               .IsUnique()
               .HasDatabaseName("UX_StocktakeAdjustmentProposals_TenantId_ProposalCode");
        builder.HasOne(x => x.Warehouse)
               .WithMany()
               .HasForeignKey(x => x.WarehouseId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Stocktake)
               .WithMany()
               .HasForeignKey(x => x.StocktakeId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product)
               .WithMany()
               .HasForeignKey(x => x.ProductId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Location)
               .WithMany()
               .HasForeignKey(x => x.LocationId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
