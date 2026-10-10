using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("PurchaseOrders");
        b.HasKey(x => x.Id);
        b.Property(x => x.ExternalPoId).HasMaxLength(100).IsRequired();
        b.Property(x => x.SourceSystem).HasMaxLength(50).IsRequired();
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(10);
        b.Property(x => x.ExternalVersion).HasMaxLength(100);
        b.Property(x => x.RowVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.SourceSystem, x.ExternalPoId }).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("PurchaseOrderLines", t =>
        {
            t.HasCheckConstraint("CK_PurchaseOrderLines_Quantity", "[OrderedQuantity] > 0 AND [BaseOrderedQuantity] > 0 AND [ConversionFactorSnapshot] > 0");
            t.HasCheckConstraint("CK_PurchaseOrderLines_Tolerance", "[AllowedOverReceiptPct] >= 0 AND [AllowedOverReceiptPct] <= 100 AND [AllowedUnderReceiptPct] >= 0 AND [AllowedUnderReceiptPct] <= 100");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.ExternalLineId).HasMaxLength(100).IsRequired();
        b.Property(x => x.OperationUnitCodeSnapshot).HasMaxLength(20).IsRequired();
        b.Property(x => x.BaseUnitCodeSnapshot).HasMaxLength(20).IsRequired();
        b.Property(x => x.OrderedQuantity).HasPrecision(18,4);
        b.Property(x => x.BaseOrderedQuantity).HasPrecision(18,4);
        b.Property(x => x.ConversionFactorSnapshot).HasPrecision(18,8);
        b.Property(x => x.AllowedOverReceiptPct).HasPrecision(9,4);
        b.Property(x => x.AllowedUnderReceiptPct).HasPrecision(9,4);
        b.HasIndex(x => new { x.PurchaseOrderId, x.ExternalLineId }).IsUnique();
        b.HasIndex(x => new { x.PurchaseOrderId, x.LineNo }).IsUnique();
        b.HasOne(x => x.PurchaseOrder).WithMany(x => x.Lines).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Unit>().WithMany().HasForeignKey(x => x.OperationUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Unit>().WithMany().HasForeignKey(x => x.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AsnConfiguration : IEntityTypeConfiguration<Asn>
{
    public void Configure(EntityTypeBuilder<Asn> b)
    {
        b.ToTable("Asns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.CarrierName).HasMaxLength(200);
        b.Property(x => x.VehiclePlate).HasMaxLength(30);
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique();
        b.HasOne(x => x.PurchaseOrder).WithMany(x => x.Asns).HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AsnLineConfiguration : IEntityTypeConfiguration<AsnLine>
{
    public void Configure(EntityTypeBuilder<AsnLine> b)
    {
        b.ToTable("AsnLines", t => t.HasCheckConstraint("CK_AsnLines_Quantity", "[ExpectedQuantity] > 0 AND [BaseExpectedQuantity] > 0 AND [ConversionFactorSnapshot] > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.OperationUnitCodeSnapshot).HasMaxLength(20).IsRequired();
        b.Property(x => x.BaseUnitCodeSnapshot).HasMaxLength(20).IsRequired();
        b.Property(x => x.ExpectedQuantity).HasPrecision(18,4);
        b.Property(x => x.BaseExpectedQuantity).HasPrecision(18,4);
        b.Property(x => x.ConversionFactorSnapshot).HasPrecision(18,8);
        b.HasIndex(x => new { x.AsnId, x.LineNo }).IsUnique();
        b.HasOne(x => x.Asn).WithMany(x => x.Lines).HasForeignKey(x => x.AsnId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PurchaseOrderLine).WithMany(x => x.AsnLines).HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Unit>().WithMany().HasForeignKey(x => x.OperationUnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Unit>().WithMany().HasForeignKey(x => x.BaseUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
