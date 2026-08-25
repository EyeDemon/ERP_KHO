namespace ERP.Domain.Entities;

public class Warehouse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public ICollection<InventoryStock> InventoryStocks { get; set; } = new List<InventoryStock>();
    public ICollection<ImportReceipt> ImportReceipts { get; set; } = new List<ImportReceipt>();
    public ICollection<ExportReceipt> ExportReceipts { get; set; } = new List<ExportReceipt>();
    public ICollection<Stocktake> Stocktakes { get; set; } = new List<Stocktake>();
    public ICollection<UserWarehouse> UserAccesses { get; set; } = new List<UserWarehouse>();
}
