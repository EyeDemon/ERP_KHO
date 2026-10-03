using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(100);
        builder.Property(x => x.EntityName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.OldValues).HasColumnType("nvarchar(max)");
        builder.Property(x => x.NewValues).HasColumnType("nvarchar(max)");
        builder.Property(x => x.IpAddress).HasMaxLength(50);
        builder.Property(x => x.Result).HasMaxLength(32);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.IdempotencyKeyHash).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.RequestFingerprint).HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.Severity).HasMaxLength(32).IsRequired().HasDefaultValue("Information");
        builder.HasIndex(x => x.Timestamp);

        builder.HasOne(x => x.User)
               .WithMany()
               .HasForeignKey(x => x.UserId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
