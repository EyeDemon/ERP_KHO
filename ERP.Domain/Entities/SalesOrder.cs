using ERP.Domain.Common;
using ERP.Domain.Enums;
using ERP.Domain.Exceptions;

namespace ERP.Domain.Entities;

public sealed class SalesOrder : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string OrderCode { get; set; } = string.Empty;
    public string ExternalOrderId { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime? RequestedShipDate { get; set; }
    public int Priority { get; set; }
    public string? ShippingMethod { get; set; }
    public string? Currency { get; set; } = "VND";
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int CreatedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public void TransitionTo(SalesOrderStatus targetStatus, DateTime? timestampUtc = null, string? cancellationReason = null)
    {
        var isValid = (Status, targetStatus) switch
        {
            (SalesOrderStatus.Draft, SalesOrderStatus.Hold) => true,
            (SalesOrderStatus.Draft, SalesOrderStatus.Released) => true,
            (SalesOrderStatus.Draft, SalesOrderStatus.Cancelled) => true,
            (SalesOrderStatus.Hold, SalesOrderStatus.Draft) => true,
            (SalesOrderStatus.Hold, SalesOrderStatus.Released) => true,
            (SalesOrderStatus.Hold, SalesOrderStatus.Cancelled) => true,
            (SalesOrderStatus.Released, SalesOrderStatus.PartiallyFulfilled) => true,
            (SalesOrderStatus.Released, SalesOrderStatus.Fulfilled) => true,
            (SalesOrderStatus.Released, SalesOrderStatus.CancelPendingPutback) => true,
            (SalesOrderStatus.Released, SalesOrderStatus.Cancelled) => true,
            (SalesOrderStatus.Released, SalesOrderStatus.Hold) => true,
            (SalesOrderStatus.PartiallyFulfilled, SalesOrderStatus.PartiallyFulfilled) => true,
            (SalesOrderStatus.PartiallyFulfilled, SalesOrderStatus.Fulfilled) => true,
            (SalesOrderStatus.PartiallyFulfilled, SalesOrderStatus.CancelPendingPutback) => true,
            (SalesOrderStatus.PartiallyFulfilled, SalesOrderStatus.Cancelled) => true,
            (SalesOrderStatus.CancelPendingPutback, SalesOrderStatus.Cancelled) => true,
            _ => false
        };

        if (!isValid)
        {
            throw new ConflictException(
                $"Chuyển trạng thái đơn bán hàng từ '{Status}' sang '{targetStatus}' không hợp lệ.",
                "DOC_INVALID_STATE_TRANSITION");
        }

        Status = targetStatus;
        var now = timestampUtc ?? DateTime.UtcNow;
        if (targetStatus == SalesOrderStatus.Released)
        {
            ReleasedAt = now;
        }
        else if (targetStatus is SalesOrderStatus.Cancelled or SalesOrderStatus.CancelPendingPutback)
        {
            CancelledAt = now;
            if (!string.IsNullOrWhiteSpace(cancellationReason))
            {
                CancellationReason = cancellationReason.Trim();
            }
        }
    }

    public BusinessPartner Customer { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<SalesOrderLine> Lines { get; set; } = [];
}

public sealed class SalesOrderLine : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int SalesOrderId { get; set; }
    public string ExternalLineId { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal CancelledQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxPercent { get; set; }
    public string UomCode { get; set; } = string.Empty;
    public bool IsFreeGift { get; set; }
    public int? ParentBundleProductId { get; set; }

    public SalesOrder SalesOrder { get; set; } = null!;
    public Product Product { get; set; } = null!;
    public Backorder? Backorder { get; set; }
}

public sealed class Backorder : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
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
    public byte[] RowVersion { get; set; } = [0, 0, 0, 0, 0, 0, 0, 1];

    public SalesOrderLine SalesOrderLine { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
}
