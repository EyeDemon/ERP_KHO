using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public sealed class SalesOrder
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string ExternalOrderId { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime? RequestedShipDate { get; set; }
    public int Priority { get; set; }
    public string? ShippingMethod { get; set; }
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public BusinessPartner Customer { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<SalesOrderLine> Lines { get; set; } = [];
}

public sealed class SalesOrderLine
{
    public int Id { get; set; }
    public int SalesOrderId { get; set; }
    public string ExternalLineId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public string UomCode { get; set; } = string.Empty;

    public SalesOrder SalesOrder { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public Backorder? Backorder { get; set; }
}

public sealed class Backorder
{
    public int Id { get; set; }
    public string BackorderCode { get; set; } = string.Empty;
    public int SalesOrderLineId { get; set; }
    public int WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal RecoveredQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public BackorderStatus Status { get; set; } = BackorderStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public SalesOrderLine SalesOrderLine { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}
