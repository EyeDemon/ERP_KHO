namespace ERP.Domain.Entities;

public class StockTransferDetail
{
    public int Id { get; set; }
    public int StockTransferId { get; set; }
    public int ProductId { get; set; }
    public decimal RequestedQuantity { get; set; }
    public decimal DispatchedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal MissingQuantity { get; set; }
    public decimal DamagedQuantity { get; set; }
    public string? Note { get; set; }

    public StockTransfer StockTransfer { get; set; } = null!;
    public Product Product { get; set; } = null!;
}
