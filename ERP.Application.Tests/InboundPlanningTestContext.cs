using ERP.Domain.Entities;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.Application.Tests;

internal sealed class InboundPlanningTestContext(DbContextOptions<ErpKhoDbContext> options) : ErpKhoDbContext(options)
{
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken=default)
    {
        foreach(var entry in ChangeTracker.Entries().Where(x=>(x.Entity is PurchaseOrder or Asn) && (x.State is EntityState.Added or EntityState.Modified)))
            entry.Property("RowVersion").CurrentValue=Guid.NewGuid().ToByteArray();
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        foreach(var property in modelBuilder.Model.GetEntityTypes().SelectMany(x=>x.GetProperties()))
            if(property.GetColumnType()?.Contains("max",StringComparison.OrdinalIgnoreCase)==true) property.SetColumnType(null);

        modelBuilder.Entity<BusinessPartner>().Property(x=>x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        modelBuilder.Entity<PurchaseOrder>().Property(x=>x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        modelBuilder.Entity<Asn>().Property(x=>x.RowVersion).IsConcurrencyToken().ValueGeneratedNever();
        modelBuilder.Entity<PurchaseOrderLine>().Property(x=>x.OrderedQuantity).HasConversion<double>();
        modelBuilder.Entity<PurchaseOrderLine>().Property(x=>x.BaseOrderedQuantity).HasConversion<double>();
        modelBuilder.Entity<PurchaseOrderLine>().Property(x=>x.ConversionFactorSnapshot).HasConversion<double>();
        modelBuilder.Entity<PurchaseOrderLine>().Property(x=>x.AllowedOverReceiptPct).HasConversion<double>();
        modelBuilder.Entity<PurchaseOrderLine>().Property(x=>x.AllowedUnderReceiptPct).HasConversion<double>();
        modelBuilder.Entity<AsnLine>().Property(x=>x.ExpectedQuantity).HasConversion<double>();
        modelBuilder.Entity<AsnLine>().Property(x=>x.BaseExpectedQuantity).HasConversion<double>();
        modelBuilder.Entity<AsnLine>().Property(x=>x.ConversionFactorSnapshot).HasConversion<double>();
    }
}
