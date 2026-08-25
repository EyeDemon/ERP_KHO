using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class StockReservation
{
    public int Id { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal ReleasedQuantity { get; set; }
    public StockReservationStatus Status { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public int? ReleasedBy { get; set; }
    public string? ReleaseReason { get; set; }

    public Product Product { get; set; } = null!;
    public Warehouse Warehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public User? ReleasedByUser { get; set; }
}
