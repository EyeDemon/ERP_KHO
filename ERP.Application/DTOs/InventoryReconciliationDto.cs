namespace ERP.Application.DTOs
{
    public sealed class InventoryReconciliationWarehouseDto
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    // Evidence only. No command or projection repair can be derived from this
    // read model without a separately authorized, audited correction process.
    public sealed class InventoryReconciliationInvestigationDto
    {
        public int ProductId { get; set; }
        public string ProductCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int WarehouseId { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public int EventAnchorId { get; set; }
        // Cursor excludes the named ID (strict less-than); every query is
        // independently authorized within an exact warehouse/product scope.
        public int? EventBeforeId { get; set; }
        public int? NextEventBeforeId { get; set; }
        public bool LedgerHasEventsAfterAnchor { get; set; }
        public int EventCount { get; set; }
        public int BucketCount { get; set; }
        public bool EventsTruncated { get; set; }
        public bool BucketsTruncated { get; set; }
        public decimal CurrentQuantity { get; set; }
        public decimal ExpectedQuantity { get; set; }
        public decimal Difference { get; set; }
        public bool IsReadOnly { get; set; } = true;
        public IReadOnlyList<InventoryReconciliationEvidenceEventDto> Events { get; set; } = [];
        public IReadOnlyList<InventoryReconciliationEvidenceBucketDto> Buckets { get; set; } = [];
    }

    public sealed class InventoryReconciliationEvidenceBucketDto
    {
        public int InventoryStockId { get; set; }
        public int? LocationId { get; set; }
        public string? LocationCode { get; set; }
        public int? LotId { get; set; }
        public string? LotNumber { get; set; }
        public int? SerialId { get; set; }
        public string? SerialNumber { get; set; }
        public decimal Quantity { get; set; }
        public decimal ReservedQuantity { get; set; }
    }

    public sealed class InventoryReconciliationEvidenceEventDto
    {
        public int TransactionId { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public int? LocationId { get; set; }
        public string? LocationCode { get; set; }
        public int? LotId { get; set; }
        public string? LotNumber { get; set; }
        public int? SerialId { get; set; }
        public string? SerialNumber { get; set; }
        public decimal Quantity { get; set; }
        public decimal SignedQuantity { get; set; }
        public string? ReferenceType { get; set; }
        public int? ReferenceId { get; set; }
        public DateTime TransactionDate { get; set; }
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
