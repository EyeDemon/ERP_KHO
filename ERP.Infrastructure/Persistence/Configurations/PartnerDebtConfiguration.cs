using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class PartnerDebtInvoiceConfiguration : IEntityTypeConfiguration<PartnerDebtInvoice>
{
    public void Configure(EntityTypeBuilder<PartnerDebtInvoice> builder)
    {
        builder.ToTable("PartnerDebtInvoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.Property(x => x.SourceType).IsRequired().HasMaxLength(50);
        builder.Property(x => x.SourceCode).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Currency).IsRequired().HasMaxLength(10).HasDefaultValue("VND");
        builder.Property(x => x.SubTotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.TaxAmount).HasPrecision(18, 2);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaidAmount).HasPrecision(18, 2);
        builder.Property(x => x.RemainingAmount).HasPrecision(18, 2);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.Code })
               .IsUnique()
               .HasDatabaseName("UX_PartnerDebtInvoices_TenantId_InvoiceCode");
        builder.HasIndex(x => new { x.TenantId, x.DebtType, x.SourceType, x.SourceId })
               .IsUnique()
               .HasDatabaseName("UX_PartnerDebtInvoices_TenantId_DebtType_SourceType_SourceId");
        builder.HasIndex(x => new { x.TenantId, x.PartnerId, x.DebtType, x.Status });

        builder.HasOne(x => x.Partner)
               .WithMany(x => x.DebtInvoices)
               .HasForeignKey(x => x.PartnerId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedBy)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PartnerDebtLedgerConfiguration : IEntityTypeConfiguration<PartnerDebtLedger>
{
    public void Configure(EntityTypeBuilder<PartnerDebtLedger> builder)
    {
        builder.ToTable("PartnerDebtLedgers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ReferenceType).IsRequired().HasMaxLength(50);
        builder.Property(x => x.ReferenceCode).IsRequired().HasMaxLength(80);
        builder.Property(x => x.AmountDelta).HasPrecision(18, 2);
        builder.Property(x => x.BalanceAfter).HasPrecision(18, 2);
        builder.Property(x => x.PaymentMethod).HasMaxLength(50);
        builder.Property(x => x.Note).HasMaxLength(500);

        builder.HasIndex(x => new { x.TenantId, x.PartnerId, x.DebtType, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.InvoiceId, x.CreatedAtUtc });

        builder.HasOne(x => x.Partner)
               .WithMany()
               .HasForeignKey(x => x.PartnerId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Invoice)
               .WithMany(x => x.LedgerEntries)
               .HasForeignKey(x => x.InvoiceId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedBy)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
