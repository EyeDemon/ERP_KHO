using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units", t => t.HasCheckConstraint("CK_Units_DecimalPlaces", "[DecimalPlaces] BETWEEN 0 AND 4"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(20);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("UX_Units_TenantId_Code");
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
    }
}
