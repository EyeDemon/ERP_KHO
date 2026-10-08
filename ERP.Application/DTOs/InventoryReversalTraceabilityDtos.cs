namespace ERP.Application.DTOs;

public sealed class InventoryReversalWarehouseDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class InventoryReversalCandidateDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string InventoryStatus { get; set; } = string.Empty;
    public string? FromInventoryStatus { get; set; }
    public string? ToInventoryStatus { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal Quantity { get; set; }
    public DateTime TransactionDate { get; set; }
    public bool IsReversed { get; set; }
}


public sealed class CreateInventoryReversalDto
{
    public int OriginalTransactionId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class InventoryReversalResultDto
{
    public int OriginalTransactionId { get; set; }
    public int CorrectiveTransactionId { get; set; }
    public int ReversalTransactionId { get; set; }
    public string OriginalTransactionType { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public string? FromInventoryStatus { get; set; }
    public string? ToInventoryStatus { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
}

public sealed class InventoryTraceabilityResultDto
{
    public IReadOnlyList<InventoryTraceabilityBucketDto> CurrentBuckets { get; set; } = [];
    public IReadOnlyList<InventoryTraceabilityEventDto> Events { get; set; } = [];
    // These flags describe whether the initial chronological event window or
    // matching current-stock window was capped; reversal chain closure is retained.
    public bool EventsTruncated { get; set; }
    public bool BucketsTruncated { get; set; }
}

public sealed class InventoryTraceabilityBucketDto
{
    public int InventoryStockId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public string InventoryStatus { get; set; } = string.Empty;
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public decimal OnHandQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
}

public sealed class InventoryTraceabilityEventDto
{
    public int TransactionId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string InventoryStatus { get; set; } = string.Empty;
    public string? FromInventoryStatus { get; set; }
    public string? ToInventoryStatus { get; set; }
    public int? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public int? FromLocationId { get; set; }
    public string? FromLocationCode { get; set; }
    public int? ToLocationId { get; set; }
    public string? ToLocationCode { get; set; }
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public decimal Quantity { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public DateTime TransactionDate { get; set; }
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string? Note { get; set; }
    public int? ReversalOfTransactionId { get; set; }
    public int? CorrectiveTransactionId { get; set; }
    public int? ReversalTransactionId { get; set; }
    public bool IsReversed { get; set; }
}
