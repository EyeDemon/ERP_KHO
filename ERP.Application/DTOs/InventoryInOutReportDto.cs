namespace ERP.Application.DTOs
{
    public class InventoryInOutReportDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string UnitName { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        
        public decimal OpeningQuantity { get; set; }
        public decimal ImportQuantity { get; set; }
        public decimal TransferInQuantity { get; set; }
        public decimal AdjustmentIncreaseQuantity { get; set; }
        public decimal InQuantity { get; set; }
        public decimal ExportQuantity { get; set; }
        public decimal TransferOutQuantity { get; set; }
        public decimal AdjustmentDecreaseQuantity { get; set; }
        public decimal OutQuantity { get; set; }
        public decimal ClosingQuantity { get; set; }
    }
}
