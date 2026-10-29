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
        public string InventoryStatus { get; set; } = string.Empty;
        public string? FromInventoryStatus { get; set; }
        public string? ToInventoryStatus { get; set; }
        public int? LotId { get; set; }
        public string? LotNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public int? SerialId { get; set; }
        public string? SerialNumber { get; set; }
        public decimal Quantity { get; set; }
        public int? ReferenceId { get; set; }
        public string? ReferenceType { get; set; }
        public DateTime TransactionDate { get; set; }
        public int CreatedBy { get; set; }
        public string? CreatedByName { get; set; }
        public string? Note { get; set; }
    }
}
