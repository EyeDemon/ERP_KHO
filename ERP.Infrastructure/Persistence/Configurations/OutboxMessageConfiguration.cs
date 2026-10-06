using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("OutboxMessages");
        b.HasKey(x => x.Id);
        b.Property(x => x.EventKey).HasMaxLength(120).IsRequired();
        b.Property(x => x.EventType).HasMaxLength(120).IsRequired();
        b.Property(x => x.AggregateType).HasMaxLength(80).IsRequired();
        b.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(x => x.LastError).HasMaxLength(2000);
        b.HasIndex(x => x.EventKey).IsUnique();
        b.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });
        b.HasIndex(x => new { x.AggregateType, x.AggregateId });
    }
}
