namespace ERP.Application.DTOs;

public sealed class InventoryLockDto
{
    public int Id { get; set; }
    public string LockType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public int? ProductId { get; set; }
    public string? ProductCode { get; set; }
    public string? InventoryStatus { get; set; }
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public int? ReleasedBy { get; set; }
    public string? ReleaseReason { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateInventoryLockDto
{
    public string LockType { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public int? LocationId { get; set; }
    public int? ProductId { get; set; }
    public string? InventoryStatus { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
}

public sealed class ReleaseInventoryLockDto
{
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class CreateInventoryMoveDto
{
    public int InventoryStockId { get; set; }
    public int DestinationLocationId { get; set; }
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class InventoryMoveResultDto
{
    public int MovementId { get; set; }
    public int TransactionId { get; set; }
    public int SourceStockId { get; set; }
    public int DestinationStockId { get; set; }
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public int FromLocationId { get; set; }
    public int ToLocationId { get; set; }
    public string InventoryStatus { get; set; } = string.Empty;
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public decimal Quantity { get; set; }
}
