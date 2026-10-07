using ERP.Domain.Entities;
using ERP.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class InventoryStatusDefinitionConfiguration : IEntityTypeConfiguration<InventoryStatusDefinition>
{
    public void Configure(EntityTypeBuilder<InventoryStatusDefinition> b)
    {
        b.ToTable("InventoryStatusDefinitions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasConversion<int>();
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasData(
            Seed(InventoryStatus.Available, "AVAILABLE", "Available", true, true, true, true, true),
            Seed(InventoryStatus.QcHold, "QC_HOLD", "QC Hold", false, false, false, false, false),
            Seed(InventoryStatus.Quarantine, "QUARANTINE", "Quarantine", false, false, false, false, false),
            Seed(InventoryStatus.Damaged, "DAMAGED", "Damaged", false, false, false, false, false),
            Seed(InventoryStatus.Rejected, "REJECTED", "Rejected", false, false, false, false, false),
            Seed(InventoryStatus.Blocked, "BLOCKED", "Blocked", false, false, false, false, false),
            Seed(InventoryStatus.Expired, "EXPIRED", "Expired", false, false, false, false, false),
            Seed(InventoryStatus.RecallBlocked, "RECALL_BLOCKED", "Recall Blocked", false, false, false, false, false));
    }

    private static InventoryStatusDefinition Seed(
        InventoryStatus id, string code, string name,
        bool available, bool reservable, bool allocatable, bool pickable, bool shippable) => new()
        {
            Id = id, Code = code, Name = name, IsAvailable = available, IsReservable = reservable,
            IsAllocatable = allocatable, IsPickable = pickable, IsShippable = shippable, IsSystem = true
        };
}

public sealed class InventoryLotConfiguration : IEntityTypeConfiguration<InventoryLot>
{
    public void Configure(EntityTypeBuilder<InventoryLot> b)
    {
        b.ToTable("InventoryLots", t => t.HasCheckConstraint(
            "CK_InventoryLots_Dates",
            "[ExpiryDate] IS NULL OR [ManufactureDate] IS NULL OR [ExpiryDate] >= [ManufactureDate]"));
        b.HasKey(x => x.Id);
        b.Property(x => x.LotNumber).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.ProductId, x.LotNumber }).IsUnique();
        b.HasIndex(x => x.ExpiryDate);
        b.HasOne(x => x.Product).WithMany(x => x.InventoryLots).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventorySerialConfiguration : IEntityTypeConfiguration<InventorySerial>
{
    public void Configure(EntityTypeBuilder<InventorySerial> b)
    {
        b.ToTable("InventorySerials");
        b.HasKey(x => x.Id);
        b.Property(x => x.SerialNumber).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.ProductId, x.SerialNumber }).IsUnique();
        b.HasOne(x => x.Product).WithMany(x => x.InventorySerials).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Lot).WithMany(x => x.Serials).HasForeignKey(x => x.LotId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ImportReceiptInventoryIdentityConfiguration : IEntityTypeConfiguration<ImportReceiptInventoryIdentity>
{
    public void Configure(EntityTypeBuilder<ImportReceiptInventoryIdentity> b)
    {
        b.ToTable("ImportReceiptInventoryIdentities", t =>
        {
            t.HasCheckConstraint("CK_ImportReceiptInventoryIdentities_Quantity", "[BaseQuantity] > 0");
            t.HasCheckConstraint("CK_ImportReceiptInventoryIdentities_SerialQuantity", "[SerialNumber] IS NULL OR [BaseQuantity] = 1");
            t.HasCheckConstraint("CK_ImportReceiptInventoryIdentities_Dates", "[ExpiryDate] IS NULL OR [ManufactureDate] IS NULL OR [ExpiryDate] >= [ManufactureDate]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.TargetStatus).HasConversion<int>();
        b.Property(x => x.BaseQuantity).HasPrecision(18, 4);
        b.Property(x => x.LotNumber).HasMaxLength(100);
        b.Property(x => x.SerialNumber).HasMaxLength(120);
        b.HasIndex(x => new { x.ImportReceiptDetailId, x.TargetStatus });
        b.HasIndex(x => new { x.ProductId, x.SerialNumber }).IsUnique().HasFilter("[SerialNumber] IS NOT NULL");
        b.HasOne(x => x.ImportReceiptDetail).WithMany(x => x.InventoryIdentities).HasForeignKey(x => x.ImportReceiptDetailId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
