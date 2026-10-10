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
        // Bucket IDs are keyset-paged independently of Ledger events. This
        // high-water ID excludes newly inserted buckets, but quantities and
        // statuses remain CURRENT reads, not a transactionally frozen snapshot.
        // Current-state bucket evidence is explicitly scoped to one status.
        public string BucketStatus { get; set; } = "Available";
        public int BucketAnchorId { get; set; }
        public int? BucketAfterId { get; set; }
        public int? NextBucketAfterId { get; set; }
        public bool BucketHasRowsAfterAnchor { get; set; }
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
        public bool AvailableLedgerExpectedIsPartial { get; set; }
        public bool IsReadOnly { get; set; } = true;
        // All-status evidence is independently aggregated from the same
        // authorized warehouse/product and immutable Ledger anchor.
        // Unsupported historical events make expected totals indeterminate,
        // never a zero-valued "repair" proposal.
        public decimal AllStatusCurrentQuantity { get; set; }
        public decimal AllStatusReservedQuantity { get; set; }
        public decimal? AllStatusExpectedQuantity { get; set; }
        public decimal? AllStatusDifference { get; set; }
        public int UnclassifiedLedgerEventCount { get; set; }
        public IReadOnlyList<InventoryReconciliationStatusEvidenceDto> StatusBreakdown { get; set; } = [];
        public IReadOnlyList<InventoryReconciliationEvidenceEventDto> Events { get; set; } = [];
        public IReadOnlyList<InventoryReconciliationEvidenceBucketDto> Buckets { get; set; } = [];
    }

    public sealed class InventoryReconciliationStatusEvidenceDto
    {
        public string Status { get; set; } = string.Empty;
        public decimal CurrentQuantity { get; set; }
        public decimal ReservedQuantity { get; set; }
        public int BucketCount { get; set; }
        public decimal DirectLedgerNetQuantity { get; set; }
        public decimal StatusChangeInQuantity { get; set; }
        public decimal StatusChangeOutQuantity { get; set; }
        // Null when any relevant Ledger event cannot be classified safely.
        public decimal? ExpectedQuantity { get; set; }
        public decimal? Difference { get; set; }
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
        // Null means the transaction type has no validated sign mapping.
        // Never confuse unknown with the legitimate zero sign of Move/Reversal.
        public decimal? SignedQuantity { get; set; }
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
        // Unsupported or malformed history makes a pair indeterminate;
        // nullable values serialize as JSON null, never invented zero.
        public decimal? ExpectedQuantity { get; set; }
        public decimal? Difference { get; set; }
        public int UnclassifiedLedgerEventCount { get; set; }
        public decimal StatusChangeInQuantity { get; set; }
        public decimal StatusChangeOutQuantity { get; set; }
        public decimal ImportQuantity { get; set; }
        public decimal ExportQuantity { get; set; }
        public decimal TransferInQuantity { get; set; }
        public decimal TransferOutQuantity { get; set; }
        public decimal AdjustmentIncreaseQuantity { get; set; }
        public decimal AdjustmentDecreaseQuantity { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
