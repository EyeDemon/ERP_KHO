using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class PutawayTask
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public int WarehouseId { get; set; }
    public PutawayTaskStatus Status { get; set; } = PutawayTaskStatus.Open;
    public int? AssignedUserId { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? ExceptionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ImportReceipt Receipt { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public ICollection<PutawayTaskItem> Items { get; set; } = [];
}

public class PutawayTaskItem
{
    public int Id { get; set; }
    public int PutawayTaskId { get; set; }
    public int ReceiptLineId { get; set; }
    public int ProductId { get; set; }
    public InventoryStatus InventoryStatus { get; set; }
    public int SourceLocationId { get; set; }
    public int OperationUnitId { get; set; }
    public string OperationUnitCodeSnapshot { get; set; } = string.Empty;
    public int BaseUnitId { get; set; }
    public string BaseUnitCodeSnapshot { get; set; } = string.Empty;
    public decimal ConversionFactorSnapshot { get; set; }
    public int ConversionVersionSnapshot { get; set; }
    public int BaseUnitDecimalPlaces { get; set; }
    public decimal RequiredOperationQuantity { get; set; }
    public decimal RequiredBaseQuantity { get; set; }
    public decimal MovedBaseQuantity { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public PutawayTask PutawayTask { get; set; } = null!;
    public ImportReceiptDetail ReceiptLine { get; set; } = null!;
    public WarehouseLocation SourceLocation { get; set; } = null!;
    public decimal RemainingBaseQuantity => RequiredBaseQuantity - MovedBaseQuantity;
}

public class InventoryLocationMovement
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public InventoryStatus InventoryStatus { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public int FromLocationId { get; set; }
    public int ToLocationId { get; set; }
    public decimal BaseQuantity { get; set; }
    public decimal EnteredQuantity { get; set; }
    public string EnteredUnitCode { get; set; } = string.Empty;
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public int? PutawayTaskId { get; set; }
    public int? PutawayTaskItemId { get; set; }
    public int? ReceiptId { get; set; }
    public int? ReceiptLineId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
