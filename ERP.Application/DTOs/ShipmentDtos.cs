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
}

public sealed class ShipmentDto : ShipmentListDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; set; }
    public IReadOnlyList<ShipmentHandlingUnitDto> HandlingUnits { get; set; } = [];
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
