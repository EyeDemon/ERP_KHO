namespace ERP.Application.DTOs;

public class ShipmentListDto
{
    public int Id { get; set; }
    public string ShipmentCode { get; set; } = string.Empty;
    public int PackingSessionId { get; set; }
    public string PackingSessionCode { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceCode { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? StagingLocationId { get; set; }
    public string? StagingLocationCode { get; set; }
    public int HandlingUnitCount { get; set; }
    public int LoadedHandlingUnitCount { get; set; }
    public int? DockAppointmentId { get; set; }
    public string? DockAppointmentCode { get; set; }
    public int? DockId { get; set; }
    public string? DockCode { get; set; }
    public string? VehiclePlate { get; set; }
    public string? TrailerPlate { get; set; }
    public string? SealNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StagedAt { get; set; }
    public DateTime? LoadingStartedAt { get; set; }
    public DateTime? LoadedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public int? DispatchedBy { get; set; }
    public string? DispatchedByName { get; set; }
    public DateTime? InTransitAt { get; set; }
    public DateTime? DeliveryFailedAt { get; set; }
    public DateTime? ReturnInitiatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class ShipmentDto : ShipmentListDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; set; }
    public IReadOnlyList<ShipmentHandlingUnitDto> HandlingUnits { get; set; } = [];
    public ShipmentProofOfDeliveryDto? ProofOfDelivery { get; set; }
    public IReadOnlyList<ShipmentTrackingEventDto> TrackingEvents { get; set; } = [];
}

public sealed class ShipmentHandlingUnitDto
{
    public int Id { get; set; }
    public int HandlingUnitId { get; set; }
    public string HuCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string? Sscc { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public decimal ContentQuantity { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? StagedAt { get; set; }
    public DateTime? LoadedAt { get; set; }
}

public class ShipmentStateCommandDto
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class StageShipmentDto : ShipmentStateCommandDto
{
    public string StagingLocationCode { get; set; } = string.Empty;
}

public sealed class StartShipmentLoadingDto : ShipmentStateCommandDto
{
    public int DockAppointmentId { get; set; }
}

public sealed class LoadShipmentHandlingUnitDto : ShipmentStateCommandDto
{
    public string HandlingUnitBarcode { get; set; } = string.Empty;
}

public sealed class CompleteShipmentLoadingDto : ShipmentStateCommandDto
{
    public string? SealNumber { get; set; }
}


public sealed class ShipmentTrackingEventDto
{
    public int Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime RecordedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? SourceEventId { get; set; }
    public string? ReasonCode { get; set; }
    public string? Note { get; set; }
}

public sealed class ShipmentProofOfDeliveryDto
{
    public DateTime DeliveredAt { get; set; }
    public string ReceiverName { get; set; } = string.Empty;
    public string? EvidenceReference { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? CarrierReference { get; set; }
    public string? DeliveryNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class ShipmentTrackingDto
{
    public int ShipmentId { get; set; }
    public string ShipmentCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DispatchedAt { get; set; }
    public DateTime? InTransitAt { get; set; }
    public DateTime? DeliveryFailedAt { get; set; }
    public DateTime? ReturnInitiatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ShipmentProofOfDeliveryDto? ProofOfDelivery { get; set; }
    public IReadOnlyList<ShipmentTrackingEventDto> Events { get; set; } = [];
}

public sealed class MarkShipmentInTransitDto : ShipmentStateCommandDto
{
    public DateTime? OccurredAt { get; set; }
    public string? Note { get; set; }
}

public sealed class ConfirmShipmentDeliveryDto : ShipmentStateCommandDto
{
    public DateTime? DeliveredAt { get; set; }
    public string ReceiverName { get; set; } = string.Empty;
    public string? EvidenceReference { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? CarrierReference { get; set; }
    public string? DeliveryNote { get; set; }
}

public sealed class FailShipmentDeliveryDto : ShipmentStateCommandDto
{
    public string ReasonCode { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime? OccurredAt { get; set; }
}

public sealed class RetryShipmentDeliveryDto : ShipmentStateCommandDto
{
    public string? Note { get; set; }
    public DateTime? OccurredAt { get; set; }
}

public sealed class InitiateShipmentReturnDto : ShipmentStateCommandDto
{
    public string ReasonCode { get; set; } = string.Empty;
    public string? Note { get; set; }
    public DateTime? OccurredAt { get; set; }
}
