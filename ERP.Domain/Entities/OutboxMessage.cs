namespace ERP.Domain.Entities;

public sealed class OutboxMessage
{
    public long Id { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public int AggregateId { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
