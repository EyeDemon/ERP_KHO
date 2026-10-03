using System;

namespace ERP.Application.DTOs
{
    public class InventoryTransactionHistoryDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string? ProductCode { get; set; }
        public string? ProductName { get; set; }
        public string? UnitName { get; set; }
        public int WarehouseId { get; set; }
        public string? WarehouseName { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public int? ReferenceId { get; set; }
        public string? ReferenceType { get; set; }
        public DateTime TransactionDate { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public string? Note { get; set; }
    }
}
