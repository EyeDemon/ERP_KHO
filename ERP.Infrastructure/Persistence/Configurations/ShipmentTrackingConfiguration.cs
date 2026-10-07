using ERP.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

public sealed class ShipmentTrackingEventConfiguration : IEntityTypeConfiguration<ShipmentTrackingEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentTrackingEvent> b)
    {
        b.ToTable("ShipmentTrackingEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasMaxLength(60).IsRequired();
        b.Property(x => x.Source).HasMaxLength(40).IsRequired();
        b.Property(x => x.SourceEventId).HasMaxLength(120);
        b.Property(x => x.ReasonCode).HasMaxLength(80);
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.ShipmentId, x.OccurredAt, x.Id });
        b.HasIndex(x => new { x.ShipmentId, x.Source, x.SourceEventId })
            .IsUnique()
            .HasFilter("[SourceEventId] IS NOT NULL");
        b.HasOne(x => x.Shipment).WithMany(x => x.TrackingEvents).HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.RecordedByUser).WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ShipmentProofOfDeliveryConfiguration : IEntityTypeConfiguration<ShipmentProofOfDelivery>
{
    public void Configure(EntityTypeBuilder<ShipmentProofOfDelivery> b)
    {
        b.ToTable("ShipmentProofOfDeliveries", t =>
        {
            t.HasCheckConstraint("CK_ShipmentProofOfDeliveries_Coordinates",
                "([Latitude] IS NULL AND [Longitude] IS NULL) OR ([Latitude] BETWEEN -90 AND 90 AND [Longitude] BETWEEN -180 AND 180)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.ReceiverName).HasMaxLength(200).IsRequired();
        b.Property(x => x.EvidenceReference).HasMaxLength(500);
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.CarrierReference).HasMaxLength(120);
        b.Property(x => x.DeliveryNote).HasMaxLength(500);
        b.HasIndex(x => x.ShipmentId).IsUnique();
        b.HasOne(x => x.Shipment).WithOne(x => x.ProofOfDelivery).HasForeignKey<ShipmentProofOfDelivery>(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
    }
}
