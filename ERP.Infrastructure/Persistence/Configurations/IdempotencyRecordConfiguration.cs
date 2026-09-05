using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CommandScope).HasMaxLength(160).IsRequired();
        builder.Property(x => x.KeyHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ResponseBody).HasColumnType("nvarchar(max)");
        builder.HasIndex(x => new { x.UserId, x.CommandScope, x.KeyHash }).IsUnique();
        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
