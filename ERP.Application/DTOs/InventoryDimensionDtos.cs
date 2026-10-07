namespace ERP.Application.DTOs;

public sealed class InventoryStatusDefinitionDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsReservable { get; set; }
    public bool IsAllocatable { get; set; }
    public bool IsPickable { get; set; }
    public bool IsShippable { get; set; }
}

public sealed class InventoryBucketDto
{
    public int InventoryStockId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsReservable { get; set; }
    public bool IsAllocatable { get; set; }
    public bool IsPickable { get; set; }
    public bool IsShippable { get; set; }
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ManufactureDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public decimal OnHandQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AvailableQuantity { get; set; }
    public DateTime LastUpdated { get; set; }
}

public sealed class CreateInventoryStatusChangeDto
{
    public int InventoryStockId { get; set; }
    public decimal Quantity { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class InventoryStatusChangeResultDto
{
    public int SourceStockId { get; set; }
    public int DestinationStockId { get; set; }
    public int TransactionId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int LocationId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
}
