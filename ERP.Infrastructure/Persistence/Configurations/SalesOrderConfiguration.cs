using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> b)
    {
        b.ToTable("SalesOrders", t => t.HasCheckConstraint("CK_SalesOrders_Priority", "[Priority] >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.OrderCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.ExternalOrderId).HasMaxLength(100).IsRequired();
        b.Property(x => x.ShippingMethod).HasMaxLength(100);
        b.Property(x => x.CancellationReason).HasMaxLength(500);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.OrderCode).IsUnique();
        b.HasIndex(x => x.ExternalOrderId).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status, x.RequestedShipDate });
        b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SalesOrderLineConfiguration : IEntityTypeConfiguration<SalesOrderLine>
{
    public void Configure(EntityTypeBuilder<SalesOrderLine> b)
    {
        b.ToTable("SalesOrderLines", t =>
        {
            t.HasCheckConstraint("CK_SalesOrderLines_OrderedQuantity", "[OrderedQuantity] > 0");
            t.HasCheckConstraint("CK_SalesOrderLines_CancelledQuantity", "[CancelledQuantity] >= 0 AND [CancelledQuantity] <= [OrderedQuantity]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.ExternalLineId).HasMaxLength(100).IsRequired();
        b.Property(x => x.UomCode).HasMaxLength(20).IsRequired();
        b.Property(x => x.OrderedQuantity).HasPrecision(18, 4);
        b.Property(x => x.CancelledQuantity).HasPrecision(18, 4);
        b.HasIndex(x => new { x.SalesOrderId, x.ExternalLineId }).IsUnique();
        b.HasIndex(x => new { x.SalesOrderId, x.ProductId }).IsUnique();
        b.HasOne(x => x.SalesOrder).WithMany(x => x.Lines).HasForeignKey(x => x.SalesOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BackorderConfiguration : IEntityTypeConfiguration<Backorder>
{
    public void Configure(EntityTypeBuilder<Backorder> b)
    {
        b.ToTable("Backorders", t =>
        {
            t.HasCheckConstraint("CK_Backorders_Quantity", "[Quantity] > 0");
            t.HasCheckConstraint("CK_Backorders_RecoveredQuantity", "[RecoveredQuantity] >= 0");
            t.HasCheckConstraint("CK_Backorders_CancelledQuantity", "[CancelledQuantity] >= 0 AND [RecoveredQuantity] + [CancelledQuantity] <= [Quantity]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.BackorderCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.Property(x => x.RecoveredQuantity).HasPrecision(18, 4);
        b.Property(x => x.CancelledQuantity).HasPrecision(18, 4);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => x.BackorderCode).IsUnique();
        b.HasIndex(x => x.SalesOrderLineId).IsUnique();
        b.HasIndex(x => new { x.WarehouseId, x.Status, x.CreatedAt });
        b.HasOne(x => x.SalesOrderLine).WithOne(x => x.Backorder).HasForeignKey<Backorder>(x => x.SalesOrderLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}
