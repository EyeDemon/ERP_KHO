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
        b.Property(x => x.Id).HasConversion<int>().ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();

        b.HasData(
            Status(InventoryStatus.Available, "AVAILABLE", "Khả dụng", true, true, true, true, true, 10),
            Status(InventoryStatus.QcHold, "QC_HOLD", "Chờ QC", false, false, false, false, false, 20),
            Status(InventoryStatus.Quarantine, "QUARANTINE", "Cách ly", false, false, false, false, false, 30),
            Status(InventoryStatus.Blocked, "BLOCKED", "Bị khóa nghiệp vụ", false, false, false, false, false, 40),
            Status(InventoryStatus.Damaged, "DAMAGED", "Hư hỏng", false, false, false, false, false, 50),
            Status(InventoryStatus.Expired, "EXPIRED", "Hết hạn", false, false, false, false, false, 60),
            Status(InventoryStatus.RecallBlocked, "RECALL_BLOCKED", "Khóa thu hồi", false, false, false, false, false, 70),
            Status(InventoryStatus.Rejected, "REJECTED", "Từ chối nhưng còn lưu kho", false, false, false, false, false, 80));
    }

    private static InventoryStatusDefinition Status(
        InventoryStatus id,
        string code,
        string displayName,
        bool available,
        bool reservable,
        bool allocatable,
        bool pickable,
        bool shippable,
        int sortOrder) => new()
        {
            Id = id,
            Code = code,
            DisplayName = displayName,
            IsAvailable = available,
            IsReservable = reservable,
            IsAllocatable = allocatable,
            IsPickable = pickable,
            IsShippable = shippable,
            IsActive = true,
            SortOrder = sortOrder
        };
}
