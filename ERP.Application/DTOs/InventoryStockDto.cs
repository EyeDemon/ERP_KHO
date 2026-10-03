namespace ERP.Application.DTOs
{
    public class InventoryStockDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal OnHandQuantity { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal AvailableQuantity { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
