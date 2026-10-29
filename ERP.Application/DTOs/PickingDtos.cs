namespace ERP.Application.DTOs;

public class PickingTaskListDto
{
    public int Id { get; set; }
    public string TaskCode { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceCode { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string PickingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int? AssignedUserId { get; set; }
    public string? AssignedUserName { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public sealed class PickingTaskDto : PickingTaskListDto
{
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? RowVersion { get; set; }
    public IReadOnlyList<PickingTaskLineDto> Lines { get; set; } = [];
    public IReadOnlyList<ShortPickExceptionDto> ShortPicks { get; set; } = [];
}

public sealed class PickingTaskLineDto
{
    public int Id { get; set; }
    public int AllocationId { get; set; }
    public string AllocationCode { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int SourceLocationId { get; set; }
    public string SourceLocationCode { get; set; } = string.Empty;
    public string SourceLocationName { get; set; } = string.Empty;
    public string InventoryStatus { get; set; } = string.Empty;
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public int Sequence { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class ShortPickExceptionDto
{
    public int Id { get; set; }
    public int PickingTaskLineId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal ShortageQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ResolutionType { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
}

public sealed class PickingStateCommandDto
{
    public string RowVersion { get; set; } = string.Empty;
    public int? AssignedUserId { get; set; }
}

public sealed class PickScanDto
{
    public int TaskLineId { get; set; }
    public string LocationBarcode { get; set; } = string.Empty;
    public string ProductBarcode { get; set; } = string.Empty;
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal Quantity { get; set; }
    public string? DestinationToteBarcode { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ReportShortPickDto
{
    public int TaskLineId { get; set; }
    public decimal ActualPickedQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class ResolveShortPickDto
{
    public string Resolution { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class OverrideShortPickDto
{
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
