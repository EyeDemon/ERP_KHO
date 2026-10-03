using ERP.Domain.Enums;

namespace ERP.Application.DTOs;

public sealed class StockTransferDetailInputDto
{
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public string? Note { get; set; }
}

public class CreateStockTransferDto
{
    public int SourceWarehouseId { get; set; }
    public int DestinationWarehouseId { get; set; }
    public string? Note { get; set; }
    public List<StockTransferDetailInputDto> Details { get; set; } = [];
}

public sealed class UpdateStockTransferDto : CreateStockTransferDto;

public sealed class ReceiveStockTransferLineDto
{
    public int ProductId { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal MissingQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public string? Note { get; set; }
}

public sealed class ReceiveStockTransferDto
{
    public List<ReceiveStockTransferLineDto> Details { get; set; } = [];
}

public sealed class StockTransferDetailDto
{
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal RequestedQuantity { get; set; }
    public decimal DispatchedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal MissingQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public decimal InTransitQuantity => DispatchedQuantity - ReceivedQuantity;
    public string? Note { get; set; }
}

public sealed class StockTransferDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public int SourceWarehouseId { get; set; }
    public string SourceWarehouseName { get; set; } = string.Empty;
    public int DestinationWarehouseId { get; set; }
    public string DestinationWarehouseName { get; set; } = string.Empty;
    public StockTransferStatus Status { get; set; }
    public string? Note { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public List<StockTransferDetailDto> Details { get; set; } = [];
}

public sealed class StockTransferQueryDto
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Search { get; set; }
    public StockTransferStatus? Status { get; set; }
    public int? SourceWarehouseId { get; set; }
    public int? DestinationWarehouseId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
