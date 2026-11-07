using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class IntegrationOutboxMessage : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public int? AggregateId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public class IntegrationInboxMessage : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string SourceSystem { get; set; } = string.Empty;
    public string ExternalMessageId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Processed";
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
}

public class ProductBundleComponent : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int ParentProductId { get; set; }
    public int ComponentProductId { get; set; }
    public decimal ComponentQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Product ParentProduct { get; set; } = null!;
    public Product ComponentProduct { get; set; } = null!;
}
