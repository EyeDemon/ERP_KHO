using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class PackingVideoEvidence : ITenantEntity
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public int? PackingSessionId { get; set; }
    public int? ExportReceiptId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public string CameraStationId { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime EndedAtUtc { get; set; } = DateTime.UtcNow;
    public int DurationSeconds { get; set; }
    public string VideoHashSha256 { get; set; } = string.Empty;
    public string StorageUrl { get; set; } = string.Empty;
    public int? RecordedByUserId { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
