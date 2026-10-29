using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class ThreePlBillingStatementConfiguration : IEntityTypeConfiguration<ThreePlBillingStatement>
{
    public void Configure(EntityTypeBuilder<ThreePlBillingStatement> builder)
    {
        builder.ToTable("ThreePlBillingStatements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StatementCode).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TotalOnHandQty).HasPrecision(19, 4);
        builder.Property(x => x.TotalInboundQty).HasPrecision(19, 4);
        builder.Property(x => x.TotalOutboundQty).HasPrecision(19, 4);
        builder.Property(x => x.StorageFeeAmount).HasPrecision(19, 4);
        builder.Property(x => x.InboundHandlingFeeAmount).HasPrecision(19, 4);
        builder.Property(x => x.OutboundHandlingFeeAmount).HasPrecision(19, 4);
        builder.Property(x => x.TotalBillingAmount).HasPrecision(19, 4);
        builder.Property(x => x.ReceivableInvoiceCode).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.PartnerId, x.StatementCode });
    }
}
