namespace ERP.Application.DTOs;

public class PackingSessionListDto
{
    public int Id { get; set; }
    public string SessionCode { get; set; } = string.Empty;
    public int PickingTaskId { get; set; }
    public string PickingTaskCode { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceCode { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal RequiredQuantity { get; set; }
    public decimal PackedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public int HandlingUnitCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? PackedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public sealed class PackingSessionDto : PackingSessionListDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; set; }
    public IReadOnlyList<PackingSourceLineDto> Lines { get; set; } = [];
    public IReadOnlyList<HandlingUnitDto> HandlingUnits { get; set; } = [];
}

public sealed class PackingSourceLineDto
{
    public int PickingTaskLineId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal PickedQuantity { get; set; }
    public decimal PackedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
}

public sealed class HandlingUnitDto
{
    public int Id { get; set; }
    public string HuCode { get; set; } = string.Empty;
    public string Barcode { get; set; } = string.Empty;
    public string? Sscc { get; set; }
    public int WarehouseId { get; set; }
    public int PackingSessionId { get; set; }
    public int? ParentHandlingUnitId { get; set; }
    public string? ParentHandlingUnitCode { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal? GrossWeightKg { get; set; }
    public decimal? NetWeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public DateTime? SealedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<HandlingUnitContentDto> Contents { get; set; } = [];
}

public sealed class HandlingUnitContentDto
{
    public int Id { get; set; }
    public int PickingTaskLineId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public DateTime PackedAt { get; set; }
}

public sealed class CreatePackingSessionDto
{
    public int PickingTaskId { get; set; }
}

public sealed class CreateHandlingUnitDto
{
    public string Type { get; set; } = string.Empty;
    public string? HuCode { get; set; }
    public string? Barcode { get; set; }
    public string? Sscc { get; set; }
    public decimal? GrossWeightKg { get; set; }
    public decimal? NetWeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class PackIntoHandlingUnitDto
{
    public int HandlingUnitId { get; set; }
    public int PickingTaskLineId { get; set; }
    public string ProductBarcode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class PackingStateCommandDto
{
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class NestHandlingUnitDto
{
    public int ParentHandlingUnitId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
