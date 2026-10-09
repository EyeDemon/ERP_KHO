namespace ERP.Application.DTOs
{
    public sealed class InventoryReconciliationWarehouseDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    public class InventoryReconciliationDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public decimal CurrentQuantity { get; set; }
        public decimal ExpectedQuantity { get; set; }
        public decimal Difference { get; set; }
        public decimal ImportQuantity { get; set; }
        public decimal ExportQuantity { get; set; }
        public decimal TransferInQuantity { get; set; }
        public decimal TransferOutQuantity { get; set; }
        public decimal AdjustmentIncreaseQuantity { get; set; }
        public decimal AdjustmentDecreaseQuantity { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
