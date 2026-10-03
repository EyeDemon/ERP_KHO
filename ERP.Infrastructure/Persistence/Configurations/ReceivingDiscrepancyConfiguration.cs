using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class ReceivingDiscrepancyConfiguration : IEntityTypeConfiguration<ReceivingDiscrepancy>
{
    public void Configure(EntityTypeBuilder<ReceivingDiscrepancy> b)
    {
        b.ToTable("ReceivingDiscrepancies"); b.HasKey(x => x.Id);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.ImportReceiptId, x.ImportReceiptDetailId }).IsUnique();
        b.HasOne(x => x.ImportReceipt).WithMany(x => x.Discrepancies).HasForeignKey(x => x.ImportReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ImportReceiptDetail).WithMany(x => x.ReceivingDiscrepancies).HasForeignKey(x => x.ImportReceiptDetailId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ReceivingObservationVersionConfiguration : IEntityTypeConfiguration<ReceivingObservationVersion>
{
    public void Configure(EntityTypeBuilder<ReceivingObservationVersion> b)
    {
        b.ToTable("ReceivingObservationVersions", t => t.HasCheckConstraint("CK_ReceivingObservationVersions_Quantity", "[ObservedQuantity] >= 0 AND [BaseObservedQuantity] >= 0 AND [ConversionFactorSnapshot] > 0"));
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.ReceivingDiscrepancyId, x.Version }).IsUnique();
        b.Property(x => x.ObservedQuantity).HasPrecision(18,4); b.Property(x => x.BaseObservedQuantity).HasPrecision(18,4); b.Property(x => x.ConversionFactorSnapshot).HasPrecision(18,8);
        b.Property(x => x.ObservedUnitCodeSnapshot).HasMaxLength(20);
        b.HasOne(x => x.ReceivingDiscrepancy).WithMany(x => x.Observations).HasForeignKey(x => x.ReceivingDiscrepancyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PreviousObservationVersion).WithMany().HasForeignKey(x => x.PreviousObservationVersionId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ReceivingObservationItemConfiguration : IEntityTypeConfiguration<ReceivingObservationItem>
{
    public void Configure(EntityTypeBuilder<ReceivingObservationItem> b)
    {
        b.ToTable("ReceivingObservationItems", t => t.HasCheckConstraint("CK_ReceivingObservationItems_Quantity", "[Quantity] > 0"));
        b.HasKey(x => x.Id); b.Property(x => x.Quantity).HasPrecision(18,4); b.Property(x => x.ScanReference).HasMaxLength(100);
        b.HasOne(x => x.ReceivingObservationVersion).WithMany(x => x.Items).HasForeignKey(x => x.ReceivingObservationVersionId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.SupersededByObservationVersion).WithMany().HasForeignKey(x => x.SupersededByObservationVersionId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ReceivingResolutionVersionConfiguration : IEntityTypeConfiguration<ReceivingResolutionVersion>
{
    public void Configure(EntityTypeBuilder<ReceivingResolutionVersion> b)
    {
        b.ToTable("ReceivingResolutionVersions", t => t.HasCheckConstraint("CK_ReceivingResolutionVersions_Quantities", "[DoorRejectedQuantity] >= 0 AND [BaseDoorRejectedQuantity] >= 0 AND [FinalReceivedQuantity] >= 0 AND [BaseFinalReceivedQuantity] >= 0"));
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.ReceivingDiscrepancyId, x.Version }).IsUnique();
        foreach (var p in new[] { nameof(ReceivingResolutionVersion.DoorRejectedQuantity), nameof(ReceivingResolutionVersion.BaseDoorRejectedQuantity), nameof(ReceivingResolutionVersion.FinalReceivedQuantity), nameof(ReceivingResolutionVersion.BaseFinalReceivedQuantity), nameof(ReceivingResolutionVersion.AbsoluteToleranceSnapshot), nameof(ReceivingResolutionVersion.AllowedBaseToleranceSnapshot) }) b.Property(p).HasPrecision(18,4);
        b.Property(x => x.PercentageToleranceSnapshot).HasPrecision(9,6); b.Property(x => x.ValueToleranceSnapshot).HasPrecision(18,2); b.Property(x => x.ReasonCodeSnapshot).HasMaxLength(50); b.Property(x => x.ReasonNameSnapshot).HasMaxLength(200); b.Property(x => x.ReasonCategorySnapshot).HasMaxLength(50); b.Property(x => x.TolerancePolicySourceSnapshot).HasMaxLength(50); b.Property(x => x.Note).HasMaxLength(500); b.Property(x => x.EvidenceReference).HasMaxLength(500);
        b.HasOne(x => x.ReceivingDiscrepancy).WithMany(x => x.Resolutions).HasForeignKey(x => x.ReceivingDiscrepancyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.PreviousResolutionVersion).WithMany().HasForeignKey(x => x.PreviousResolutionVersionId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ReceivingReasonCodeConfiguration : IEntityTypeConfiguration<ReceivingReasonCode>
{
    public void Configure(EntityTypeBuilder<ReceivingReasonCode> b)
    {
        b.ToTable("ReceivingReasonCodes"); b.HasKey(x => x.Id); b.HasIndex(x => new { x.Code, x.Version }).IsUnique(); b.HasIndex(x => new { x.Code, x.IsActive, x.EffectiveFromUtc });
        b.Property(x => x.Code).HasMaxLength(50); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Category).HasMaxLength(50);
        var effective = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        b.HasData(
            new ReceivingReasonCode { Id = 1, Code = "UNDER_RECEIPT", Name = "Nhận thiếu", Category = "Quantity", Version = 1, EffectiveFromUtc = effective, RequiresNote = true, IsActive = true },
            new ReceivingReasonCode { Id = 2, Code = "OVER_RECEIPT", Name = "Nhận thừa", Category = "Quantity", Version = 1, EffectiveFromUtc = effective, RequiresNote = true, IsActive = true },
            new ReceivingReasonCode { Id = 3, Code = "DAMAGED_ON_RECEIPT", Name = "Hư hỏng khi nhận", Category = "Condition", Version = 1, EffectiveFromUtc = effective, RequiresNote = true, IsActive = true },
            new ReceivingReasonCode { Id = 4, Code = "WRONG_PRODUCT", Name = "Sai sản phẩm", Category = "Identity", Version = 1, EffectiveFromUtc = effective, RequiresNote = true, IsActive = true },
            new ReceivingReasonCode { Id = 5, Code = "WRONG_LOT", Name = "Sai lô", Category = "Identity", Version = 1, EffectiveFromUtc = effective, RequiresNote = true, IsActive = true },
            new ReceivingReasonCode { Id = 6, Code = "REJECTED_AT_DOOR", Name = "Từ chối tại cửa", Category = "Custody", Version = 1, EffectiveFromUtc = effective, RequiresNote = true, IsActive = true },
            new ReceivingReasonCode { Id = 7, Code = "UOM_MISMATCH", Name = "Sai đơn vị tính", Category = "Uom", Version = 1, EffectiveFromUtc = effective, RequiresNote = true, IsActive = true },
            new ReceivingReasonCode { Id = 8, Code = "DUPLICATE_COUNT", Name = "Đếm trùng", Category = "Counting", Version = 1, EffectiveFromUtc = effective, RequiresNote = true, IsActive = true });
    }
}

public sealed class ReceivingTolerancePolicyConfiguration : IEntityTypeConfiguration<ReceivingTolerancePolicy>
{
    public void Configure(EntityTypeBuilder<ReceivingTolerancePolicy> b)
    {
        b.ToTable("ReceivingTolerancePolicies", t =>
        {
            t.HasCheckConstraint("CK_ReceivingTolerancePolicies_Tolerance", "[AbsoluteQuantityTolerance] >= 0 AND [PercentageTolerance] >= 0 AND ([ValueTolerance] IS NULL OR [ValueTolerance] >= 0)");
            t.HasCheckConstraint("CK_ReceivingTolerancePolicies_Scope", "([ProductId] IS NOT NULL AND [SupplierId] IS NOT NULL) OR ([ProductId] IS NOT NULL AND [SupplierId] IS NULL AND [WarehouseId] IS NULL) OR ([ProductId] IS NULL AND [SupplierId] IS NULL)");
        });
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.ProductId, x.SupplierId, x.WarehouseId, x.Version }).IsUnique(); b.HasIndex(x => new { x.IsActive, x.EffectiveFromUtc });
        b.Property(x => x.AbsoluteQuantityTolerance).HasPrecision(18,4); b.Property(x => x.PercentageTolerance).HasPrecision(9,6); b.Property(x => x.ValueTolerance).HasPrecision(18,2); b.Property(x => x.ApproverTarget).HasMaxLength(100);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BusinessPartner>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasData(new ReceivingTolerancePolicy
        {
            Id = 1, Version = 1, EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AbsoluteQuantityTolerance = 0, PercentageTolerance = 0, OverageAllowed = true, ShortageAllowed = true,
            RequiresApprovalOutsideTolerance = true, ApproverTarget = "receiving_discrepancy.approve", IsActive = true
        });
    }
}
