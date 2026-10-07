namespace ERP.Application.DTOs;

public sealed class InventoryStatusDefinitionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsReservable { get; set; }
    public bool IsAllocatable { get; set; }
    public bool IsPickable { get; set; }
    public bool IsShippable { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class InventoryStatusBucketDto
{
    public int StockId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusDisplayName { get; set; } = string.Empty;
    public decimal OnHandQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal EligibleAvailableQuantity { get; set; }
    public DateTime LastUpdated { get; set; }
}

public sealed class InventoryStatusChangeRequestDto
{
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public int? LocationId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class QuarantineReleaseRequestDto
{
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class InventoryStatusChangeResultDto
{
    public int TransactionId { get; set; }
    public int SourceStockId { get; set; }
    public int DestinationStockId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal SourceRemainingQuantity { get; set; }
    public decimal DestinationQuantity { get; set; }
    public DateTime PostedAt { get; set; }
}
