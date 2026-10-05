using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class PickingTask
{
    public int Id { get; set; }
    public string TaskCode { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceCode { get; set; }
    public int WarehouseId { get; set; }
    public string PickingType { get; set; } = "STANDARD";
    public PickingTaskStatus Status { get; set; } = PickingTaskStatus.Open;
    public int Priority { get; set; }
    public int? AssignedUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User? AssignedUser { get; set; }
    public ICollection<PickingTaskLine> Lines { get; set; } = [];
}

public sealed class PickingTaskLine
{
    public int Id { get; set; }
    public int PickingTaskId { get; set; }
    public int AllocationId { get; set; }
    public int ProductId { get; set; }
    public int SourceLocationId { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public int Sequence { get; set; }
    public PickingTaskLineStatus Status { get; set; } = PickingTaskLineStatus.Open;

    public PickingTask PickingTask { get; set; } = null!;
    public StockAllocation Allocation { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public WarehouseLocation SourceLocation { get; set; } = null!;
    public ICollection<ShortPickException> ShortPicks { get; set; } = [];

    public decimal RemainingQuantity => RequestedQuantity - PickedQuantity;
}

public sealed class ShortPickException
{
    public int Id { get; set; }
    public int PickingTaskLineId { get; set; }
    public decimal ExpectedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal ShortageQuantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ShortPickResolutionType? ResolutionType { get; set; }
    public ShortPickExceptionStatus Status { get; set; } = ShortPickExceptionStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }

    public PickingTaskLine PickingTaskLine { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User? ResolvedByUser { get; set; }
}
