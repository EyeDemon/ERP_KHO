using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class ShipmentTrackingEvent
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public ShipmentStatus FromStatus { get; set; }
    public ShipmentStatus ToStatus { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public string Source { get; set; } = "Internal";
    public string? SourceEventId { get; set; }
    public string? ReasonCode { get; set; }
    public string? Note { get; set; }
    public int? RecordedBy { get; set; }

    public Shipment Shipment { get; set; } = null!;
    public User? RecordedByUser { get; set; }
}

public sealed class ShipmentProofOfDelivery
{
    public int Id { get; set; }
    public int ShipmentId { get; set; }
    public DateTime DeliveredAt { get; set; }
    public string ReceiverName { get; set; } = string.Empty;
    public string? EvidenceReference { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? CarrierReference { get; set; }
    public string? DeliveryNote { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }

    public Shipment Shipment { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
}
