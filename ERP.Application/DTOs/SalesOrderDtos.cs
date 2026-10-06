namespace ERP.Application.DTOs;

public sealed class CreateSalesOrderDto
{
    public string ExternalOrderId { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime? RequestedShipDate { get; set; }
    public int Priority { get; set; }
    public string? ShippingMethod { get; set; }
    public IReadOnlyList<CreateSalesOrderLineDto> Lines { get; set; } = [];
}

public sealed class CreateSalesOrderLineDto
{
    public string ExternalLineId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public decimal OrderedQuantity { get; set; }
}

public sealed class SalesOrderStateCommandDto
{
    public string RowVersion { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public sealed class SalesOrderListDto
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string ExternalOrderId { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public DateTime? RequestedShipDate { get; set; }
    public int Priority { get; set; }
    public string? ShippingMethod { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal ShippedQuantity { get; set; }
    public decimal BackorderQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public decimal OpenQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
}

public sealed class SalesOrderDto : SalesOrderListDto
{
    public string RowVersion { get; set; } = string.Empty;
    public IReadOnlyList<SalesOrderLineDto> Lines { get; set; } = [];
}

public sealed class SalesOrderLineDto
{
    public int Id { get; set; }
    public string ExternalLineId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string UomCode { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal PickedQuantity { get; set; }
    public decimal ShippedQuantity { get; set; }
    public decimal BackorderQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public decimal OpenQuantity { get; set; }
}

public sealed class BackorderDto
{
    public int Id { get; set; }
    public string BackorderCode { get; set; } = string.Empty;
    public int SalesOrderId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string ExternalOrderId { get; set; } = string.Empty;
    public int SalesOrderLineId { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal Quantity { get; set; }
    public decimal RecoveredQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class BackorderReallocateDto
{
    public decimal? Quantity { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class BackorderCancelDto
{
    public decimal? Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
