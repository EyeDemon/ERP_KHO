using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class QcPolicyConfiguration : IEntityTypeConfiguration<QcPolicy>
{
    public void Configure(EntityTypeBuilder<QcPolicy> builder)
    {
        builder.ToTable("QcPolicies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Rule).HasMaxLength(500);
        builder.HasIndex(x => new { x.ProductId, x.SupplierId, x.Version }).IsUnique().HasFilter("[SupplierId] IS NOT NULL");
        builder.HasIndex(x => new { x.ProductId, x.Version }).IsUnique().HasFilter("[SupplierId] IS NULL");
        builder.HasOne(x => x.Product).WithMany(x => x.QcPolicies).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}
