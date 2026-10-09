namespace ERP.Application.DTOs;

public sealed class InventoryReversalWarehouseDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class InventoryReversalCandidateDto
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string InventoryStatus { get; set; } = string.Empty;
    public string? FromInventoryStatus { get; set; }
    public string? ToInventoryStatus { get; set; }
    public string? LotNumber { get; set; }
    public string? SerialNumber { get; set; }
    public decimal Quantity { get; set; }
    public DateTime TransactionDate { get; set; }
    public bool IsReversed { get; set; }
}


public sealed class InventoryReversalReasonDto
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    // Null means allowed for both currently supported raw reversal sources.
    public string? TransactionType { get; set; }
}

public sealed class CreateInventoryReversalDto
{
    public int OriginalTransactionId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class InventoryReversalResultDto
{
    public string ReasonCode { get; set; } = string.Empty;
    public int OriginalTransactionId { get; set; }
    public int CorrectiveTransactionId { get; set; }
    public int ReversalTransactionId { get; set; }
    public string OriginalTransactionType { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public int ProductId { get; set; }
    public decimal Quantity { get; set; }
    public int? FromLocationId { get; set; }
    public int? ToLocationId { get; set; }
    public string? FromInventoryStatus { get; set; }
    public string? ToInventoryStatus { get; set; }
    public int? LotId { get; set; }
    public int? SerialId { get; set; }
}

public sealed class InventoryTraceabilityResultDto
{
    public IReadOnlyList<InventoryTraceabilityBucketDto> CurrentBuckets { get; set; } = [];
    public IReadOnlyList<InventoryTraceabilityEventDto> Events { get; set; } = [];
    public IReadOnlyList<InventoryTraceabilityRelatedDocumentDto> RelatedDocuments { get; set; } = [];
    public bool RelatedDocumentsTruncated { get; set; }
    public IReadOnlyList<InventoryTraceabilityReceiptExposureDto> ReceiptExposures { get; set; } = [];
    public bool ReceiptExposuresTruncated { get; set; }
    public IReadOnlyList<InventoryTraceabilityShipmentExposureDto> ShipmentExposures { get; set; } = [];
    public bool ShipmentExposuresTruncated { get; set; }
    public IReadOnlyList<InventoryTraceabilityShipmentPickingEvidenceDto> ShipmentPickingEvidence { get; set; } = [];
    public bool ShipmentPickingEvidenceTruncated { get; set; }
    // These flags describe whether the initial chronological event window or
    // matching current-stock window was capped; reversal chain closure is retained.
    // Stable transaction-ID fence for ledger pagination. Null for older clients only.
    public int? EventAnchorId { get; set; }
    public bool EventsTruncated { get; set; }
    public bool BucketsTruncated { get; set; }
}

// One authorized warehouse/document occurrence for an explicitly tracked
// product+lot/serial. Do not mistake co-occurrence for causal custody links.
// Read-only posted ImportReceipt evidence, scoped by tracked ledger identity.
// This is not proof of per-lot QC lineage or a causal source-to-shipment link.
public sealed class InventoryTraceabilityReceiptExposureDto
{
    public int ReceiptId { get; set; }
    public int WarehouseId { get; set; }
    public string ReceiptCode { get; set; } = string.Empty;
    public decimal PostedQuantity { get; set; }
    public DateTime LastPostedAt { get; set; }
    public int LedgerEventCount { get; set; }
    public int LastTransactionId { get; set; }
}

// Read-only gross SHIP ledger evidence. It does not imply delivery or net returns.
public sealed class InventoryTraceabilityShipmentExposureDto
{
    public int ShipmentId { get; set; }
    public int WarehouseId { get; set; }
    public string ShipmentCode { get; set; } = string.Empty;
    public string ShipmentStatus { get; set; } = string.Empty;
    public DateTime? DispatchedAt { get; set; }
    public decimal DispatchedQuantity { get; set; }
    public int LedgerEventCount { get; set; }
    public int LastTransactionId { get; set; }
}

// Canonical shipment/picking/packing document linkage backed by an actual
// matching SHIP ledger bucket, not inferred receipt origin or a HU custody path.
public sealed class InventoryTraceabilityShipmentPickingEvidenceDto
{
    public int ShipmentId { get; set; }
    public string ShipmentCode { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public int PackingSessionId { get; set; }
    public string PackingSessionCode { get; set; } = string.Empty;
    public int PickingTaskId { get; set; }
    public string PickingTaskCode { get; set; } = string.Empty;
    public int PickingTaskLineId { get; set; }
    public int AllocationId { get; set; }
    public string SourceLocationCode { get; set; } = string.Empty;
    public decimal PickedQuantity { get; set; }
}

public sealed class InventoryTraceabilityRelatedDocumentDto
{
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public int ReferenceId { get; set; }
    public int EventCount { get; set; }
    public DateTime FirstTransactionDate { get; set; }
    public DateTime LastTransactionDate { get; set; }
    public int LastTransactionId { get; set; }
}

public sealed class InventoryTraceabilityBucketDto
{
    public int InventoryStockId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public string InventoryStatus { get; set; } = string.Empty;
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public decimal OnHandQuantity { get; set; }
    public decimal ReservedQuantity { get; set; }
}

public sealed class InventoryTraceabilityEventDto
{
    public int TransactionId { get; set; }
    public int ProductId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string InventoryStatus { get; set; } = string.Empty;
    public string? FromInventoryStatus { get; set; }
    public string? ToInventoryStatus { get; set; }
    public int? LocationId { get; set; }
    public string? LocationCode { get; set; }
    public int? FromLocationId { get; set; }
    public string? FromLocationCode { get; set; }
    public int? ToLocationId { get; set; }
    public string? ToLocationCode { get; set; }
    public int? LotId { get; set; }
    public string? LotNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public int? SerialId { get; set; }
    public string? SerialNumber { get; set; }
    public decimal Quantity { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public DateTime TransactionDate { get; set; }
    public int CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string? ReasonCode { get; set; }
    public int? ReversalOfTransactionId { get; set; }
    public int? CorrectiveTransactionId { get; set; }
    public int? ReversalTransactionId { get; set; }
    public bool IsReversed { get; set; }
}
