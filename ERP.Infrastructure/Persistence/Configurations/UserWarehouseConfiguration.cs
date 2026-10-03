using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public class UserWarehouseConfiguration : IEntityTypeConfiguration<UserWarehouse>
{
    public void Configure(EntityTypeBuilder<UserWarehouse> builder)
    {
        builder.ToTable("UserWarehouses");
        builder.HasKey(x => new { x.UserId, x.WarehouseId });
        builder.HasIndex(x => x.WarehouseId);

        builder.HasOne(x => x.User)
            .WithMany(x => x.WarehouseAccesses)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Warehouse)
            .WithMany(x => x.UserAccesses)
            .HasForeignKey(x => x.WarehouseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.CreatedByUser)
            .WithMany(x => x.GrantedWarehouseAccesses)
            .HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
