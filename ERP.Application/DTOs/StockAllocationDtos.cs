namespace ERP.Application.DTOs;

public sealed class CreateStockAllocationDto
{
    public int ReservationId { get; set; }
    public decimal Quantity { get; set; }
    public int? LocationId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
}

public sealed class ReallocateStockAllocationDto
{
    public int? LocationId { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ReleaseStockAllocationDto
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class StockAllocationDto
{
    public int Id { get; set; }
    public string AllocationCode { get; set; } = string.Empty;
    public int ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public int? SourceId { get; set; }
    public string? SourceCode { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public string InventoryStatus { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string SelectionReason { get; set; } = string.Empty;
    public DateTime AllocatedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string? ReleaseReason { get; set; }
}

public sealed class StockAllocationPageDto
{
    public IReadOnlyList<StockAllocationDto> Items { get; set; } = [];
    public int TotalRecords { get; set; }
    public int PageIndex { get; set; }
    public int PageSize { get; set; }
}

public sealed class StockAllocationCandidateDto
{
    public int LocationId { get; set; }
    public string LocationCode { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public string InventoryStatus { get; set; } = string.Empty;
    public decimal ReservedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal AllocatableQuantity { get; set; }
    public int Rank { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AllocatableReservationDto
{
    public int ReservationId { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string? SourceCode { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal ReservedQuantity { get; set; }
    public decimal AllocatedQuantity { get; set; }
    public decimal AllocatableQuantity { get; set; }
    public DateTime ExpiresAt { get; set; }
}
