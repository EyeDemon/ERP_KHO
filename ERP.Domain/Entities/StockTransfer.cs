using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class StockTransfer
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int SourceWarehouseId { get; set; }
    public int DestinationWarehouseId { get; set; }
    public StockTransferStatus Status { get; set; } = StockTransferStatus.Draft;
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? DispatchedBy { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public int? ReceivedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }

    public Warehouse SourceWarehouse { get; set; } = null!;
    public Warehouse DestinationWarehouse { get; set; } = null!;
    public User CreatedByUser { get; set; } = null!;
    public ICollection<StockTransferDetail> Details { get; set; } = new List<StockTransferDetail>();
}
