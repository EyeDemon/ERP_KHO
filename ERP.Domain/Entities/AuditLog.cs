namespace ERP.Domain.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public int? WarehouseId { get; set; }
    public int? SourceWarehouseId { get; set; }
    public int? DestinationWarehouseId { get; set; }
    public string? Result { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public string? IdempotencyKeyHash { get; set; }
    public string? RequestFingerprint { get; set; }
    public string Severity { get; set; } = "Information";

    // Navigation
    public User? User { get; set; }
}
