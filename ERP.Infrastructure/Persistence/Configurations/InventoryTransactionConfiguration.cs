using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("InventoryTransactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Quantity).HasPrecision(18, 4);
        builder.Property(x => x.ReferenceType).HasMaxLength(50);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.Property(x => x.ReasonCode).HasMaxLength(40);
        builder.Property(x => x.InventoryStatus).HasConversion<int>();
        builder.Property(x => x.FromInventoryStatus).HasConversion<int?>();
        builder.Property(x => x.ToInventoryStatus).HasConversion<int?>();
        builder.HasIndex(x => x.ProductId);
        builder.HasIndex(x => x.WarehouseId);
        builder.HasIndex(x => x.TransactionDate);
        builder.HasIndex(x => new { x.ProductId, x.WarehouseId, x.TransactionDate });
        builder.HasIndex(x => x.ReversalOfTransactionId)
               .IsUnique()
               .HasDatabaseName("UX_InventoryTransactions_ReversalOfTransaction")
               .HasFilter("[ReversalOfTransactionId] IS NOT NULL");
        builder.HasIndex(x => x.CorrectiveTransactionId)
               .IsUnique()
               .HasDatabaseName("UX_InventoryTransactions_CorrectiveTransaction")
               .HasFilter("[CorrectiveTransactionId] IS NOT NULL");
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId, x.TransactionType, x.ProductId, x.WarehouseId })
               .IsUnique()
               .HasFilter("[ReferenceType] = 'StockTransfer'");
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId, x.TransactionType, x.ProductId, x.WarehouseId, x.LocationId, x.InventoryStatus, x.LotId, x.SerialId })
               .IsUnique()
               .HasDatabaseName("IX_InventoryTransactions_ShipmentReference")
               .HasFilter("[ReferenceType] = 'Shipment'");

        builder.HasOne(x => x.Product)
               .WithMany()
               .HasForeignKey(x => x.ProductId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Warehouse)
               .WithMany()
               .HasForeignKey(x => x.WarehouseId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.FromLocation).WithMany().HasForeignKey(x => x.FromLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToLocation).WithMany().HasForeignKey(x => x.ToLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Lot).WithMany().HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Serial).WithMany().HasForeignKey(x => x.SerialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StatusDefinition).WithMany().HasForeignKey(x => x.InventoryStatus).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReversalOfTransaction).WithMany().HasForeignKey(x => x.ReversalOfTransactionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CorrectiveTransaction).WithMany().HasForeignKey(x => x.CorrectiveTransactionId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByUser)
               .WithMany()
               .HasForeignKey(x => x.CreatedBy)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
