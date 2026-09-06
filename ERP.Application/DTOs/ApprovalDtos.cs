using ERP.Application.Common;

namespace ERP.Application.DTOs;

public sealed class ApprovalRejectRequest
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class ApprovalActionResult
{
    public string DocumentType { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public string OldState { get; set; } = "Draft";
    public string NewState { get; set; } = "Cancelled";
    public string DisplayStatus { get; set; } = "Rejected";
    public string Result { get; set; } = "Success";
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class ApprovalQueueQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? DocumentType { get; set; }
    public int? WarehouseId { get; set; }
    public int? CreatorId { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    public string? Keyword { get; set; }
    public string? SlaStatus { get; set; }
    public string SortBy { get; set; } = "RequestedAt";
    public bool SortDescending { get; set; }
}

public sealed class ApprovalQueueItem
{
    public string DocumentType { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public string PendingState { get; set; } = "Draft";
    public int CreatorId { get; set; }
    public string CreatorName { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public long? WaitingMinutes { get; set; }
    public string? SlaStatus { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int? DestinationWarehouseId { get; set; }
    public string? DestinationWarehouseName { get; set; }
    public decimal TotalQuantity { get; set; }
    public bool CanApprove { get; set; }
    public bool CanReject { get; set; }
    public string? DeniedReasonCode { get; set; }
}

public sealed class ApprovalDetail
{
    public ApprovalQueueItem Summary { get; set; } = new();
    public string? Note { get; set; }
    public IReadOnlyList<ApprovalDetailLine> Lines { get; set; } = Array.Empty<ApprovalDetailLine>();
    public IReadOnlyList<ApprovalHistoryItem> History { get; set; } = Array.Empty<ApprovalHistoryItem>();
}

public sealed class ApprovalDetailLine
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}

public sealed class ApprovalHistoryQuery
{
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? DocumentType { get; set; }
    public int? DocumentId { get; set; }
    public int? WarehouseId { get; set; }
    public int? ActorId { get; set; }
    public string? Action { get; set; }
    public string? Result { get; set; }
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
}

public sealed class ApprovalHistoryItem
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public int? ActorId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string DisplayAction { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public int DocumentId { get; set; }
    public string DocumentCode { get; set; } = string.Empty;
    public int? WarehouseId { get; set; }
    public string? OldState { get; set; }
    public string? NewState { get; set; }
    public string? Result { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public string Severity { get; set; } = string.Empty;
}
