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
