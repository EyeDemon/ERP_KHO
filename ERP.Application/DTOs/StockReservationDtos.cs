namespace ERP.Application.DTOs;

public class CreateStockReservationDto
{
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ReleaseStockReservationDto
{
    public decimal? Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class StockReservationDto
{
    public int Id { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ConsumedQuantity { get; set; }
    public decimal ReleasedQuantity { get; set; }
    public decimal RemainingQuantity => Quantity - ConsumedQuantity - ReleasedQuantity;
    public string Status { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class StockReservationPageDto
{
    public IReadOnlyList<StockReservationDto> Items { get; set; } = [];
    public int TotalRecords { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
}

public class ReservationReconciliationIssueDto
{
    public string Issue { get; set; } = string.Empty;
    public int? ReservationId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public decimal? LedgerReserved { get; set; }
    public decimal? StockReserved { get; set; }
}
